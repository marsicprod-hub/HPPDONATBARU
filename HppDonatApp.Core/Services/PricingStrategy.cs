using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;
using HppDonatApp.Core.Models;

namespace HppDonatApp.Core.Services;

/// <summary>
/// Defines the contract for pricing strategies.
/// Different pricing strategies determine how the final selling price is calculated from unit cost.
/// </summary>
public interface IPricingStrategy
{
    /// <summary>
    /// Calculates the selling price based on unit cost and batch request parameters.
    /// The specific calculation depends on the strategy implementation.
    /// </summary>
    /// <param name="unitCost">The calculated cost per unit</param>
    /// <param name="request">The batch request with strategy-specific parameters</param>
    /// <returns>The calculated selling price (before VAT and rounding)</returns>
    decimal CalculatePrice(decimal unitCost, BatchRequest request);

    /// <summary>
    /// Gets the name of the pricing strategy for display and logging purposes.
    /// </summary>
    /// <returns>Descriptive name of the strategy</returns>
    string GetStrategyName();

    /// <summary>
    /// Gets a description of how the strategy works for user documentation.
    /// </summary>
    /// <returns>Human-readable description of the strategy</returns>
    string GetDescription();

    /// <summary>
    /// Validates whether the strategy parameters in the request are valid.
    /// </summary>
    /// <param name="request">The batch request to validate</param>
    /// <returns>True if valid; false otherwise</returns>
    bool ValidateParameters(BatchRequest request);
}

/// <summary>
/// Fixed Markup pricing strategy: multiplies unit cost by (1 + Markup percentage).
/// Simple and commonly used approach where a fixed percentage margin is applied to all products.
/// 
/// Formula: SuggestedPrice = UnitCost * (1 + Markup)
/// 
/// Best used when:
/// - Consistent profit margins are desired across products
/// - Market pricing is not a constraint
/// - Simplicity is preferred over market optimization
/// 
/// Example:
/// - UnitCost = 10,000 IDR
/// - Markup = 0.50 (50%)
/// - SuggestedPrice = 10,000 * 1.50 = 15,000 IDR
/// - Profit per unit = 5,000 IDR (50% margin)
/// </summary>
public class FixedMarkupPricingStrategy : IPricingStrategy
{
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the fixed markup pricing strategy.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic purposes</param>
    public FixedMarkupPricingStrategy(ILogger? logger = null)
    {
        _logger = logger;
        _logger?.Debug("FixedMarkupPricingStrategy initialized");
    }

    /// <summary>
    /// Calculates price using fixed markup formula.
    /// </summary>
    /// <param name="unitCost">Cost per unit</param>
    /// <param name="request">Batch request with markup parameter</param>
    /// <returns>Selling price before VAT</returns>
    public decimal CalculatePrice(decimal unitCost, BatchRequest request)
    {
        if (unitCost < 0)
        {
            _logger?.Warning("Invalid unit cost for fixed markup calculation: {Cost:C}", unitCost);
            throw new ArgumentException("Unit cost cannot be negative", nameof(unitCost));
        }

        if (!ValidateParameters(request))
        {
            _logger?.Warning("Invalid parameters for fixed markup strategy");
            throw new ArgumentException("Batch request contains invalid parameters for fixed markup strategy");
        }

        var markup = request.Markup;
        if (markup < 0)
        {
            _logger?.Warning("Negative markup provided: {Markup:P}. Using 0%.", markup);
            markup = 0m;
        }

        var suggestedPrice = unitCost * (1 + markup);

        _logger?.Debug("FixedMarkup calculation: UnitCost={UnitCost:C} * (1 + {Markup:P}) = {Price:C}",
            unitCost, markup, suggestedPrice);

        return suggestedPrice;
    }

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string GetStrategyName() => "Fixed Markup";

    /// <summary>
    /// Gets a description of the strategy.
    /// </summary>
    public string GetDescription() =>
        "Applies a fixed percentage markup to the unit cost. Simple and consistent across products.";

    /// <summary>
    /// Validates that markup is a sensible value.
    /// </summary>
    public bool ValidateParameters(BatchRequest request)
    {
        if (request == null)
        {
            return false;
        }

        // Markup should be non-negative and reasonable (< 500%)
        return request.Markup >= 0 && request.Markup < 5m;
    }
}

