using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HppDonatApp.Services.Mvvm;
using Serilog;

namespace HppDonatApp.Services.Ai;

public interface IGenerativeAiService
{
    bool IsConfigured { get; }
    string ActiveModel { get; }
    Task<string> GenerateTextAsync(string systemInstruction, string userPrompt, CancellationToken cancellationToken = default);
    Task<T?> GenerateJsonAsync<T>(string systemInstruction, string userPrompt, CancellationToken cancellationToken = default);
}

public sealed class GeminiGenerativeAiService : IGenerativeAiService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingsService _settingsService;
    private readonly ILogger? _logger;

    private const string DefaultModel = "gemini-2.5-flash";
    private const string ApiKeySetting = "Ai.GeminiApiKey";
    private const string ModelSetting = "Ai.GeminiModel";
    private const string EndpointTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

    public GeminiGenerativeAiService(ISettingsService settingsService, ILogger? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());

    public string ActiveModel
    {
        get
        {
            var model = _settingsService.GetSetting(ModelSetting, DefaultModel);
            return string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        }
    }

    public async Task<string> GenerateTextAsync(string systemInstruction, string userPrompt, CancellationToken cancellationToken = default)
    {
        var raw = await CallGeminiAsync(systemInstruction, userPrompt, asJson: false, cancellationToken);
        return raw;
    }

    public async Task<T?> GenerateJsonAsync<T>(string systemInstruction, string userPrompt, CancellationToken cancellationToken = default)
    {
        var raw = await CallGeminiAsync(systemInstruction, userPrompt, asJson: true, cancellationToken);
        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("AI response did not contain valid JSON.");
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private async Task<string> CallGeminiAsync(string systemInstruction, string userPrompt, bool asJson, CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var endpoint = string.Format(EndpointTemplate, ActiveModel, apiKey);
        var payload = new GeminiGenerateRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts = new[] { new GeminiPart { Text = systemInstruction } }
            },
            Contents = new[]
            {
                new GeminiContent
                {
                    Parts = new[] { new GeminiPart { Text = userPrompt } }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = asJson ? 0.1m : 0.35m,
                ResponseMimeType = asJson ? "application/json" : "text/plain"
            }
        };

        using var response = await HttpClient.PostAsJsonAsync(endpoint, payload, JsonOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.Error("Gemini API error: {StatusCode} {Body}", response.StatusCode, responseText);
            throw new InvalidOperationException($"Gemini API request failed ({(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini returned no candidate response.");
        }

        var candidate = candidates[0];
        var text = candidate
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? string.Empty;
    }

    private string GetApiKey()
    {
        return _settingsService.GetSetting(ApiKeySetting, string.Empty).Trim();
    }

    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            return trimmed;
        }

        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var fenceEnd = trimmed.IndexOf("```", fenceStart + 3, StringComparison.Ordinal);
            if (fenceEnd > fenceStart)
            {
                var fenced = trimmed.Substring(fenceStart + 3, fenceEnd - fenceStart - 3).Trim();
                if (fenced.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    fenced = fenced.Substring(4).Trim();
                }

                if (fenced.StartsWith("{") || fenced.StartsWith("["))
                {
                    return fenced;
                }
            }
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            return trimmed.Substring(objectStart, objectEnd - objectStart + 1);
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return trimmed.Substring(arrayStart, arrayEnd - arrayStart + 1);
        }

        return string.Empty;
    }

    private sealed class GeminiGenerateRequest
    {
        [JsonPropertyName("systemInstruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public decimal Temperature { get; set; }

        [JsonPropertyName("responseMimeType")]
        public string ResponseMimeType { get; set; } = "text/plain";
    }
}
