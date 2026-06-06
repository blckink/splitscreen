using Microsoft.Extensions.DependencyInjection;
using SplitPlay.App.Services;
using SplitPlay.App.ViewModels;
using SplitPlay.App.Views;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Services;
using SplitPlay.Input;
using SplitPlay.Launch;
using SplitPlay.Launch.InputIsolation;
using SplitPlay.Steam;

namespace SplitPlay.App;

/// <summary>
/// Registers every service and view model with the DI container. This is the one
/// place that knows which concrete type implements each abstraction, so swapping
/// an implementation (e.g. the stub launch engine for the real one) is a one-line
/// change here.
/// </summary>
public static class AppBootstrapper
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // --- Feature services (one instance for the app lifetime) ---
        services.AddSingleton<ISteamLibraryScanner, SteamLibraryScanner>();
        services.AddSingleton<IGameArtworkProvider, SteamArtworkProvider>();
        services.AddSingleton<IGameProfileStore, JsonGameProfileStore>();
        services.AddSingleton<IDisplayService, DisplayService>();
        services.AddSingleton<ISplitLayoutCalculator, SplitLayoutCalculator>();
        services.AddSingleton<IGamepadService, XInputGamepadService>();

        // Controller isolation (XInput proxy install/restore). Singleton so its
        // restore-on-startup runs once and the app can restore on exit.
        services.AddSingleton<InputIsolationManager>();

        // The launch engine. RealLaunchEngine launches the game (with a test-window
        // fallback), isolates controller input per window and places the windows
        // into the split. StubLaunchEngine remains available as a no-op preview.
        services.AddSingleton<ILaunchEngine, RealLaunchEngine>();

        // --- Shell + navigation ---
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IShellNavigator>(sp => sp.GetRequiredService<MainViewModel>());

        // --- Pages ---
        // Persistent pages are singletons; the detail page is transient so each
        // game gets a clean view model.
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<ControlsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<GameDetailViewModel>();

        // --- Windows ---
        services.AddSingleton<MainWindow>();
    }
}