/// <summary>
/// Target Margin pricing strategy: calculates price to achieve a target profit margin percentage.
/// More sophisticated approach that works backwards from a desired margin to set the selling price.
/// 
/// Formula: SuggestedPrice = UnitCost / (1 - TargetMargin)
/// 
/// Derivation:
/// - Margin% = (Price - Cost) / Price
/// - TargetMargin% = (Price - Cost) / Price
/// - Price = Cost / (1 - TargetMargin%)
/// 
/// Best used when:
/// - A specific profit margin percentage is required
/// - Competitive pricing analysis determines the margin
/// - Different products need different margins
/// 
/// Example:
/// - UnitCost = 10,000 IDR
/// - TargetMargin = 0.40 (40%)
/// - SuggestedPrice = 10,000 / (1 - 0.40) = 10,000 / 0.60 = 16,666.67 IDR
/// - Actual Margin = (16,667 - 10,000) / 16,667 = 40%
/// </summary>
public class TargetMarginPricingStrategy : IPricingStrategy
{
    private readonly ILogger? _logger;

    // Maximum target margin to prevent infinite prices (100% margin would mean 0 cost, invalid)
    private const decimal MaxValidMargin = 0.99m;

    /// <summary>
    /// Initializes the target margin pricing strategy.
    /// </summary>
    /// <param name="logger">Optional logger</param>
    public TargetMarginPricingStrategy(ILogger? logger = null)
    {
        _logger = logger;
        _logger?.Debug("TargetMarginPricingStrategy initialized");
    }

    /// <summary>
    /// Calculates price to achieve target margin.
    /// </summary>
    /// <param name="unitCost">Cost per unit</param>
    /// <param name="request">Batch request with target margin</param>
    /// <returns>Selling price before VAT</returns>
    public decimal CalculatePrice(decimal unitCost, BatchRequest request)
    {
        if (unitCost < 0)
        {
            _logger?.Warning("Invalid unit cost for target margin calculation: {Cost:C}", unitCost);
            throw new ArgumentException("Unit cost cannot be negative", nameof(unitCost));
        }

        if (!ValidateParameters(request))
        {
            _logger?.Warning("Invalid parameters for target margin strategy");
            throw new ArgumentException("Batch request contains invalid parameters for target margin strategy");
        }

        var targetMargin = request.TargetMarginPercent;

        // Clamp margin to valid range
        if (targetMargin < 0)
        {
            _logger?.Warning("Negative target margin: {Margin:P}. Using 0%.", targetMargin);
            targetMargin = 0m;
        }
        else if (targetMargin >= MaxValidMargin)
        {
            _logger?.Warning("Target margin too high: {Margin:P}. Clamping to {Maximum:P}.", targetMargin, MaxValidMargin);
            targetMargin = MaxValidMargin;
        }

        // Special case: if no margin desired, price equals cost
        if (targetMargin == 0)
        {
            _logger?.Debug("TargetMargin with 0% target: Price = Cost = {Cost:C}", unitCost);
            return unitCost;
        }

        // Calculate: Price = Cost / (1 - Margin%)
        var denominator = 1 - targetMargin;
        if (Math.Abs(denominator) < 0.0001m) // Near-zero check
        {
            _logger?.Error("Invalid denominator for target margin calculation");
            throw new InvalidOperationException("Cannot calculate price: margin too close to 100%");
        }

        var suggestedPrice = unitCost / denominator;

        // Verify the calculation
        var actualMargin = (suggestedPrice - unitCost) / suggestedPrice;
        _logger?.Debug("TargetMargin calculation: UnitCost={UnitCost:C}, TargetMargin={Target:P}, Calculated Price={Price:C}, Actual Margin={Actual:P}",
            unitCost, targetMargin, suggestedPrice, actualMargin);

        return suggestedPrice;
    }

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string GetStrategyName() => "Target Margin";

    /// <summary>
    /// Gets strategy description.
    /// </summary>
    public string GetDescription() =>
        "Calculates price based on a target profit margin percentage. More sophisticated than fixed markup.";

    /// <summary>
    /// Validates target margin is reasonable.
    /// </summary>
    public bool ValidateParameters(BatchRequest request)
    {
        if (request == null)
        {
            return false;
        }

        // Margin should be between 0 and just under 100%
        return request.TargetMarginPercent >= 0 && request.TargetMarginPercent < MaxValidMargin;
    }
}

