using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace HppDonatApp.Core.Services;

/// <summary>
/// Defines rounding strategy for price calculations.
/// </summary>
public interface IRoundingEngine
{
    /// <summary>
    /// Applies the specified rounding rule to a price.
    /// </summary>
    /// <param name="price">The price to round</param>
    /// <param name="roundingRule">The rounding rule (e.g., "0.05", "0.10")</param>
    /// <returns>The rounded price</returns>
    decimal ApplyRounding(decimal price, string roundingRule);

    /// <summary>
    /// Validates a rounding rule format.
    /// </summary>
    /// <param name="roundingRule">The rule to validate</param>
    /// <returns>True if valid; false otherwise</returns>
    bool IsValidRoundingRule(string roundingRule);

    /// <summary>
    /// Gets a description of how to format rounding rules.
    /// </summary>
    /// <returns>Instructions for users</returns>
    string GetRoundingRuleInstructions();
}

/// <summary>
/// Implementation of rounding engine for pricing strategies.
/// Supports multiple rounding methods: round-to-interval, banker's rounding, ceiling, floor.
/// 
/// This engine helps normalize prices to psychologically friendly or currency-standard values.
/// For example, rounding to 0.05 creates prices like 12.90, 12.95, 13.00 which are common retail prices.
/// </summary>
public class RoundingEngine : IRoundingEngine
{
    private readonly ILogger? _logger;

    // Common rounding intervals
    public static readonly decimal[] CommonRoundingIntervals =
    {
        0.01m, 0.05m, 0.10m, 0.25m, 0.50m, 1.00m, 5.00m, 10.00m
    };

    /// <summary>
    /// Initializes the rounding engine.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics</param>
    public RoundingEngine(ILogger? logger = null)
    {
        _logger = logger;
        _logger?.Debug("RoundingEngine initialized");
    }

    /// <summary>
    /// Applies rounding using the "round to nearest interval" method.
    /// This is the most common pricing rounding method.
    /// 
    /// Examples:
    /// - Price: 12.348, RoundingRule: "0.05" -> 12.35
    /// - Price: 45.622, RoundingRule: "0.10" -> 45.60
    /// - Price: 99.999, RoundingRule: "1.00" -> 100.00
    /// </summary>
    /// <param name="price">Original price</param>
    /// <param name="roundingRule">Rounding interval as string</param>
    /// <returns>Rounded price</returns>
    public decimal ApplyRounding(decimal price, string roundingRule)
    {
        if (string.IsNullOrWhiteSpace(roundingRule))
        {
            _logger?.Information("No rounding rule provided, returning price as-is: {Price:C}", price);
            return price;
        }

        if (!IsValidRoundingRule(roundingRule))
        {
            _logger?.Warning("Invalid rounding rule format: '{Rule}'. Using price as-is.", roundingRule);
            return price;
        }

        try
        {
            if (!decimal.TryParse(roundingRule, out var roundTo))
            {
                _logger?.Warning("Failed to parse rounding rule '{Rule}' to decimal", roundingRule);
                return price;
            }

            if (roundTo <= 0)
            {
                _logger?.Warning("Invalid rounding rule value (negative or zero): {Value}", roundTo);
                return price;
            }

            var rounded = RoundToInterval(price, roundTo);

            _logger?.Debug("Rounding applied: {Original:C} -> {Rounded:C} using rule {Rule}",
                price, rounded, roundingRule);

            return rounded;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error applying rounding: price={Price:C}, rule={Rule}", price, roundingRule);
            return price;
        }
    }

    /// <summary>
    /// Validates that a rounding rule has proper format.
    /// Valid formats: numeric strings like "0.01", "0.05", "0.10", "1.00"
    /// </summary>
    /// <param name="roundingRule">The rule to validate</param>
    /// <returns>True if format is valid, false otherwise</returns>
    public bool IsValidRoundingRule(string roundingRule)
    {
        if (string.IsNullOrWhiteSpace(roundingRule))
        {
            return false;
        }

        if (!decimal.TryParse(roundingRule, out var value))
        {
            return false;
        }

        // Rounding interval must be positive and reasonable (not larger than 1000)
        return value > 0 && value <= 1000m;
    }

    /// <summary>
    /// Gets user-friendly instructions for creating rounding rules.
    /// </summary>
    public string GetRoundingRuleInstructions()
    {
        return "Rounding rules specify the interval to round prices to. Examples:\n" +
               "  0.01 - Round to nearest 1 cent (exact prices)\n" +
               "  0.05 - Round to nearest 5 cents (common in retail)\n" +
               "  0.10 - Round to nearest 10 cents\n" +
               "  1.00 - Round to nearest whole unit\n" +
               "  5.00 - Round to nearest 5 units\n" +
               "\nEnter the value as a decimal number.";
    }

