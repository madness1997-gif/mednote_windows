using MedNote.Windows.App.Infrastructure;
using Microsoft.UI.Xaml;

namespace MedNote.Windows.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            WriteStartupCrash("App.InitializeComponent", exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            RenderProbe.Initialize();
            var startupDocumentPath = Environment.GetCommandLineArgs()
                .Skip(1)
                .Select(argument => argument.Trim().Trim('"'))
                .FirstOrDefault(argument => argument.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(startupDocumentPath) && !string.IsNullOrWhiteSpace(args.Arguments))
            {
                startupDocumentPath = args.Arguments.Trim().Trim('"');
            }

            _window = new MainWindow(startupDocumentPath);
            _window.Activate();
        }
        catch (Exception exception)
        {
            WriteStartupCrash("App.OnLaunched", exception);
            throw;
        }
    }

    private static string StartupCrashPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MedNote Reader",
        "startup-crash.log");

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs args) =>
        WriteStartupCrash("AppDomain.UnhandledException", args.ExceptionObject);

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) =>
        WriteStartupCrash("Application.UnhandledException", args.Exception);

    private static void WriteStartupCrash(string source, object? error)
    {
        try
        {
            var path = StartupCrashPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash diagnostics must never mask the original startup failure.
        }
    }
}
