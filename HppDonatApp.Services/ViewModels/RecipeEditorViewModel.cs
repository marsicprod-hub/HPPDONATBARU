using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using HppDonatApp.Core.Models;
using HppDonatApp.Core.Services;
using HppDonatApp.Data.Repositories;
using HppDonatApp.Services.Ai;
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
public partial class RecipeEditorViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipeRepository;
    private readonly IIngredientRepository _ingredientRepository;
    private readonly IPricingEngine _pricingEngine;
    private readonly ISettingsService _settingsService;
    private readonly IGenerativeAiService _generativeAiService;

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
    private string _newIngredientName = string.Empty;
    private string _newIngredientUnit = "g";
    private decimal _newIngredientQuantity = 100m;
    private decimal? _newIngredientPackNetQuantity = 1000m;
    private decimal? _newIngredientPricePerPack = 10000m;
    private decimal? _newIngredientManualCost;
    private bool _newIngredientIncludeInDoughWeight = true;

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
    private bool _useWeightBasedOutput = true;
    private decimal _donutWeightGrams = 25m;
    private decimal _toppingWeightPerDonutGrams = 15m;
    private decimal _toppingPackWeightGrams = 1000m;
    private decimal _toppingPackPrice = 70000m;
    private string _workbookPath = @"C:\Users\hazel\Documents\MODAL_DONAT.xlsx";
    private string _statusMessage = string.Empty;

    // Pricing parameters
    private decimal _markup = 0.50m;
    private decimal _vatPercent = 0.10m;
    private string _roundingRule = "100";
    private string _pricingStrategy = "FixedMarkup";
    private decimal _targetMarginPercent = 0.30m;
    private decimal _priceVolatilityPercent = 0.08m;
    private decimal _riskAppetitePercent = 0.50m;
    private decimal _marketPressurePercent;
    private decimal _targetProfitPerBatch;
    private decimal _monthlyFixedCost;

    // Calculation results
    private BatchCostResult? _calculationResult;
    private bool _hasCalculationResult;
    private ObservableCollection<CostBreakdownRowViewModel> _costBreakdownRows = new();

    // Labor roles
    private ObservableCollection<LaborRoleViewModel> _laborRoles = new();
    private LaborRoleViewModel? _selectedLaborRole;
    private string _newLaborName = string.Empty;
    private decimal _newLaborRate = 50m;
    private decimal _newLaborHours = 2m;

    // Recipe browser
    private ObservableCollection<RecipeSummaryViewModel> _savedRecipes = new();
    private RecipeSummaryViewModel? _selectedRecipeSummary;
    private ObservableCollection<string> _pricingStrategies = new();

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
    public ICommand ImportWorkbookCommand { get; }
    public ICommand AddCustomIngredientCommand { get; }
    public ICommand OpenSelectedRecipeCommand { get; }

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

    public ObservableCollection<RecipeSummaryViewModel> SavedRecipes
    {
        get => _savedRecipes;
        set => SetProperty(ref _savedRecipes, value);
    }

    public RecipeSummaryViewModel? SelectedRecipeSummary
    {
        get => _selectedRecipeSummary;
        set => SetProperty(ref _selectedRecipeSummary, value);
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

    public string NewIngredientName
    {
        get => _newIngredientName;
        set => SetProperty(ref _newIngredientName, value);
    }

    public string NewIngredientUnit
    {
        get => _newIngredientUnit;
        set => SetProperty(ref _newIngredientUnit, string.IsNullOrWhiteSpace(value) ? "g" : value.Trim());
    }

    public decimal NewIngredientQuantity
    {
        get => _newIngredientQuantity;
        set => SetProperty(ref _newIngredientQuantity, Math.Max(0.01m, value));
    }

    public decimal? NewIngredientPackNetQuantity
    {
        get => _newIngredientPackNetQuantity;
        set => SetProperty(ref _newIngredientPackNetQuantity, value.HasValue ? Math.Max(0m, value.Value) : null);
    }

    public decimal? NewIngredientPricePerPack
    {
        get => _newIngredientPricePerPack;
        set => SetProperty(ref _newIngredientPricePerPack, value.HasValue ? Math.Max(0m, value.Value) : null);
    }

    public decimal? NewIngredientManualCost
    {
        get => _newIngredientManualCost;
        set => SetProperty(ref _newIngredientManualCost, value.HasValue ? Math.Max(0m, value.Value) : null);
    }

    public bool NewIngredientIncludeInDoughWeight
    {
        get => _newIngredientIncludeInDoughWeight;
        set => SetProperty(ref _newIngredientIncludeInDoughWeight, value);
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

    public string PricingStrategy
    {
        get => _pricingStrategy;
        set => SetProperty(ref _pricingStrategy, NormalizePricingStrategy(value));
    }

    public ObservableCollection<string> PricingStrategies
    {
        get => _pricingStrategies;
        set => SetProperty(ref _pricingStrategies, value);
    }

    public decimal TargetMarginPercent
    {
        get => _targetMarginPercent;
        set => SetProperty(ref _targetMarginPercent, Math.Clamp(value, 0m, 0.95m));
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

    public decimal PriceVolatilityPercent
    {
        get => _priceVolatilityPercent;
        set => SetProperty(ref _priceVolatilityPercent, Math.Clamp(value, 0m, 1m));
    }

    public decimal RiskAppetitePercent
    {
        get => _riskAppetitePercent;
        set => SetProperty(ref _riskAppetitePercent, Math.Clamp(value, 0m, 1m));
    }

    public decimal MarketPressurePercent
    {
        get => _marketPressurePercent;
        set => SetProperty(ref _marketPressurePercent, Math.Clamp(value, -0.50m, 0.50m));
    }

    public decimal TargetProfitPerBatch
    {
        get => _targetProfitPerBatch;
        set => SetProperty(ref _targetProfitPerBatch, Math.Max(0m, value));
    }

    public decimal MonthlyFixedCost
    {
        get => _monthlyFixedCost;
        set => SetProperty(ref _monthlyFixedCost, Math.Max(0m, value));
    }

    public BatchCostResult? CalculationResult
    {
        get => _calculationResult;
        set
        {
            SetProperty(ref _calculationResult, value);
            HasCalculationResult = value != null;
            RefreshBreakdownRows();
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

    public LaborRoleViewModel? SelectedLaborRole
    {
        get => _selectedLaborRole;
        set => SetProperty(ref _selectedLaborRole, value);
    }

    public string NewLaborName
    {
        get => _newLaborName;
        set => SetProperty(ref _newLaborName, value);
    }

    public decimal NewLaborRate
    {
        get => _newLaborRate;
        set => SetProperty(ref _newLaborRate, Math.Max(0m, value));
    }

    public decimal NewLaborHours
    {
        get => _newLaborHours;
        set => SetProperty(ref _newLaborHours, Math.Max(0m, value));
    }

    public ObservableCollection<CostBreakdownRowViewModel> CostBreakdownRows
    {
        get => _costBreakdownRows;
        set => SetProperty(ref _costBreakdownRows, value);
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

    public bool UseWeightBasedOutput
    {
        get => _useWeightBasedOutput;
        set => SetProperty(ref _useWeightBasedOutput, value);
    }

    public decimal DonutWeightGrams
    {
        get => _donutWeightGrams;
        set => SetProperty(ref _donutWeightGrams, Math.Max(1m, value));
    }

    public decimal ToppingWeightPerDonutGrams
    {
        get => _toppingWeightPerDonutGrams;
        set => SetProperty(ref _toppingWeightPerDonutGrams, Math.Max(0, value));
    }

    public decimal ToppingPackWeightGrams
    {
        get => _toppingPackWeightGrams;
        set => SetProperty(ref _toppingPackWeightGrams, Math.Max(0, value));
    }

    public decimal ToppingPackPrice
    {
        get => _toppingPackPrice;
        set => SetProperty(ref _toppingPackPrice, Math.Max(0, value));
    }

    public string WorkbookPath
    {
        get => _workbookPath;
        set => SetProperty(ref _workbookPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
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
        IGenerativeAiService generativeAiService,
        ILogger? logger = null) : base(logger)
    {
        _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
        _ingredientRepository = ingredientRepository ?? throw new ArgumentNullException(nameof(ingredientRepository));
        _pricingEngine = pricingEngine ?? throw new ArgumentNullException(nameof(pricingEngine));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _generativeAiService = generativeAiService ?? throw new ArgumentNullException(nameof(generativeAiService));

        // Initialize commands
        LoadRecipesCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(LoadRecipesAsync, logger: Logger);
        NewRecipeCommand = new RelayCommand(NewRecipe);
        SaveRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(SaveRecipeAsync, CanSaveRecipe, Logger);
        DeleteRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(DeleteRecipeAsync, CanDeleteRecipe, Logger);
        AddIngredientCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(AddIngredientAsync, CanAddIngredient, Logger);
        RemoveIngredientCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(RemoveIngredientAsync, CanRemoveIngredient, Logger);
        UpdateIngredientQuantityCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(UpdateIngredientQuantityAsync, logger: Logger);
        // Keep this command always executable; validation is handled inside CalculateBatchCostAsync.
        // This avoids stale CanExecute states after workbook import / grid edits.
        CalculateBatchCostCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(CalculateBatchCostAsync, logger: Logger);
        AddLaborRoleCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(AddLaborRoleAsync, logger: Logger);
        RemoveLaborRoleCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(RemoveLaborRoleAsync, logger: Logger);
        ExportRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(ExportRecipeAsync, logger: Logger);
        ShowParametersCommand = new RelayCommand(() => ShowCalculationPanel = !ShowCalculationPanel);
        ImportWorkbookCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(ImportWorkbookAsync, logger: Logger);
        AddCustomIngredientCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(AddCustomIngredientAsync, CanAddCustomIngredient, Logger);
        OpenSelectedRecipeCommand = new HppDonatApp.Services.Mvvm.AsyncRelayCommand(OpenSelectedRecipeAsync, logger: Logger);

        PricingStrategies = new ObservableCollection<string>(PricingStrategyFactory.GetAvailableStrategies());
        PricingStrategy = NormalizePricingStrategy(_pricingStrategy);

        LoadSettings();
        InitializeAiFeatures();

        Logger?.Debug("RecipeEditorViewModel initialized with all commands");
    }

    /// <summary>
    /// Loads settings from the settings service.
    /// </summary>
    private void LoadSettings()
    {
        _markup = _settingsService.GetSetting("DefaultMarkup", 0.50m);
        _vatPercent = _settingsService.GetSetting("DefaultVAT", 0.10m);
        var configuredRoundingRule = _settingsService.GetSetting("RoundingRule", "100");
        if (!decimal.TryParse(configuredRoundingRule, NumberStyles.Number, CultureInfo.InvariantCulture, out var roundingStep) ||
            roundingStep < 1m)
        {
            configuredRoundingRule = "100";
            _settingsService.SetSetting("RoundingRule", configuredRoundingRule);
            _settingsService.SaveSettings();
        }

        _roundingRule = configuredRoundingRule;
        _pricingStrategy = NormalizePricingStrategy(_settingsService.GetSetting("PricingStrategy", "FixedMarkup"));
        _targetMarginPercent = _settingsService.GetSetting("TargetMarginPercent", 0.30m);

        Logger?.Debug("Settings loaded: Markup={Markup:P}, VAT={VAT:P}, RoundingRule={Rule}, Strategy={Strategy}, TargetMargin={TargetMargin:P}",
            _markup, _vatPercent, _roundingRule, _pricingStrategy, _targetMarginPercent);
    }

    /// <summary>
    /// Loads all recipes from the repository.
    /// </summary>
    private async Task LoadRecipesAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            IsLoadingRecipes = true;
            try
            {
                Logger?.Debug("Loading recipes");

                var recipes = await _recipeRepository.GetAllAsync();
                SavedRecipes = new ObservableCollection<RecipeSummaryViewModel>(recipes
                    .OrderBy(x => x.Name)
                    .Select(x => new RecipeSummaryViewModel
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Description = x.Description,
                        TheoreticalOutput = x.TheoreticalOutput,
                        WastePercent = x.WastePercent
                    }));

                if (_selectedRecipeId > 0)
                {
                    SelectedRecipeSummary = SavedRecipes.FirstOrDefault(x => x.Id == _selectedRecipeId);
                }

                Logger?.Information("Loaded {Count} recipes", SavedRecipes.Count);
            }
            finally
            {
                IsLoadingRecipes = false;
            }
        }, "Loading recipes");
    }

    /// <summary>
    /// Initializes a new recipe for editing.
    /// </summary>
    private void NewRecipe()
    {
        _selectedRecipeId = 0;
        SelectedRecipeSummary = null;
        RecipeName = string.Empty;
        RecipeDescription = string.Empty;
        TheoreticalOutput = 100;
        WastePercent = 0.10m;
        RecipeIngredients.Clear();
        CalculationResult = null;
        CostBreakdownRows.Clear();
        IsEditingRecipe = true;

        Logger?.Information("New recipe initialized");
    }

    private async Task OpenSelectedRecipeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (SelectedRecipeSummary == null)
            {
                await ShowErrorAsync("Pilih resep dulu.");
                return;
            }

            var recipe = await _recipeRepository.GetByIdAsync(SelectedRecipeSummary.Id);
            if (recipe == null)
            {
                await ShowErrorAsync("Resep tidak ditemukan.");
                await LoadRecipesAsync();
                return;
            }

            _selectedRecipeId = recipe.Id;
            RecipeName = recipe.Name;
            RecipeDescription = recipe.Description;
            TheoreticalOutput = recipe.TheoreticalOutput;
            WastePercent = recipe.WastePercent;

            var recipeIngredients = await _recipeRepository.GetRecipeIngredientsAsync(recipe.Id);
            RecipeIngredients = new ObservableCollection<RecipeIngredientViewModel>(
                recipeIngredients.Select((item, index) => new RecipeIngredientViewModel
                {
                    IngredientId = item.IngredientId,
                    IngredientName = item.IngredientName,
                    Unit = item.Unit,
                    Quantity = item.Quantity,
                    CurrentPrice = item.CurrentPrice,
                    IncludeInDoughWeight = true,
                    DisplayOrder = index + 1
                }));

            CalculationResult = null;
            IsEditingRecipe = true;

            await ShowSuccessAsync($"Resep '{RecipeName}' berhasil dibuka.");
        }, "Opening selected recipe");
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
                await SaveRecipeIngredientsAsync(_selectedRecipeId);
                await ShowSuccessAsync($"Recipe '{RecipeName}' created successfully");
            }
            else
            {
                await _recipeRepository.UpdateAsync(recipe);
                await SaveRecipeIngredientsAsync(_selectedRecipeId);
                await ShowSuccessAsync($"Recipe '{RecipeName}' updated successfully");
            }

            IsEditingRecipe = false;
            await LoadRecipesAsync();
            SelectedRecipeSummary = SavedRecipes.FirstOrDefault(x => x.Id == _selectedRecipeId);
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
            await LoadRecipesAsync();
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

            var ingredientName = recipeIngredient.IngredientName;
            var ingredientQty = recipeIngredient.Quantity;

            RecipeIngredients.Add(recipeIngredient);
            ReindexIngredientDisplayOrder();
            SelectedAvailableIngredient = null;
            SelectedIngredientQuantity = 1m;

            Logger?.Information("Ingredient added to recipe: {Name}, Qty={Qty}",
                ingredientName, ingredientQty);

            await ShowSuccessAsync($"Ingredient '{ingredientName}' added");
        }, "Adding ingredient");
    }

    /// <summary>
    /// Adds a custom ingredient line directly from the enterprise input panel.
    /// </summary>
    private async Task AddCustomIngredientAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewIngredientName))
            {
                await ShowErrorAsync("Nama bahan wajib diisi.");
                return;
            }

            if (NewIngredientQuantity <= 0m)
            {
                await ShowErrorAsync("Jumlah bahan harus lebih dari 0.");
                return;
            }

            var ingredient = new RecipeIngredientViewModel
            {
                IngredientId = GetNextIngredientId(),
                IngredientName = NewIngredientName.Trim(),
                Unit = string.IsNullOrWhiteSpace(NewIngredientUnit) ? "g" : NewIngredientUnit.Trim(),
                Quantity = NewIngredientQuantity,
                PackNetQuantity = NewIngredientPackNetQuantity,
                PricePerPack = NewIngredientPricePerPack,
                ManualCost = NewIngredientManualCost,
                IncludeInDoughWeight = NewIngredientIncludeInDoughWeight,
                DisplayOrder = RecipeIngredients.Count + 1
            };

            // Keep backward compatibility for older unit-price mode.
            ingredient.CurrentPrice = ingredient.Quantity > 0m
                ? ingredient.TotalCost / ingredient.Quantity
                : 0m;

            RecipeIngredients.Add(ingredient);
            ReindexIngredientDisplayOrder();
            SelectedIngredient = ingredient;

            NewIngredientName = string.Empty;
            NewIngredientQuantity = 100m;
            NewIngredientPackNetQuantity = 1000m;
            NewIngredientPricePerPack = 10000m;
            NewIngredientManualCost = null;
            NewIngredientUnit = "g";
            NewIngredientIncludeInDoughWeight = true;

            await ShowSuccessAsync($"Bahan '{ingredient.IngredientName}' ditambahkan.");
        }, "Adding custom ingredient");
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
            ReindexIngredientDisplayOrder();
            await ShowSuccessAsync($"Ingredient '{SelectedIngredient.IngredientName}' removed");

            Logger?.Information("Ingredient removed from recipe: {Name}", SelectedIngredient.IngredientName);
        }, "Removing ingredient");
    }

    private int GetNextIngredientId()
    {
        if (RecipeIngredients.Count == 0)
        {
            return 1;
        }

        return RecipeIngredients.Max(x => x.IngredientId) + 1;
    }

    private void ReindexIngredientDisplayOrder()
    {
        var idx = 1;
        foreach (var ingredient in RecipeIngredients)
        {
            ingredient.DisplayOrder = idx++;
        }
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
                UseWeightBasedOutput = UseWeightBasedOutput,
                DonutWeightGrams = DonutWeightGrams,
                ToppingWeightPerDonutGrams = ToppingWeightPerDonutGrams,
                ToppingPackWeightGrams = ToppingPackWeightGrams,
                ToppingPackPrice = ToppingPackPrice,
                PriceVolatilityPercent = PriceVolatilityPercent,
                RiskAppetitePercent = RiskAppetitePercent,
                MarketPressurePercent = MarketPressurePercent,
                TargetProfitPerBatch = TargetProfitPerBatch,
                MonthlyFixedCost = MonthlyFixedCost,
                Markup = Markup,
                VatPercent = VatPercent,
                RoundingRule = RoundingRule,
                PricingStrategy = PricingStrategy,
                TargetMarginPercent = TargetMarginPercent
            };

            if (!request.IsValid())
            {
                await ShowErrorAsync("Invalid batch parameters. Please check all values.");
                return;
            }

            CalculationResult = await _pricingEngine.CalculateBatchCostAsync(request);
            if (CalculationResult != null)
            {
                CaptureCalculationHistory(request, CalculationResult);
            }
            ShowCalculationPanel = true;

            Logger?.Information("Batch cost calculated: UnitCost={UnitCost:C}, SuggestedPrice={Price:C}",
                CalculationResult?.UnitCost, CalculationResult?.SuggestedPrice);

            await ShowSuccessAsync("Batch cost calculated successfully");
        }, "Calculating batch cost");
    }

    private async Task SaveRecipeIngredientsAsync(int recipeId)
    {
        var existing = await _recipeRepository.GetRecipeIngredientsAsync(recipeId);
        foreach (var item in existing)
        {
            await _recipeRepository.RemoveIngredientAsync(recipeId, item.RecipeIngredientId);
        }

        foreach (var line in RecipeIngredients)
        {
            var ingredientId = await EnsureIngredientExistsAsync(line);
            await _recipeRepository.AddIngredientAsync(recipeId, ingredientId, line.Quantity);
        }
    }

    private async Task<int> EnsureIngredientExistsAsync(RecipeIngredientViewModel line)
    {
        if (line.IngredientId > 0)
        {
            var existingById = await _ingredientRepository.GetByIdAsync(line.IngredientId);
            if (existingById != null)
            {
                return existingById.Id;
            }
        }

        var existingByName = await _ingredientRepository.GetByNameAsync(line.IngredientName);
        if (existingByName != null)
        {
            line.IngredientId = existingByName.Id;
            return existingByName.Id;
        }

        var inferredPrice = line.Quantity > 0m
            ? line.TotalCost / line.Quantity
            : 0m;

        var created = await _ingredientRepository.CreateAsync(new Ingredient
        {
            Name = line.IngredientName,
            Unit = string.IsNullOrWhiteSpace(line.Unit) ? "g" : line.Unit,
            CurrentPrice = Math.Max(0m, inferredPrice)
        });

        line.IngredientId = created.Id;

        if (AvailableIngredients.All(x => x.Id != created.Id))
        {
            AvailableIngredients.Add(created);
        }

        return created.Id;
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
            PricePerUnit = ri.CurrentPrice,
            PackNetQuantity = ri.PackNetQuantity,
            PricePerPack = ri.PricePerPack,
            ManualCost = ri.ManualCost,
            IncludeInDoughWeight = ri.IncludeInDoughWeight
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
            if (string.IsNullOrWhiteSpace(NewLaborName))
            {
                await ShowErrorAsync("Labor role name is required");
                return;
            }

            var labor = new LaborRoleViewModel
            {
                Id = LaborRoles.Count == 0 ? 1 : LaborRoles.Max(x => x.Id) + 1,
                Name = NewLaborName.Trim(),
                HourlyRate = NewLaborRate,
                Hours = NewLaborHours
            };

            LaborRoles.Add(labor);
            NewLaborName = string.Empty;
            NewLaborRate = 50m;
            NewLaborHours = 2m;
            SelectedLaborRole = labor;

            Logger?.Information("Labor role added: {Name}, Rate={Rate:C}/h", labor.Name, labor.HourlyRate);
            await ShowSuccessAsync($"Tenaga kerja '{labor.Name}' ditambahkan.");
        }, "Adding labor role");
    }

    /// <summary>
    /// Removes the selected labor role.
    /// </summary>
    private async Task RemoveLaborRoleAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (SelectedLaborRole == null)
            {
                await ShowErrorAsync("Pilih data tenaga kerja yang ingin dihapus.");
                return;
            }

            var name = SelectedLaborRole.Name;
            LaborRoles.Remove(SelectedLaborRole);
            SelectedLaborRole = null;
            await ShowSuccessAsync($"Tenaga kerja '{name}' dihapus.");
        }, "Removing labor role");
    }

    /// <summary>
    /// Exports the current recipe to CSV or other format.
    /// </summary>
    private async Task ExportRecipeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(RecipeName))
            {
                await ShowErrorAsync("Isi nama resep dulu sebelum export.");
                return;
            }

            var exportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "HppDonatExports");
            Directory.CreateDirectory(exportDirectory);

            var safeName = string.Concat(RecipeName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var filePath = Path.Combine(exportDirectory, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            await writer.WriteLineAsync("Section,Field,Value");
            await writer.WriteLineAsync($"Recipe,Name,\"{RecipeName}\"");
            await writer.WriteLineAsync($"Recipe,Description,\"{RecipeDescription}\"");
            await writer.WriteLineAsync($"Recipe,PricingStrategy,{PricingStrategy}");
            await writer.WriteLineAsync($"Recipe,Markup,{Markup}");
            await writer.WriteLineAsync($"Recipe,VAT,{VatPercent}");
            await writer.WriteLineAsync($"Recipe,RoundingRule,{RoundingRule}");
            await writer.WriteLineAsync($"Recipe,TheoreticalOutput,{TheoreticalOutput}");
            await writer.WriteLineAsync($"Recipe,WastePercent,{WastePercent}");
            await writer.WriteLineAsync($"Recipe,BatchMultiplier,{BatchMultiplier}");
            await writer.WriteLineAsync($"Recipe,PackagingPerUnit,{PackagingPerUnit}");
            await writer.WriteLineAsync("Ingredients,Name,Unit,Quantity,PricePerPack,PackNetQuantity,ManualCost,LineTotal");

            foreach (var ingredient in RecipeIngredients)
            {
                await writer.WriteLineAsync(
                    $"Ingredients,\"{ingredient.IngredientName}\",{ingredient.Unit},{ingredient.Quantity},{ingredient.PricePerPack},{ingredient.PackNetQuantity},{ingredient.ManualCost},{ingredient.TotalCost}");
            }

            if (CalculationResult != null)
            {
                await writer.WriteLineAsync("Results,Metric,Value");
                await writer.WriteLineAsync($"Results,TotalBatchCost,{CalculationResult.TotalBatchCost}");
                await writer.WriteLineAsync($"Results,UnitCost,{CalculationResult.UnitCost}");
                await writer.WriteLineAsync($"Results,SuggestedPrice,{CalculationResult.SuggestedPrice}");
                await writer.WriteLineAsync($"Results,PriceIncVat,{CalculationResult.PriceIncVat}");
                await writer.WriteLineAsync($"Results,Margin,{CalculationResult.Margin}");
                await writer.WriteLineAsync($"Results,ContributionMarginPerUnit,{CalculationResult.ContributionMarginPerUnit}");
                await writer.WriteLineAsync($"Results,ProfitPerBatch,{CalculationResult.ProfitPerBatchAtSuggestedPrice}");
            }

            Logger?.Information("Recipe exported to {Path}", filePath);
            await ShowSuccessAsync($"Recipe exported: {filePath}");
        }, "Exporting recipe");
    }

    /// <summary>
    /// Imports workbook values from MODAL_DONAT.xlsx-compatible file.
    /// </summary>
    private async Task ImportWorkbookAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(WorkbookPath))
            {
                await ShowErrorAsync("Workbook path is required.");
                return;
            }

            if (!File.Exists(WorkbookPath))
            {
                await ShowErrorAsync($"Workbook not found: {WorkbookPath}");
                return;
            }

            var imported = ParseWorkbook(WorkbookPath);
            RecipeIngredients = new ObservableCollection<RecipeIngredientViewModel>(imported.Ingredients);
            ReindexIngredientDisplayOrder();

            RecipeName = Path.GetFileNameWithoutExtension(WorkbookPath);
            RecipeDescription = "Imported from MODAL_DONAT workbook";

            UseWeightBasedOutput = true;
            DonutWeightGrams = imported.DonutWeightGrams;
            TheoreticalOutput = imported.TheoreticalOutput;
            WastePercent = 0m;

            OilUsedLiters = imported.OilUsedLiters;
            OilPricePerLiter = imported.OilPricePerLiter;
            OilChangeCost = 0m;
            BatchesPerOilChange = 1;

            EnergyKwh = imported.EnergyCost > 0m ? 1m : 0m;
            EnergyRatePerKwh = imported.EnergyCost;

            LaborRoles = new ObservableCollection<LaborRoleViewModel>(imported.LaborRoles);
            OverheadAllocated = imported.OverheadAllocated;

            ToppingWeightPerDonutGrams = imported.ToppingWeightPerDonutGrams;
            ToppingPackWeightGrams = imported.ToppingPackWeightGrams;
            ToppingPackPrice = imported.ToppingPackPrice;
            Markup = 0m;
            VatPercent = 0m;
            RoundingRule = "100";
            PricingStrategy = "FixedMarkup";
            TargetMarginPercent = 0.30m;
            PriceVolatilityPercent = 0.08m;
            RiskAppetitePercent = 0.50m;
            MarketPressurePercent = 0m;
            TargetProfitPerBatch = 0m;
            MonthlyFixedCost = 0m;

            await ShowSuccessAsync($"Workbook imported: {RecipeIngredients.Count} ingredients loaded.");
        }, "Import workbook");
    }

    private WorkbookImportResult ParseWorkbook(string workbookPath)
    {
        using var archive = ZipFile.OpenRead(workbookPath);
        var sharedStrings = LoadSharedStrings(archive);
        var sheetXml = ReadZipEntry(archive, "xl/worksheets/sheet1.xml");
        var sheetDoc = XDocument.Parse(sheetXml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var cells = sheetDoc
            .Descendants(ns + "c")
            .Select(c => new XlsxCell(
                Reference: c.Attribute("r")?.Value ?? string.Empty,
                Type: c.Attribute("t")?.Value,
                Formula: c.Element(ns + "f")?.Value,
                Value: c.Element(ns + "v")?.Value))
            .Where(c => !string.IsNullOrWhiteSpace(c.Reference))
            .ToDictionary(c => c.Reference, c => c, StringComparer.OrdinalIgnoreCase);

        var ingredients = new List<RecipeIngredientViewModel>();

        for (var row = 3; row <= 10; row++)
        {
            var quantity = GetCellDecimal(cells, $"C{row}", sharedStrings) ?? 0m;
            if (quantity <= 0m)
            {
                continue;
            }

            var name = GetCellText(cells, $"B{row}", sharedStrings);
            var unit = GetCellText(cells, $"D{row}", sharedStrings);
            var packNet = GetCellDecimal(cells, $"E{row}", sharedStrings);
            var pricePerPack = GetCellDecimal(cells, $"G{row}", sharedStrings);
            var lineCost = GetCellDecimal(cells, $"H{row}", sharedStrings);

            decimal? manualCost = null;
            if ((!packNet.HasValue || !pricePerPack.HasValue) && lineCost.HasValue)
            {
                manualCost = lineCost.Value;
            }

            var currentPrice = 0m;
            if (packNet.HasValue && packNet.Value > 0m && pricePerPack.HasValue && pricePerPack.Value >= 0m)
            {
                currentPrice = pricePerPack.Value / packNet.Value;
            }
            else if (manualCost.HasValue)
            {
                currentPrice = manualCost.Value / quantity;
            }

            ingredients.Add(new RecipeIngredientViewModel
            {
                IngredientId = row - 2,
                IngredientName = string.IsNullOrWhiteSpace(name) ? $"Item {row - 2}" : name,
                Unit = string.IsNullOrWhiteSpace(unit) ? "g" : unit,
                Quantity = quantity,
                CurrentPrice = currentPrice,
                PackNetQuantity = packNet,
                PricePerPack = pricePerPack,
                ManualCost = manualCost,
                IncludeInDoughWeight = true,
                DisplayOrder = row - 2
            });
        }

        var theoreticalOutput = (int)Math.Floor(GetCellDecimal(cells, "K1", sharedStrings) ?? 0m);
        if (theoreticalOutput <= 0)
        {
            theoreticalOutput = 80;
        }

        var donutWeight = ParseDonutWeightFromFormula(cells.TryGetValue("K1", out var k1) ? k1.Formula : null);
        var toppingWeightPerDonut = GetCellDecimal(cells, "N2", sharedStrings) ?? 0m;
        var toppingPackWeight = GetCellDecimal(cells, "O2", sharedStrings) ?? 0m;
        var toppingPackPrice = GetCellDecimal(cells, "P2", sharedStrings) ?? 0m;

        var oilUsed = 0m;
        var oilUsageQty = GetCellDecimal(cells, "C13", sharedStrings) ?? 0m;
        var oilPackNet = GetCellDecimal(cells, "E13", sharedStrings) ?? 0m;
        if (oilUsageQty > 0m && oilPackNet > 0m)
        {
            oilUsed = oilUsageQty / oilPackNet;
        }

        var oilPrice = GetCellDecimal(cells, "G13", sharedStrings) ?? 0m;
        var energyCost = GetCellDecimal(cells, "H14", sharedStrings) ?? 0m;
        var gasCost = GetCellDecimal(cells, "H15", sharedStrings) ?? 0m;
        var laborCost = GetCellDecimal(cells, "H16", sharedStrings) ?? 0m;
        var riskCost = GetCellDecimal(cells, "H17", sharedStrings) ?? 0m;

        var laborRoles = new List<LaborRoleViewModel>();
        if (laborCost > 0m)
        {
            laborRoles.Add(new LaborRoleViewModel
            {
                Id = 1,
                Name = "Tenaga",
                Hours = 1m,
                HourlyRate = laborCost
            });
        }

        return new WorkbookImportResult(
            Ingredients: ingredients,
            TheoreticalOutput: theoreticalOutput,
            DonutWeightGrams: donutWeight,
            OilUsedLiters: oilUsed,
            OilPricePerLiter: oilPrice,
            EnergyCost: energyCost,
            OverheadAllocated: gasCost + riskCost,
            LaborRoles: laborRoles,
            ToppingWeightPerDonutGrams: toppingWeightPerDonut,
            ToppingPackWeightGrams: toppingPackWeight,
            ToppingPackPrice: toppingPackPrice);
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
        {
            return new List<string>();
        }

        var xml = ReadZipEntry(archive, "xl/sharedStrings.xml");
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        return doc
            .Descendants(ns + "si")
            .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string ReadZipEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Entry not found in workbook: {entryName}");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetCellText(Dictionary<string, XlsxCell> cells, string cellReference, IReadOnlyList<string> sharedStrings)
    {
        if (!cells.TryGetValue(cellReference, out var cell))
        {
            return string.Empty;
        }

        if (string.Equals(cell.Type, "s", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(cell.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) &&
                idx >= 0 && idx < sharedStrings.Count)
            {
                return sharedStrings[idx];
            }

            return string.Empty;
        }

        return cell.Value ?? string.Empty;
    }

    private static decimal? GetCellDecimal(Dictionary<string, XlsxCell> cells, string cellReference, IReadOnlyList<string> sharedStrings)
    {
        var text = GetCellText(cells, cellReference, sharedStrings);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal ParseDonutWeightFromFormula(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            return 25m;
        }

        var slashIdx = formula.LastIndexOf('/');
        if (slashIdx < 0 || slashIdx == formula.Length - 1)
        {
            return 25m;
        }

        var tail = formula.Substring(slashIdx + 1);
        if (decimal.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m)
        {
            return parsed;
        }

        return 25m;
    }

    private string NormalizePricingStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return "FixedMarkup";
        }

        var available = PricingStrategyFactory.GetAvailableStrategies();
        var normalized = available.FirstOrDefault(x => string.Equals(x, strategy, StringComparison.OrdinalIgnoreCase));
        return normalized ?? "FixedMarkup";
    }

    private void RefreshBreakdownRows()
    {
        CostBreakdownRows.Clear();
        if (CalculationResult?.BreakdownDictionary == null || CalculationResult.BreakdownDictionary.Count == 0)
        {
            return;
        }

        var total = CalculationResult.TotalBatchCost <= 0m ? 1m : CalculationResult.TotalBatchCost;
        var preferredOrder = new[]
        {
            "Ingredients",
            "Oil (Usage)",
            "Oil (Maintenance)",
            "Energy",
            "Labor",
            "Overhead",
            "Packaging",
            "ToppingPerDonut",
            "CostPerDonutWithTopping",
            "MinimumSafePrice",
            "ContributionMarginPerUnit",
            "RiskBufferPercent"
        };

        foreach (var key in preferredOrder)
        {
            if (!CalculationResult.BreakdownDictionary.TryGetValue(key, out var amount))
            {
                continue;
            }

            var isPercent = key.EndsWith("Percent", StringComparison.OrdinalIgnoreCase);
            var percent = isPercent ? amount : amount / total;
            CostBreakdownRows.Add(new CostBreakdownRowViewModel
            {
                Component = key,
                Amount = amount,
                Percent = percent
            });
        }
    }

    // Validation methods for commands
    private bool CanSaveRecipe() => !string.IsNullOrWhiteSpace(RecipeName) && RecipeIngredients.Count > 0;
    private bool CanDeleteRecipe() => _selectedRecipeId > 0;
    private bool CanAddIngredient() => SelectedAvailableIngredient != null;
    private bool CanRemoveIngredient() => SelectedIngredient != null;
    private bool CanAddCustomIngredient() => !string.IsNullOrWhiteSpace(NewIngredientName) && NewIngredientQuantity > 0m;

    /// <summary>
    /// Overrides to load ingredients when navigated to.
    /// </summary>
    public override async Task OnNavigatedToAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RefreshAiConfigurationState();
            IsLoadingRecipes = true;
            try
            {
                var ingredients = await _ingredientRepository.GetAllAsync();
                AvailableIngredients = new ObservableCollection<Ingredient>(ingredients);

                var recipes = await _recipeRepository.GetAllAsync();
                SavedRecipes = new ObservableCollection<RecipeSummaryViewModel>(recipes
                    .OrderBy(x => x.Name)
                    .Select(x => new RecipeSummaryViewModel
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Description = x.Description,
                        TheoreticalOutput = x.TheoreticalOutput,
                        WastePercent = x.WastePercent
                    }));

                if (_selectedRecipeId > 0)
                {
                    SelectedRecipeSummary = SavedRecipes.FirstOrDefault(x => x.Id == _selectedRecipeId);
                }

                Logger?.Information("Loaded {IngredientCount} ingredients and {RecipeCount} recipes", ingredients.Count(), SavedRecipes.Count);
            }
            finally
            {
                IsLoadingRecipes = false;
            }
        }, "Loading ingredients");
    }

    protected override Task ShowErrorAsync(string message)
    {
        StatusMessage = message;
        return base.ShowErrorAsync(message);
    }

    protected override Task ShowSuccessAsync(string message)
    {
        StatusMessage = message;
        return base.ShowSuccessAsync(message);
    }
}

