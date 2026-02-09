using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using HppDonatApp.Data.Entities;

namespace HppDonatApp.Data;

/// <summary>
/// Entity Framework Core DbContext for the HPP Donat application.
/// Manages database connections, entity mapping, and persistence operations.
/// </summary>
public class HppDonatDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the database context.
    /// </summary>
    /// <param name="options">Database context options</param>
    public HppDonatDbContext(DbContextOptions<HppDonatDbContext> options) : base(options)
    {
    }

    // DbSet properties for each entity
    public DbSet<IngredientEntity> Ingredients { get; set; }
    public DbSet<PriceHistoryEntity> PriceHistories { get; set; }
    public DbSet<RecipeEntity> Recipes { get; set; }
    public DbSet<RecipeIngredientEntity> RecipeIngredients { get; set; }
    public DbSet<ScenarioEntity> Scenarios { get; set; }
    public DbSet<ScenarioResultEntity> ScenarioResults { get; set; }
    public DbSet<SettingEntity> Settings { get; set; }
    public DbSet<AuditLogEntity> AuditLogs { get; set; }

    /// <summary>
    /// Configures the entity models and their relationships.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Ingredient entity
        modelBuilder.Entity<IngredientEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Unit).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CurrentPrice).HasPrecision(18, 2);
            entity.HasIndex(e => e.Name).IsUnique();
            
            entity.HasMany(e => e.PriceHistory)
                .WithOne(ph => ph.Ingredient)
                .HasForeignKey(ph => ph.IngredientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure PriceHistory entity
        modelBuilder.Entity<PriceHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.IngredientId, e.RecordedDate });
        });

        // Configure Recipe entity
        modelBuilder.Entity<RecipeEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.WastePercent).HasPrecision(5, 4);
            entity.HasIndex(e => e.Name);

            entity.HasMany(e => e.RecipeIngredients)
                .WithOne(ri => ri.Recipe)
                .HasForeignKey(ri => ri.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure RecipeIngredient entity
        modelBuilder.Entity<RecipeIngredientEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
            
            entity.HasOne(e => e.Recipe)
                .WithMany(r => r.RecipeIngredients)
                .HasForeignKey(e => e.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Ingredient)
                .WithMany(i => i.RecipeIngredients)
                .HasForeignKey(e => e.IngredientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.RecipeId, e.IngredientId }).IsUnique();
        });

        // Configure Scenario entity
        modelBuilder.Entity<ScenarioEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.BatchMultiplier).HasPrecision(10, 4);
            entity.Property(e => e.OilUsedLiters).HasPrecision(10, 2);
            entity.Property(e => e.OilPricePerLiter).HasPrecision(18, 2);
            entity.Property(e => e.Markup).HasPrecision(5, 4);
            entity.Property(e => e.VatPercent).HasPrecision(5, 4);
            entity.Property(e => e.TargetMarginPercent).HasPrecision(5, 4);

            entity.HasOne(e => e.Recipe)
                .WithMany(r => r.Scenarios)
                .HasForeignKey(e => e.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure ScenarioResult entity
        modelBuilder.Entity<ScenarioResultEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IngredientCost).HasPrecision(18, 2);
            entity.Property(e => e.TotalBatchCost).HasPrecision(18, 2);
            entity.Property(e => e.UnitCost).HasPrecision(18, 2);
            entity.Property(e => e.SuggestedPrice).HasPrecision(18, 2);
            entity.Property(e => e.PriceIncVat).HasPrecision(18, 2);
            entity.Property(e => e.Margin).HasPrecision(5, 4);

            entity.HasOne(e => e.Scenario)
                .WithMany(s => s.Results)
                .HasForeignKey(e => e.ScenarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Setting entity
        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnType("TEXT");
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // Configure AuditLog entity
        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
        });
    }

    /// <summary>
    /// Applies the configured data type conversions and precision rules.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Ensure all decimal properties use proper precision for currency
        configurationBuilder
            .Properties<decimal>()
            .HaveConversion<decimal>()
            .HavePrecision(18, 2);
    }
}
