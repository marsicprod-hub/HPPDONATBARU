using System;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Animations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HppDonatApp.Services.ViewModels;

namespace HppDonatApp.Views;

public sealed partial class RecipeEditorPage : Page
{
    private readonly RecipeEditorViewModel? _viewModel;
    private bool _hasPlayedEntranceAnimation;

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

        await PlayEntranceAnimationAsync();
    }

    private async Task PlayEntranceAnimationAsync()
    {
        if (_hasPlayedEntranceAnimation)
        {
            return;
        }

        _hasPlayedEntranceAnimation = true;
        await AnimationBuilder
            .Create()
            .Opacity(1d, 0d, duration: TimeSpan.FromMilliseconds(320))
            .Translation(Axis.Y, 0d, 18d, duration: TimeSpan.FromMilliseconds(380))
            .StartAsync(RootContent);
    }
}
