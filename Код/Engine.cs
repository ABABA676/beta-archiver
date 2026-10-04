using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BetaApp;

/// <summary>Одна запись внутри архива — как её показывает 7-Zip.</summary>
/// <param name="IsFolder">
/// 7-Zip ставит «Folder = +» только ПУСТЫМ папкам. Непустые папки приходят обычными записями,
/// их видно только по общему началу путей (папка = префикс путей файлов).
/// </param>
public sealed record EntryInfo(string Path, long Size, long PackedSize, bool IsFolder)
{
    public string SizeText => EntryInfo.Human(Size);

    public static string Human(long bytes) => bytes switch
    {
        < 0 => "?",
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} МБ",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} ГБ"
    };
}

public sealed class EngineException : Exception
{
    public EngineException(string message) : base(message) { }
}

/// <summary>Результат запуска 7z: код возврата + всё, что он напечатал.</summary>
public sealed class EngineRun
{
    public int ExitCode { get; set; } = -1;
    public bool TimedOut { get; set; }
    public List<string> Lines { get; } = new();
}

/// <summary>
/// Движок архиватора. Мы НЕ пишем свой код сжатия — зовём установленный 7z.exe (7-Zip 23.01),
/// поэтому любой наш архив — обычный 7z и открывается 7-Zip/WinRAR без нашего участия.
/// </summary>
public static partial class Engine
{
    /// <summary>Наше оригинальное расширение. Содержимое файла — обычный 7z.</summary>
    public const string Extension = ".b3ta";

    public static string ExePath { get; } = ResolveExe();
    public static bool Available => File.Exists(ExePath);

