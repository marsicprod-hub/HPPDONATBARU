using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using HppDonatApp.Core.Models;
using HppDonatApp.Services.Mvvm;

namespace HppDonatApp.Services.ViewModels;

public partial class RecipeEditorViewModel
{
    private readonly List<CalculationHistoryEntry> _calculationHistory = new();

    private bool _isAiConfigured;
    private string _aiImportRawText = string.Empty;
    private string _aiWhatIfPrompt = string.Empty;
    private string _aiCopilotQuestion = string.Empty;
    private string _aiPriceRecommendation = string.Empty;
    private string _aiWhatIfSummary = string.Empty;
    private string _aiAnomalyReport = string.Empty;
    private string _aiForecastReport = string.Empty;
    private string _aiCopilotAnswer = string.Empty;

    public ICommand AiSmartImportCommand { get; private set; } = null!;
    public ICommand AiNormalizeIngredientsCommand { get; private set; } = null!;
    public ICommand AiPriceRecommendationCommand { get; private set; } = null!;
    public ICommand AiWhatIfCommand { get; private set; } = null!;
    public ICommand AiDetectAnomaliesCommand { get; private set; } = null!;
    public ICommand AiForecastCommand { get; private set; } = null!;
    public ICommand AiCopilotCommand { get; private set; } = null!;

    public bool IsAiConfigured
    {
        get => _isAiConfigured;
        set
        {
            if (SetProperty(ref _isAiConfigured, value))
            {
                OnPropertyChanged(nameof(AiConfigurationStatus));
            }
        }
    }

    public string AiConfigurationStatus => IsAiConfigured
        ? "AI Status: Connected"
        : "AI Status: Not Configured (set API key in Settings)";

    public string AiImportRawText
    {
        get => _aiImportRawText;
        set => SetProperty(ref _aiImportRawText, value);
    }

    public string AiWhatIfPrompt
    {
        get => _aiWhatIfPrompt;
        set => SetProperty(ref _aiWhatIfPrompt, value);
    }

    public string AiCopilotQuestion
    {
        get => _aiCopilotQuestion;
        set => SetProperty(ref _aiCopilotQuestion, value);
    }

    public string AiPriceRecommendation
    {
        get => _aiPriceRecommendation;
        set => SetProperty(ref _aiPriceRecommendation, value);
    }

    public string AiWhatIfSummary
    {
        get => _aiWhatIfSummary;
        set => SetProperty(ref _aiWhatIfSummary, value);
    }

    public string AiAnomalyReport
    {
        get => _aiAnomalyReport;
        set => SetProperty(ref _aiAnomalyReport, value);
    }

    public string AiForecastReport
    {
        get => _aiForecastReport;
        set => SetProperty(ref _aiForecastReport, value);
    }

    public string AiCopilotAnswer
    {
        get => _aiCopilotAnswer;
        set => SetProperty(ref _aiCopilotAnswer, value);
    }

    private void InitializeAiFeatures()
    {
        AiSmartImportCommand = new AsyncRelayCommand(SmartImportWithAiAsync, logger: Logger);
        AiNormalizeIngredientsCommand = new AsyncRelayCommand(NormalizeIngredientsWithAiAsync, logger: Logger);
        AiPriceRecommendationCommand = new AsyncRelayCommand(GenerateAiPriceRecommendationAsync, logger: Logger);
        AiWhatIfCommand = new AsyncRelayCommand(RunAiWhatIfAsync, logger: Logger);
        AiDetectAnomaliesCommand = new AsyncRelayCommand(DetectAnomaliesWithAiAsync, logger: Logger);
        AiForecastCommand = new AsyncRelayCommand(GenerateForecastWithAiAsync, logger: Logger);
        AiCopilotCommand = new AsyncRelayCommand(AskCopilotAsync, logger: Logger);

        RefreshAiConfigurationState();
    }

    private void RefreshAiConfigurationState()
    {
        IsAiConfigured = _generativeAiService.IsConfigured;
    }

    private async Task SmartImportWithAiAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (string.IsNullOrWhiteSpace(AiImportRawText))
            {
                await ShowErrorAsync("Isi teks invoice/nota dulu sebelum Smart Import.");
                return;
            }

