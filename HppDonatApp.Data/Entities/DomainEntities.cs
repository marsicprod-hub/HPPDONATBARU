using System;
using System.Collections.Generic;

namespace HppDonatApp.Data.Entities;

/// <summary>
/// EF Core entity representing a single ingredient used in recipes.
/// </summary>
public class IngredientEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty; // e.g., "kg", "liter"
    public decimal CurrentPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ICollection<PriceHistoryEntity> PriceHistory { get; set; } = new List<PriceHistoryEntity>();
    public virtual ICollection<RecipeIngredientEntity> RecipeIngredients { get; set; } = new List<RecipeIngredientEntity>();
}

/// <summary>
/// EF Core entity representing price history for an ingredient.
/// Enables price trend analysis and historical cost calculations.
/// </summary>
public class PriceHistoryEntity
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public decimal Price { get; set; }
    public DateTime RecordedDate { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    // Navigation property
    public virtual IngredientEntity Ingredient { get; set; } = null!;
}

/// <summary>
/// EF Core entity representing a donut recipe.
/// Contains the definition of a way to make donuts with specific ingredients and quantities.
/// </summary>
public class RecipeEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TheoreticalOutput { get; set; } // Units per batch
    public decimal WastePercent { get; set; } = 0.10m; // 10% waste by default
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ICollection<RecipeIngredientEntity> RecipeIngredients { get; set; } = new List<RecipeIngredientEntity>();
    public virtual ICollection<ScenarioEntity> Scenarios { get; set; } = new List<ScenarioEntity>();
}

/// <summary>
/// EF Core junction entity connecting recipes to ingredients with quantities.
/// Many-to-many relationship with additional data (quantity, unit).
/// </summary>
public class RecipeIngredientEntity
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quantity { get; set; }
    public int DisplayOrder { get; set; } = 0; // For ordering ingredients in UI

    // Navigation properties
    public virtual RecipeEntity Recipe { get; set; } = null!;
    public virtual IngredientEntity Ingredient { get; set; } = null!;
}

/// <summary>
/// EF Core entity representing pricing scenarios and what-if analyses.
/// Allows users to save and compare different cost calculation scenarios.
/// </summary>
public class ScenarioEntity
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    // Batch parameters
    public decimal BatchMultiplier { get; set; } = 1m;
    public decimal OilUsedLiters { get; set; }
    public decimal OilPricePerLiter { get; set; }
    public decimal OilChangeCost { get; set; }
    public int BatchesPerOilChange { get; set; } = 1;
    
    // Energy
    public decimal EnergyKwh { get; set; }
    public decimal EnergyRatePerKwh { get; set; }
    
    // Costs
    public decimal OverheadAllocated { get; set; }
    public decimal PackagingPerUnit { get; set; }
    
    // Pricing
    public decimal Markup { get; set; } = 0.50m;
    public decimal VatPercent { get; set; } = 0.10m;
    public string RoundingRule { get; set; } = "0.05";
    public string PricingStrategy { get; set; } = "FixedMarkup";
    public decimal TargetMarginPercent { get; set; } = 0.30m;

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Labor (JSON serialized for simplicity)
    public string LaborJson { get; set; } = "[]";

    // Navigation property
    public virtual RecipeEntity Recipe { get; set; } = null!;
    public virtual ICollection<ScenarioResultEntity> Results { get; set; } = new List<ScenarioResultEntity>();
}

/// <summary>
/// EF Core entity storing cached cost calculation results for scenarios.
/// Allows quick comparison without recalculating.
/// </summary>
public class ScenarioResultEntity
{
    public int Id { get; set; }
    public int ScenarioId { get; set; }
    
    // Cost breakdown
    public decimal IngredientCost { get; set; }
    public decimal OilCost { get; set; }
    public decimal OilAmortization { get; set; }
    public decimal EnergyCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal PackagingCost { get; set; }
    public decimal TotalBatchCost { get; set; }
    
    // Unit cost and pricing
    public decimal UnitCost { get; set; }
    public int SellableUnits { get; set; }
    public decimal SuggestedPrice { get; set; }
    public decimal PriceIncVat { get; set; }
    public decimal Margin { get; set; }
    
    // Metadata
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public virtual ScenarioEntity Scenario { get; set; } = null!;
}

/// <summary>
/// EF Core entity representing application settings/configuration.
/// Stores user preferences and defaults.
/// </summary>
public class SettingEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DataType { get; set; } = "string"; // string, int, decimal, bool, datetime
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// EF Core entity for versioning and audit trail.
/// Tracks changes to recipes and ingredients over time.
/// </summary>
public class AuditLogEntity
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty; // "Recipe", "Ingredient"
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty; // "Created", "Updated", "Deleted"
    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