    public static bool LooksLikeArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is Extension or ".7z" or ".zip" or ".rar" or ".tar" or ".gz" or ".tgz"
            or ".bz2" or ".xz" or ".cab" or ".iso" or ".001" or ".arj" or ".lzh" or ".z";
    }

    [GeneratedRegex(@"(?<pct>\d{1,3})\s*%")]
    private static partial Regex PercentRegex();

    private static string ResolveExe()
    {
        foreach (var name in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)" })
        {
            var root = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, "7-Zip", "7z.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "7-Zip", "7z.exe");
        return File.Exists(local) ? local : Path.Combine(@"C:\Program Files", "7-Zip", "7z.exe");
    }

    private static EngineRun Run(
        IEnumerable<string> args,
        Action<int>? progress = null,
        Action<string>? log = null,
        string? workingDir = null,
        int timeoutMs = 30 * 60 * 1000,
        CancellationToken ct = default)
    {
        if (!Available)
            throw new EngineException("Не найден 7z.exe — движок архиватора. Установи 7-Zip, и всё заработает.");

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;
        foreach (var a in args) psi.ArgumentList.Add(a);
        log?.Invoke("7z " + string.Join(' ', psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

        using var proc = new Process { StartInfo = psi };
        var run = new EngineRun();
        proc.Start();

        using var killOnCancel = ct.Register(() => { try { proc.Kill(true); } catch { /* уже умер */ } });

        var stdout = Task.Run(() =>
        {
            for (string? line; (line = proc.StandardOutput.ReadLine()) is not null;)
            {
                lock (run.Lines) run.Lines.Add(line);
                if (progress is not null)
                {
                    var m = PercentRegex().Match(line);
                    if (m.Success && int.TryParse(m.Groups["pct"].Value, out var pct))
                        progress(Math.Clamp(pct, 0, 100));
                }
                log?.Invoke(line);
            }
        });
        var stderr = Task.Run(() =>
        {
            for (string? line; (line = proc.StandardError.ReadLine()) is not null;)
            {
                lock (run.Lines) run.Lines.Add("! " + line);
                log?.Invoke("! " + line);
            }
        });

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(true); } catch { }
            run.TimedOut = true;
        }
        try { Task.WaitAll(new[] { stdout, stderr }, 5000); } catch { }
        ct.ThrowIfCancellationRequested();

        run.ExitCode = run.TimedOut || !proc.HasExited ? -1 : proc.ExitCode;
        return run;
    }

    /// <summary>Последняя строка, которую 7-Zip написал в stderr, — чтобы показывать пользователю настоящую причину, а не общую фразу.</summary>
    private static string LastError(EngineRun run)
    {
        var errors = run.Lines
            .Where(l => l.StartsWith("! ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        return errors.Length == 0 ? string.Empty : errors[^1];
    }

    /// <summary>
    /// Проверка ДО запуска 7z: 7-Zip для «не архива» отдаёт код 2 с невнятным сообщением,
    /// поэтому проверяем сами и говорим по-человечески. Сигнатура 7z — единственный честный признак.
    /// </summary>
    public static void EnsureLooksLikeArchive(string path)
    {
        if (Directory.Exists(path))
            throw new EngineException($"Это папка, а не архив: {Path.GetFileName(path)}");
        if (!File.Exists(path))
            throw new EngineException($"Файл не найден: {Path.GetFileName(path)} — возможно, он перемещён или удалён.");
        if (!Has7zSignature(path))
            throw new EngineException(
                $"Это не архив: {Path.GetFileName(path)}\n\n" +
                "В начале файла нет сигнатуры 7z, значит он не создан «Бетой», а просто переименован.");
    }

    private static void EnsureOk(EngineRun run, string what)
    {
        if (run.TimedOut)
            throw new EngineException($"7-Zip не закончил работу за 30 минут — операция прервана. Проблема при {what}.");
        if (run.ExitCode == 0) return;

        var detail = LastError(run);
        var tail = detail.Length == 0 ? string.Empty : $" 7-Zip написал: «{detail}»";
        switch (run.ExitCode)
        {
            case 1: throw new EngineException($"7-Zip закончил с предупреждениями (код 1) при {what}.{tail}");
            case 2: throw new EngineException($"7-Zip не смог выполнить операцию (код 2) при {what}.{tail}");
            case 7: throw new EngineException($"7-Zip не понял команду (код 7) при {what}.{tail}");
            default: throw new EngineException($"7-Zip вернул код {run.ExitCode} при {what}.{tail}");
        }
    }

    private static string? CommonParent(IReadOnlyList<string> dirs)
    {
        if (dirs.Count == 0) return null;
        var parts = dirs[0].TrimEnd('\\', '/').Split('\\', '/');
        for (var i = 1; i < dirs.Count; i++)
        {
            var other = dirs[i].TrimEnd('\\', '/').Split('\\', '/');
            var n = 0;
            while (n < parts.Length && n < other.Length &&
                   string.Equals(parts[n], other[n], StringComparison.OrdinalIgnoreCase)) n++;
            parts = parts.Take(n).ToArray();
            if (parts.Length == 0) return null;
        }
        // Диск на месте («C:») — иначе Path.Combine даст «C:имя» (относительно диска), а нам нужен «C:\».
        if (parts.Length == 1 && parts[0].Length == 2 && parts[0][1] == ':') parts = new[] { parts[0], "\\" };
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    /// <summary>Упаковать что угодно (файлы, папки, смесь) в архив 7z с нашим расширением .b3ta.</summary>
    public static EngineRun Pack(
        string archivePath,
        IReadOnlyList<string> items,
        int level,
        Action<int>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (items.Count == 0) throw new EngineException("Нечего упаковывать — не выбрано ни одного файла.");

        var dirOf = new List<string>(items.Count);
        foreach (var item in items)
        {
            var full = Path.GetFullPath(item);
            dirOf.Add(Directory.Exists(full) ? Directory.GetParent(full)!.FullName : Path.GetDirectoryName(full)!);
        }

        string? workDir = CommonParent(dirOf);
        var args = new List<string>
        {
            "a", "-t7z", $"-mx={Math.Clamp(level, 0, 9)}",
            "-bso1", "-bsp1", "-bb0", "-y", "-sccUTF-8",
            "--", Path.GetFullPath(archivePath)
        };

        foreach (var item in items)
        {
            var full = Path.GetFullPath(item).TrimEnd('\\', '/');
            if (workDir is null)
            {
                args.Add(full);                       // разные диски — единственный вариант
                continue;
            }
            var rel = Path.GetRelativePath(workDir, full);
            if (rel.StartsWith("..", StringComparison.Ordinal))
            {
                workDir = null;                       // не вышло — пересоберём списком (см. ниже)
                args.Add(full);
                continue;
            }
            args.Add(rel);
        }
        if (workDir is null) log?.Invoke("Файлы с разных дисков — пути внутри архива будут полными.");

        var parent = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        var run = Run(args, progress, log, workDir, ct: ct);
        EnsureOk(run, "упаковки");
        return run;
    }

    public static EngineRun Extract(
        string archivePath,
        string destDir,
        Action<int>? progress = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        EnsureLooksLikeArchive(archivePath);
        Directory.CreateDirectory(destDir);
        var run = Run(
            new[] { "x", "-y", $"-o{destDir}", "-bso1", "-bsp1", "-bb0", "-sccUTF-8", "--", Path.GetFullPath(archivePath) },
            progress, log, ct: ct);
        EnsureOk(run, "распаковки");
        return run;
    }

    /// <summary>Что внутри архива — список записей с размерами.</summary>
    public static List<EntryInfo> List(string archivePath)
    {
        // -ba убирает баннер, шапку архива и разделители — остаётся чистый поток блоков «пустая строка / Поле = Значение»
        EnsureLooksLikeArchive(archivePath);
        var run = Run(new[] { "l", "-slt", "-ba", "-bb0", "-sccUTF-8", "--", Path.GetFullPath(archivePath) });
        EnsureOk(run, "чтения списка файлов");

        var list = new List<EntryInfo>();
        string? path = null, size = null, packed = null, attrs = null, type = null;

        void Flush()
        {
            if (path is not null && type is null)   // type != null → это «шапка» самого архива, пропускаем
                list.Add(new EntryInfo(path, Num(size), Num(packed), IsFolder(attrs)));
            path = size = packed = attrs = type = null;
        }

        foreach (var raw in run.Lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("Path = ", StringComparison.Ordinal))
                // 7-Zip отдаёт пути с обратными слэшами — приводим к «/», как в ZIP
                path = line[7..].Replace('\\', '/');
            else if (line.StartsWith("Size = ", StringComparison.Ordinal)) size = line[7..];
            else if (line.StartsWith("Packed Size = ", StringComparison.Ordinal)) packed = line[14..];
            else if (line.StartsWith("Attributes = ", StringComparison.Ordinal)) attrs = line[13..];
            else if (line.StartsWith("Type = ", StringComparison.Ordinal)) type = line[7..];
        }
        Flush();
        return list;

        static long Num(string? s) => long.TryParse(s, out var v) ? v : -1;

        // Проверено на 7-Zip 23.01: признак папки — буква D в Attributes.
        // Поля «Folder = +» у НЕПУСТЫХ папок нет вообще, ориентироваться на него нельзя.
        static bool IsFolder(string? a) => a is not null && a.Contains('D');
    }

    /// <summary>Проверка целостности (аналог «Тестировать архив» в 7-Zip).</summary>
    public static EngineRun Test(string archivePath, CancellationToken ct = default)
    {
        EnsureLooksLikeArchive(archivePath);
        var run = Run(new[] { "t", "-bb0", "-sccUTF-8", "--", Path.GetFullPath(archivePath) }, ct: ct);
        EnsureOk(run, "проверки архива");
        return run;
    }

    /// <summary>Есть ли в начале файла сигнатура 7z — доказательство, что .b3ta это обычный 7z.</summary>
    public static bool Has7zSignature(string path)
    {
        byte[] signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[6];
            return fs.Read(head, 0, 6) == 6 && head.SequenceEqual(signature);
        }
        catch { return false; }
    }
}