internal sealed record WorkbookImportResult(
    IReadOnlyList<RecipeIngredientViewModel> Ingredients,
    int TheoreticalOutput,
    decimal DonutWeightGrams,
    decimal OilUsedLiters,
    decimal OilPricePerLiter,
    decimal EnergyCost,
    decimal OverheadAllocated,
    IReadOnlyList<LaborRoleViewModel> LaborRoles,
    decimal ToppingWeightPerDonutGrams,
    decimal ToppingPackWeightGrams,
    decimal ToppingPackPrice);

internal sealed record XlsxCell(
    string Reference,
    string? Type,
    string? Formula,
    string? Value);

/// <summary>
/// View model for a single recipe ingredient in the list.
/// </summary>
public class RecipeIngredientViewModel : ObservableObject
{
    private decimal _quantity;
    private decimal _currentPrice;
    private int _displayOrder;
    private decimal? _packNetQuantity;
    private decimal? _pricePerPack;
    private decimal? _manualCost;
    private bool _includeInDoughWeight = true;

    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
            {
                OnPropertyChanged(nameof(TotalCost));
            }
        }
    }

    public decimal CurrentPrice
    {
        get => _currentPrice;
        set
        {
            if (SetProperty(ref _currentPrice, value))
            {
                OnPropertyChanged(nameof(TotalCost));
            }
        }
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    public decimal? PackNetQuantity
    {
        get => _packNetQuantity;
        set
        {
            if (SetProperty(ref _packNetQuantity, value))
            {
                OnPropertyChanged(nameof(TotalCost));
            }
        }
    }

    public decimal? PricePerPack
    {
        get => _pricePerPack;
        set
        {
            if (SetProperty(ref _pricePerPack, value))
            {
                OnPropertyChanged(nameof(TotalCost));
            }
        }
    }

    public decimal? ManualCost
    {
        get => _manualCost;
        set
        {
            if (SetProperty(ref _manualCost, value))
            {
                OnPropertyChanged(nameof(TotalCost));
            }
        }
    }

    public bool IncludeInDoughWeight
    {
        get => _includeInDoughWeight;
        set => SetProperty(ref _includeInDoughWeight, value);
    }

    public decimal TotalCost
    {
        get
        {
            if (ManualCost.HasValue)
            {
                return ManualCost.Value;
            }

            if (PackNetQuantity.HasValue && PackNetQuantity.Value > 0m &&
                PricePerPack.HasValue && PricePerPack.Value >= 0m)
            {
                return PricePerPack.Value / PackNetQuantity.Value * Quantity;
            }

            return Quantity * CurrentPrice;
        }
    }
}

public class RecipeSummaryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TheoreticalOutput { get; set; }
    public decimal WastePercent { get; set; }
}

public class CostBreakdownRowViewModel
{
    public string Component { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percent { get; set; }
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