            var payload = new
            {
                rawText = AiImportRawText,
                defaultUnit = "g",
                knownUnits = new[] { "g", "kg", "ml", "liter", "pcs" },
                knownIngredients = AvailableIngredients.Select(x => new { x.Name, x.Unit }).ToList()
            };

            var response = await _generativeAiService.GenerateJsonAsync<AiSmartImportResponse>(
                "You convert invoice-like ingredient text into structured JSON. Return only valid JSON object.",
                JsonSerializer.Serialize(payload));

            if (response?.Ingredients == null || response.Ingredients.Count == 0)
            {
                await ShowErrorAsync("AI tidak menemukan bahan yang bisa diimport.");
                return;
            }

            foreach (var line in response.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(line.IngredientName) || line.Quantity <= 0m)
                {
                    continue;
                }

                var ingredient = new RecipeIngredientViewModel
                {
                    IngredientId = GetNextIngredientId(),
                    IngredientName = line.IngredientName.Trim(),
                    Unit = string.IsNullOrWhiteSpace(line.Unit) ? "g" : line.Unit.Trim(),
                    Quantity = line.Quantity,
                    PackNetQuantity = line.PackNetQuantity,
                    PricePerPack = line.PricePerPack,
                    ManualCost = line.ManualCost,
                    IncludeInDoughWeight = line.IncludeInDoughWeight,
                    DisplayOrder = RecipeIngredients.Count + 1
                };

