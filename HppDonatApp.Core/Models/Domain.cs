using System;
using System.Collections.Generic;
using System.Linq;

namespace HppDonatApp.Core.Models;

/// <summary>
/// Represents a labor role with hourly rate and duration for batch production.
/// Used to calculate labor costs in pricing engine calculations.
/// </summary>
public class LaborRole
{
    /// <summary>
    /// Gets or sets the unique identifier for the labor role.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the labor role (e.g., "Baker", "Packaging Staff").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hourly rate in decimal for accurate monetary calculations.
    /// </summary>
    public decimal HourlyRate { get; set; }

    /// <summary>
    /// Gets or sets the number of hours required for this role per batch.
    /// </summary>
    public decimal Hours { get; set; }

    /// <summary>
    /// Gets or sets the total cost for this labor role (Hours * HourlyRate).
    /// This is a computed property typically used in cost breakdown summaries.
    /// </summary>
    public decimal TotalCost => Hours * HourlyRate;

    /// <summary>
    /// Validates that the labor role has sensible values.
    /// </summary>
    /// <returns>True if valid; otherwise false.</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Name) && HourlyRate >= 0 && Hours >= 0;
    }
}

/// <summary>
/// Represents a single ingredient line item with quantity and unit of measurement.
/// Used in recipe composition and batch cost calculations.
/// </summary>
public class RecipeItem
{
    /// <summary>
    /// Gets or sets the ingredient identifier that this recipe item references.
    /// </summary>
    public int IngredientId { get; set; }

    /// <summary>
    /// Gets or sets the quantity of ingredient needed.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Gets or sets the unit of measurement (e.g., "kg", "liter", "piece").
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the price per unit for this ingredient in this recipe context.
    /// This is used for direct pricing mode where cost = Quantity * PricePerUnit.
    /// </summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>
    /// Gets or sets the net weight/volume per pack.
    /// When set together with <see cref="PricePerPack"/>, item cost follows spreadsheet formula:
    /// Cost = Quantity * (PricePerPack / PackNetQuantity).
    /// </summary>
    public decimal? PackNetQuantity { get; set; }

    /// <summary>
    /// Gets or sets the price per pack.
    /// Used with <see cref="PackNetQuantity"/> for pack-based calculations.
    /// </summary>
    public decimal? PricePerPack { get; set; }

    /// <summary>
    /// Gets or sets a manual fixed cost override for this ingredient line.
    /// If provided, this value takes precedence over unit/pack calculations.
    /// </summary>
    public decimal? ManualCost { get; set; }

    /// <summary>
    /// Gets or sets whether this line contributes to dough-weight based output calculations.
    /// </summary>
    public bool IncludeInDoughWeight { get; set; } = true;

    /// <summary>
    /// Gets the total cost of this recipe item for a single batch.
    /// </summary>
    public decimal TotalCost => CalculateCost();

    /// <summary>
    /// Calculates line cost using manual, pack, or direct pricing mode.
    /// Priority: ManualCost -> Pack pricing -> PricePerUnit.
    /// </summary>
    /// <param name="batchMultiplier">Batch scale multiplier.</param>
    public decimal CalculateCost(decimal batchMultiplier = 1m)
    {
        var scaledQuantity = Quantity * Math.Max(0m, batchMultiplier);

        if (ManualCost.HasValue)
        {
            return ManualCost.Value * Math.Max(0m, batchMultiplier);
        }

        if (PackNetQuantity.HasValue && PackNetQuantity.Value > 0 &&
            PricePerPack.HasValue && PricePerPack.Value >= 0)
        {
            return (PricePerPack.Value / PackNetQuantity.Value) * scaledQuantity;
        }

        return scaledQuantity * PricePerUnit;
    }

    /// <summary>
    /// Validates the recipe item fields.
    /// </summary>
    /// <returns>True if all required fields are valid; otherwise false.</returns>
    public bool IsValid()
    {
        var hasManualCost = ManualCost.HasValue && ManualCost.Value >= 0;
        var hasPackPricing = PackNetQuantity.HasValue && PackNetQuantity.Value > 0 &&
                             PricePerPack.HasValue && PricePerPack.Value >= 0;
        var hasDirectPricing = PricePerUnit >= 0;

        return IngredientId > 0 &&
               Quantity > 0 &&
               !string.IsNullOrWhiteSpace(Unit) &&
               (hasManualCost || hasPackPricing || hasDirectPricing);
    }
}

/// <summary>
/// Represents a comprehensive request for batch cost calculation.
/// Contains all parameters needed to compute comprehensive production cost analysis.
/// </summary>
public class BatchRequest
{
    /// <summary>
    /// Gets or sets the collection of recipe items (ingredients with quantities and prices).
    /// </summary>
    public IEnumerable<RecipeItem> Items { get; set; } = new List<RecipeItem>();

