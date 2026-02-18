using Microsoft.UI.Xaml;
using Serilog;
using HppDonatApp.Views;

namespace HppDonatApp;

/// <summary>
/// Main application window.
/// Serves as the root UI container for the entire application.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        // Set window properties
        this.Title = "HPP Donat Calculator - WinUI 3";

        // Set RecipeEditor as the default home screen.
        this.Content = new RecipeEditorPage();
        
        // Log window initialization
        var logger = Log.ForContext<MainWindow>();
        logger.Information("MainWindow initialized");
    }
}