/// <summary>
/// Cost-Plus Pricing Strategy: applies both a fixed amount and a percentage markup.
/// Combines fixed overhead cost recovery with percentage-based profit margin.
/// 
/// Formula: SuggestedPrice = (UnitCost + FixedAdder) * (1 + PercentageMarkup)
/// 
/// Best used when:
/// - Both fixed and variable costs need to be recovered
/// - Minimum price thresholds must be met
/// - Blended cost structures justify dual markup approach
/// 
/// Example:
/// - UnitCost = 10,000 IDR
/// - FixedAdder = 2,000 IDR (to cover fixed overhead)
/// - PercentageMarkup = 0.25 (25%)
/// - SuggestedPrice = (10,000 + 2,000) * 1.25 = 15,000 IDR
/// </summary>
public class CostPlusPricingStrategy : IPricingStrategy
{
    private readonly ILogger? _logger;
    private decimal _fixedAdderPerUnit = 0m;

    /// <summary>
    /// Initializes the cost-plus pricing strategy.
    /// </summary>
    /// <param name="fixedAdderPerUnit">Fixed amount to add per unit (optional)</param>
    /// <param name="logger">Optional logger</param>
    public CostPlusPricingStrategy(decimal fixedAdderPerUnit = 0m, ILogger? logger = null)
    {
        _fixedAdderPerUnit = Math.Max(0, fixedAdderPerUnit);
        _logger = logger;
        _logger?.Debug("CostPlusPricingStrategy initialized with fixed adder={FixedAdder:C}", _fixedAdderPerUnit);
    }

    /// <summary>
    /// Sets the fixed adder amount for this strategy.
    /// </summary>
    /// <param name="fixedAdder">The fixed amount per unit</param>
    public void SetFixedAdder(decimal fixedAdder)
    {
        _fixedAdderPerUnit = Math.Max(0, fixedAdder);
        _logger?.Debug("Fixed adder updated to {FixedAdder:C}", _fixedAdderPerUnit);
    }

    /// <summary>
    /// Calculates price using cost-plus formula.
    /// </summary>
    /// <param name="unitCost">Cost per unit</param>
    /// <param name="request">Batch request with markup</param>
    /// <returns>Selling price</returns>
    public decimal CalculatePrice(decimal unitCost, BatchRequest request)
    {
        if (unitCost < 0)
        {
            _logger?.Warning("Invalid unit cost: {Cost:C}", unitCost);
            throw new ArgumentException("Unit cost cannot be negative", nameof(unitCost));
        }

        if (!ValidateParameters(request))
        {
            _logger?.Warning("Invalid parameters for cost-plus strategy");
            throw new ArgumentException("Invalid parameters");
        }

        var adjustedCost = unitCost + _fixedAdderPerUnit;
        var suggestedPrice = adjustedCost * (1 + request.Markup);

        _logger?.Debug("CostPlus calculation: ({UnitCost:C} + {FixedAdder:C}) * (1 + {Markup:P}) = {Price:C}",
            unitCost, _fixedAdderPerUnit, request.Markup, suggestedPrice);

        return suggestedPrice;
    }

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string GetStrategyName() => "Cost-Plus";

    /// <summary>
    /// Gets strategy description.
    /// </summary>
    public string GetDescription() =>
        "Adds a fixed amount per unit and applies a percentage markup. Suitable for recovering mixed cost structures.";

    /// <summary>
    /// Validates parameters.
    /// </summary>
    public bool ValidateParameters(BatchRequest request)
    {
        if (request == null)
        {
            return false;
        }

        return request.Markup >= 0 && request.Markup < 5m;
    }
}

/// <summary>
/// Competitive/Market-Based Pricing Strategy: calculates price based on market conditions.
/// This would typically look up competitor prices or market data.
/// For now, it implements a placeholder that behaves like target margin.
/// 
/// In a real implementation, this would:
/// - Query market databases
/// - Consider competitor pricing
/// - Adjust based on demand elasticity
/// - Factor in product differentiation
/// </summary>
public class CompetitivePricingStrategy : IPricingStrategy
{
    private readonly ILogger? _logger;
    private readonly Dictionary<int, decimal> _marketPrices = new();