    /// <summary>
    /// Gets or sets the batch multiplier (e.g., 1.0 for single batch, 2.5 for 2.5x batch).
    /// </summary>
    public decimal BatchMultiplier { get; set; } = 1m;

    /// <summary>
    /// Gets or sets the amount of oil used in liters.
    /// </summary>
    public decimal OilUsedLiters { get; set; }

    /// <summary>
    /// Gets or sets the price per liter for oil.
    /// </summary>
    public decimal OilPricePerLiter { get; set; }

    /// <summary>
    /// Gets or sets the cost of oil change in currency units.
    /// </summary>
    public decimal OilChangeCost { get; set; }

    /// <summary>
    /// Gets or sets the number of batches that can be produced per oil change.
    /// </summary>
    public int BatchesPerOilChange { get; set; } = 1;

    /// <summary>
    /// Gets or sets the energy consumption in kilowatt-hours.
    /// </summary>
    public decimal EnergyKwh { get; set; }

    /// <summary>
    /// Gets or sets the rate per kilowatt-hour.
    /// </summary>
    public decimal EnergyRatePerKwh { get; set; }

    /// <summary>
    /// Gets or sets the collection of labor roles involved in production.
    /// </summary>
    public IEnumerable<LaborRole> Labor { get; set; } = new List<LaborRole>();

    /// <summary>
    /// Gets or sets the allocated overhead cost for this batch.
    /// </summary>
    public decimal OverheadAllocated { get; set; }

    /// <summary>
    /// Gets or sets the theoretical output count from production.
    /// </summary>
    public int TheoreticalOutput { get; set; }

    /// <summary>
    /// Gets or sets the waste percentage (0.0 to 1.0, where 0.15 = 15% waste).
    /// </summary>
    public decimal WastePercent { get; set; }

    /// <summary>
    /// Gets or sets the cost of packaging per sellable unit.
    /// </summary>
    public decimal PackagingPerUnit { get; set; }

    /// <summary>
    /// Gets or sets the markup ratio (0.0 to 1.0+, where 0.50 = 50% markup).
    /// </summary>
    public decimal Markup { get; set; }

    /// <summary>
    /// Gets or sets the VAT percentage (0.0 to 1.0, where 0.10 = 10% VAT).
    /// </summary>
    public decimal VatPercent { get; set; }

    /// <summary>
    /// Gets or sets the rounding rule for suggested price (e.g., "0.05", "0.10", "1.00").
    /// </summary>
    public string RoundingRule { get; set; } = "0.05";

    /// <summary>
    /// Gets or sets the pricing strategy type name (e.g., "FixedMarkup", "TargetMargin").
    /// </summary>
    public string PricingStrategy { get; set; } = "FixedMarkup";

    /// <summary>
    /// For TargetMargin strategy: target margin percentage (0.0 to 1.0).
    /// </summary>
    public decimal TargetMarginPercent { get; set; } = 0.30m;

    /// <summary>
    /// Enables output calculation based on dough weight instead of theoretical output.
    /// Spreadsheet formula equivalent: Jumlah donat = TotalBeratDough / BeratPerDonat.
    /// </summary>
    public bool UseWeightBasedOutput { get; set; }

    /// <summary>
    /// Weight of one donut in grams for weight-based output calculation.
    /// Default follows workbook sample (25g).
    /// </summary>
    public decimal DonutWeightGrams { get; set; } = 25m;

    /// <summary>
    /// Topping usage per donut in grams (workbook N2).
    /// </summary>
    public decimal ToppingWeightPerDonutGrams { get; set; }

    /// <summary>
    /// Topping pack weight in grams (workbook O2).
    /// </summary>
    public decimal ToppingPackWeightGrams { get; set; }

    /// <summary>
    /// Topping pack price (workbook P2).
    /// </summary>
    public decimal ToppingPackPrice { get; set; }

    /// <summary>
    /// Optional target profit amount per batch in currency.
    /// If set above zero, engine will compute units required to hit this target.
    /// </summary>
    public decimal TargetProfitPerBatch { get; set; }

    /// <summary>
    /// Optional monthly fixed cost for break-even planning.
    /// If set above zero, engine computes monthly break-even units.
    /// </summary>
    public decimal MonthlyFixedCost { get; set; }

    /// <summary>
    /// Estimated ingredient/input volatility (0 to 1).
    /// Higher values increase risk buffer in recommended selling prices.
    /// </summary>
    public decimal PriceVolatilityPercent { get; set; } = 0.08m;

    /// <summary>
    /// Risk appetite for pricing decisions (0 to 1).
    /// 0 = conservative, 1 = aggressive.
    /// </summary>
    public decimal RiskAppetitePercent { get; set; } = 0.50m;

