using System;
using CommunityToolkit.WinUI.Animations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HppDonatApp.Services.ViewModels;

namespace HppDonatApp.Views;

public sealed partial class SettingsPage : Page
{
    private bool _hasPlayedEntranceAnimation;

    public SettingsPage()
    {
        this.InitializeComponent();

        var app = Application.Current as App;
        var viewModel = app?.Services?.GetService<SettingsViewModel>();
        if (viewModel != null)
        {
            DataContext = viewModel;
        }

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasPlayedEntranceAnimation)
        {
            return;
        }

        _hasPlayedEntranceAnimation = true;
        await AnimationBuilder
            .Create()
            .Opacity(1d, 0d, duration: TimeSpan.FromMilliseconds(250))
            .Translation(Axis.Y, 0d, 12d, duration: TimeSpan.FromMilliseconds(300))
            .StartAsync(RootSettingsContent);
    }
}