    /// <summary>
    /// Initializes the competitive pricing strategy.
    /// </summary>
    /// <param name="logger">Optional logger</param>
    public CompetitivePricingStrategy(ILogger? logger = null)
    {
        _logger = logger;
        _logger?.Debug("CompetitivePricingStrategy initialized");
    }

    /// <summary>
    /// Registers a known market price for a product.
    /// In production, this would load from a market database.
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="marketPrice">The market price</param>
    public void SetMarketPrice(int productId, decimal marketPrice)
    {
        if (marketPrice >= 0)
        {
            _marketPrices[productId] = marketPrice;
            _logger?.Debug("Market price set for product {ProductId}: {Price:C}", productId, marketPrice);
        }
    }

    /// <summary>
    /// Calculates price considering market conditions and cost floor.
    /// </summary>
    /// <param name="unitCost">Cost per unit (minimum acceptable price)</param>
    /// <param name="request">Batch request with recipe/product info</param>
    /// <returns>Competitive price (at least equal to cost)</returns>
    public decimal CalculatePrice(decimal unitCost, BatchRequest request)
    {
        if (unitCost < 0)
        {
            throw new ArgumentException("Unit cost cannot be negative", nameof(unitCost));
        }

        // Fallback: use target margin approach if no market data available
        var targetMargin = request.TargetMarginPercent > 0 ? request.TargetMarginPercent : 0.30m;
        var minPrice = unitCost / (1 - targetMargin);

        _logger?.Debug("Competitive pricing: MinPrice={MinPrice:C} based on cost={Cost:C}, targetMargin={Margin:P}",
            minPrice, unitCost, targetMargin);

        return minPrice;
    }

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string GetStrategyName() => "Competitive";

    /// <summary>
    /// Gets strategy description.
    /// </summary>
    public string GetDescription() =>
        "Pricing based on market conditions and competitive analysis. Ensures prices meet market expectations while covering costs.";

    /// <summary>
    /// Validates parameters.
    /// </summary>
    public bool ValidateParameters(BatchRequest request)
    {
        return request != null && request.TargetMarginPercent >= 0 && request.TargetMarginPercent < 0.99m;
    }
}

/// <summary>
/// Strategy factory to create pricing strategies by name.
/// Useful for dynamically selecting strategies based on configuration.
/// </summary>
public static class PricingStrategyFactory
{
    /// <summary>
    /// Creates a pricing strategy by name.
    /// </summary>
    /// <param name="strategyName">The name of the strategy (e.g., "FixedMarkup", "TargetMargin")</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>The created strategy instance</returns>
    public static IPricingStrategy CreateStrategy(string strategyName, ILogger? logger = null)
    {
        var normalizedName = strategyName?.Trim().ToLowerInvariant() ?? "fixedmarkup";

        IPricingStrategy strategy = normalizedName switch
        {
            "fixedmarkup" => new FixedMarkupPricingStrategy(logger),
            "targetmargin" => new TargetMarginPricingStrategy(logger),
            "costplus" => new CostPlusPricingStrategy(0m, logger),
            "competitive" => new CompetitivePricingStrategy(logger),
            _ => new FixedMarkupPricingStrategy(logger)
        };

        logger?.Debug("Created pricing strategy: {StrategyType} from name '{StrategyName}'", strategy.GetType().Name, strategyName);

        return strategy;
    }

    /// <summary>
    /// Gets the list of available strategy names.
    /// </summary>
    /// <returns>Collection of available strategy names</returns>
    public static IEnumerable<string> GetAvailableStrategies()
    {
        return new[]
        {
            "FixedMarkup",
            "TargetMargin",
            "CostPlus",
            "Competitive"
        };
    }

    /// <summary>
    /// Gets descriptions for all available strategies.
    /// </summary>
    /// <returns>Dictionary mapping strategy names to descriptions</returns>
    public static Dictionary<string, string> GetStrategyDescriptions()
    {
        var strategies = new Dictionary<string, string>
        {
            { "FixedMarkup", new FixedMarkupPricingStrategy().GetDescription() },
            { "TargetMargin", new TargetMarginPricingStrategy().GetDescription() },
            { "CostPlus", new CostPlusPricingStrategy().GetDescription() },
            { "Competitive", new CompetitivePricingStrategy().GetDescription() }
        };

        return strategies;
    }
}