    /// <summary>
    /// Market pressure adjustment (-0.50 to 0.50).
    /// Negative values push prices down (high competition), positive values push up (strong demand).
    /// </summary>
    public decimal MarketPressurePercent { get; set; }

    /// <summary>
    /// Validates the batch request for required and sensible values.
    /// </summary>
    /// <returns>True if valid; otherwise false.</returns>
    public bool IsValid()
    {
        var hasWeightBasedOutput = UseWeightBasedOutput && DonutWeightGrams > 0;
        var hasTheoreticalOutput = TheoreticalOutput > 0;
        var hasValidOutputSource = hasTheoreticalOutput || hasWeightBasedOutput;

        var hasAnyToppingInput = ToppingWeightPerDonutGrams > 0 || ToppingPackWeightGrams > 0 || ToppingPackPrice > 0;
        var hasValidToppingInputs = !hasAnyToppingInput ||
            (ToppingWeightPerDonutGrams >= 0 && ToppingPackWeightGrams > 0 && ToppingPackPrice >= 0);
        var hasValidRiskInputs = PriceVolatilityPercent >= 0 && PriceVolatilityPercent <= 1 &&
                                 RiskAppetitePercent >= 0 && RiskAppetitePercent <= 1 &&
                                 MarketPressurePercent >= -0.50m && MarketPressurePercent <= 0.50m;

        return Items.Any(i => i.IsValid()) &&
               BatchMultiplier > 0 &&
               (OilUsedLiters == 0 || OilPricePerLiter >= 0) &&
               (EnergyKwh == 0 || EnergyRatePerKwh >= 0) &&
               BatchesPerOilChange > 0 &&
               hasValidOutputSource &&
               WastePercent >= 0 && WastePercent < 1 &&
               Markup >= 0 &&
               VatPercent >= 0 && VatPercent < 1 &&
               hasValidToppingInputs &&
               TargetProfitPerBatch >= 0 &&
               MonthlyFixedCost >= 0 &&
               hasValidRiskInputs;
    }
}

/// <summary>
/// Comprehensive result of a batch cost calculation.
/// Contains detailed breakdown of all costs and pricing information.
/// </summary>
public class BatchCostResult
{
    /// <summary>
    /// Gets or sets the total cost of all ingredient items in the batch.
    /// Formula: Sum(RecipeItem.Quantity * RecipeItem.PricePerUnit * BatchMultiplier)
    /// </summary>
    public decimal IngredientCost { get; set; }

    /// <summary>
    /// Gets or sets the cost of oil used for frying/cooking in this batch.
    /// Formula: OilUsedLiters * OilPricePerLiter
    /// </summary>
    public decimal OilCost { get; set; }

    /// <summary>
    /// Gets or sets the amortized oil change cost spread across batches.
    /// Formula: OilChangeCost / BatchesPerOilChange
    /// </summary>
    public decimal OilAmortization { get; set; }

    /// <summary>
    /// Gets or sets the cost of energy consumed in the batch.
    /// Formula: EnergyKwh * EnergyRatePerKwh
    /// </summary>
    public decimal EnergyCost { get; set; }

    /// <summary>
    /// Gets or sets the total labor cost for the batch.
    /// Formula: Sum(LaborRole.Hours * LaborRole.HourlyRate)
    /// </summary>
    public decimal LaborCost { get; set; }

    /// <summary>
    /// Gets or sets the allocated overhead cost.
    /// </summary>
    public decimal OverheadCost { get; set; }

    /// <summary>
    /// Gets or sets the total packaging cost.
    /// Formula: PackagingPerUnit * SellableUnits
    /// </summary>
    public decimal PackagingCost { get; set; }

    /// <summary>
    /// Gets or sets the total cost of producing this batch.
    /// Formula: IngredientCost + OilCost + OilAmortization + EnergyCost + LaborCost + OverheadAllocated + PackagingCost
    /// </summary>
    public decimal TotalBatchCost { get; set; }

    /// <summary>
    /// Gets or sets the cost per single sellable unit.
    /// Formula: TotalBatchCost / SellableUnits
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Gets or sets the number of sellable units after accounting for waste.
    /// Formula: Floor(TheoreticalOutput * (1 - WastePercent))
    /// </summary>
    public int SellableUnits { get; set; }

    /// <summary>
    /// Gets or sets total dough weight from ingredients included in dough calculation.
    /// </summary>
    public decimal DoughWeightTotal { get; set; }

    /// <summary>
    /// Gets or sets donut count derived from dough weight and donut weight.
    /// </summary>
    public decimal DonutCountByWeight { get; set; }

