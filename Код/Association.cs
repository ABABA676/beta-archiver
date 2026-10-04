using Microsoft.Win32;

namespace BetaApp;

/// <summary>
/// Привязка своего расширения .b3ta в реестре — только в профиле пользователя (HKCU), без прав администратора.
/// На другие машины и на чужие .7z/.zip это никак не влияет.
/// </summary>
public static class Association
{
    public const string ProgId = "Beta.b3ta.Archive";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return key?.GetValue(null) is string s && s.Contains("Бета", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Привязать .b3ta к программе и показать 7-Zip/WinRAR в «Открыть с помощью».</summary>
    public static void Register(string exePath)
    {
        exePath = Path.GetFullPath(exePath);

        using (var ext = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Engine.Extension}"))
        {
            if (ext is null) throw new InvalidOperationException("не создался ключ .b3ta");
            ext.SetValue(null, ProgId);                  // двойной клик → наша программа
            ext.SetValue("PerceivedType", "archive");    // Проводник покажет архив
            using var openWith = ext.CreateSubKey("OpenWithProgids");
            openWith?.SetValue("7-Zip", string.Empty);   // 7-Zip в «Открыть с помощью»
            openWith?.SetValue("WinRAR", string.Empty);  // WinRAR в «Открыть с помощью»
        }

        using var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}")
            ?? throw new InvalidOperationException("не создался ключ программы");
        prog.SetValue(null, $"Архив Бета ({Engine.Extension}) — формат 7z, открывается 7-Zip и WinRAR");

        using (var icon = prog.CreateSubKey("DefaultIcon")) icon?.SetValue(null, $"{exePath},0");
        using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd?.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Engine.Extension}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
    }

    /// <summary>Кем сейчас назначен .b3ta (для подписи в окне программы).</summary>
    public static string DefaultHint()
    {
        using var ext = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Engine.Extension}");
        return ext?.GetValue(null) as string ?? "не привязан";
    }
}
