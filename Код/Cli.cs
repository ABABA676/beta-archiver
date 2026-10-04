using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BetaApp;

/// <summary>
/// Режим командной строки — для тестов, скриптов и проверки сборки:
///   Бета.exe --pack &lt;архив.b3ta&gt; &lt;файл/папка…&gt;
///   Бета.exe --extract &lt;архив.b3ta&gt; &lt;папку&gt;
///   Бета.exe --list &lt;архив.b3ta&gt;
///   Бета.exe --test &lt;архив.b3ta&gt;
///   Бета.exe --selftest        ← самопроверка «поехали ли мы вообще»
/// </summary>
internal static class Cli
{
    public static int Run(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "--pack" => Pack(args[1..]),
                "--extract" => Extract(args[1..]),
                "--list" => ListCmd(args[1..]),
                "--test" => TestCmd(args[1..]),
                "--selftest" => SelfTest(),
                "--uitest" => UiTest(),
                "--help" or "-h" or "/?" => Help(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ОШИБКА: " + ex.Message);
            return 2;
        }
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Не понимаю команду «{cmd}». Набери --help.");
        return 2;
    }

    private static int Help()
    {
        Console.WriteLine($"""
            Бета {AppInfo.Version} — архиватор с расширением {Engine.Extension}
            Движок: {Engine.ExePath}{(Engine.Available ? "" : "  [НЕ НАЙДЕН]")}

            Команды:
              --pack [-mx=N] <архив{Engine.Extension}> <файл|папка…>   упаковать (обычный 7z внутри)
              --extract <архив{Engine.Extension}> <папку>      распаковать
              --list <архив{Engine.Extension}>                 что внутри
              --test <архив{Engine.Extension}>                 проверить целостность
              --selftest                                       полная самопроверка движка и ошибок
              --uitest                                         проверка, что окно создаётся
              -h, /?                                           эта справка
            """);
        return 0;
    }

    private static int Pack(string[] a)
    {
        var level = 5;
        // ВНИМАНИЕ: сюда уже пришли аргументы без имени команды (Cli зовёт Pack(args[1..])),
        // поэтому Skip(1) здесь был бы ошибкой — он молча отбрасывал первый аргумент,
        // и «--pack -mx=0 арх файл» уходил в 7z с уровнем по умолчанию.
        var rest = new List<string>(a);

        // -mx может стоять где угодно: раньше он распознавался только первым аргументом, и команда
        // «--pack -mx=9 out.b3ta файл» молча создавала архив с именем «-mx=9», а уровень оставался 5.
        rest.RemoveAll(x =>
        {
            if (!x.StartsWith("-mx", StringComparison.OrdinalIgnoreCase)) return false;
            // string.TrimStart(params char[]) — у ReadOnlySpan<char> такой перегрузки нет, только один символ
            var digits = x.Length > 3 ? x[3..].TrimStart('=', ' ') : string.Empty;
            if (!int.TryParse(digits, out var parsed)) return false;
            level = Math.Clamp(parsed, 0, 9);
            return true;
        });

        if (rest.Count < 2)
        {
            Console.Error.WriteLine("Нужно: --pack [-mx=N] <архив> <файл|папка…>");
            return 2;
        }

        var archive = rest[0];
        var items = rest.Skip(1).ToList();
        var run = Engine.Pack(archive, items, level, p => Console.Write($"\r  {p,3}% "), Console.WriteLine);
        Console.WriteLine($"\nГотово: {archive} ({EntryInfo.Human(new FileInfo(archive).Length)}), " +
                          $"уровень {level}, строк в выводе 7-Zip: {run.Lines.Count}");
        return 0;
    }

    private static int Extract(string[] a)
    {
        if (a.Length < 2) { Console.Error.WriteLine("Нужно: --extract <архив> <папку>"); return 2; }
        Engine.Extract(a[0], a[1], p => Console.Write($"\r  {p,3}% "), Console.WriteLine);
        Console.WriteLine($"\nГотово: {a[1]}");
        return 0;
    }

    private static int ListCmd(string[] a)
    {
        if (a.Length < 1) { Console.Error.WriteLine("Нужно: --list <архив>"); return 2; }
        foreach (var e in Engine.List(a[0]))
            Console.WriteLine($"{(e.IsFolder ? "[папка]" : "        ")} {e.SizeText,10}  {e.Path}");
        return 0;
    }

    private static int TestCmd(string[] a)
    {
        if (a.Length < 1) { Console.Error.WriteLine("Нужно: --test <архив>"); return 2; }
        Engine.Test(a[0]);
        Console.WriteLine("Архив целый.");
        return 0;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>
    /// Проверка интерфейса без человека: окно должно создаться, показаться и закрыться без исключений.
    /// Ловит ошибки вёрстки, которые обычная самопроверка движка не видит.
    /// </summary>
    private static int UiTest()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm([]);
            form.Show();
            Application.DoEvents();
            Thread.Sleep(400);
            Application.DoEvents();

            var buttons = Walk(form).OfType<Button>()
                .Select(b => $"{b.Text} [{(b.Enabled ? "вкл" : "выкл")}]")
                .Distinct()
                .ToArray();
            Console.WriteLine($"  OK    окно создалось: «{form.Text}» {form.ClientSize.Width}x{form.ClientSize.Height}");
            Console.WriteLine($"  OK    кнопки в окне: {string.Join(", ", buttons)}");
            form.Close();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FAIL  окно не создалось:");
            Console.WriteLine(ex);
            return 1;
        }

