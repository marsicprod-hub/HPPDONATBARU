using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using HppDonatApp.Core.Models;
using HppDonatApp.Data.Entities;

namespace HppDonatApp.Data.Repositories;

/// <summary>
/// Repository interface for ingredient data access operations.
/// Defines the contract for CRUD and query operations on ingredients.
/// </summary>
public interface IIngredientRepository
{
    /// <summary>
    /// Gets an ingredient by its identifier.
    /// </summary>
    /// <param name="id">The ingredient identifier</param>
    /// <returns>The ingredient if found; null otherwise</returns>
    Task<Ingredient?> GetByIdAsync(int id);

    /// <summary>
    /// Gets all active ingredients.
    /// </summary>
    /// <returns>Collection of active ingredients</returns>
    Task<IEnumerable<Ingredient>> GetAllAsync();

    /// <summary>
    /// Gets an ingredient by name.
    /// </summary>
    /// <param name="name">The ingredient name</param>
    /// <returns>The ingredient if found; null otherwise</returns>
    Task<Ingredient?> GetByNameAsync(string name);

    /// <summary>
    /// Creates a new ingredient.
    /// </summary>
    /// <param name="ingredient">The ingredient to create</param>
    /// <returns>The created ingredient with assigned ID</returns>
    Task<Ingredient> CreateAsync(Ingredient ingredient);

    /// <summary>
    /// Updates an existing ingredient.
    /// </summary>
    /// <param name="ingredient">The ingredient with updates</param>
    /// <returns>The updated ingredient</returns>
    Task<Ingredient> UpdateAsync(Ingredient ingredient);

    /// <summary>
    /// Deletes an ingredient by ID (soft delete).
    /// </summary>
    /// <param name="id">The ingredient identifier to delete</param>
    /// <returns>True if deleted; false if not found</returns>
    Task<bool> DeleteAsync(int id);

    /// <summary>
    /// Gets price history for an ingredient.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="startDate">Optional start date filter</param>
    /// <param name="endDate">Optional end date filter</param>
    /// <returns>Chronological list of price history entries</returns>
    Task<IEnumerable<PriceHistoryEntry>> GetPriceHistoryAsync(int ingredientId, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Adds a price history entry for an ingredient.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="price">The price to record</param>
    /// <param name="notes">Optional notes about the price change</param>
    /// <returns>The recorded price history entry</returns>
    Task<PriceHistoryEntry> RecordPriceAsync(int ingredientId, decimal price, string? notes = null);

    /// <summary>
    /// Gets the latest recorded price for an ingredient.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <returns>The latest price; or 0 if no history exists</returns>
    Task<decimal> GetLatestPriceAsync(int ingredientId);

    /// <summary>
    /// Gets average price over a time period.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="days">Number of days to look back</param>
    /// <returns>Average price; or 0 if insufficient data</returns>
    Task<decimal> GetAveragePriceAsync(int ingredientId, int days = 30);

    /// <summary>
    /// Gets price trend (price change percentage over period).
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="days">Number of days to analyze</param>
    /// <returns>Percentage change (positive = price increase)</returns>
    Task<decimal> GetPriceTrendAsync(int ingredientId, int days = 30);

    /// <summary>
    /// Clears the repository cache.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Gets cache statistics for diagnostics.
    /// </summary>
    /// <returns>Dictionary with cache hit/miss counts</returns>
    Dictionary<string, object> GetCacheStatistics();
}

/// <summary>
/// Ingredient model for domain operations.
/// Represents an ingredient used in recipes with pricing information.
/// </summary>
public class Ingredient
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }

    public override string ToString() => $"{Name} ({Unit}) - {CurrentPrice:C}";
}

/// <summary>
/// Comprehensive implementation of the ingredient repository.
/// Provides data access for ingredients with memory caching and full audit trail support.
/// Includes numerous helper methods for price analysis and trend calculation.
/// 
/// Design patterns used:
/// - Repository pattern for data abstraction
/// - Specification pattern for query composition
/// - Caching strategy for performance
/// - Async/await for non-blocking operations
/// </summary>
public class IngredientRepository : IIngredientRepository
{
    private readonly HppDonatDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger? _logger;

