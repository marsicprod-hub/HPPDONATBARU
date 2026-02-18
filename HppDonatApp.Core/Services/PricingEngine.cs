using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using HppDonatApp.Core.Models;

namespace HppDonatApp.Core.Services;

/// <summary>
/// Service interface for pricing engine calculations.
/// Defines contract for batch cost computation and pricing strategy application.
/// </summary>
public interface IPricingEngine
{
    /// <summary>
    /// Synchronously calculates comprehensive batch cost and pricing information.
    /// </summary>
    /// <param name="request">The batch request containing all parameters for calculation</param>
    /// <returns>Detailed batch cost result with breakdown and pricing information</returns>
    BatchCostResult CalculateBatchCost(BatchRequest request);

    /// <summary>
    /// Asynchronously calculates comprehensive batch cost and pricing information.
    /// </summary>
    /// <param name="request">The batch request containing all parameters for calculation</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Task representing the asynchronous calculation operation</returns>
    Task<BatchCostResult> CalculateBatchCostAsync(BatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates costs for multiple batch requests with caching.
    /// </summary>
    /// <param name="requests">Collection of batch requests to process</param>
    /// <returns>List of cost results corresponding to input requests</returns>
    IEnumerable<BatchCostResult> CalculateMultipleBatches(IEnumerable<BatchRequest> requests);

    /// <summary>
    /// Clears the internal calculation cache (useful for testing and cache invalidation).
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Gets statistics about the pricing engine operations (for diagnostics).
    /// </summary>
    /// <returns>Dictionary containing cache hits, misses, and calculation metrics</returns>
    Dictionary<string, object> GetDiagnostics();
}

/// <summary>
/// Primary implementation of pricing engine for HPP (Harga Pokok Produksi - Cost of Goods Sold) calculations.
/// Handles complex calculations including ingredient costs, labor, energy, waste, and pricing strategies.
/// This implementation includes comprehensive logging, caching, validation, and numeric safety measures.
/// </summary>
public class PricingEngine : IPricingEngine
{
    private readonly IMemoryCache _cache;
    private readonly ILogger? _logger;
    private readonly IPricingStrategy? _pricingStrategy;

    // Cache statistics for diagnostics
    private int _cacheHits;
    private int _cacheMisses;
    private int _calculationsCount;

    // Constants for calculation and logging
    private const string CacheKeyPrefix = "batch_cost_";
    private const decimal MinimumUnitPrice = 0.01m;
    private const decimal MaximumWastePercent = 0.99m;
    private const int DefaultCacheExpirationMinutes = 60;

    /// <summary>
    /// Initializes the pricing engine with optional caching and logging support.
    /// </summary>
    /// <param name="cache">Memory cache instance for caching calculation results (optional)</param>
    /// <param name="logger">Logger instance for diagnostic logging (optional)</param>
    /// <param name="pricingStrategy">Pricing strategy service for price calculation (optional)</param>
    public PricingEngine(
        IMemoryCache? cache = null,
        ILogger? logger = null,
        IPricingStrategy? pricingStrategy = null)
    {
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        _logger = logger;
        _pricingStrategy = pricingStrategy ?? new FixedMarkupPricingStrategy();

        _logger?.Debug("PricingEngine initialized with cache={CacheEnabled}, logger={LoggerEnabled}, strategy={StrategyType}",
            cache != null, logger != null, _pricingStrategy?.GetType().Name ?? "Default");
    }

