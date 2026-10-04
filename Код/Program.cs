using System.Text;

namespace BetaApp;

/// <summary>Точка входа. Без аргументов — окно программы, с командой — режим консоли для тестов и скриптов.</summary>
internal static class Program
{
    private const int AttachParentProcess = -1;

    private static readonly string[] ConsoleCommands =
    [
        "--pack", "--extract", "--list", "--test", "--selftest", "--uitest",
        "--help", "-h", "-?", "/?", "/help"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && Array.Exists(ConsoleCommands, c => string.Equals(c, args[0], StringComparison.OrdinalIgnoreCase)))
            return RunConsole(args);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash.Report(e.ExceptionObject as Exception);
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args));
            return 0;
        }
        catch (Exception ex)
        {
            Crash.Report(ex);
            return 1;
        }
    }

    private static int RunConsole(string[] args)
    {
        // Если у процесса уже есть родительская консоль (запуск из cmd/PowerShell) — присоединяемся
        // к ней, иначе AllocConsole открыл бы второе окно, которое мигнёт и исчезнет.
        if (!Console.IsOutputRedirected && !Native.AttachConsole(AttachParentProcess)) Native.AllocConsole();
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* консоль может не поддержать */ }
        try { return Cli.Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ОШИБКА: " + ex.Message);
            return 2;
        }
    }
}

internal static class Native
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(int processId);
}

/// <summary>Аварийный отчёт: окно сразу не показываем, но пишем в файл у пользователя.</summary>
internal static class Crash
{
    public static void Report(Exception? ex)
    {
        if (ex is null) return;
        var log = Path.Combine(Path.GetTempPath(), "beta_crash.log");
        try
        {
            File.AppendAllText(log, $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\r\n{ex}\r\n\r\n");
            MessageBox.Show(
                $"Случилась ошибка:\n\n{ex.Message}\n\nПодробности записаны сюда:\n{log}",
                "Бета", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception) { /* если и это не выходит — молча, чтобы не зациклиться */ }
    }
}

internal static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}
