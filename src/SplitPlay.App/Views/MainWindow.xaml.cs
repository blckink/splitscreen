using System.Windows;
using SplitPlay.App.ViewModels;

namespace SplitPlay.App.Views;

/// <summary>
/// Code-behind for the shell window. It is intentionally thin: only the window
/// caption buttons (minimize / maximize-restore / close) live here, since those
/// operate directly on the <see cref="Window"/> and have no place in a view model.
/// </summary>
public partial class MainWindow : Window
{
    // Segoe MDL2 Assets glyphs for the maximize vs. restore states.
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        StateChanged += OnWindowStateChanged;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaxRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Swaps the maximize/restore glyph and tooltip to match the state.</summary>
    private void OnWindowStateChanged(object? sender, System.EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaxRestoreButton.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        MaxRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";
    }
}