    /// <summary>
    /// Gets or sets topping cost per donut.
    /// Formula: ToppingPackPrice / ToppingPackWeightGrams * ToppingWeightPerDonutGrams
    /// </summary>
    public decimal ToppingCostPerDonut { get; set; }

    /// <summary>
    /// Gets or sets total cost per donut including topping.
    /// Formula: UnitCost + ToppingCostPerDonut
    /// </summary>
    public decimal CostPerDonutWithTopping { get; set; }

    /// <summary>
    /// Gets or sets risk buffer percentage derived from volatility, waste, and cost structure.
    /// </summary>
    public decimal RiskBufferPercent { get; set; }

    /// <summary>
    /// Gets or sets minimum safe price per unit after risk buffer.
    /// </summary>
    public decimal MinimumSafePrice { get; set; }

    /// <summary>
    /// Gets or sets conservative recommended price.
    /// </summary>
    public decimal SuggestedPriceConservative { get; set; }

    /// <summary>
    /// Gets or sets aggressive recommended price.
    /// </summary>
    public decimal SuggestedPriceAggressive { get; set; }

    /// <summary>
    /// Gets or sets lower bound of recommended price range.
    /// </summary>
    public decimal RecommendedPriceLow { get; set; }

    /// <summary>
    /// Gets or sets upper bound of recommended price range.
    /// </summary>
    public decimal RecommendedPriceHigh { get; set; }

    /// <summary>
    /// Gets or sets the suggested selling price per unit (before VAT) after applying rounding rules.
    /// Formula: ApplyRounding(UnitCost * (1 + Markup), RoundingRule)
    /// </summary>
    public decimal SuggestedPrice { get; set; }

    /// <summary>
    /// Gets or sets the suggested selling price per unit including VAT.
    /// Formula: SuggestedPrice * (1 + VatPercent)
    /// </summary>
    public decimal PriceIncVat { get; set; }

    /// <summary>
    /// Gets or sets the profit margin as a percentage (0.0 to 1.0).
    /// Formula: (SuggestedPrice - UnitCost) / SuggestedPrice
    /// </summary>
    public decimal Margin { get; set; }

    /// <summary>
    /// Gets or sets contribution margin per unit at suggested price.
    /// </summary>
    public decimal ContributionMarginPerUnit { get; set; }

    /// <summary>
    /// Gets or sets profit per unit at suggested price.
    /// </summary>
    public decimal ProfitPerUnitAtSuggestedPrice { get; set; }

    /// <summary>
    /// Gets or sets total profit for this batch at suggested price.
    /// </summary>
    public decimal ProfitPerBatchAtSuggestedPrice { get; set; }

    /// <summary>
    /// Gets or sets units needed to achieve target profit per batch.
    /// </summary>
    public int UnitsForTargetProfit { get; set; }

    /// <summary>
    /// Gets or sets monthly break-even units when monthly fixed cost is provided.
    /// </summary>
    public int MonthlyBreakEvenUnits { get; set; }

    /// <summary>
    /// Gets or sets normalized cost volatility score (0 to 1).
    /// </summary>
    public decimal CostVolatilityScore { get; set; }

    /// <summary>
    /// Gets or sets confidence score of pricing recommendation (0 to 1).
    /// </summary>
    public decimal PricingConfidenceScore { get; set; }

    /// <summary>
    /// Gets or sets short recommendation note for operators.
    /// </summary>
    public string RecommendationNote { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets warning messages emitted by smart pricing heuristics.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Gets or sets a detailed breakdown dictionary mapping cost category names to decimal values.
    /// Contains entries for all cost components.
    /// </summary>
    public Dictionary<string, decimal> BreakdownDictionary { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when this calculation was performed.
    /// </summary>
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the total of all cost components (for verification).
    /// </summary>
    public decimal TotalCosts => IngredientCost + OilCost + OilAmortization + EnergyCost + LaborCost + OverheadCost + PackagingCost;

    /// <summary>
    /// Gets a summary string representation of the cost breakdown.
    /// </summary>
    public override string ToString()
    {
        return $"BatchCostResult: UnitCost={UnitCost:C}, SuggestedPrice={SuggestedPrice:C}, Margin={Margin:P1}, SellableUnits={SellableUnits}";
    }
}

/// <summary>
/// Represents a price history entry for an ingredient at a specific point in time.
/// Used to track price changes and enable price trend analysis.
/// </summary>
public class PriceHistoryEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this price history record.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the ingredient identifier this history record belongs to.
    /// </summary>
    public int IngredientId { get; set; }

    /// <summary>
    /// Gets or sets the price at the recorded date.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Gets or sets the date when this price was recorded.
    /// </summary>
    public DateTime RecordedDate { get; set; }

    /// <summary>
    /// Gets or sets optional notes about why the price changed.
    /// </summary>
    public string? Notes { get; set; }
}
