using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BetaApp;

/// <summary>
/// Привязка своего расширения .b3ta в реестре — только в профиле пользователя (HKCU), без прав администратора.
/// На другие машины и на чужие .7z/.zip это никак не влияет.
/// </summary>
public static class Association
{
    public const string ProgId = "Beta.b3ta.Archive";

    /// <summary>Проводник кэширует ассоциации и иконки — без этого уведомления он продолжит показывать старое.</summary>
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private const int ShcneAssocChanged = 0x08000000;   // SHCNE_ASSOCCHANGED

    /// <summary>Путь к exe, записанный в реестр (null, если привязки нет).</summary>
    public static string? RegisteredExe()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        var command = key?.GetValue(null) as string;
        if (command is null) return null;

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>Привязка считается живой, только если записанный exe ещё существует (иначе Проводник скажет «программа не установлена»).</summary>
    public static bool IsRegistered()
    {
        var exe = RegisteredExe();
        return exe is not null && File.Exists(exe);
    }

    /// <summary>
    /// Настоящий ProgID, которым на этой машине открывается чужое расширение. Имена РАЗНЫЕ
    /// на разных машинах: у 7-Zip это «7-Zip.7z», а не «7-Zip». Нет такого расширения — null.
    /// </summary>
    private static string? ProgIdOfExtension(string ext)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(ext);
            return key?.GetValue(null) as string;
        }
        catch (Exception) { return null; }   // нет доступа к HKCR — просто не добавляем
    }

    /// <summary>Программа, выбранная пользователем с галочкой «Всегда». Пока она есть, Windows игнорирует нашу привязку.</summary>
    public static string? UserChoiceProgId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{Engine.Extension}\UserChoice");
            return key?.GetValue("ProgId") as string;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Не даём записать в реестр не нашу программу: при `dotnet Бета.dll` в ProcessPath лежит
    /// путь к dotnet.exe, и .b3ta стал бы открываться через dotnet.
    /// </summary>
    public static bool IsOurExecutable(string exePath)
    {
        var own = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        var name = Path.GetFileNameWithoutExtension(exePath);
        return !string.IsNullOrEmpty(own) && string.Equals(name, own, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Привязать .b3ta к программе. Возвращает ProgID'ы чужих программ, добавленных в «Открыть с помощью».</summary>
    public static string[] Register(string exePath)
    {
        exePath = Path.GetFullPath(exePath);
        var added = new List<string>();

        using (var ext = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Engine.Extension}"))
        {
            ext.SetValue(null, ProgId);                    // двойной клик → наша программа
            ext.SetValue("PerceivedType", "archive");      // Проводник покажет «Архив»
            using var openWith = ext.CreateSubKey("OpenWithProgids");
            foreach (var probe in new[] { ".7z", ".rar", ".zip" })
            {
                var id = ProgIdOfExtension(probe);
                if (string.IsNullOrEmpty(id) || id == ProgId) continue;
                openWith.SetValue(id, string.Empty);
                added.Add(id);
            }
        }

        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue(null, $"Архив Бета ({Engine.Extension}) — формат 7z, открывается 7-Zip и WinRAR");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exePath}\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        NotifyExplorer();
        return added.ToArray();
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Engine.Extension}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);

        // MRU Проводника живёт отдельно от HKCU\Software\Classes — там может остаться мёртвая запись
        try
        {
            using var mr = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{Engine.Extension}\OpenWithProgids",
                writable: true);
            mr?.DeleteValue(ProgId, throwOnMissingValue: false);
        }
        catch (Exception) { /* кэш Проводника — не критично */ }

        NotifyExplorer();
    }

    /// <summary>Сбросить «Всегда»-выбор Проводника (Windows пересоздаст ключ сам). Пока он есть, наша привязка игнорируется.</summary>
    public static bool ResetUserChoice()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{Engine.Extension}\UserChoice",
                throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception) { return false; }   // ключ защищён политикой — тогда предупреждаем пользователя
    }

    /// <summary>Кем сейчас назначен .b3ta (для подписи в окне программы).</summary>
    public static string DefaultHint()
    {
        try
        {
            return Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Engine.Extension}")?.GetValue(null) as string
                   ?? "не привязано";
        }
        catch (Exception) { return "не удалось прочитать реестр"; }
    }

    private static void NotifyExplorer() =>
        SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
}