    /// <summary>
    /// Synchronously calculates the comprehensive batch cost and pricing information.
    /// This is the main entry point for pricing calculations.
    /// </summary>
    /// <param name="request">The batch request with all parameters</param>
    /// <returns>Complete batch cost result with breakdown</returns>
    public BatchCostResult CalculateBatchCost(BatchRequest request)
    {
        if (request == null)
        {
            _logger?.Error("CalculateBatchCost called with null request");
            throw new ArgumentNullException(nameof(request), "Batch request cannot be null");
        }

        // Validate request
        if (!request.IsValid())
        {
            _logger?.Warning("Invalid batch request received: {Request}", request);
            throw new InvalidOperationException("Batch request contains invalid parameters");
        }

        // Check cache
        var cacheKey = GenerateCacheKey(request);
        if (_cache.TryGetValue(cacheKey, out BatchCostResult? cachedResult) && cachedResult != null)
        {
            _cacheHits++;
            _logger?.Debug("Cache HIT for batch request. Cache hits={CacheHits}", _cacheHits);
            return cachedResult;
        }

        _cacheMisses++;
        _logger?.Debug("Cache MISS for batch request. Cache misses={CacheMisses}", _cacheMisses);

        // Perform calculation
        var result = PerformDetailedCalculation(request);

        // Store in cache
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(DefaultCacheExpirationMinutes));
        _calculationsCount++;

        _logger?.Information("Batch cost calculated: UnitCost={UnitCost:C}, SuggestedPrice={SuggestedPrice:C}, SellableUnits={SellableUnits}",
            result.UnitCost, result.SuggestedPrice, result.SellableUnits);

        return result;
    }