    /// <summary>
    /// Rounds a price to the nearest interval using standard banker's rounding (round half to even).
    /// This ensures unbiased rounding and is the default in most financial systems.
    /// 
    /// Formula: rounded = Math.Round(price / interval) * interval
    /// </summary>
    /// <param name="price">The price to round</param>
    /// <param name="interval">The rounding interval</param>
    /// <returns>The rounded price</returns>
    private decimal RoundToInterval(decimal price, decimal interval)
    {
        if (interval == 0)
        {
            _logger?.Warning("Cannot round to zero interval");
            return price;
        }

        var quotient = price / interval;
        var rounded = Math.Round(quotient, MidpointRounding.ToEven) * interval;

        return rounded;
    }

    /// <summary>
    /// Rounds up to the next interval (ceiling).
    /// Useful for conservative pricing where you always round up.
    /// 
    /// Example: Price 10.12 with interval 0.05 rounds up to 10.15
    /// </summary>
    /// <param name="price">The price to round up</param>
    /// <param name="interval">The rounding interval</param>
    /// <returns>The rounded up price</returns>
    public decimal RoundUp(decimal price, string roundingRule)
    {
        if (!decimal.TryParse(roundingRule, out var interval) || interval <= 0)
        {
            return price;
        }

        var quotient = price / interval;
        var roundedUp = Math.Ceiling(quotient) * interval;

        _logger?.Debug("RoundUp: {Original:C} -> {Rounded:C} with interval {Interval}",
            price, roundedUp, interval);

        return roundedUp;
    }

    /// <summary>
    /// Rounds down to the previous interval (floor).
    /// Useful for aggressive pricing or cost-based minimum prices.
    /// 
    /// Example: Price 10.18 with interval 0.05 rounds down to 10.15
    /// </summary>
    /// <param name="price">The price to round down</param>
    /// <param name="roundingRule">The rounding interval as string</param>
    /// <returns>The rounded down price</returns>
    public decimal RoundDown(decimal price, string roundingRule)
    {
        if (!decimal.TryParse(roundingRule, out var interval) || interval <= 0)
        {
            return price;
        }

        var quotient = price / interval;
        var roundedDown = Math.Floor(quotient) * interval;

        _logger?.Debug("RoundDown: {Original:C} -> {Rounded:C} with interval {Interval}",
            price, roundedDown, interval);

        return roundedDown;
    }

    /// <summary>
    /// Applies "charm pricing" - prices ending in .99 or .95 that are psychologically attractive.
    /// Based on research showing consumers perceive prices ending in 9 as significantly cheaper.
    /// </summary>
    /// <param name="price">The base price</param>
    /// <param name="charmSuffix">The desired suffix (0.99, 0.95, etc.)</param>
    /// <returns>The charm-priced value</returns>
    public decimal ApplyCharmPricing(decimal price, decimal charmSuffix = 0.99m)
    {
        if (charmSuffix < 0 || charmSuffix >= 1m)
        {
            _logger?.Warning("Invalid charm suffix: {Suffix}. Using standard rounding.", charmSuffix);
            return price;
        }

        // Round down to integer part, then add charm suffix
        var basePrice = Math.Floor(price);
        var charmPrice = basePrice + charmSuffix;

        // If the original price was below the charm price + margin, use original
        if (price < charmPrice - 0.5m)
        {
            charmPrice = basePrice - 0.01m; // Use previous integer's charm price
            if (charmPrice < 0) charmPrice = price;
        }

        _logger?.Debug("Charm pricing applied: {Original:C} -> {Charmed:C}",
            price, charmPrice);

        return charmPrice;
    }

    /// <summary>
    /// Provides price recommendations based on standard interval rounding.
    /// Shows what the price would be with different rounding rules.
    /// </summary>
    /// <param name="price">The price to analyze</param>
    /// <returns>Dictionary of rounding rule -> resulting price pairs</returns>
    public Dictionary<string, decimal> GetRoundingProposals(decimal price)
    {
        var proposals = new Dictionary<string, decimal>();

        foreach (var interval in CommonRoundingIntervals)
        {
            var rounded = RoundToInterval(price, interval);
            proposals[interval.ToString("F2")] = rounded;
        }

        _logger?.Debug("Generated {Count} rounding proposals for price {Price:C}",
            proposals.Count, price);

        return proposals;
    }