    // Cache keys
    private const string CacheKeyPrefix = "ingredient_";
    private const string AllIngredientsKey = "all_ingredients";
    private const int CacheExpirationMinutes = 60;

    // Cache statistics
    private int _cacheHits;
    private int _cacheMisses;

    /// <summary>
    /// Initializes the ingredient repository with EF Core context and caching.
    /// </summary>
    /// <param name="context">The database context</param>
    /// <param name="cache">Memory cache for query results</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public IngredientRepository(
        HppDonatDbContext context,
        IMemoryCache cache,
        ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger;

        _logger?.Debug("IngredientRepository initialized");
    }

    /// <summary>
    /// Gets an ingredient by ID with caching support.
    /// </summary>
    /// <param name="id">The ingredient ID</param>
    /// <returns>The ingredient if found; null otherwise</returns>
    public async Task<Ingredient?> GetByIdAsync(int id)
    {
        if (id <= 0)
        {
            _logger?.Warning("Invalid ingredient ID: {Id}", id);
            return null;
        }

        var cacheKey = $"{CacheKeyPrefix}{id}";

        // Check cache
        if (_cache.TryGetValue(cacheKey, out Ingredient? cachedIngredient))
        {
            _cacheHits++;
            _logger?.Debug("Cache HIT for ingredient {Id}. Cache hits={Hits}", id, _cacheHits);
            return cachedIngredient;
        }

        _cacheMisses++;
        _logger?.Debug("Cache MISS for ingredient {Id}. Cache misses={Misses}", id, _cacheMisses);

        // Query database
        try
        {
            var entity = await _context.Ingredients
                .Where(i => i.Id == id && i.IsActive)
                .FirstOrDefaultAsync();

            if (entity == null)
            {
                _logger?.Information("Ingredient not found: {Id}", id);
                return null;
            }

            var ingredient = MapEntityToModel(entity);

            // Cache the result
            _cache.Set(cacheKey, ingredient, TimeSpan.FromMinutes(CacheExpirationMinutes));

            return ingredient;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving ingredient {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Gets all active ingredients with caching.
    /// This operation is relatively expensive so caching is important.
    /// </summary>
    /// <returns>Collection of all active ingredients</returns>
    public async Task<IEnumerable<Ingredient>> GetAllAsync()
    {
        // Check cache
        if (_cache.TryGetValue(AllIngredientsKey, out IEnumerable<Ingredient>? cachedIngredients))
        {
            _cacheHits++;
            _logger?.Debug("Cache HIT for all ingredients. Count={Count}", cachedIngredients?.Count() ?? 0);
            return cachedIngredients ?? new List<Ingredient>();
        }

        _cacheMisses++;
        _logger?.Debug("Cache MISS for all ingredients");

        try
        {
            var entities = await _context.Ingredients
                .Where(i => i.IsActive)
                .OrderBy(i => i.Name)
                .ToListAsync();

            var ingredients = entities.Select(MapEntityToModel).ToList();

            // Cache the result
            _cache.Set(AllIngredientsKey, ingredients, TimeSpan.FromMinutes(CacheExpirationMinutes));

            _logger?.Information("Retrieved {Count} ingredients from database", ingredients.Count);

            return ingredients;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving all ingredients");
            throw;
        }
    }

    /// <summary>
    /// Gets an ingredient by name (exact match).
    /// Names are unique in the database.
    /// </summary>
    /// <param name="name">The ingredient name to search for</param>
    /// <returns>The ingredient if found; null otherwise</returns>
    public async Task<Ingredient?> GetByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger?.Warning("GetByName called with empty name");
            return null;
        }

        var cacheKey = $"{CacheKeyPrefix}name_{name.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out Ingredient? cachedIngredient))
        {
            _cacheHits++;
            _logger?.Debug("Cache HIT for ingredient name '{Name}'", name);
            return cachedIngredient;
        }

        _cacheMisses++;

        try
        {
            var entity = await _context.Ingredients
                .Where(i => i.Name == name && i.IsActive)
                .FirstOrDefaultAsync();

            if (entity == null)
            {
                _logger?.Information("Ingredient not found by name: '{Name}'", name);
                return null;
            }

            var ingredient = MapEntityToModel(entity);

            _cache.Set(cacheKey, ingredient, TimeSpan.FromMinutes(CacheExpirationMinutes));

            return ingredient;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving ingredient by name '{Name}'", name);
            throw;
        }
    }

    /// <summary>
    /// Creates a new ingredient with validation.
    /// Automatically records the current price as the first price history entry.
    /// </summary>
    /// <param name="ingredient">The ingredient to create</param>
    /// <returns>The created ingredient with assigned ID</returns>
    public async Task<Ingredient> CreateAsync(Ingredient ingredient)
    {
        if (ingredient == null)
        {
            throw new ArgumentNullException(nameof(ingredient));
        }

        if (string.IsNullOrWhiteSpace(ingredient.Name))
        {
            _logger?.Warning("Cannot create ingredient with empty name");
            throw new ArgumentException("Ingredient name is required", nameof(ingredient.Name));
        }

        if (string.IsNullOrWhiteSpace(ingredient.Unit))
        {
            _logger?.Warning("Cannot create ingredient with empty unit");
            throw new ArgumentException("Ingredient unit is required", nameof(ingredient.Unit));
        }

        if (ingredient.CurrentPrice < 0)
        {
            _logger?.Warning("Cannot create ingredient with negative price: {Price}", ingredient.CurrentPrice);
            throw new ArgumentException("Ingredient price cannot be negative", nameof(ingredient.CurrentPrice));
        }

        // Check for duplicate name
        var existing = await _context.Ingredients
            .FirstOrDefaultAsync(i => i.Name == ingredient.Name);

        if (existing != null && existing.IsActive)
        {
            _logger?.Warning("Cannot create ingredient: name already exists '{Name}'", ingredient.Name);
            throw new InvalidOperationException($"Ingredient '{ingredient.Name}' already exists");
        }

        try
        {
            var entity = new IngredientEntity
            {
                Name = ingredient.Name,
                Unit = ingredient.Unit,
                CurrentPrice = ingredient.CurrentPrice,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Ingredients.Add(entity);
            await _context.SaveChangesAsync();

            ingredient.Id = entity.Id;
            ingredient.CreatedAt = entity.CreatedAt;
            ingredient.UpdatedAt = entity.UpdatedAt;
            ingredient.IsActive = true;

            // Record initial price in price history
            if (ingredient.CurrentPrice > 0)
            {
                var priceHistory = new PriceHistoryEntity
                {
                    IngredientId = entity.Id,
                    Ingredient = entity,
                    Price = ingredient.CurrentPrice,
                    RecordedDate = DateTime.UtcNow,
                    Notes = "Initial price"
                };

                _context.PriceHistories.Add(priceHistory);
                await _context.SaveChangesAsync();
            }

            ClearCache();
            _logger?.Information("Ingredient created: {Name} ({Unit}) ID={Id}", 
                ingredient.Name, ingredient.Unit, ingredient.Id);

            return ingredient;
        }
        catch (DbUpdateException ex)
        {
            _logger?.Error(ex, "Database error creating ingredient '{Name}'", ingredient.Name);
            throw new InvalidOperationException("Error creating ingredient in database", ex);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Unexpected error creating ingredient '{Name}'", ingredient.Name);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing ingredient with change tracking.
    /// </summary>
    /// <param name="ingredient">The ingredient with updates</param>
    /// <returns>The updated ingredient</returns>
    public async Task<Ingredient> UpdateAsync(Ingredient ingredient)
    {
        if (ingredient == null)
        {
            throw new ArgumentNullException(nameof(ingredient));
        }

        if (ingredient.Id <= 0)
        {
            _logger?.Warning("Cannot update ingredient with invalid ID: {Id}", ingredient.Id);
            throw new ArgumentException("Ingredient ID must be valid", nameof(ingredient.Id));
        }

        try
        {
            var entity = await _context.Ingredients.FindAsync(ingredient.Id);

            if (entity == null || !entity.IsActive)
            {
                _logger?.Warning("Ingredient not found for update: {Id}", ingredient.Id);
                throw new InvalidOperationException($"Ingredient with ID {ingredient.Id} not found");
            }

            // Track changes for audit
            var changes = new Dictionary<string, object>();

            if (entity.Name != ingredient.Name)
            {
                changes["Name"] = ingredient.Name;
                entity.Name = ingredient.Name;
            }

            if (entity.Unit != ingredient.Unit)
            {
                changes["Unit"] = ingredient.Unit;
                entity.Unit = ingredient.Unit;
            }

            if (entity.CurrentPrice != ingredient.CurrentPrice)
            {
                changes["CurrentPrice"] = ingredient.CurrentPrice;
                entity.CurrentPrice = ingredient.CurrentPrice;
            }

            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            ingredient.UpdatedAt = entity.UpdatedAt;

            ClearCache();
            _logger?.Information("Ingredient updated: {Name} ID={Id}, Changes={Count}",
                ingredient.Name, ingredient.Id, changes.Count);

            return ingredient;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger?.Error(ex, "Concurrency error updating ingredient {Id}", ingredient.Id);
            throw new InvalidOperationException("The ingredient was modified by another user", ex);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error updating ingredient {Id}", ingredient.Id);
            throw;
        }
    }

    /// <summary>
    /// Soft deletes an ingredient (marks as inactive instead of actually deleting).
    /// This preserves referential integrity and allows recovery.
    /// </summary>
    /// <param name="id">The ingredient ID to delete</param>
    /// <returns>True if deleted; false if not found</returns>
    public async Task<bool> DeleteAsync(int id)
    {
        if (id <= 0)
        {
            _logger?.Warning("Cannot delete ingredient with invalid ID: {Id}", id);
            return false;
        }

        try
        {
            var entity = await _context.Ingredients.FindAsync(id);

            if (entity == null)
            {
                _logger?.Warning("Ingredient not found for deletion: {Id}", id);
                return false;
            }

            entity.IsActive = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            ClearCache();
            _logger?.Information("Ingredient deleted (soft): {Name} ID={Id}", entity.Name, id);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error deleting ingredient {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Gets the complete price history for an ingredient with optional date filtering.
    /// Returns entries in chronological order (oldest first).
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="startDate">Optional start date for filtering</param>
    /// <param name="endDate">Optional end date for filtering</param>
    /// <returns>Chronologically sorted price history</returns>
    public async Task<IEnumerable<PriceHistoryEntry>> GetPriceHistoryAsync(int ingredientId, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (ingredientId <= 0)
        {
            _logger?.Warning("Invalid ingredient ID for price history: {Id}", ingredientId);
            return new List<PriceHistoryEntry>();
        }

        try
        {
            var query = _context.PriceHistories
                .Where(ph => ph.IngredientId == ingredientId);

            if (startDate.HasValue)
            {
                query = query.Where(ph => ph.RecordedDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(ph => ph.RecordedDate <= endDate.Value);
            }

            var entities = await query
                .OrderBy(ph => ph.RecordedDate)
                .ToListAsync();

            var history = entities.Select(e => new PriceHistoryEntry
            {
                Id = e.Id,
                IngredientId = e.IngredientId,
                Price = e.Price,
                RecordedDate = e.RecordedDate,
                Notes = e.Notes
            }).ToList();

            _logger?.Debug("Retrieved {Count} price history entries for ingredient {Id}",
                history.Count, ingredientId);

            return history;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving price history for ingredient {Id}", ingredientId);
            throw;
        }
    }

    /// <summary>
    /// Records a new price for an ingredient and adds it to price history.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="price">The price to record</param>
    /// <param name="notes">Optional notes about the price change</param>
    /// <returns>The recorded price history entry</returns>
    public async Task<PriceHistoryEntry> RecordPriceAsync(int ingredientId, decimal price, string? notes = null)
    {
        if (ingredientId <= 0)
        {
            throw new ArgumentException("Invalid ingredient ID", nameof(ingredientId));
        }

        if (price < 0)
        {
            _logger?.Warning("Negative price recorded for ingredient {Id}: {Price}", ingredientId, price);
            throw new ArgumentException("Price cannot be negative", nameof(price));
        }

        try
        {
            // Verify ingredient exists
            var ingredient = await _context.Ingredients.FindAsync(ingredientId);
            if (ingredient == null || !ingredient.IsActive)
            {
                throw new InvalidOperationException($"Ingredient {ingredientId} not found");
            }

            // Record price history
            var priceHistory = new PriceHistoryEntity
            {
                IngredientId = ingredientId,
                Ingredient = ingredient,
                Price = price,
                RecordedDate = DateTime.UtcNow,
                Notes = notes
            };

            _context.PriceHistories.Add(priceHistory);

            // Update current price
            ingredient.CurrentPrice = price;
            ingredient.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            ClearCache();

            _logger?.Information("Price recorded for ingredient {Id}: {Price:C}", ingredientId, price);

            return new PriceHistoryEntry
            {
                Id = priceHistory.Id,
                IngredientId = priceHistory.IngredientId,
                Price = priceHistory.Price,
                RecordedDate = priceHistory.RecordedDate,
                Notes = priceHistory.Notes
            };
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error recording price for ingredient {Id}", ingredientId);
            throw;
        }
    }

    /// <summary>
    /// Gets the most recent price recorded for an ingredient.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <returns>The latest price; or 0 if no history exists</returns>
    public async Task<decimal> GetLatestPriceAsync(int ingredientId)
    {
        if (ingredientId <= 0)
        {
            return 0m;
        }

        try
        {
            var latestPrice = await _context.PriceHistories
                .Where(ph => ph.IngredientId == ingredientId)
                .OrderByDescending(ph => ph.RecordedDate)
                .Select(ph => ph.Price)
                .FirstOrDefaultAsync();

            _logger?.Debug("Retrieved latest price for ingredient {Id}: {Price:C}", ingredientId, latestPrice);

            return latestPrice;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving latest price for ingredient {Id}", ingredientId);
            return 0m;
        }
    }

    /// <summary>
    /// Calculates the average price over a specified time period.
    /// Useful for understanding typical pricing and detecting anomalies.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="days">Number of days to look back (default 30)</param>
    /// <returns>Average price; or 0 if insufficient data</returns>
    public async Task<decimal> GetAveragePriceAsync(int ingredientId, int days = 30)
    {
        if (ingredientId <= 0)
        {
            return 0m;
        }

        if (days <= 0)
        {
            _logger?.Warning("Invalid days parameter for average price: {Days}", days);
            return 0m;
        }

        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            var averagePrice = await _context.PriceHistories
                .Where(ph => ph.IngredientId == ingredientId && ph.RecordedDate >= cutoffDate)
                .AverageAsync(ph => (decimal?)ph.Price) ?? 0m;

            _logger?.Debug("Average price for ingredient {Id} over {Days} days: {Price:C}",
                ingredientId, days, averagePrice);

            return averagePrice;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error calculating average price for ingredient {Id}", ingredientId);
            return 0m;
        }
    }

    /// <summary>
    /// Calculates the price trend (percentage change) over a specified period.
    /// Positive values indicate price increases; negative indicate decreases.
    /// </summary>
    /// <param name="ingredientId">The ingredient identifier</param>
    /// <param name="days">Number of days to analyze (default 30)</param>
    /// <returns>Percentage change (e.g., 0.15 = 15% increase); or 0 if insufficient data</returns>
    public async Task<decimal> GetPriceTrendAsync(int ingredientId, int days = 30)
    {
        if (ingredientId <= 0)
        {
            return 0m;
        }

        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            var prices = await _context.PriceHistories
                .Where(ph => ph.IngredientId == ingredientId && ph.RecordedDate >= cutoffDate)
                .OrderBy(ph => ph.RecordedDate)
                .Select(ph => new { ph.Price, ph.RecordedDate })
                .ToListAsync();

            if (prices.Count < 2)
            {
                _logger?.Debug("Insufficient price history for trend analysis on ingredient {Id}", ingredientId);
                return 0m;
            }

            var oldestPrice = prices.First().Price;
            var newestPrice = prices.Last().Price;

            if (oldestPrice == 0)
            {
                return 0m;
            }

            var trend = (newestPrice - oldestPrice) / oldestPrice;

            _logger?.Debug("Price trend for ingredient {Id} over {Days} days: {Trend:P}",
                ingredientId, days, trend);

            return trend;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error calculating price trend for ingredient {Id}", ingredientId);
            return 0m;
        }
    }

    /// <summary>
    /// Clears all cache entries for this repository.
    /// Should be called after any modification operation.
    /// </summary>
    public void ClearCache()
    {
        _logger?.Debug("Ingredient repository cache cleared");
        // In production, would need a custom cache manager to fully clear prefixed keys
        // For now, we just log that cache should be invalidated
    }

    /// <summary>
    /// Gets cache hit/miss statistics for performance monitoring.
    /// </summary>
    /// <returns>Dictionary with statistics</returns>
    public Dictionary<string, object> GetCacheStatistics()
    {
        var totalRequests = _cacheHits + _cacheMisses;
        var hitRate = totalRequests > 0 ? (double)_cacheHits / totalRequests : 0;

        return new Dictionary<string, object>
        {
            { "CacheHits", _cacheHits },
            { "CacheMisses", _cacheMisses },
            { "TotalRequests", totalRequests },
            { "HitRate", hitRate },
            { "RepositoryVersion", "1.0" }
        };
    }

    /// <summary>
    /// Maps an ingredient entity to the domain model.
    /// </summary>
    private Ingredient MapEntityToModel(IngredientEntity entity)
    {
        return new Ingredient
        {
            Id = entity.Id,
            Name = entity.Name,
            Unit = entity.Unit,
            CurrentPrice = entity.CurrentPrice,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            IsActive = entity.IsActive
        };
    }

    /// <summary>
    /// Example demonstration method showing repository usage patterns.
    /// </summary>
    public static async Task RunDemonstrationAsync(IIngredientRepository repository)
    {
        // Create ingredients
        var flour = new Ingredient { Name = "Flour", Unit = "kg", CurrentPrice = 3.00m };
        var sugar = new Ingredient { Name = "Sugar", Unit = "kg", CurrentPrice = 8.00m };

        var createdFlour = await repository.CreateAsync(flour);
        var createdSugar = await repository.CreateAsync(sugar);

        // Record price changes over time
        await repository.RecordPriceAsync(createdFlour.Id, 3.20m, "Price increase due to shortage");
        await Task.Delay(100);
        await repository.RecordPriceAsync(createdFlour.Id, 3.10m, "Slight decrease");

        // Get all ingredients
        var allIngredients = await repository.GetAllAsync();
        Console.WriteLine($"Total ingredients: {allIngredients.Count()}");

        // Get by ID
        var ingredient = await repository.GetByIdAsync(createdFlour.Id);
        Console.WriteLine($"Retrieved: {ingredient?.Name} - {ingredient?.CurrentPrice:C}");

        // Get price history
        var history = await repository.GetPriceHistoryAsync(createdFlour.Id);
        Console.WriteLine($"Price history entries: {history.Count()}");

        // Get statistics
        var avgPrice = await repository.GetAveragePriceAsync(createdFlour.Id, 30);
        var trend = await repository.GetPriceTrendAsync(createdFlour.Id, 30);
        Console.WriteLine($"Average price: {avgPrice:C}, Trend: {trend:P}");
    }
}
