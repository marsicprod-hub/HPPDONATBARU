using System;
using System.Threading.Tasks;
using System.Windows.Input;
using HppDonatApp.Services.Ai;
using HppDonatApp.Services.Mvvm;
using Serilog;

namespace HppDonatApp.Services.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IGenerativeAiService _generativeAiService;

    private string _geminiApiKey = string.Empty;
    private string _geminiModel = "gemini-2.5-flash";
    private string _statusMessage = string.Empty;
    private bool _isAiConfigured;

    public SettingsViewModel(
        ISettingsService settingsService,
        IGenerativeAiService generativeAiService,
        ILogger? logger = null) : base(logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _generativeAiService = generativeAiService ?? throw new ArgumentNullException(nameof(generativeAiService));

        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, logger: Logger);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, logger: Logger);

        LoadFromSettings();
    }

    public string GeminiApiKey
    {
        get => _geminiApiKey;
        set => SetProperty(ref _geminiApiKey, value);
    }

    public string GeminiModel
    {
        get => _geminiModel;
        set => SetProperty(ref _geminiModel, string.IsNullOrWhiteSpace(value) ? "gemini-2.5-flash" : value.Trim());
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsAiConfigured
    {
        get => _isAiConfigured;
        set => SetProperty(ref _isAiConfigured, value);
    }

    public ICommand SaveSettingsCommand { get; }
    public ICommand TestConnectionCommand { get; }

    private void LoadFromSettings()
    {
        GeminiApiKey = _settingsService.GetSetting("Ai.GeminiApiKey", string.Empty);
        GeminiModel = _settingsService.GetSetting("Ai.GeminiModel", "gemini-2.5-flash");
        IsAiConfigured = !string.IsNullOrWhiteSpace(GeminiApiKey);
        StatusMessage = IsAiConfigured
            ? "Gemini API key sudah tersimpan."
            : "Masukkan Gemini API key lalu simpan.";
    }

    private async Task SaveSettingsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            _settingsService.SetSetting("Ai.GeminiApiKey", GeminiApiKey.Trim());
            _settingsService.SetSetting("Ai.GeminiModel", GeminiModel.Trim());
            _settingsService.SaveSettings();

            IsAiConfigured = !string.IsNullOrWhiteSpace(GeminiApiKey);
            StatusMessage = IsAiConfigured
                ? "Settings tersimpan. AI siap dipakai."
                : "Settings tersimpan, tapi API key masih kosong.";

            await Task.CompletedTask;
        }, "Saving AI settings");
    }

    private async Task TestConnectionAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(GeminiApiKey))
            {
                StatusMessage = "Isi API key dulu sebelum test koneksi.";
                return;
            }

            _settingsService.SetSetting("Ai.GeminiApiKey", GeminiApiKey.Trim());
            _settingsService.SetSetting("Ai.GeminiModel", GeminiModel.Trim());
            _settingsService.SaveSettings();

            var response = await _generativeAiService.GenerateTextAsync(
                "You are a concise assistant. Reply with one short sentence.",
                "Say: connection ok.");

            IsAiConfigured = true;
            StatusMessage = $"Koneksi berhasil: {response}";
        }, "Testing Gemini connection");
    }
}
