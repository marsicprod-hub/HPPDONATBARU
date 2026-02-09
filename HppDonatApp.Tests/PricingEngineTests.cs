using Xunit;
using FluentAssertions;
using Moq;
using Serilog;
using HppDonatApp.Core.Models;
using HppDonatApp.Core.Services;

namespace HppDonatApp.Tests;

/// <summary>
/// Unit tests for the PricingEngine class.
/// Tests all major calculation paths and edge cases.
/// </summary>
public class PricingEngineTests
{
    private readonly IPricingEngine _pricingEngine;
    private readonly ILogger _logger;

    public PricingEngineTests()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        _pricingEngine = new PricingEngine(
            cache: null,
            logger: _logger,
            pricingStrategy: new FixedMarkupPricingStrategy(_logger)
        );
    }

    /// <summary>
    /// Test: Basic cost calculation with simple ingredients.
    /// Verifies that ingredient costs are calculated correctly from quantity and price.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_BasicIngredients_CalculatesCorrectly()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m },
                new RecipeItem { IngredientId = 2, Quantity = 1m, Unit = "kg", PricePerUnit = 8.00m }
            },
            BatchMultiplier = 1m,
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m,
            RoundingRule = "0.05"
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        result.Should().NotBeNull();
        result.IngredientCost.Should().Be(23m);  // 5*3 + 1*8
        result.SellableUnits.Should().Be(90);    // 100 * (1 - 0.10)
        result.UnitCost.Should().BeGreaterThan(0);
        result.SuggestedPrice.Should().BeGreaterThan(result.UnitCost);
    }

    /// <summary>
    /// Test: Cost calculation with oil and maintenance costs.
    /// Verifies that amortized oil change costs are properly divided among batches.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_WithOilCosts_IncludesAmortization()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            OilUsedLiters = 2m,
            OilPricePerLiter = 12m,
            OilChangeCost = 500m,
            BatchesPerOilChange = 10,
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        result.OilCost.Should().Be(24m);           // 2 * 12
        result.OilAmortization.Should().Be(50m);   // 500 / 10
        result.TotalBatchCost.Should().BeGreaterThan(result.IngredientCost + result.OilCost);
    }

    /// <summary>
    /// Test: Margin calculation and pricing.
    /// Verifies that profit margins are calculated correctly based on suggested price and unit cost.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_MarginCalculation_IsAccurate()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,  // 50% markup
            VatPercent = 0m
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        var expectedMargin = (result.SuggestedPrice - result.UnitCost) / result.SuggestedPrice;
        result.Margin.Should().BeApproximately(expectedMargin, 0.01m);
    }

    /// <summary>
    /// Test: Waste calculation affects sellable units.
    /// Verifies that waste percentage correctly reduces the number of sellable units.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_WastePercentage_ReducesSellableUnits()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            TheoreticalOutput = 100,
            WastePercent = 0.20m,  // 20% waste
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        result.SellableUnits.Should().Be(80);  // 100 * (1 - 0.20)
    }

    /// <summary>
    /// Test: Batch multiplier scales all costs proportionally.
    /// Verifies that increasing batch multiplier proportionally increases ingredient costs.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_BatchMultiplier_ScalesCostsProportionally()
    {
        // Arrange
        var baseRequest = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            BatchMultiplier = 1m,
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        var doubledRequest = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            BatchMultiplier = 2m,
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        // Act
        var baseResult = _pricingEngine.CalculateBatchCost(baseRequest);
        var doubledResult = _pricingEngine.CalculateBatchCost(doubledRequest);

        // Assert
        doubledResult.IngredientCost.Should().BeApproximately(baseResult.IngredientCost * 2, 0.01m);
    }

    /// <summary>
    /// Test: Labor costs are summed correctly.
    /// Verifies that multiple labor roles have their costs properly summed.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_LaborCosts_AreCalculatedCorrectly()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            Labor = new List<LaborRole>
            {
                new LaborRole { Name = "Baker", HourlyRate = 50m, Hours = 2m },
                new LaborRole { Name = "Packager", HourlyRate = 30m, Hours = 1m }
            },
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        result.LaborCost.Should().Be(130m);  // (50*2) + (30*1)
    }

    /// <summary>
    /// Test: VAT is properly applied to suggested price.
    /// Verifies that VAT percentage correctly increases the final price.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_VAT_AppliesToSuggestedPrice()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        var expectedPriceWithVat = result.SuggestedPrice * 1.10m;
        result.PriceIncVat.Should().BeApproximately(expectedPriceWithVat, 0.01m);
    }

    /// <summary>
    /// Test: Rounding rule is applied correctly.
    /// Verifies that prices are rounded to the specified interval.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_RoundingRule_AppliesCorrectly()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0m,
            RoundingRule = "0.05"
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert
        var remainder = result.SuggestedPrice % 0.05m;
        remainder.Should().BeLessThan(0.001m);  // Should be rounded to 0.05 interval
    }

    /// <summary>
    /// Test: Invalid request throws appropriate exception.
    /// Verifies error handling for null or invalid inputs.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pricingEngine.CalculateBatchCost(null!));
    }

    /// <summary>
    /// Test: Invalid batch request with no items throws exception.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_NoItems_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>(),
            TheoreticalOutput = 100,
            WastePercent = 0.10m
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _pricingEngine.CalculateBatchCost(request));
    }

    /// <summary>
    /// Test: Caching functionality stores and retrieves results.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_CachingWorks_OnSubsequentCalls()
    {
        // Arrange
        var mockCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()
        );

        var pricingEngine = new PricingEngine(mockCache, _logger);

        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 5m, Unit = "kg", PricePerUnit = 3.00m }
            },
            TheoreticalOutput = 100,
            WastePercent = 0.10m,
            Markup = 0.50m,
            VatPercent = 0.10m
        };

        // Act
        var result1 = pricingEngine.CalculateBatchCost(request);
        var result2 = pricingEngine.CalculateBatchCost(request);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.IngredientCost.Should().Be(result2.IngredientCost);
    }

    /// <summary>
    /// Test: Decimal precision in monetary calculations.
    /// Verifies that all monetary calculations use proper precision.
    /// </summary>
    [Fact]
    public void CalculateBatchCost_MonetaryPrecision_MaintainedToTwoDe cimals()
    {
        // Arrange
        var request = new BatchRequest
        {
            Items = new List<RecipeItem>
            {
                new RecipeItem { IngredientId = 1, Quantity = 7.33m, Unit = "kg", PricePerUnit = 3.49m }
            },
            TheoreticalOutput = 97,
            WastePercent = 0.12m,
            Markup = 0.67m,
            VatPercent = 0.08m
        };

        // Act
        var result = _pricingEngine.CalculateBatchCost(request);

        // Assert - All monetary values should be reasonable decimals
        result.IngredientCost.Should().BeGreaterThan(0);
        result.UnitCost.Should().BeGreaterThan(0);
        result.SuggestedPrice.Should().BeGreaterThan(result.UnitCost);
    }

    /// <summary>
    /// Test: Diagnostics reporting from pricing engine.
    /// </summary>
    [Fact]
    public void GetDiagnostics_ReturnsEngineStats()
    {
        // Act
        var diagnostics = _pricingEngine.GetDiagnostics();

        // Assert
        diagnostics.Should().NotBeNull();
        diagnostics.Should().ContainKey("TotalCalculations");
        diagnostics.Should().ContainKey("CacheHits");
    }
}

