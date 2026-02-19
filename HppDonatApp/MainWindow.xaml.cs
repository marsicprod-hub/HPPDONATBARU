using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using HppDonatApp.Views;
using WinUIEx;

namespace HppDonatApp;

/// <summary>
/// Main application window.
/// Serves as the root UI container for the entire application.
/// </summary>
public sealed partial class MainWindow : WindowEx
{
    private readonly RecipeEditorPage _recipeEditorPage = new();
    private readonly SettingsPage _settingsPage = new();
    private readonly ThemeListener _themeListener;

    public MainWindow()
    {
        this.InitializeComponent();

        // Set window properties
        _themeListener = new ThemeListener(DispatcherQueue);
        this.Closed += (_, _) => _themeListener.Dispose();

        UpdateWindowTitle();
        this.SetWindowSize(1460, 940);
        this.CenterOnScreen();

        RootNavigationView.SelectedItem = CalculatorNavItem;
        RootFrame.Content = _recipeEditorPage;
        
        // Log window initialization
        var logger = Log.ForContext<MainWindow>();
        logger.Information("MainWindow initialized");
    }

    private void UpdateWindowTitle()
    {
        Title = $"HPP Donat Enterprise - {_themeListener.CurrentThemeName}";
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selected = args.SelectedItem as NavigationViewItem;
        var tag = selected?.Tag?.ToString();

        RootFrame.Content = tag switch
        {
            "settings" => _settingsPage,
            _ => _recipeEditorPage
        };
    }
}