    /// <summary>
    /// Gets all available standard rounding intervals.
    /// </summary>
    /// <returns>Array of common rounding intervals</returns>
    public IEnumerable<decimal> GetCommonRoundingIntervals()
    {
        return CommonRoundingIntervals.ToList();
    }

    /// <summary>
    /// Example usage demonstrating the rounding engine.
    /// This method shows how rounding rules affect different price points.
    /// </summary>
    public static void RunDemonstration()
    {
        var engine = new RoundingEngine();

        var testPrices = new[] { 12.348m, 45.622m, 99.999m, 100.234m };
        var roundingRules = new[] { "0.01", "0.05", "0.10", "1.00" };

        Console.WriteLine("=== Rounding Engine Demonstration ===\n");

        foreach (var price in testPrices)
        {
            Console.WriteLine($"Original Price: {price:C}");
            foreach (var rule in roundingRules)
            {
                var rounded = engine.ApplyRounding(price, rule);
                Console.WriteLine($"  Rule {rule}: {rounded:C}");
            }
            Console.WriteLine();
        }

        // Demonstrate charm pricing
        Console.WriteLine("=== Charm Pricing ===");
        foreach (var price in testPrices)
        {
            var charm = engine.ApplyCharmPricing(price, 0.99m);
            Console.WriteLine($"{price:C} -> {charm:C}");
        }
    }
}

/// <summary>
/// Currency-specific rounding helper.
/// Different currencies have different standard rounding intervals (some countries eliminated small coins).
/// </summary>
public class CurrencyRoundingHelper
{
    private readonly Dictionary<string, decimal> _currencyRoundingRules;

    /// <summary>
    /// Initializes the helper with standard currency rounding rules.
    /// </summary>
    public CurrencyRoundingHelper()
    {
        _currencyRoundingRules = new Dictionary<string, decimal>
        {
            { "USD", 0.01m },      // 1 cent minimum
            { "EUR", 0.01m },      // 1 cent minimum
            { "IDR", 100m },       // Indonesia uses Rp. 100 as smallest
            { "MYR", 0.05m },      // Malaysia doesn't have 1-cent coin anymore
            { "SGD", 0.05m },      // Singapore 5-cent minimum
            { "JPY", 1m },         // Japan has no decimal currency
            { "TRY", 0.01m },      // Turkey uses cents
            { "INR", 1m }          // India uses whole rupees as smallest
        };
    }

    /// <summary>
    /// Gets the recommended rounding rule for a specific currency.
    /// </summary>
    /// <param name="currencyCode">ISO 4217 currency code</param>
    /// <returns>Rounding interval for that currency, or 0.01 as default</returns>
    public decimal GetCurrencyRoundingRule(string currencyCode)
    {
        if (string.IsNullOrEmpty(currencyCode))
        {
            return 0.01m;
        }

        return _currencyRoundingRules.TryGetValue(currencyCode.ToUpper(), out var rule) ? rule : 0.01m;
    }

    /// <summary>
    /// Adds or updates a currency rounding rule.
    /// </summary>
    /// <param name="currencyCode">ISO 4217 currency code</param>
    /// <param name="roundingInterval">The rounding interval for that currency</param>
    public void SetCurrencyRoundingRule(string currencyCode, decimal roundingInterval)
    {
        if (!string.IsNullOrEmpty(currencyCode) && roundingInterval > 0)
        {
            _currencyRoundingRules[currencyCode.ToUpper()] = roundingInterval;
        }
    }

    /// <summary>
    /// Gets all configured currency rules.
    /// </summary>
    public Dictionary<string, decimal> GetAllRules()
    {
        return new Dictionary<string, decimal>(_currencyRoundingRules);
    }

