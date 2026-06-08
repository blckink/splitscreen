using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SplitPlay.App.Diagnostics;
using SplitPlay.App.ViewModels;
using SplitPlay.App.Views;
using SplitPlay.Core.Abstractions;
using SplitPlay.Launch.InputIsolation;

namespace SplitPlay.App;

/// <summary>
/// Application entry point and composition root. Builds the DI container, wires up
/// the main window and starts the app. Keeping all wiring here means the rest of
/// the code never news-up its dependencies.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface any unhandled error instead of letting the app vanish silently.
        SetupGlobalExceptionHandling();

        var services = new ServiceCollection();
        AppBootstrapper.ConfigureServices(services);
        _provider = services.BuildServiceProvider();

        // Begin watching for controller connect/disconnect for the whole session.
        _provider.GetRequiredService<IGamepadService>().StartMonitoring();

        // Eagerly create the isolation manager so any proxy DLLs left in game
        // folders by a previously crashed session are restored right away.
        _provider.GetRequiredService<InputIsolationManager>();

        var window = _provider.GetRequiredService<MainWindow>();
        var shell = _provider.GetRequiredService<MainViewModel>();
        window.DataContext = shell;
        MainWindow = window;

        window.Show();
        shell.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Restore any game folders we shadowed with the XInput proxy. Folders whose
        // game is still running stay recorded and are restored on the next start.
        _provider?.GetService<InputIsolationManager>()?.RestoreAll();

        // Disposes singletons, including the gamepad service (stops its timer).
        _provider?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Routes unhandled exceptions from the UI thread, background threads and tasks
    /// to a single place: they are written to %AppData%/SplitPlay/crash.log and
    /// shown in a dialog. UI-thread exceptions are marked handled so a single bad
    /// action (e.g. opening one page) doesn't kill the whole app.
    /// </summary>
    private void SetupGlobalExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashReporter.Report(args.Exception, "UI thread");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashReporter.Report(args.ExceptionObject as Exception, "background thread");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReporter.Report(args.Exception, "task");
            args.SetObserved();
        };
    }
}
