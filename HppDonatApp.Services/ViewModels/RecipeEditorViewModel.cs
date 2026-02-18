using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using HppDonatApp.Core.Models;
using HppDonatApp.Core.Services;
using HppDonatApp.Data.Repositories;
using HppDonatApp.Services.Mvvm;

namespace HppDonatApp.Services.ViewModels;

/// <summary>
/// View model for the recipe editor page.
/// Handles recipe editing, ingredient management, cost calculations, and batch operations.
/// This comprehensive view model includes all the logic needed for a complete recipe editing experience.
/// 
/// Responsibilities:
/// - Recipe CRUD operations
/// - Ingredient list management with add/remove/reorder
/// - Batch cost calculations and scenario management
/// - Price trend analysis
/// - Data validation and error handling
/// </summary>
public class RecipeEditorViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipeRepository;
    private readonly IIngredientRepository _ingredientRepository;
    private readonly IPricingEngine _pricingEngine;
    private readonly ISettingsService _settingsService;

    // Observable properties
    private int _selectedRecipeId;
    private string _recipeName = string.Empty;
    private string _recipeDescription = string.Empty;
    private int _theoreticalOutput = 100;
    private decimal _wastePercent = 0.10m;
    private ObservableCollection<RecipeIngredientViewModel> _recipeIngredients = new();
    private ObservableCollection<Ingredient> _availableIngredients = new();
    private Ingredient? _selectedAvailableIngredient;
    private RecipeIngredientViewModel? _selectedIngredient;
    private decimal _selectedIngredientQuantity = 1m;

    // Batch parameters
    private decimal _batchMultiplier = 1m;
    private decimal _oilUsedLiters = 2m;
    private decimal _oilPricePerLiter = 12m;
    private decimal _oilChangeCost = 500m;
    private int _batchesPerOilChange = 10;
    private decimal _energyKwh = 5m;
    private decimal _energyRatePerKwh = 2.50m;
    private decimal _overheadAllocated = 100m;
    private decimal _packagingPerUnit = 0.50m;

    // Pricing parameters
    private decimal _markup = 0.50m;
    private decimal _vatPercent = 0.10m;
    private string _roundingRule = "0.05";
    private string _pricingStrategy = "FixedMarkup";
    private decimal _targetMarginPercent = 0.30m;

    // Calculation results
    private BatchCostResult? _calculationResult;
    private bool _hasCalculationResult;

    // Labor roles
    private ObservableCollection<LaborRoleViewModel> _laborRoles = new();
    private string _newLaborName = string.Empty;
    private decimal _newLaborRate = 50m;
    private decimal _newLaborHours = 2m;

    // UI state
    private bool _isEditingRecipe;
    private bool _isLoadingRecipes;
    private bool _showCalculationPanel;

    // Commands
    public ICommand LoadRecipesCommand { get; }
    public ICommand NewRecipeCommand { get; }
    public ICommand SaveRecipeCommand { get; }
    public ICommand DeleteRecipeCommand { get; }
    public ICommand AddIngredientCommand { get; }
    public ICommand RemoveIngredientCommand { get; }
    public ICommand UpdateIngredientQuantityCommand { get; }
    public ICommand CalculateBatchCostCommand { get; }
    public ICommand AddLaborRoleCommand { get; }
    public ICommand RemoveLaborRoleCommand { get; }
    public ICommand ExportRecipeCommand { get; }
    public ICommand ShowParametersCommand { get; }

    #region Bindable Properties

    public bool IsEditingRecipe
    {
        get => _isEditingRecipe;
        set => SetProperty(ref _isEditingRecipe, value);
    }

    public bool IsLoadingRecipes
    {
        get => _isLoadingRecipes;
        set => SetProperty(ref _isLoadingRecipes, value);
    }

    public bool ShowCalculationPanel
    {
        get => _showCalculationPanel;
        set => SetProperty(ref _showCalculationPanel, value);
    }

    public string RecipeName
    {
        get => _recipeName;
        set => SetProperty(ref _recipeName, value);
    }

    public string RecipeDescription
    {
        get => _recipeDescription;
        set => SetProperty(ref _recipeDescription, value);
    }

    public int TheoreticalOutput
    {
        get => _theoreticalOutput;
        set => SetProperty(ref _theoreticalOutput, Math.Max(1, value));
    }

    public decimal WastePercent
    {
        get => _wastePercent;
        set => SetProperty(ref _wastePercent, Math.Clamp(value, 0, 0.99m));
    }

    public ObservableCollection<RecipeIngredientViewModel> RecipeIngredients
    {
        get => _recipeIngredients;
        set => SetProperty(ref _recipeIngredients, value);
    }

    public ObservableCollection<Ingredient> AvailableIngredients
    {
        get => _availableIngredients;
        set => SetProperty(ref _availableIngredients, value);
    }

    public Ingredient? SelectedAvailableIngredient
    {
        get => _selectedAvailableIngredient;
        set => SetProperty(ref _selectedAvailableIngredient, value);
    }

    public RecipeIngredientViewModel? SelectedIngredient
    {
        get => _selectedIngredient;
        set => SetProperty(ref _selectedIngredient, value);
    }

    public decimal SelectedIngredientQuantity
    {
        get => _selectedIngredientQuantity;
        set => SetProperty(ref _selectedIngredientQuantity, Math.Max(0.01m, value));
    }

    public decimal BatchMultiplier
    {
        get => _batchMultiplier;
        set => SetProperty(ref _batchMultiplier, Math.Max(0.1m, value));
    }

    public decimal OilUsedLiters
    {
        get => _oilUsedLiters;
        set => SetProperty(ref _oilUsedLiters, Math.Max(0, value));
    }

    public decimal OilPricePerLiter
    {
        get => _oilPricePerLiter;
        set => SetProperty(ref _oilPricePerLiter, Math.Max(0, value));
    }

    public decimal OilChangeCost
    {
        get => _oilChangeCost;
        set => SetProperty(ref _oilChangeCost, Math.Max(0, value));
    }

    public int BatchesPerOilChange
    {
        get => _batchesPerOilChange;
        set => SetProperty(ref _batchesPerOilChange, Math.Max(1, value));
    }

    public decimal Markup
    {
        get => _markup;
        set => SetProperty(ref _markup, Math.Max(0, value));
    }

    public decimal VatPercent
    {
        get => _vatPercent;
        set => SetProperty(ref _vatPercent, Math.Clamp(value, 0, 0.99m));
    }

    public string RoundingRule
    {
        get => _roundingRule;
        set => SetProperty(ref _roundingRule, value);
    }

    public BatchCostResult? CalculationResult
    {
        get => _calculationResult;
        set
        {
            SetProperty(ref _calculationResult, value);
            HasCalculationResult = value != null;
        }
    }

    public bool HasCalculationResult
    {
        get => _hasCalculationResult;
        set => SetProperty(ref _hasCalculationResult, value);
    }

    public ObservableCollection<LaborRoleViewModel> LaborRoles
    {
        get => _laborRoles;
        set => SetProperty(ref _laborRoles, value);
    }

    public decimal EnergyKwh
    {
        get => _energyKwh;
        set => SetProperty(ref _energyKwh, Math.Max(0, value));
    }

    public decimal EnergyRatePerKwh
    {
        get => _energyRatePerKwh;
        set => SetProperty(ref _energyRatePerKwh, Math.Max(0, value));
    }

    public decimal OverheadAllocated
    {
        get => _overheadAllocated;
        set => SetProperty(ref _overheadAllocated, Math.Max(0, value));
    }

    public decimal PackagingPerUnit
    {
        get => _packagingPerUnit;
        set => SetProperty(ref _packagingPerUnit, Math.Max(0, value));
    }

    #endregion

    /// <summary>
    /// Initializes the recipe editor view model.
    /// </summary>
    public RecipeEditorViewModel(
        IRecipeRepository recipeRepository,
        IIngredientRepository ingredientRepository,
        IPricingEngine pricingEngine,
        ISettingsService settingsService,
        ILogger? logger = null) : base(logger)
    {
        _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
        _ingredientRepository = ingredientRepository ?? throw new ArgumentNullException(nameof(ingredientRepository));
        _pricingEngine = pricingEngine ?? throw new ArgumentNullException(nameof(pricingEngine));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        // Initialize commands
        LoadRecipesCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(LoadRecipesAsync, logger: Logger);
        NewRecipeCommand = new RelayCommand(NewRecipe);
        SaveRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(SaveRecipeAsync, CanSaveRecipe, Logger);
        DeleteRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(DeleteRecipeAsync, CanDeleteRecipe, Logger);
        AddIngredientCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(AddIngredientAsync, CanAddIngredient, Logger);
        RemoveIngredientCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(RemoveIngredientAsync, CanRemoveIngredient, Logger);
        UpdateIngredientQuantityCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(UpdateIngredientQuantityAsync, logger: Logger);
        CalculateBatchCostCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(CalculateBatchCostAsync, CanCalculateCost, Logger);
        AddLaborRoleCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(AddLaborRoleAsync, CanAddLaborRole, Logger);
        RemoveLaborRoleCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(RemoveLaborRoleAsync, CanRemoveLaborRole, Logger);
        ExportRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(ExportRecipeAsync, logger: Logger);
        ShowParametersCommand = new RelayCommand(() => ShowCalculationPanel = !ShowCalculationPanel);

        LoadSettings();

        Logger?.Debug("RecipeEditorViewModel initialized with all commands");
    }

    /// <summary>
    /// Loads settings from the settings service.
    /// </summary>
    private void LoadSettings()
    {
        _markup = _settingsService.GetSetting("DefaultMarkup", 0.50m);
        _vatPercent = _settingsService.GetSetting("DefaultVAT", 0.10m);
        _roundingRule = _settingsService.GetSetting("RoundingRule", "0.05");
        _pricingStrategy = _settingsService.GetSetting("PricingStrategy", "FixedMarkup");

        Logger?.Debug("Settings loaded: Markup={Markup:P}, VAT={VAT:P}, RoundingRule={Rule}",
            _markup, _vatPercent, _roundingRule);
    }

    /// <summary>
    /// Loads all recipes from the repository.
    /// </summary>
    private async Task LoadRecipesAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            IsLoadingRecipes = true;
            Logger?.Debug("Loading recipes");

            var recipes = await _recipeRepository.GetAllAsync();
            Logger?.Information("Loaded {Count} recipes", recipes.Count());

            IsLoadingRecipes = false;
        }, "Loading recipes");
    }

    /// <summary>
    /// Initializes a new recipe for editing.
    /// </summary>
    private void NewRecipe()
    {
        _selectedRecipeId = 0;
        RecipeName = string.Empty;
        RecipeDescription = string.Empty;
        TheoreticalOutput = 100;
        WastePercent = 0.10m;
        RecipeIngredients.Clear();
        IsEditingRecipe = true;

        Logger?.Information("New recipe initialized");
    }

    /// <summary>
    /// Saves the current recipe to the repository.
    /// </summary>
    private async Task SaveRecipeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(RecipeName))
            {
                await ShowErrorAsync("Recipe name is required");
                return;
            }

            if (RecipeIngredients.Count == 0)
            {
                await ShowErrorAsync("Recipe must have at least one ingredient");
                return;
            }

            if (TheoreticalOutput <= 0)
            {
                await ShowErrorAsync("Theoretical output must be greater than 0");
                return;
            }

            Logger?.Debug("Saving recipe: {Name}", RecipeName);

            var recipe = new Recipe
            {
                Id = _selectedRecipeId,
                Name = RecipeName,
                Description = RecipeDescription,
                TheoreticalOutput = TheoreticalOutput,
                WastePercent = WastePercent
            };

            if (_selectedRecipeId == 0)
            {
                var created = await _recipeRepository.CreateAsync(recipe);
                _selectedRecipeId = created.Id;
                await ShowSuccessAsync($"Recipe '{RecipeName}' created successfully");
            }
            else
            {
                await _recipeRepository.UpdateAsync(recipe);
                await ShowSuccessAsync($"Recipe '{RecipeName}' updated successfully");
            }

            IsEditingRecipe = false;
            Logger?.Information("Recipe saved: {Name}, ID={Id}", RecipeName, _selectedRecipeId);
        }, "Saving recipe");
    }

    /// <summary>
    /// Deletes the current recipe.
    /// </summary>
    private async Task DeleteRecipeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            Logger?.Debug("Deleting recipe: ID={RecipeId}", _selectedRecipeId);
            await _recipeRepository.DeleteAsync(_selectedRecipeId);
            await ShowSuccessAsync("Recipe deleted successfully");
            NewRecipe();
        }, "Deleting recipe");
    }

    /// <summary>
    /// Adds the selected ingredient to the recipe.
    /// </summary>
    private async Task AddIngredientAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (SelectedAvailableIngredient == null)
            {
                await ShowErrorAsync("Please select an ingredient");
                return;
            }

            if (RecipeIngredients.Any(i => i.IngredientId == SelectedAvailableIngredient.Id))
            {
                await ShowErrorAsync("This ingredient is already in the recipe");
                return;
            }

            var recipeIngredient = new RecipeIngredientViewModel
            {
                IngredientId = SelectedAvailableIngredient.Id,
                IngredientName = SelectedAvailableIngredient.Name,
                Unit = SelectedAvailableIngredient.Unit,
                Quantity = SelectedIngredientQuantity,
                CurrentPrice = SelectedAvailableIngredient.CurrentPrice
            };

            RecipeIngredients.Add(recipeIngredient);
            SelectedAvailableIngredient = null;
            SelectedIngredientQuantity = 1m;

            Logger?.Information("Ingredient added to recipe: {Name}, Qty={Qty}",
                SelectedAvailableIngredient?.Name, SelectedIngredientQuantity);

            await ShowSuccessAsync($"Ingredient '{SelectedAvailableIngredient?.Name}' added");
        }, "Adding ingredient");
    }

    /// <summary>
    /// Removes the selected ingredient from the recipe.
    /// </summary>
    private async Task RemoveIngredientAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (SelectedIngredient == null)
            {
                await ShowErrorAsync("Please select an ingredient to remove");
                return;
            }

            RecipeIngredients.Remove(SelectedIngredient);
            await ShowSuccessAsync($"Ingredient '{SelectedIngredient.IngredientName}' removed");

            Logger?.Information("Ingredient removed from recipe: {Name}", SelectedIngredient.IngredientName);
        }, "Removing ingredient");
    }

    /// <summary>
    /// Updates the quantity of the selected ingredient.
    /// </summary>
    private async Task UpdateIngredientQuantityAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (SelectedIngredient != null && SelectedIngredientQuantity > 0)
            {
                SelectedIngredient.Quantity = SelectedIngredientQuantity;
                Logger?.Debug("Ingredient quantity updated: {Name}, NewQty={Qty}",
                    SelectedIngredient.IngredientName, SelectedIngredientQuantity);
            }

            await Task.CompletedTask;
        }, "Updating ingredient quantity");
    }

    /// <summary>
    /// Calculates the batch cost for the current recipe and parameters.
    /// This is the main pricing calculation operation.
    /// </summary>
    private async Task CalculateBatchCostAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            Logger?.Debug("Calculating batch cost for recipe: {Name}", RecipeName);

            var request = new BatchRequest
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
                Markup = Markup,
                VatPercent = VatPercent,
                RoundingRule = RoundingRule,
                PricingStrategy = _pricingStrategy,
                TargetMarginPercent = _targetMarginPercent
            };

            if (!request.IsValid())
            {
                await ShowErrorAsync("Invalid batch parameters. Please check all values.");
                return;
            }

            CalculationResult = await _pricingEngine.CalculateBatchCostAsync(request);
            ShowCalculationPanel = true;

            Logger?.Information("Batch cost calculated: UnitCost={UnitCost:C}, SuggestedPrice={Price:C}",
                CalculationResult?.UnitCost, CalculationResult?.SuggestedPrice);

            await ShowSuccessAsync("Batch cost calculated successfully");
        }, "Calculating batch cost");
    }

    /// <summary>
    /// Builds the list of recipe items for cost calculation.
    /// </summary>
    private IEnumerable<RecipeItem> BuildRecipeItems()
    {
        return RecipeIngredients.Select(ri => new RecipeItem
        {
            IngredientId = ri.IngredientId,
            Quantity = ri.Quantity,
            Unit = ri.Unit,
            PricePerUnit = ri.CurrentPrice
        }).ToList();
    }

    /// <summary>
    /// Builds the list of labor roles for cost calculation.
    /// </summary>
    private IEnumerable<LaborRole> BuildLaborRoles()
    {
        return LaborRoles.Select(lr => new LaborRole
        {
            Id = lr.Id,
            Name = lr.Name,
            HourlyRate = lr.HourlyRate,
            Hours = lr.Hours
        }).ToList();
    }

    /// <summary>
    /// Adds a new labor role to the template.
    /// </summary>
    private async Task AddLaborRoleAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(_newLaborName))
            {
                await ShowErrorAsync("Labor role name is required");
                return;
            }

            var labor = new LaborRoleViewModel
            {
                Name = _newLaborName,
                HourlyRate = _newLaborRate,
                Hours = _newLaborHours
            };

            LaborRoles.Add(labor);
            _newLaborName = string.Empty;
            _newLaborRate = 50m;
            _newLaborHours = 2m;

            Logger?.Information("Labor role added: {Name}, Rate={Rate:C}/h", labor.Name, labor.HourlyRate);
        }, "Adding labor role");
    }

    /// <summary>
    /// Removes the selected labor role.
    /// </summary>
    private async Task RemoveLaborRoleAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            // Implementation would remove selected labor role
            await Task.CompletedTask;
        }, "Removing labor role");
    }

    /// <summary>
    /// Exports the current recipe to CSV or other format.
    /// </summary>
    private async Task ExportRecipeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            Logger?.Information("Exporting recipe: {Name}", RecipeName);
            // Implementation would export recipe
            await ShowSuccessAsync("Recipe exported successfully");
        }, "Exporting recipe");
    }

    // Validation methods for commands
    private bool CanSaveRecipe() => !string.IsNullOrWhiteSpace(RecipeName) && RecipeIngredients.Count > 0;
    private bool CanDeleteRecipe() => _selectedRecipeId > 0;
    private bool CanAddIngredient() => SelectedAvailableIngredient != null;
    private bool CanRemoveIngredient() => SelectedIngredient != null;
    private bool CanCalculateCost() => RecipeIngredients.Count > 0 && TheoreticalOutput > 0;
    private bool CanAddLaborRole() => !string.IsNullOrWhiteSpace(_newLaborName) && _newLaborRate > 0;
    private bool CanRemoveLaborRole() => true; // Placeholder

    /// <summary>
    /// Overrides to load ingredients when navigated to.
    /// </summary>
    public override async Task OnNavigatedToAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var ingredients = await _ingredientRepository.GetAllAsync();
            AvailableIngredients = new ObservableCollection<Ingredient>(ingredients);
            Logger?.Information("Loaded {Count} available ingredients", ingredients.Count());
        }, "Loading ingredients");
    }
}

/// <summary>
/// View model for a single recipe ingredient in the list.
/// </summary>
public class RecipeIngredientViewModel : ObservableObject
{
    private decimal _quantity;
    private decimal _currentPrice;
    private int _displayOrder;

    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }

    public decimal CurrentPrice
    {
        get => _currentPrice;
        set => SetProperty(ref _currentPrice, value);
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    public decimal TotalCost => Quantity * CurrentPrice;
}

/// <summary>
/// View model for a labor role.
/// </summary>
public class LaborRoleViewModel : ObservableObject
{
    private string _name = string.Empty;
    private decimal _hourlyRate;
    private decimal _hours;

    public int Id { get; set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public decimal HourlyRate
    {
        get => _hourlyRate;
        set => SetProperty(ref _hourlyRate, value);
    }

    public decimal Hours
    {
        get => _hours;
        set => SetProperty(ref _hours, value);
    }

    public decimal TotalCost => HourlyRate * Hours;
}