    /// <summary>
    /// Advanced rounding strategy that combines multiple algorithms to find optimal price point.
    /// Considers psychological pricing, competitor pricing windows, and profit margins.
    /// </summary>
    /// <param name="basePrice">The calculated base price before rounding</param>
    /// <param name="targetMarginPercent">Desired profit margin percentage</param>
    /// <param name="minAcceptablePrice">Minimum acceptable price (floor for profit)</param>
    /// <returns>Optimized price that balances appeal with profitability</returns>
    public decimal ApplyIntelligentRounding(decimal basePrice, decimal targetMarginPercent, decimal minAcceptablePrice)
    {
        _logger?.Debug("Applying intelligent rounding: BasePrice={Base}, TargetMargin={Margin}%, MinPrice={Min}",
            basePrice, targetMarginPercent, minAcceptablePrice);

        // Strategy 1: Try charm pricing first
        var charmPrice = ApplyCharmPricing(basePrice);
        if (charmPrice >= minAcceptablePrice)
        {
            _logger?.Debug("Intelligent rounding selected charm price: {Price:C}", charmPrice);
            return charmPrice;
        }

        // Strategy 2: Try standard rounding with common intervals
        var commonIntervals = new[] { 0.01m, 0.05m, 0.10m, 0.50m, 1.00m };
        foreach (var interval in commonIntervals)
        {
            var roundedPrice = RoundUp(basePrice, interval);
            if (roundedPrice >= minAcceptablePrice)
            {
                _logger?.Debug("Intelligent rounding selected standard interval {Interval}: {Price:C}", interval, roundedPrice);
                return roundedPrice;
            }
        }

        // Fallback: Use base price if all strategies fail
        _logger?.Warning("Intelligent rounding fallback: all strategies below minimum. Using base price: {Price:C}", basePrice);
        return basePrice;
    }

    /// <summary>
    /// Calculates the markdown from original price to achieve target selling price.
    /// Useful for promotional pricing and discount calculations.
    /// </summary>
    /// <param name="originalPrice">Original selling price</param>
    /// <param name="targetPrice">Desired selling price</param>
    /// <returns>Markdown percentage (0-1, negative if price increased)</returns>
    public decimal CalculateMarkdownPercent(decimal originalPrice, decimal targetPrice)
    {
        if (originalPrice <= 0)
        {
            _logger?.Warning("Invalid original price for markdown calculation: {Price}", originalPrice);
            return 0m;
        }

        var markdown = (originalPrice - targetPrice) / originalPrice;
        _logger?.Debug("Markdown from {Original} to {Target}: {Percent:P}", originalPrice, targetPrice, markdown);
        return markdown;
    }

    /// <summary>
    /// Gets rounding recommendations based on retail best practices.
    /// Returns prices optimized for different sales channels and strategies.
    /// </summary>
    /// <param name="costPerUnit">Cost to produce one unit</param>
    /// <param name="targetMarginPercent">Desired profit margin (0-1)</param>
    /// <returns>Dictionary of strategy names to recommended prices</returns>
    public Dictionary<string, decimal> GetRetailRecommendations(decimal costPerUnit, decimal targetMarginPercent)
    {
        if (costPerUnit < 0 || targetMarginPercent < 0 || targetMarginPercent > 1)
        {
            _logger?.Warning("Invalid parameters for retail recommendations: Cost={Cost}, Margin={Margin}",
                costPerUnit, targetMarginPercent);
            return new Dictionary<string, decimal>();
        }

        var recommendations = new Dictionary<string, decimal>();

        // Premium strategy: Higher margin, charm pricing
        var premiumBase = costPerUnit / (1 - targetMarginPercent - 0.15m);
        recommendations["Premium (Charm)"] = ApplyCharmPricing(premiumBase);

        // Standard strategy: Target margin with standard rounding
        var standardBase = costPerUnit / (1 - targetMarginPercent);
        recommendations["Standard (Rounded)"] = RoundUp(standardBase, 0.05m);

        // Value strategy: Lower margin, bulk appeal
        var valueBase = costPerUnit / (1 - (targetMarginPercent * 0.75m));
        recommendations["Value (Low Margin)"] = RoundDown(valueBase, 0.10m);

        // Competitive strategy: Aggressive pricing for market penetration
        var competitiveBase = costPerUnit / (1 - (targetMarginPercent * 0.50m));
        recommendations["Competitive (Aggressive)"] = RoundUp(competitiveBase, 0.01m);

        _logger?.Debug("Generated {Count} retail pricing recommendations for unit cost {Cost}", 
            recommendations.Count, costPerUnit);

        return recommendations;
    }

    /// <summary>
    /// Validates if a price point meets minimum profitability requirements.
    /// </summary>
    /// <param name="sellingPrice">Final selling price</param>
    /// <param name="costPerUnit">Cost to produce</param>
    /// <param name="minimumMarginPercent">Minimum acceptable margin (0-1)</param>
    /// <returns>True if price is profitable; false otherwise</returns>
    public bool IsProfitable(decimal sellingPrice, decimal costPerUnit, decimal minimumMarginPercent)
    {
        if (sellingPrice <= 0 || costPerUnit < 0 || minimumMarginPercent < 0)
        {
            _logger?.Warning("Invalid profitability parameters: Price={Price}, Cost={Cost}, MinMargin={Margin}",
                sellingPrice, costPerUnit, minimumMarginPercent);
            return false;
        }

        var actualMargin = (sellingPrice - costPerUnit) / sellingPrice;
        var isProfitable = actualMargin >= minimumMarginPercent;

        _logger?.Debug("Profitability check: Price={Price}, Cost={Cost}, ActualMargin={Actual:P}, MinMargin={Min:P}, Result={Result}",
            sellingPrice, costPerUnit, actualMargin, minimumMarginPercent, isProfitable);

        return isProfitable;
    }

