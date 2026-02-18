using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HppDonatApp.Services.ViewModels;

namespace HppDonatApp.Views;

public sealed partial class RecipeEditorPage : Page
{
    private readonly RecipeEditorViewModel? _viewModel;

    public RecipeEditorPage()
    {
        this.InitializeComponent();

        var app = Application.Current as App;
        _viewModel = app?.Services?.GetService<RecipeEditorViewModel>();

        if (_viewModel != null)
        {
            DataContext = _viewModel;
            Loaded += OnLoaded;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.OnNavigatedToAsync();
        }
    }
}