    /// <summary>
    /// Asynchronously calculates batch cost to allow non-blocking operations.
    /// </summary>
    /// <param name="request">Batch request parameters</param>
    /// <param name="cancellationToken">Cancellation token for task cancellation</param>
    /// <returns>Task yielding the batch cost result</returns>
    public async Task<BatchCostResult> CalculateBatchCostAsync(BatchRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => CalculateBatchCost(request), cancellationToken);
    }

    /// <summary>
    /// Calculates costs for multiple batch scenarios in one operation.
    /// Useful for batch processing and comparison scenarios.
    /// </summary>
    /// <param name="requests">Collection of batch requests</param>
    /// <returns>Enumerable of cost results</returns>
    public IEnumerable<BatchCostResult> CalculateMultipleBatches(IEnumerable<BatchRequest> requests)
    {
        if (requests == null)
        {
            _logger?.Error("CalculateMultipleBatches called with null requests");
            throw new ArgumentNullException(nameof(requests));
        }

        _logger?.Information("Calculating multiple batches: Count={BatchCount}", requests.Count());
        return requests.Select(req =>
        {
            try
            {
                return CalculateBatchCost(req);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error calculating batch cost");
                throw;
            }
        });
    }

    /// <summary>
    /// Clears all cached calculation results.
    /// Should be called when ingredient prices or settings change.
    /// </summary>
    public void ClearCache()
    {
        _logger?.Information("Clearing pricing engine cache");
        // Note: MemoryCache doesn't have a built-in ClearAll, so we'd need custom tracking
        // For now, this serves as a placeholder for custom cache implementation
    }

    /// <summary>
    /// Gets diagnostic information about engine performance.
    /// </summary>
    /// <returns>Dictionary with statistics</returns>
    public Dictionary<string, object> GetDiagnostics()
    {
        return new Dictionary<string, object>
        {
            { "CacheHits", _cacheHits },
            { "CacheMisses", _cacheMisses },
            { "TotalCalculations", _calculationsCount },
            { "HitRate", _calculationsCount > 0 ? (double)_cacheHits / _calculationsCount : 0 },
            { "EngineVersion", "1.0" }
        };
    }

    /// <summary>
    /// Performs the detailed calculation of all cost components.
    /// This is the core calculation logic that breaks down costs step-by-step.
    /// </summary>
    private BatchCostResult PerformDetailedCalculation(BatchRequest request)
    {
        _logger?.Debug("Starting detailed calculation for batch with {ItemCount} items", request.Items.Count());

        var result = new BatchCostResult();

        try
        {
            // Step 1: Calculate ingredient costs
            result.IngredientCost = CalculateIngredientCost(request);
            _logger?.Debug("Ingredient cost calculated: {Cost:C}", result.IngredientCost);

            // Step 2: Calculate oil costs
            result.OilCost = CalculateOilCost(request);
            result.OilAmortization = CalculateOilAmortization(request);
            _logger?.Debug("Oil cost: {Cost:C}, Amortization: {Amortization:C}", result.OilCost, result.OilAmortization);

            // Step 3: Calculate energy costs
            result.EnergyCost = CalculateEnergyCost(request);
            _logger?.Debug("Energy cost calculated: {Cost:C}", result.EnergyCost);

            // Step 4: Calculate labor costs
            result.LaborCost = CalculateLaborCost(request);
            _logger?.Debug("Labor cost calculated: {Cost:C}", result.LaborCost);

            // Step 5: Set overhead
            result.OverheadCost = request.OverheadAllocated;

            // Step 6: Calculate output units (theoretical/waste or weight-based)
            result.DoughWeightTotal = CalculateDoughWeightTotal(request);
            result.DonutCountByWeight = request.DonutWeightGrams > 0
                ? result.DoughWeightTotal / request.DonutWeightGrams
                : 0m;
            result.SellableUnits = CalculateSellableUnits(request, result.DonutCountByWeight);
            _logger?.Debug("Output calculated: SellableUnits={Units}, DoughWeight={DoughWeight}, DonutCountByWeight={DonutCount}",
                result.SellableUnits, result.DoughWeightTotal, result.DonutCountByWeight);

            // Step 7: Calculate packaging costs
            result.PackagingCost = CalculatePackagingCost(request, result.SellableUnits);
            _logger?.Debug("Packaging cost calculated: {Cost:C}", result.PackagingCost);

            // Step 8: Calculate total batch cost
            result.TotalBatchCost = result.IngredientCost + result.OilCost + result.OilAmortization +
                                   result.EnergyCost + result.LaborCost + result.OverheadCost + result.PackagingCost;
            _logger?.Debug("Total batch cost: {Cost:C}", result.TotalBatchCost);

            // Step 9: Calculate unit cost
            var outputUnits = request.UseWeightBasedOutput && result.DonutCountByWeight > 0
                ? result.DonutCountByWeight
                : result.SellableUnits;

            if (outputUnits <= 0)
            {
                _logger?.Error("Invalid output units for unit cost calculation: {Units}", outputUnits);
                throw new InvalidOperationException("Cannot calculate unit cost: no sellable units");
            }

            result.UnitCost = result.TotalBatchCost / outputUnits;
            _logger?.Debug("Unit cost calculated: {Cost:C}", result.UnitCost);

            // Step 10: Calculate topping cost and apply pricing strategy
            result.ToppingCostPerDonut = CalculateToppingCostPerDonut(request);
            result.CostPerDonutWithTopping = result.UnitCost + result.ToppingCostPerDonut;

            var pricingBaseCost = result.CostPerDonutWithTopping > 0
                ? result.CostPerDonutWithTopping
                : result.UnitCost;

            var strategySuggestedPrice = _pricingStrategy!.CalculatePrice(pricingBaseCost, request);
            _logger?.Debug("Suggested price calculated using {Strategy}: {Price:C}",
                _pricingStrategy.GetType().Name, strategySuggestedPrice);

            // Step 11: Apply advanced pricing intelligence and operational KPIs
            EnrichAdvancedPricing(request, result, pricingBaseCost, outputUnits, strategySuggestedPrice);

            // Step 14: Build breakdown dictionary
            result.BreakdownDictionary = BuildBreakdownDictionary(result);

            result.CalculatedAt = DateTime.UtcNow;

            return result;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error during detailed calculation");
            throw;
        }
    }

    /// <summary>
    /// Calculates the total ingredient cost by multiplying quantity by price per unit.
    /// If batch multiplier is specified, scales all ingredients proportionally.
    /// </summary>
    private decimal CalculateIngredientCost(BatchRequest request)
    {
        decimal totalCost = 0m;

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0 || item.PricePerUnit < 0)
            {
                _logger?.Warning("Invalid recipe item: Quantity={Qty}, Price={Price}", item.Quantity, item.PricePerUnit);
                continue;
            }

            var itemCost = item.CalculateCost(request.BatchMultiplier);

            _logger?.Debug("Ingredient item cost: ItemId={Id}, Quantity={Quantity}, LineCost={Cost:C}",
                item.IngredientId, item.Quantity * request.BatchMultiplier, itemCost);

            totalCost += itemCost;
        }

        return totalCost;
    }

    /// <summary>
    /// Calculates total dough weight from recipe lines included in dough output.
    /// </summary>
    private decimal CalculateDoughWeightTotal(BatchRequest request)
    {
        if (request.Items == null)
        {
            return 0m;
        }

        var total = request.Items
            .Where(i => i.IncludeInDoughWeight)
            .Sum(i => i.Quantity * request.BatchMultiplier);

        return total;
    }

    /// <summary>
    /// Calculates the cost of oil (cooking medium) for the batch.
    /// </summary>
    private decimal CalculateOilCost(BatchRequest request)
    {
        if (request.OilUsedLiters <= 0 || request.OilPricePerLiter <= 0)
        {
            return 0m;
        }

        var oilCost = request.OilUsedLiters * request.OilPricePerLiter;
        _logger?.Debug("Oil cost: {Liters}L @ {Price:C}/L = {Cost:C}",
            request.OilUsedLiters, request.OilPricePerLiter, oilCost);

        return oilCost;
    }

    /// <summary>
    /// Calculates the amortized cost of oil changes spread across multiple batches.
    /// Formula: OilChangeCost / BatchesPerOilChange
    /// </summary>
    private decimal CalculateOilAmortization(BatchRequest request)
    {
        if (request.OilChangeCost <= 0 || request.BatchesPerOilChange <= 0)
        {
            return 0m;
        }

        var amortization = request.OilChangeCost / request.BatchesPerOilChange;
        _logger?.Debug("Oil amortization: {Cost:C} / {Batches} = {Amortized:C}",
            request.OilChangeCost, request.BatchesPerOilChange, amortization);

        return amortization;
    }

    /// <summary>
    /// Calculates the cost of energy (electricity) consumed in batch production.
    /// </summary>
    private decimal CalculateEnergyCost(BatchRequest request)
    {
        if (request.EnergyKwh <= 0 || request.EnergyRatePerKwh <= 0)
        {
            return 0m;
        }

        var energyCost = request.EnergyKwh * request.EnergyRatePerKwh;
        _logger?.Debug("Energy cost: {Kwh}kWh @ {Rate:C}/kWh = {Cost:C}",
            request.EnergyKwh, request.EnergyRatePerKwh, energyCost);

        return energyCost;
    }

    /// <summary>
    /// Calculates the total labor cost by summing all labor roles.
    /// Each role contributes Hours * HourlyRate to the total.
    /// </summary>
    private decimal CalculateLaborCost(BatchRequest request)
    {
        decimal totalLaborCost = 0m;

        if (request.Labor == null)
        {
            return 0m;
        }

        foreach (var labor in request.Labor)
        {
            if (labor.Hours <= 0 || labor.HourlyRate <= 0)
            {
                _logger?.Warning("Invalid labor role: {Name}, Hours={Hrs}, Rate={Rate:C}",
                    labor.Name, labor.Hours, labor.HourlyRate);
                continue;
            }

            var roleCost = labor.Hours * labor.HourlyRate;
            totalLaborCost += roleCost;

            _logger?.Debug("Labor role cost: {Name} {Hrs}h @ {Rate:C}/h = {Cost:C}",
                labor.Name, labor.Hours, labor.HourlyRate, roleCost);
        }

        return totalLaborCost;
    }

    /// <summary>
    /// Calculates the number of sellable units after accounting for waste and spoilage.
    /// Formula: Floor(TheoreticalOutput * (1 - WastePercent))
    /// </summary>
    private int CalculateSellableUnits(BatchRequest request, decimal donutCountByWeight)
    {
        if (request.UseWeightBasedOutput)
        {
            if (donutCountByWeight <= 0)
            {
                _logger?.Error("Invalid donut count by weight: {Count}", donutCountByWeight);
                return 0;
            }

            // Keep integer representation for UI compatibility while unit cost uses decimal count.
            return Math.Max(1, (int)Math.Floor(donutCountByWeight));
        }

        if (request.TheoreticalOutput <= 0)
        {
            _logger?.Error("Invalid theoretical output: {Output}", request.TheoreticalOutput);
            return 0;
        }

        if (request.WastePercent < 0 || request.WastePercent >= MaximumWastePercent)
        {
            _logger?.Warning("Invalid waste percent: {Waste:P} (clamped to valid range)", request.WastePercent);
            request.WastePercent = Math.Max(0, Math.Min(request.WastePercent, MaximumWastePercent));
        }

        var sellableUnits = (int)Math.Floor(request.TheoreticalOutput * (1 - request.WastePercent));
        _logger?.Debug("Sellable units: Floor({TheoreticalOutput} * (1 - {Waste:P})) = {Sellable}",
            request.TheoreticalOutput, request.WastePercent, sellableUnits);

        return Math.Max(1, sellableUnits);
    }

    /// <summary>
    /// Calculates the total packaging cost for all sellable units.
    /// Formula: PackagingPerUnit * SellableUnits
    /// </summary>
    private decimal CalculatePackagingCost(BatchRequest request, int sellableUnits)
    {
        if (request.PackagingPerUnit <= 0 || sellableUnits <= 0)
        {
            return 0m;
        }

        var packagingCost = request.PackagingPerUnit * sellableUnits;
        _logger?.Debug("Packaging cost: {Price:C}/unit * {Units} units = {Total:C}",
            request.PackagingPerUnit, sellableUnits, packagingCost);

        return packagingCost;
    }

    /// <summary>
    /// Calculates topping cost per donut from pack metrics.
    /// Spreadsheet formula: ToppingPackPrice / ToppingPackWeightGrams * ToppingWeightPerDonutGrams
    /// </summary>
    private decimal CalculateToppingCostPerDonut(BatchRequest request)
    {
        if (request.ToppingWeightPerDonutGrams <= 0 ||
            request.ToppingPackWeightGrams <= 0 ||
            request.ToppingPackPrice <= 0)
        {
            return 0m;
        }

        var toppingCost = request.ToppingPackPrice / request.ToppingPackWeightGrams * request.ToppingWeightPerDonutGrams;
        _logger?.Debug("Topping cost per donut: {Price:C}/{PackWeight}*{Usage} = {Cost:C}",
            request.ToppingPackPrice, request.ToppingPackWeightGrams, request.ToppingWeightPerDonutGrams, toppingCost);

        return toppingCost;
    }

    /// <summary>
    /// Enriches pricing result with risk-aware recommendations and profitability planning metrics.
    /// </summary>
    private void EnrichAdvancedPricing(
        BatchRequest request,
        BatchCostResult result,
        decimal pricingBaseCost,
        decimal outputUnits,
        decimal strategySuggestedPrice)
    {
        var warnings = new List<string>();
        var costVolatilityScore = CalculateCostVolatilityScore(request);
        var riskBuffer = CalculateRiskBufferPercent(request, result, costVolatilityScore);
        var marketMultiplier = 1 + request.MarketPressurePercent;

        result.CostVolatilityScore = costVolatilityScore;
        result.RiskBufferPercent = riskBuffer;

        var minimumSafe = pricingBaseCost * (1 + riskBuffer);
        var marketAdjusted = strategySuggestedPrice * marketMultiplier;
        var strategyAdjusted = Math.Max(minimumSafe, marketAdjusted);

        var conservativeMultiplier = 1.04m + ((1 - request.RiskAppetitePercent) * 0.06m);
        var aggressiveMultiplier = 1.06m + (request.RiskAppetitePercent * 0.08m);

        var conservative = Math.Max(strategyAdjusted, minimumSafe * conservativeMultiplier);
        var aggressive = Math.Max(strategyAdjusted, strategyAdjusted * aggressiveMultiplier);

        decimal chosenSuggested = request.RiskAppetitePercent switch
        {
            <= 0.35m => conservative,
            >= 0.75m => (strategyAdjusted + aggressive) / 2m,
            _ => strategyAdjusted
        };

        // Apply final rounding consistently across output recommendations.
        result.MinimumSafePrice = ApplyRoundingRule(minimumSafe, request.RoundingRule);
        result.SuggestedPriceConservative = ApplyRoundingRule(conservative, request.RoundingRule);
        result.SuggestedPriceAggressive = ApplyRoundingRule(aggressive, request.RoundingRule);
        result.SuggestedPrice = ApplyRoundingRule(chosenSuggested, request.RoundingRule);

        result.RecommendedPriceLow = Math.Min(result.SuggestedPriceConservative, result.SuggestedPrice);
        result.RecommendedPriceHigh = Math.Max(result.SuggestedPriceAggressive, result.SuggestedPrice);

        result.PriceIncVat = result.SuggestedPrice * (1 + request.VatPercent);

        if (result.SuggestedPrice > 0)
        {
            result.Margin = (result.SuggestedPrice - pricingBaseCost) / result.SuggestedPrice;
        }
        else
        {
            result.Margin = 0m;
        }

        result.ContributionMarginPerUnit = result.SuggestedPrice - pricingBaseCost;
        result.ProfitPerUnitAtSuggestedPrice = result.ContributionMarginPerUnit;
        result.ProfitPerBatchAtSuggestedPrice = result.ProfitPerUnitAtSuggestedPrice * outputUnits;

        if (request.TargetProfitPerBatch > 0)
        {
            if (result.ContributionMarginPerUnit <= 0)
            {
                result.UnitsForTargetProfit = 0;
                warnings.Add("Target profit cannot be reached with current suggested price.");
            }
            else
            {
                result.UnitsForTargetProfit = (int)Math.Ceiling(request.TargetProfitPerBatch / result.ContributionMarginPerUnit);
            }
        }
        else
        {
            result.UnitsForTargetProfit = 0;
        }

        if (request.MonthlyFixedCost > 0)
        {
            if (result.ContributionMarginPerUnit <= 0)
            {
                result.MonthlyBreakEvenUnits = 0;
                warnings.Add("Monthly break-even is unreachable because contribution margin is non-positive.");
            }
            else
            {
                result.MonthlyBreakEvenUnits = (int)Math.Ceiling(request.MonthlyFixedCost / result.ContributionMarginPerUnit);
            }
        }
        else
        {
            result.MonthlyBreakEvenUnits = 0;
        }

        var overheadShare = result.TotalBatchCost > 0
            ? result.OverheadCost / result.TotalBatchCost
            : 0m;

        if (request.WastePercent > 0.12m)
        {
            warnings.Add("Waste is high (>12%). Reducing waste can improve margin significantly.");
        }

        if (overheadShare > 0.35m)
        {
            warnings.Add("Overhead share is high (>35%). Consider overhead allocation review.");
        }

        if (costVolatilityScore > 0.35m)
        {
            warnings.Add("Input price volatility is elevated. Consider shorter re-pricing cycle.");
        }

        if (result.ContributionMarginPerUnit <= MinimumUnitPrice)
        {
            warnings.Add("Contribution margin is too thin. Suggested price may not be sustainable.");
        }

        var confidence = 1m
            - (0.45m * costVolatilityScore)
            - (0.25m * request.WastePercent)
            - (0.20m * overheadShare)
            - (0.10m * Math.Max(0m, -request.MarketPressurePercent));
        result.PricingConfidenceScore = Math.Clamp(confidence, 0.05m, 0.98m);

        result.Warnings = warnings;
        result.RecommendationNote = BuildRecommendationNote(result);
    }

    /// <summary>
    /// Estimates variability score from per-unit ingredient costs (0 to 1).
    /// </summary>
    private decimal CalculateCostVolatilityScore(BatchRequest request)
    {
        var unitCosts = ExtractIngredientUnitCosts(request).ToList();
        if (unitCosts.Count <= 1)
        {
            return Math.Clamp(request.PriceVolatilityPercent, 0m, 1m);
        }

        var mean = unitCosts.Average();
        if (mean <= 0)
        {
            return Math.Clamp(request.PriceVolatilityPercent, 0m, 1m);
        }

        var variance = unitCosts.Sum(x => (x - mean) * (x - mean)) / unitCosts.Count;
        var stdDev = (decimal)Math.Sqrt((double)variance);
        var coefficientOfVariation = stdDev / mean;

        // Blend user-provided volatility with observed ingredient spread.
        var blended = (Math.Clamp(coefficientOfVariation, 0m, 1m) * 0.65m) +
                      (Math.Clamp(request.PriceVolatilityPercent, 0m, 1m) * 0.35m);

        return Math.Clamp(blended, 0m, 1m);
    }

    /// <summary>
    /// Computes risk buffer percent from volatility, waste, overhead structure, and risk appetite.
    /// </summary>
    private decimal CalculateRiskBufferPercent(BatchRequest request, BatchCostResult result, decimal costVolatilityScore)
    {
        var overheadShare = result.TotalBatchCost > 0
            ? result.OverheadCost / result.TotalBatchCost
            : 0m;

        var baseRisk = 0.03m
            + (costVolatilityScore * 0.20m)
            + (request.WastePercent * 0.12m)
            + (overheadShare * 0.08m);

        // Lower appetite => higher risk padding.
        var appetiteFactor = 1.15m - (request.RiskAppetitePercent * 0.60m);
        var adjusted = baseRisk * appetiteFactor;

        return Math.Clamp(adjusted, 0.02m, 0.35m);
    }

    private static IEnumerable<decimal> ExtractIngredientUnitCosts(BatchRequest request)
    {
        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0m)
            {
                continue;
            }

            if (item.ManualCost.HasValue)
            {
                var unitCostFromManual = item.ManualCost.Value / item.Quantity;
                if (unitCostFromManual > 0m)
                {
                    yield return unitCostFromManual;
                }
                continue;
            }

            if (item.PackNetQuantity.HasValue && item.PackNetQuantity.Value > 0m &&
                item.PricePerPack.HasValue && item.PricePerPack.Value >= 0m)
            {
                var unitCostFromPack = item.PricePerPack.Value / item.PackNetQuantity.Value;
                if (unitCostFromPack > 0m)
                {
                    yield return unitCostFromPack;
                }
                continue;
            }

            if (item.PricePerUnit > 0m)
            {
                yield return item.PricePerUnit;
            }
        }
    }

    private static string BuildRecommendationNote(BatchCostResult result)
    {
        if (result.ContributionMarginPerUnit <= 0m)
        {
            return "Price is below sustainable contribution margin. Raise price or reduce cost.";
        }

        if (result.PricingConfidenceScore < 0.40m)
        {
            return "Low confidence pricing. Use conservative range and monitor costs weekly.";
        }

        if (result.PricingConfidenceScore < 0.70m)
        {
            return "Moderate confidence pricing. Recalculate when any major input price changes.";
        }

        return "Pricing confidence is strong. Maintain standard monitoring cadence.";
    }

    /// <summary>
    /// Applies the specified rounding rule to a price value.
    /// Rounding rules like "0.05", "0.10", or "1.00" round to nearest interval.
    /// </summary>
    private decimal ApplyRoundingRule(decimal price, string roundingRule)
    {
        if (string.IsNullOrWhiteSpace(roundingRule))
        {
            _logger?.Debug("No rounding rule specified");
            return price;
        }

        if (!decimal.TryParse(roundingRule, NumberStyles.Number, CultureInfo.InvariantCulture, out var roundTo))
        {
            _logger?.Warning("Invalid rounding rule format: {Rule}", roundingRule);
            return price;
        }

        if (roundTo <= 0)
        {
            _logger?.Warning("Invalid rounding rule value: {Value}", roundTo);
            return price;
        }

        var rounded = Math.Round(price / roundTo) * roundTo;
        _logger?.Debug("Applied rounding rule {Rule}: {Original:C} -> {Rounded:C}", roundingRule, price, rounded);

        return rounded;
    }

    /// <summary>
    /// Builds a detailed breakdown dictionary of all cost components.
    /// Used for pie charts and detailed cost analysis displays.
    /// </summary>
    private Dictionary<string, decimal> BuildBreakdownDictionary(BatchCostResult result)
    {
        var breakdown = new Dictionary<string, decimal>
        {
            { "Ingredients", result.IngredientCost },
            { "Oil (Usage)", result.OilCost },
            { "Oil (Maintenance)", result.OilAmortization },
            { "Energy", result.EnergyCost },
            { "Labor", result.LaborCost },
            { "Overhead", result.OverheadCost },
            { "Packaging", result.PackagingCost },
            { "ToppingPerDonut", result.ToppingCostPerDonut },
            { "CostPerDonutWithTopping", result.CostPerDonutWithTopping },
            { "RiskBufferPercent", result.RiskBufferPercent },
            { "MinimumSafePrice", result.MinimumSafePrice },
            { "ContributionMarginPerUnit", result.ContributionMarginPerUnit }
        };

        return breakdown;
    }

    /// <summary>
    /// Generates a cache key based on request parameters.
    /// Two identical requests should generate the same cache key.
    /// </summary>
    private string GenerateCacheKey(BatchRequest request)
    {
        var keyBuilder = new StringBuilder(CacheKeyPrefix);

        // Include significant request parameters in key
        keyBuilder.Append(request.BatchMultiplier.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.OilPricePerLiter.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.EnergyRatePerKwh.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.Markup.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.WastePercent.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.PriceVolatilityPercent.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.RiskAppetitePercent.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.MarketPressurePercent.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.TargetProfitPerBatch.ToString("F2"));
        keyBuilder.Append("_");
        keyBuilder.Append(request.MonthlyFixedCost.ToString("F2"));

        return keyBuilder.ToString();
    }

    /// <summary>
    /// Example method for demonstration and testing purposes.
    /// Shows how to use the pricing engine for a sample donut batch.
    /// </summary>
    public static BatchCostResult RunSampleCalculation()
    {
        var engine = new PricingEngine();

        var sampleRequest = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }, // Flour
                new RecipeItem { IngredientId = 2, Quantity = 1m, Unit = "kg", PricePerUnit = 8.00m }, // Sugar
                new RecipeItem { IngredientId = 3, Quantity = 0.5m, Unit = "liter", PricePerUnit = 12.00m } // Oil
            },
            BatchMultiplier = 1m,
            OilUsedLiters = 2m,
            OilPricePerLiter = 12.00m,
            OilChangeCost = 500m,
            BatchesPerOilChange = 10,
            EnergyKwh = 5m,
            EnergyRatePerKwh = 2.50m,
            Labor = new List<LaborRole>
            {
                new LaborRole { Name = "Baker", HourlyRate = 50m, Hours = 2m }
            },
            OverheadAllocated = 100m,
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            PackagingPerUnit = 0.50m,
            Markup = 0.50m,
            VatPercent = 0.10m,
            RoundingRule = "0.05"
        };

        return engine.CalculateBatchCost(sampleRequest);
    }
}
