using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
}
