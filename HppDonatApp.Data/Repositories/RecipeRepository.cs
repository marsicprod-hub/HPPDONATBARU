using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using HppDonatApp.Data.Entities;

namespace HppDonatApp.Data.Repositories;

/// <summary>
/// Repository interface for recipe data access operations.
/// </summary>
public interface IRecipeRepository
{
    Task<Recipe?> GetByIdAsync(int id);
    Task<IEnumerable<Recipe>> GetAllAsync();
    Task<Recipe> CreateAsync(Recipe recipe);
    Task<Recipe> UpdateAsync(Recipe recipe);
    Task<bool> DeleteAsync(int id);
    Task<IEnumerable<RecipeIngredientInfo>> GetRecipeIngredientsAsync(int recipeId);
    Task AddIngredientAsync(int recipeId, int ingredientId, decimal quantity);
    Task RemoveIngredientAsync(int recipeId, int ingredientId);
}

public class RecipeIngredientInfo
{
    public int RecipeIngredientId { get; set; }
    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal CurrentPrice { get; set; }
}

public class Recipe
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TheoreticalOutput { get; set; }
    public decimal WastePercent { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Comprehensive recipe repository implementation with full CRUD and ingredient management.
/// </summary>
public class RecipeRepository : IRecipeRepository
{
    private readonly HppDonatDbContext _context;
    private readonly ILogger? _logger;

    public RecipeRepository(HppDonatDbContext context, ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
        _logger?.Debug("RecipeRepository initialized");
    }

    public async Task<Recipe?> GetByIdAsync(int id)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            var entity = await _context.Recipes
                .Where(r => r.Id == id && r.IsActive)
                .FirstOrDefaultAsync();

            return entity == null ? null : MapEntityToModel(entity);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving recipe {Id}", id);
            throw;
        }
    }

    public async Task<IEnumerable<Recipe>> GetAllAsync()
    {
        try
        {
            var entities = await _context.Recipes
                .Where(r => r.IsActive)
                .OrderBy(r => r.Name)
                .ToListAsync();

            return entities.Select(MapEntityToModel);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving all recipes");
            throw;
        }
    }

    public async Task<Recipe> CreateAsync(Recipe recipe)
    {
        if (recipe == null)
            throw new ArgumentNullException(nameof(recipe));

        if (string.IsNullOrWhiteSpace(recipe.Name))
            throw new ArgumentException("Recipe name is required");

        if (recipe.TheoreticalOutput <= 0)
            throw new ArgumentException("Theoretical output must be positive");

        try
        {
            var entity = new RecipeEntity
            {
                Name = recipe.Name,
                Description = recipe.Description,
                TheoreticalOutput = recipe.TheoreticalOutput,
                WastePercent = recipe.WastePercent,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Recipes.Add(entity);
            await _context.SaveChangesAsync();

            recipe.Id = entity.Id;
            recipe.CreatedAt = entity.CreatedAt;
            recipe.UpdatedAt = entity.UpdatedAt;
            recipe.Version = 1;
            recipe.IsActive = true;

            _logger?.Information("Recipe created: {Name} ID={Id}", recipe.Name, recipe.Id);

            return recipe;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error creating recipe");
            throw;
        }
    }

    public async Task<Recipe> UpdateAsync(Recipe recipe)
    {
        if (recipe == null || recipe.Id <= 0)
            throw new ArgumentException("Invalid recipe");

        try
        {
            var entity = await _context.Recipes.FindAsync(recipe.Id);
            if (entity == null || !entity.IsActive)
                throw new InvalidOperationException("Recipe not found");

            entity.Name = recipe.Name;
            entity.Description = recipe.Description;
            entity.TheoreticalOutput = recipe.TheoreticalOutput;
            entity.WastePercent = recipe.WastePercent;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            recipe.UpdatedAt = entity.UpdatedAt;
            recipe.Version = entity.Version;

            _logger?.Information("Recipe updated: {Name} ID={Id}", recipe.Name, recipe.Id);

            return recipe;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error updating recipe");
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (id <= 0)
            return false;

        try
        {
            var entity = await _context.Recipes.FindAsync(id);
            if (entity == null)
                return false;

            entity.IsActive = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger?.Information("Recipe deleted: ID={Id}", id);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error deleting recipe");
            throw;
        }
    }

    public async Task<IEnumerable<RecipeIngredientInfo>> GetRecipeIngredientsAsync(int recipeId)
    {
        if (recipeId <= 0)
            return new List<RecipeIngredientInfo>();

        try
        {
            var ingredients = await _context.RecipeIngredients
                .Where(ri => ri.RecipeId == recipeId)
                .Join(_context.Ingredients,
                    ri => ri.IngredientId,
                    i => i.Id,
                    (ri, i) => new RecipeIngredientInfo
                    {
                        RecipeIngredientId = ri.Id,
                        IngredientId = i.Id,
                        IngredientName = i.Name,
                        Unit = i.Unit,
                        Quantity = ri.Quantity,
                        CurrentPrice = i.CurrentPrice
                    })
                .OrderBy(x => x.RecipeIngredientId)
                .ToListAsync();

            return ingredients;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error retrieving recipe ingredients for recipe {RecipeId}", recipeId);
            throw;
        }
    }

    public async Task AddIngredientAsync(int recipeId, int ingredientId, decimal quantity)
    {
        if (recipeId <= 0 || ingredientId <= 0 || quantity <= 0)
            throw new ArgumentException("Invalid parameters");

        try
        {
            var entity = new RecipeIngredientEntity
            {
                RecipeId = recipeId,
                IngredientId = ingredientId,
                Quantity = quantity
            };

            _context.RecipeIngredients.Add(entity);
            await _context.SaveChangesAsync();

            _logger?.Information("Ingredient added to recipe: RecipeId={RecipeId}, IngredientId={IngredientId}, Qty={Quantity}",
                recipeId, ingredientId, quantity);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error adding ingredient to recipe");
            throw;
        }
    }

    public async Task RemoveIngredientAsync(int recipeId, int recipeIngredientId)
    {
        if (recipeId <= 0 || recipeIngredientId <= 0)
            throw new ArgumentException("Invalid parameters");

        try
        {
            var entity = await _context.RecipeIngredients.FindAsync(recipeIngredientId);
            if (entity == null || entity.RecipeId != recipeId)
                throw new InvalidOperationException("Recipe ingredient not found");

            _context.RecipeIngredients.Remove(entity);
            await _context.SaveChangesAsync();

            _logger?.Information("Ingredient removed from recipe: RecipeId={RecipeId}, RecipeIngredientId={Id}",
                recipeId, recipeIngredientId);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error removing ingredient from recipe");
            throw;
        }
    }

    private Recipe MapEntityToModel(RecipeEntity entity)
    {
        return new Recipe
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            TheoreticalOutput = entity.TheoreticalOutput,
            WastePercent = entity.WastePercent,
            Version = entity.Version,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            IsActive = entity.IsActive
        };
    }
}