                ingredient.CurrentPrice = ingredient.Quantity > 0m ? ingredient.TotalCost / ingredient.Quantity : 0m;
                RecipeIngredients.Add(ingredient);
            }

            ReindexIngredientDisplayOrder();
            AiImportRawText = string.Empty;

            StatusMessage = $"Smart import selesai. {response.Ingredients.Count} bahan ditambahkan.";
        }, "AI Smart Import");
    }

    private async Task NormalizeIngredientsWithAiAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (RecipeIngredients.Count == 0)
            {
                await ShowErrorAsync("Belum ada bahan untuk dinormalisasi.");
                return;
            }

            var payload = new
            {
                ingredients = RecipeIngredients.Select(x => new
                {
                    x.DisplayOrder,
                    x.IngredientName,
                    x.Unit,
                    x.Quantity
                }).ToList(),
                knownIngredients = AvailableIngredients.Select(x => new { x.Name, x.Unit }).ToList()
            };

            var response = await _generativeAiService.GenerateJsonAsync<AiNormalizeResponse>(
                "You normalize ingredient names/units. Return valid JSON with minimal edits and quantityMultiplier when unit conversion is needed.",
                JsonSerializer.Serialize(payload));

            if (response?.Items == null || response.Items.Count == 0)
            {
                await ShowErrorAsync("AI tidak mengembalikan normalisasi.");
                return;
            }

            foreach (var item in response.Items)
            {
                var target = RecipeIngredients.FirstOrDefault(x => x.DisplayOrder == item.DisplayOrder);
                if (target == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item.NormalizedName))
                {
                    target.IngredientName = item.NormalizedName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(item.NormalizedUnit))
                {
                    target.Unit = item.NormalizedUnit.Trim();
                }

                if (item.QuantityMultiplier.HasValue && item.QuantityMultiplier.Value > 0m)
                {
                    target.Quantity *= item.QuantityMultiplier.Value;
                }
            }

            StatusMessage = "Normalisasi bahan selesai.";
        }, "AI Normalize Ingredients");
    }

    private async Task GenerateAiPriceRecommendationAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (CalculationResult == null)
            {
                await ShowErrorAsync("Hitung modal dulu agar AI bisa memberi rekomendasi harga.");
                return;
            }

            var payload = new
            {
                summary = new
                {
                    CalculationResult.UnitCost,
                    CalculationResult.CostPerDonutWithTopping,
                    CalculationResult.SuggestedPrice,
                    CalculationResult.RecommendedPriceLow,
                    CalculationResult.RecommendedPriceHigh,
                    CalculationResult.Margin,
                    CalculationResult.PricingConfidenceScore,
                    CalculationResult.CostVolatilityScore,
                    CalculationResult.Warnings
                },
                topIngredientCosts = RecipeIngredients
                    .OrderByDescending(x => x.TotalCost)
                    .Take(5)
                    .Select(x => new { x.IngredientName, x.TotalCost })
                    .ToList()
            };

            var response = await _generativeAiService.GenerateJsonAsync<AiPriceRecommendationResponse>(
                "You are a pricing analyst for food production. Return JSON with executiveSummary and actionItems only.",
                JsonSerializer.Serialize(payload));

            if (response == null)
            {
                await ShowErrorAsync("AI tidak mengembalikan rekomendasi harga.");
                return;
            }

            var actions = response.ActionItems ?? new List<string>();
            AiPriceRecommendation = string.Join(Environment.NewLine, new[]
            {
                response.ExecutiveSummary ?? string.Empty,
                string.Empty,
                actions.Count > 0 ? "Aksi:" : string.Empty,
                string.Join(Environment.NewLine, actions.Select(x => $"- {x}"))
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }, "AI Price Recommendation");
    }

    private async Task RunAiWhatIfAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (string.IsNullOrWhiteSpace(AiWhatIfPrompt))
            {
                await ShowErrorAsync("Isi prompt what-if dulu.");
                return;
            }

            var baseline = BuildCurrentBatchRequest();
            if (!baseline.IsValid())
            {
                await ShowErrorAsync("Data batch belum valid. Pastikan bahan dan parameter sudah benar.");
                return;
            }

            var payload = new
            {
                prompt = AiWhatIfPrompt,
                editableFields = new[]
                {
                    "markupDelta", "wasteDelta", "oilPriceDeltaPercent", "overheadDeltaPercent",
                    "ingredientPriceAdjustments[{ingredientName,deltaPercent}]"
                },
                baseline = new
                {
                    baseline.Markup,
                    baseline.WastePercent,
                    baseline.OilPricePerLiter,
                    baseline.OverheadAllocated,
                    Ingredients = RecipeIngredients.Select(x => new { x.IngredientName, x.CurrentPrice, x.PricePerPack })
                }
            };

            var plan = await _generativeAiService.GenerateJsonAsync<AiWhatIfResponse>(
                "Extract structured scenario adjustments from user prompt. Return JSON only.",
                JsonSerializer.Serialize(payload));

            if (plan == null)
            {
                await ShowErrorAsync("AI tidak bisa membaca prompt what-if.");
                return;
            }

            var scenarioRequest = CloneRequest(baseline);
            scenarioRequest.Markup = Math.Max(0m, scenarioRequest.Markup + (plan.MarkupDelta ?? 0m));
            scenarioRequest.WastePercent = Math.Clamp(scenarioRequest.WastePercent + (plan.WasteDelta ?? 0m), 0m, 0.99m);
            scenarioRequest.OilPricePerLiter = Math.Max(0m, scenarioRequest.OilPricePerLiter * (1 + (plan.OilPriceDeltaPercent ?? 0m)));
            scenarioRequest.OverheadAllocated = Math.Max(0m, scenarioRequest.OverheadAllocated * (1 + (plan.OverheadDeltaPercent ?? 0m)));

            var scenarioItems = scenarioRequest.Items.ToList();
            foreach (var adjustment in plan.IngredientPriceAdjustments ?? new List<AiIngredientPriceAdjustment>())
            {
                var target = scenarioItems.FirstOrDefault(x =>
                    string.Equals(RecipeIngredients.FirstOrDefault(r => r.IngredientId == x.IngredientId)?.IngredientName,
                                  adjustment.IngredientName,
                                  StringComparison.OrdinalIgnoreCase));

                if (target != null)
                {
                    target.PricePerUnit = Math.Max(0m, target.PricePerUnit * (1 + adjustment.DeltaPercent));
                    if (target.PricePerPack.HasValue)
                    {
                        target.PricePerPack = Math.Max(0m, target.PricePerPack.Value * (1 + adjustment.DeltaPercent));
                    }
                }
            }

            scenarioRequest.Items = scenarioItems;

            var baselineResult = await _pricingEngine.CalculateBatchCostAsync(baseline);
            var scenarioResult = await _pricingEngine.CalculateBatchCostAsync(scenarioRequest);

            AiWhatIfSummary =
                $"{plan.Explanation}\n" +
                $"Baseline Suggested: {baselineResult.SuggestedPrice:N2}\n" +
                $"Scenario Suggested: {scenarioResult.SuggestedPrice:N2}\n" +
                $"Delta Suggested: {(scenarioResult.SuggestedPrice - baselineResult.SuggestedPrice):N2}\n" +
                $"Delta Margin: {(scenarioResult.Margin - baselineResult.Margin):P2}";
        }, "AI What-if");
    }

    private async Task DetectAnomaliesWithAiAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (RecipeIngredients.Count == 0)
            {
                await ShowErrorAsync("Belum ada data bahan untuk analisis anomali.");
                return;
            }

            var lineCosts = RecipeIngredients.Select(x => x.TotalCost).OrderBy(x => x).ToList();
            var median = lineCosts.Count == 0
                ? 0m
                : lineCosts.Count % 2 == 0
                    ? (lineCosts[(lineCosts.Count / 2) - 1] + lineCosts[lineCosts.Count / 2]) / 2m
                    : lineCosts[lineCosts.Count / 2];

            var anomalies = RecipeIngredients
                .Where(x => median > 0m && x.TotalCost >= (median * 2.5m))
                .Select(x => new { x.IngredientName, x.TotalCost, Median = median })
                .ToList();

            var payload = new
            {
                anomalies,
                totalIngredients = RecipeIngredients.Count,
                summary = CalculationResult == null ? null : new
                {
                    CalculationResult.TotalBatchCost,
                    CalculationResult.Margin,
                    CalculationResult.PricingConfidenceScore
                }
            };

            var response = await _generativeAiService.GenerateJsonAsync<AiAnomalyResponse>(
                "You are an operations auditor. Return JSON with findings and mitigationActions.",
                JsonSerializer.Serialize(payload));

            if (response == null)
            {
                await ShowErrorAsync("AI tidak mengembalikan analisis anomali.");
                return;
            }

            var findings = response.Findings ?? new List<string>();
            var actions = response.MitigationActions ?? new List<string>();
            AiAnomalyReport = string.Join(Environment.NewLine, new[]
            {
                findings.Count == 0 ? "Tidak ada anomali signifikan terdeteksi." : string.Join(Environment.NewLine, findings.Select(x => $"- {x}")),
                actions.Count == 0 ? string.Empty : "Mitigasi:",
                actions.Count == 0 ? string.Empty : string.Join(Environment.NewLine, actions.Select(x => $"- {x}"))
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }, "AI Anomaly Detection");
    }

    private async Task GenerateForecastWithAiAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (RecipeIngredients.Count == 0)
            {
                await ShowErrorAsync("Belum ada bahan untuk forecast.");
                return;
            }

            var batchesPerMonth = _settingsService.GetSetting("BatchesPerMonth", 4);
            var projected = new List<object>();
            foreach (var item in RecipeIngredients)
            {
                decimal trend = 0m;
                if (item.IngredientId > 0)
                {
                    trend = await _ingredientRepository.GetPriceTrendAsync(item.IngredientId, 60);
                }

                projected.Add(new
                {
                    item.IngredientName,
                    item.Unit,
                    MonthlyUsage = item.Quantity * batchesPerMonth,
                    PriceTrend60Days = trend
                });
            }

            var payload = new
            {
                batchesPerMonth,
                recentHistory = _calculationHistory.TakeLast(8).ToList(),
                projectedIngredients = projected
            };

            var response = await _generativeAiService.GenerateJsonAsync<AiForecastResponse>(
                "You are a procurement planner. Return JSON with executiveSummary and purchaseActions.",
                JsonSerializer.Serialize(payload));

            if (response == null)
            {
                await ShowErrorAsync("AI tidak mengembalikan forecast.");
                return;
            }

            AiForecastReport = string.Join(Environment.NewLine, new[]
            {
                response.ExecutiveSummary ?? string.Empty,
                string.Empty,
                response.PurchaseActions == null || response.PurchaseActions.Count == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, response.PurchaseActions.Select(x => $"- {x}"))
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }, "AI Forecast");
    }

    private async Task AskCopilotAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            if (!IsAiConfigured)
            {
                await ShowErrorAsync("Gemini API key belum diatur. Buka halaman Settings dulu.");
                return;
            }

            if (string.IsNullOrWhiteSpace(AiCopilotQuestion))
            {
                await ShowErrorAsync("Isi pertanyaan Copilot dulu.");
                return;
            }

            var context = new
            {
                RecipeName,
                RecipeDescription,
                Ingredients = RecipeIngredients.Select(x => new { x.IngredientName, x.Quantity, x.Unit, x.TotalCost }).ToList(),
                CurrentResult = CalculationResult == null ? null : new
                {
                    CalculationResult.TotalBatchCost,
                    CalculationResult.UnitCost,
                    CalculationResult.SuggestedPrice,
                    CalculationResult.Margin,
                    CalculationResult.PricingConfidenceScore
                },
                AiPriceRecommendation,
                AiAnomalyReport,
                AiForecastReport
            };

            AiCopilotAnswer = await _generativeAiService.GenerateTextAsync(
                "You are an enterprise HPP copilot for donut production. Be concise and actionable.",
                $"Context:\n{JsonSerializer.Serialize(context)}\n\nQuestion:\n{AiCopilotQuestion}");
        }, "AI Copilot");
    }

    private BatchRequest BuildCurrentBatchRequest()
    {
        return new BatchRequest
        {
            Items = BuildRecipeItems(),
            BatchMultiplier = BatchMultiplier,
            OilUsedLiters = OilUsedLiters,
            OilPricePerLiter = OilPricePerLiter,
            OilChangeCost = OilChangeCost,
            BatchesPerOilChange = BatchesPerOilChange,
            EnergyKwh = EnergyKwh,
            EnergyRatePerKwh = EnergyRatePerKwh,
            Labor = BuildLaborRoles(),
            OverheadAllocated = OverheadAllocated,
            TheoreticalOutput = TheoreticalOutput,
            WastePercent = WastePercent,
            PackagingPerUnit = PackagingPerUnit,
            UseWeightBasedOutput = UseWeightBasedOutput,
            DonutWeightGrams = DonutWeightGrams,
            ToppingWeightPerDonutGrams = ToppingWeightPerDonutGrams,
            ToppingPackWeightGrams = ToppingPackWeightGrams,
            ToppingPackPrice = ToppingPackPrice,
            PriceVolatilityPercent = PriceVolatilityPercent,
            RiskAppetitePercent = RiskAppetitePercent,
            MarketPressurePercent = MarketPressurePercent,
            TargetProfitPerBatch = TargetProfitPerBatch,
            MonthlyFixedCost = MonthlyFixedCost,
            Markup = Markup,
            VatPercent = VatPercent,
            RoundingRule = RoundingRule,
            PricingStrategy = PricingStrategy,
            TargetMarginPercent = TargetMarginPercent
        };
    }

    private static BatchRequest CloneRequest(BatchRequest request)
    {
        return new BatchRequest
        {
            Items = request.Items.Select(x => new RecipeItem
            {
                IngredientId = x.IngredientId,
                Quantity = x.Quantity,
                Unit = x.Unit,
                PricePerUnit = x.PricePerUnit,
                PackNetQuantity = x.PackNetQuantity,
                PricePerPack = x.PricePerPack,
                ManualCost = x.ManualCost,
                IncludeInDoughWeight = x.IncludeInDoughWeight
            }).ToList(),
            BatchMultiplier = request.BatchMultiplier,
            OilUsedLiters = request.OilUsedLiters,
            OilPricePerLiter = request.OilPricePerLiter,
            OilChangeCost = request.OilChangeCost,
            BatchesPerOilChange = request.BatchesPerOilChange,
            EnergyKwh = request.EnergyKwh,
            EnergyRatePerKwh = request.EnergyRatePerKwh,
            Labor = request.Labor.Select(l => new LaborRole
            {
                Id = l.Id,
                Name = l.Name,
                HourlyRate = l.HourlyRate,
                Hours = l.Hours
            }).ToList(),
            OverheadAllocated = request.OverheadAllocated,
            TheoreticalOutput = request.TheoreticalOutput,
            WastePercent = request.WastePercent,
            PackagingPerUnit = request.PackagingPerUnit,
            Markup = request.Markup,
            VatPercent = request.VatPercent,
            RoundingRule = request.RoundingRule,
            PricingStrategy = request.PricingStrategy,
            TargetMarginPercent = request.TargetMarginPercent,
            UseWeightBasedOutput = request.UseWeightBasedOutput,
            DonutWeightGrams = request.DonutWeightGrams,
            ToppingWeightPerDonutGrams = request.ToppingWeightPerDonutGrams,
            ToppingPackWeightGrams = request.ToppingPackWeightGrams,
            ToppingPackPrice = request.ToppingPackPrice,
            TargetProfitPerBatch = request.TargetProfitPerBatch,
            MonthlyFixedCost = request.MonthlyFixedCost,
            PriceVolatilityPercent = request.PriceVolatilityPercent,
            RiskAppetitePercent = request.RiskAppetitePercent,
            MarketPressurePercent = request.MarketPressurePercent
        };
    }

    private void CaptureCalculationHistory(BatchRequest request, BatchCostResult result)
    {
        _calculationHistory.Add(new CalculationHistoryEntry
        {
            Timestamp = DateTime.UtcNow,
            BatchMultiplier = request.BatchMultiplier,
            SuggestedPrice = result.SuggestedPrice,
            UnitCost = result.UnitCost,
            Margin = result.Margin
        });

        if (_calculationHistory.Count > 40)
        {
            _calculationHistory.RemoveAt(0);
        }
    }

    private sealed class AiSmartImportResponse
    {
        [JsonPropertyName("ingredients")]
        public List<AiSmartImportLine> Ingredients { get; set; } = new();
    }

    private sealed class AiSmartImportLine
    {
        [JsonPropertyName("ingredientName")]
        public string IngredientName { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public decimal Quantity { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = "g";

        [JsonPropertyName("packNetQuantity")]
        public decimal? PackNetQuantity { get; set; }

        [JsonPropertyName("pricePerPack")]
        public decimal? PricePerPack { get; set; }

        [JsonPropertyName("manualCost")]
        public decimal? ManualCost { get; set; }

        [JsonPropertyName("includeInDoughWeight")]
        public bool IncludeInDoughWeight { get; set; } = true;
    }

    private sealed class AiNormalizeResponse
    {
        [JsonPropertyName("items")]
        public List<AiNormalizeItem> Items { get; set; } = new();
    }

    private sealed class AiNormalizeItem
    {
        [JsonPropertyName("displayOrder")]
        public int DisplayOrder { get; set; }

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("normalizedUnit")]
        public string? NormalizedUnit { get; set; }

        [JsonPropertyName("quantityMultiplier")]
        public decimal? QuantityMultiplier { get; set; }
    }

    private sealed class AiPriceRecommendationResponse
    {
        [JsonPropertyName("executiveSummary")]
        public string? ExecutiveSummary { get; set; }

        [JsonPropertyName("actionItems")]
        public List<string>? ActionItems { get; set; }
    }

    private sealed class AiWhatIfResponse
    {
        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("markupDelta")]
        public decimal? MarkupDelta { get; set; }

        [JsonPropertyName("wasteDelta")]
        public decimal? WasteDelta { get; set; }

        [JsonPropertyName("oilPriceDeltaPercent")]
        public decimal? OilPriceDeltaPercent { get; set; }

        [JsonPropertyName("overheadDeltaPercent")]
        public decimal? OverheadDeltaPercent { get; set; }

        [JsonPropertyName("ingredientPriceAdjustments")]
        public List<AiIngredientPriceAdjustment>? IngredientPriceAdjustments { get; set; }
    }

    private sealed class AiIngredientPriceAdjustment
    {
        [JsonPropertyName("ingredientName")]
        public string IngredientName { get; set; } = string.Empty;

        [JsonPropertyName("deltaPercent")]
        public decimal DeltaPercent { get; set; }
    }

    private sealed class AiAnomalyResponse
    {
        [JsonPropertyName("findings")]
        public List<string>? Findings { get; set; }

        [JsonPropertyName("mitigationActions")]
        public List<string>? MitigationActions { get; set; }
    }

    private sealed class AiForecastResponse
    {
        [JsonPropertyName("executiveSummary")]
        public string? ExecutiveSummary { get; set; }

        [JsonPropertyName("purchaseActions")]
        public List<string>? PurchaseActions { get; set; }
    }

    private sealed class CalculationHistoryEntry
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("batchMultiplier")]
        public decimal BatchMultiplier { get; set; }

        [JsonPropertyName("suggestedPrice")]
        public decimal SuggestedPrice { get; set; }

        [JsonPropertyName("unitCost")]
        public decimal UnitCost { get; set; }

        [JsonPropertyName("margin")]
        public decimal Margin { get; set; }
    }
}