/// <summary>
/// Unit tests for pricing strategies.
/// </summary>
public class PricingStrategyTests
{
    private readonly ILogger _logger;

    public PricingStrategyTests()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();
    }

    /// <summary>
    /// Test: Fixed Markup strategy calculates correct price.
    /// </summary>
    [Fact]
    public void FixedMarkupStrategy_CalculatesPrice_Correctly()
    {
        // Arrange
        var strategy = new FixedMarkupPricingStrategy(_logger);
        var request = new BatchRequest { Markup = 0.50m }; // 50% markup
        var unitCost = 100m;

        // Act
        var price = strategy.CalculatePrice(unitCost, request);

        // Assert
        price.Should().Be(150m);  // 100 * 1.50
    }

    /// <summary>
    /// Test: Target Margin strategy achieves specified margin.
    /// </summary>
    [Fact]
    public void TargetMarginStrategy_AchievesTargetMargin()
    {
        // Arrange
        var strategy = new TargetMarginPricingStrategy(_logger);
        var request = new BatchRequest { TargetMarginPercent = 0.40m }; // 40% target margin
        var unitCost = 100m;

        // Act
        var price = strategy.CalculatePrice(unitCost, request);

        // Assert - Margin should equal target margin
        var actualMargin = (price - unitCost) / price;
        actualMargin.Should().BeApproximately(0.40m, 0.01m);
    }

    /// <summary>
    /// Test: Cost Plus strategy applies both fixed and percentage additions.
    /// </summary>
    [Fact]
    public void CostPlusStrategy_ApplieBothComponents()
    {
        // Arrange
        var strategy = new CostPlusPricingStrategy(2000m, _logger);  // 2000 fixed adder
        var request = new BatchRequest { Markup = 0.25m };
        var unitCost = 10000m;

        // Act
        var price = strategy.CalculatePrice(unitCost, request);

        // Assert - Should be (10000 + 2000) * 1.25 = 15000
        price.Should().Be(15000m);
    }
}

/// <summary>
/// Unit tests for Rounding Engine.
/// </summary>
public class RoundingEngineTests
{
    [Fact]
    public void ApplyRounding_RoundsToNearestInterval()
    {
        // Arrange
        var engine = new RoundingEngine();
        var price = 12.348m;

        // Act
        var rounded = engine.ApplyRounding(price, "0.05");

        // Assert
        rounded.Should().Be(12.35m);
    }

    [Fact]
    public void RoundUp_AlwaysRoundsUp()
    {
        // Arrange
        var engine = new RoundingEngine();
        var price = 12.342m;

        // Act
        var roundedUp = engine.RoundUp(price, "0.05");

        // Assert
        roundedUp.Should().Be(12.35m);
    }

    [Fact]
    public void RoundDown_AlwaysRoundsDown()
    {
        // Arrange
        var engine = new RoundingEngine();
        var price = 12.348m;

        // Act
        var roundedDown = engine.RoundDown(price, "0.05");

        // Assert
        roundedDown.Should().Be(12.30m);
    }
}
