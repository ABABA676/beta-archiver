using System.Text;

namespace BetaApp;

/// <summary>Точка входа. Без аргументов — окно программы, с «--командой» — режим консоли для тестов и скриптов.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
        {
            // Консольный режим: «Бета.exe --pack …», «--selftest». Из Проводника окна нет — выделим консоль.
            if (!Console.IsOutputRedirected) Native.AllocConsole();
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* консоль может не поддержать */ }
            return Cli.Run(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
        return 0;
    }
}

internal static class Native
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool AllocConsole();
}

internal static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}