        static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Walk(child)) yield return nested;
            }
        }
    }

    private static int SelfTest()
    {
        var pass = 0;
        var fail = 0;

        void Check(string name, bool ok, string? detail = null)
        {
            if (ok) { pass++; Console.WriteLine($"  OK    {name}"); }
            else { fail++; Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : " — " + detail)}"); }
        }

        // Вызывает Engine.List и ждёт понятную ошибку вместо «код 2» от 7-Zip
        static bool TryFails(string path, out string message)
        {
            try { Engine.List(path); message = string.Empty; return false; }
            catch (EngineException ex) { message = ex.Message; return true; }
        }

        // Создаёт архив НЕ нашего формата (zip/gz) прямо через 7z — для проверки совместимости
        static void RunZip(string command, string archive, params string[] items)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Engine.ExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(command);
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-bb0");
            psi.ArgumentList.Add("-sccUTF-8");
            psi.ArgumentList.Add(archive);
            foreach (var i in items) psi.ArgumentList.Add(i);
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit();
        }

        Console.WriteLine($"Бета {AppInfo.Version} — самопроверка");
        Console.WriteLine($"движок: {Engine.ExePath} — {(Engine.Available ? "найден" : "НЕ НАЙДЕН")}");
        if (!Engine.Available)
        {
            Console.WriteLine("итог: 0 OK / 1 FAIL (нет 7z.exe — установи 7-Zip)");
            return 1;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "beta_selftest_" + Guid.NewGuid().ToString("N")[..8]);
        var srcRoot = Path.Combine(tmp, "Исходные данные");
        var nested = Path.Combine(srcRoot, "вложенная");
        Directory.CreateDirectory(nested);

        try
        {
            // 1. данные: кириллица в имени папки, файла и внутри текста + содержимое, которое НЕ сожмётся в ноль
            var rnd = new Random(20261004);
            var sb = new StringBuilder();
            for (var i = 0; i < 5000; i++)
                sb.Append((char)('А' + rnd.Next(26))).Append((char)('а' + rnd.Next(26))).Append((char)('0' + rnd.Next(10))).Append(' ');
            var payload = sb.ToString();

            var file = Path.Combine(nested, "проверка текста.txt");
            File.WriteAllText(file, payload, new UTF8Encoding(false));
            var sha = Sha256(file);
            var size = new FileInfo(file).Length;

            var bigFile = Path.Combine(srcRoot, "картинка.bin");
            var bytes = new byte[2 * 1024 * 1024];
            rnd.NextBytes(bytes);
            File.WriteAllBytes(bigFile, bytes);

            // 2. упаковка
            var arc = Path.Combine(tmp, $"проверка{Engine.Extension}");
            Engine.Pack(arc, new[] { srcRoot }, 5);
            Check("архив создан", File.Exists(arc), arc);
            Check("внутри — настоящий 7z (сигнатура)", Engine.Has7zSignature(arc));

            // 3. список
            var list = Engine.List(arc);
            var textEntry = list.FirstOrDefault(e => e.Path.EndsWith("проверка текста.txt", StringComparison.OrdinalIgnoreCase));
            Check("список содержит наш файл", textEntry is not null, string.Join(" | ", list.Take(5).Select(e => e.Path)));
            Check("размер в списке совпал с исходником", textEntry is not null && textEntry.Size == size,
                textEntry is null ? "нет записи" : $"{textEntry.Size} != {size}");
            // 7z не помечает непустые папки — папка видна по общему началу путей
            var roots = list.Select(e => e.Path.Split('/')[0]).Distinct().ToList();
            Check("структура папок сохранена (общий корень)", roots.Count == 1 && roots[0] == "Исходные данные",
                string.Join(" | ", roots));
            Check("в архиве оба файла",
                new[] { "проверка текста.txt", "картинка.bin" }
                    .All(name => list.Any(e => e.Path.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase))),
                string.Join(" | ", list.Select(e => e.Path)));
            // 7-Zip 23.01: у непустых папок нет «Folder = +», признак папки — буква D в Attributes
            Check("папки помечены правильно (Attributes = D, у папок Size = 0)",
                list.Count(e => e.IsFolder) == 2
                && list.Where(e => e.IsFolder).All(e => e.Size == 0)
                && list.Where(e => !e.IsFolder).All(e => e.Size > 0),
                string.Join(" | ", list.Select(e => $"{e.Path}{(e.IsFolder ? " [папка]" : "")} size={e.Size}")));

            // 4. распаковка
            var outDir = Path.Combine(tmp, "Распаковка");
            Engine.Extract(arc, outDir);
            var extracted = Path.Combine(outDir, "Исходные данные", "вложенная", "проверка текста.txt");
            Check("файл на месте после распаковки", File.Exists(extracted), extracted);

            // 5. содержимое совпало побайтно — это и есть «без потери качества»
            if (File.Exists(extracted))
            {
                Check("SHA-256 совпал", Sha256(extracted) == sha);
                Check("размер совпал", new FileInfo(extracted).Length == size);
                Check("текст совпал посимвольно", File.ReadAllText(extracted) == payload);
            }
            var extractedBin = Path.Combine(outDir, "Исходные данные", "картинка.bin");
            Check("двоичный файл 2 МБ совпал", File.Exists(extractedBin) && Sha256(extractedBin) == Sha256(bigFile));

            // 6. 7-Zip считает наш .b3ta целым
            Check("7z t — архив без ошибок", Engine.Test(arc).ExitCode == 0);

            // 7. расширение не важно: тот же файл под другим именем читается
            var renamed = Path.Combine(tmp, "чужая_имя.xyz");
            File.Copy(arc, renamed);
            Check("файл с чужим расширением читается как 7z", Engine.List(renamed).Count == list.Count);
            var renamedOut = Path.Combine(tmp, "Распаковка2");
            Engine.Extract(renamed, renamedOut);
            Check("…и распаковывается", File.Exists(Path.Combine(renamedOut, "Исходные данные", "вложенная", "проверка текста.txt")));

            // 8. уровни сжатия реально различаются
            var arc0 = Path.Combine(tmp, $"без сжатия{Engine.Extension}");
            var arc9 = Path.Combine(tmp, $"максимум{Engine.Extension}");
            Engine.Pack(arc0, new[] { srcRoot }, 0);
            Engine.Pack(arc9, new[] { srcRoot }, 9);
            var len0 = new FileInfo(arc0).Length;
            var len9 = new FileInfo(arc9).Length;
            Check("уровень 9 жмёт не хуже уровня 0", len9 <= len0, $"{len9} > {len0}");

            // 9. прогресс реально приходит
            var seen = new List<int>();
            Engine.Pack(Path.Combine(tmp, $"прогресс{Engine.Extension}"), new[] { srcRoot }, 1, seen.Add);
            Check("прогресс приходит", seen.Count > 0, $"событий: {seen.Count}");

            // 10. «не архив» распознаём сами ДО запуска 7-Zip и объясняем по-человечески:
            //     на такие файлы 7-Zip отвечает кодом 2 и невнятным «Cannot open the file as archive»
            var junk = Path.Combine(tmp, $"мусор{Engine.Extension}");
            File.WriteAllBytes(junk, [1, 2, 3, 4, 5, 6, 7, 8]);
            Check("у мусора нет сигнатуры 7z", !Engine.Has7zSignature(junk));
            Check("мусорный .b3ta → «это не архив», а не «код 2»",
                TryFails(junk, out var junkMsg) && junkMsg.Contains("не архив"), junkMsg);
            var fakeDir = Path.Combine(tmp, $"папка{Engine.Extension}");
            Directory.CreateDirectory(fakeDir);
            Check("папка с расширением .b3ta → «это папка»",
                TryFails(fakeDir, out var dirMsg) && dirMsg.Contains("папка"), dirMsg);
            Check("несуществующий файл → «не найден»",
                TryFails(Path.Combine(tmp, "нет-такого.b3ta"), out var missMsg) && missMsg.Contains("не найден"), missMsg);

            // 11. Регрессия: чужие форматы 7z нельзя рубить по сигнатуре 7z — «Что внутри» должно работать и на .zip
            var zip = Path.Combine(tmp, "проверка.zip");
            RunZip("a", zip, Path.Combine(srcRoot, "картинка.bin"));
            Check("чужой формат .zip проходит проверку (не рубим по сигнатуре 7z)",
                File.Exists(zip) && Engine.Has7zSignature(zip) == false && Engine.List(zip).Count > 0);
            var gz = Path.Combine(tmp, "проверка.txt.gz");
            RunZip("a", gz, Path.Combine(srcRoot, "картинка.bin"));
            Check("чужой формат .gz тоже читается", !gz.Contains(".b3ta") && Engine.List(gz).Count > 0);

            // 11. отмена: токен отменён до старта — 7z должен быть убит, а программа — выйти без зависания
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var cancelled = false;
                var zombie = false;
                try
                {
                    Engine.Pack(Path.Combine(tmp, $"отмена{Engine.Extension}"), new[] { srcRoot }, 1, ct: cts.Token);
                }
                catch (OperationCanceledException) { cancelled = true; }
                catch (EngineException) { /* отмена не успела сработать — тоже не страшно */ }
                try
                {
                    zombie = Process.GetProcessesByName("7z").Length > 0;
                }
                catch { /* не смогли проверить процессы */ }
                Check("отмена отрабатывает (без OperationCanceledException — отмена не сработала)", cancelled);
                Check("процессов 7z не осталось", !zombie);
            }

            Console.WriteLine();
            Console.WriteLine($"итог: {pass} OK / {fail} FAIL");
            return fail == 0 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* файл открыт Проводником — удалим в следующий раз */ }
        }
    }
}
