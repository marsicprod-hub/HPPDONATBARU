using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace HppDonatApp.Services.Mvvm;

/// <summary>
/// Base class for all view models in the application.
/// Provides property change notification and command infrastructure.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    protected readonly ILogger? Logger;
    private bool _isBusy;

    /// <summary>
    /// Gets or sets whether the view model is currently performing an operation.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Initializes the view model base class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics</param>
    protected ViewModelBase(ILogger? logger = null)
    {
        Logger = logger;
        Logger?.Debug("ViewModel initialized: {ViewModel}", GetType().Name);
    }

    /// <summary>
    /// Called when the view model is navigated to.
    /// Override to load data or initialize state.
    /// </summary>
    public virtual Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the view model is navigated away from.
    /// Override to save state or cleanup resources.
    /// </summary>
    public virtual Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Safely wraps an async operation with busy state and error handling.
    /// </summary>
    protected async Task SafeExecuteAsync(Func<Task> operation, string operationName = "Operation")
    {
        try
        {
            IsBusy = true;
            Logger?.Debug("Starting operation: {Operation}", operationName);
            await operation();
            Logger?.Information("Completed operation: {Operation}", operationName);
        }
        catch (Exception ex)
        {
            Logger?.Error(ex, "Error during operation: {Operation}", operationName);
            await ShowErrorAsync($"An error occurred during {operationName}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Safely wraps an async operation that returns a value.
    /// </summary>
    protected async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> operation, string operationName = "Operation")
    {
        try
        {
            IsBusy = true;
            Logger?.Debug("Starting operation: {Operation}", operationName);
            var result = await operation();
            Logger?.Information("Completed operation: {Operation}", operationName);
            return result;
        }
        catch (Exception ex)
        {
            Logger?.Error(ex, "Error during operation: {Operation}", operationName);
            await ShowErrorAsync($"An error occurred during {operationName}: {ex.Message}");
            return default;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows an error message to the user.
    /// Override in derived classes to implement actual UI feedback.
    /// </summary>
    protected virtual Task ShowErrorAsync(string message)
    {
        Logger?.Warning("Error message for user: {Message}", message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows a success message to the user.
    /// Override in derived classes to implement actual UI feedback.
    /// </summary>
    protected virtual Task ShowSuccessAsync(string message)
    {
        Logger?.Information("Success message for user: {Message}", message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Async variant of RelayCommand with async/await support.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly ILogger? _logger;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, ILogger? logger = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _logger = logger;
    }

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
            return false;

        return _canExecute == null || _canExecute();
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        try
        {
            _isExecuting = true;
            _logger?.Debug("Executing async command");
            await _execute();
            _logger?.Debug("Async command executed successfully");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error executing async command");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Async variant of RelayCommand that returns a value.
/// </summary>
public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly ILogger? _logger;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, ILogger? logger = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _logger = logger;
    }

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
            return false;

        var typedParam = parameter is T typed ? typed : default;
        return _canExecute == null || _canExecute(typedParam);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        try
        {
            _isExecuting = true;
            var typedParam = parameter is T typed ? typed : default;
            _logger?.Debug("Executing async command with parameter");
            await _execute(typedParam);
            _logger?.Debug("Async command executed successfully");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error executing async command");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Service for displaying notifications/messages to users.
/// </summary>
public interface INotificationService
{
    Task ShowNotificationAsync(string title, string message, NotificationType type = NotificationType.Information);
    Task<bool> ShowConfirmationAsync(string title, string message);
}

public enum NotificationType
{
    Information,
    Success,
    Warning,
    Error
}

/// <summary>
/// Dialog service for showing dialogs and getting user input.
/// </summary>
public interface IDialogService
{
    Task<string> ShowInputDialogAsync(string title, string prompt, string defaultValue = "");
    Task<bool> ShowConfirmationAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
}

/// <summary>
/// Service for navigation between views.
/// </summary>
public interface INavigationService
{
    bool CanGoBack { get; }
    Task NavigateToAsync(string viewName, object? parameter = null);
    Task GoBackAsync();
}

/// <summary>
/// Settings service for managing application settings.
/// </summary>
public interface ISettingsService
{
    T GetSetting<T>(string key, T defaultValue);
    void SetSetting<T>(string key, T value);
    void SaveSettings();
}

/// <summary>
/// Default implementation of settings service backed by a JSON file in LocalAppData.
/// </summary>
public class DefaultSettingsService : ISettingsService
{
    private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public DefaultSettingsService(ILogger? logger = null)
    {
        _logger = logger;
        _settingsFilePath = ResolveSettingsPath();
        LoadSettingsFromDisk();
        InitializeDefaults();
        SaveSettings();
    }

    private void InitializeDefaults()
    {
        SetDefault("Theme", "Light");
        SetDefault("Currency", "IDR");
        SetDefault("Language", "en-US");
        SetDefault("RoundingRule", "100");
        SetDefault("VAT", 0.10m);
        SetDefault("BatchesPerMonth", 4);
        SetDefault("Ai.GeminiModel", "gemini-2.5-flash");
        SetDefault("Ai.GeminiApiKey", string.Empty);
    }

    public T GetSetting<T>(string key, T defaultValue)
    {
        if (_settings.TryGetValue(key, out var value))
        {
            try
            {
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)value;
                }

                if (typeof(T) == typeof(int) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    return (T)(object)intValue;
                }

                if (typeof(T) == typeof(decimal) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    return (T)(object)decimalValue;
                }

                if (typeof(T) == typeof(bool) && bool.TryParse(value, out var boolValue))
                {
                    return (T)(object)boolValue;
                }

                var typedValue = JsonSerializer.Deserialize<T>(value, _jsonOptions);
                if (typedValue != null)
                {
                    return typedValue;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed parsing setting {Key}; using default value", key);
            }
        }

        return defaultValue;
    }

    public void SetSetting<T>(string key, T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _settings[key] = ConvertToStorageValue(value);
        _logger?.Debug("Setting updated: {Key}", key);
    }

    public void SaveSettings()
    {
        var json = JsonSerializer.Serialize(_settings, _jsonOptions);
        File.WriteAllText(_settingsFilePath, json);
        _logger?.Information("Settings saved to {Path}", _settingsFilePath);
    }

    private void LoadSettingsFromDisk()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return;
        }

        try
        {
            var raw = File.ReadAllText(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(raw, _jsonOptions);
            if (loaded == null)
            {
                return;
            }

            foreach (var entry in loaded)
            {
                _settings[entry.Key] = entry.Value;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed loading settings file, defaults will be used");
        }
    }

    private void SetDefault<T>(string key, T value)
    {
        if (_settings.ContainsKey(key))
        {
            return;
        }

        _settings[key] = ConvertToStorageValue(value);
    }

    private static string ConvertToStorageValue<T>(T value)
    {
        return value switch
        {
            string s => s,
            int i => i.ToString(CultureInfo.InvariantCulture),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            bool b => b.ToString(CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };
    }

    private static string ResolveSettingsPath()
    {
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(baseFolder, "HppDonatApp");
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.json");
    }
}