    /// <summary>
    /// Rounds a price to match psychological pricing patterns common in retail.
    /// Uses locale-specific rounding preferences if available.
    /// </summary>
    /// <param name="price">Price to apply psychological rounding</param>
    /// <param name="locale">Optional locale for culture-specific rounding (e.g., "en-US", "id-ID")</param>
    /// <returns>Price with psychological pricing applied</returns>
    public decimal ApplyPsychologicalPricing(decimal price, string? locale = null)
    {
        _logger?.Debug("Applying psychological pricing to {Price} with locale {Locale}", price, locale ?? "default");

        // Different cultures prefer different pricing endings
        decimal psychPrice = locale?.ToLowerInvariant() switch
        {
            "en-us" or "en-gb" or "en-au" => 
                // English-speaking markets prefer .99 pricing
                ApplyCharmPricing(price),
            
            "id-id" or "id-idn" => 
                // Indonesian market often uses .500 (half-prices)
                RoundUp(price, 0.50m) - 0.01m,
            
            "ja-jp" => 
                // Japanese market prefers prices ending in 8 or 9
                RoundUp(price, 1m) - 0.10m,
            
            "de-de" or "de-at" => 
                // German market prefers .99 but also respects round numbers
                ApplyCharmPricing(price),
            
            _ => 
                // Default: Apply charm pricing
                ApplyCharmPricing(price)
        };

        _logger?.Debug("Psychological pricing result: {Original} -> {Result} for locale {Locale}",
            price, psychPrice, locale ?? "default");

        return psychPrice;
    }

    /// <summary>
    /// Provides comprehensive rounding analysis for decision making.
    /// Shows impact of different rounding strategies on profit and appeal.
    /// </summary>
    /// <param name="costPerUnit">Unit production cost</param>
    /// <param name="targetSellingPrice">Initial calculated selling price</param>
    /// <returns>Analysis object with multiple perspectives</returns>
    public (decimal Standard, decimal RoundedUp, decimal RoundedDown, decimal Charm, decimal Optimized) GetComprehensiveAnalysis(
        decimal costPerUnit, 
        decimal targetSellingPrice)
    {
        _logger?.Debug("Comprehensive rounding analysis: Cost={Cost}, TargetPrice={Target}",
            costPerUnit, targetSellingPrice);

        var standard = Math.Round(targetSellingPrice, 2, System.MidpointRounding.ToEven);
        var roundedUp = RoundUp(targetSellingPrice, 0.05m);
        var roundedDown = RoundDown(targetSellingPrice, 0.05m);
        var charm = ApplyCharmPricing(targetSellingPrice);
        var optimized = ApplyIntelligentRounding(targetSellingPrice, 0.25m, costPerUnit * 1.10m);

        _logger?.Debug("Rounding analysis complete: Standard={S}, Up={U}, Down={D}, Charm={C}, Optimized={O}",
            standard, roundedUp, roundedDown, charm, optimized);

        return (standard, roundedUp, roundedDown, charm, optimized);
    }

    /// <summary>
    /// Batch applies rounding to multiple prices with consistent strategy.
    /// </summary>
    /// <param name="prices">Collection of prices to round</param>
    /// <param name="roundingInterval">Interval to round to</param>
    /// <param name="roundDirection">Direction: "up", "down", or "nearest"</param>
    /// <returns>Dictionary of original to rounded prices</returns>
    public Dictionary<decimal, decimal> ApplyBatchRounding(
        IEnumerable<decimal> prices, 
        decimal roundingInterval,
        string roundDirection = "nearest")
    {
        var results = new Dictionary<decimal, decimal>();
        var direction = roundDirection.ToLowerInvariant();

        foreach (var price in prices)
        {
            decimal roundedPrice = direction switch
            {
                "up" => RoundUp(price, roundingInterval),
                "down" => RoundDown(price, roundingInterval),
                _ => ApplyRounding(price, roundingInterval)
            };

            results[price] = roundedPrice;
        }

        _logger?.Debug("Applied batch rounding to {Count} prices using {Direction} direction with interval {Interval}",
            results.Count, roundDirection, roundingInterval);

        return results;
    }
}

