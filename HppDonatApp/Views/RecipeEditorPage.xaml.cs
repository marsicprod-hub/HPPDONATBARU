using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HppDonatApp.Data;
using HppDonatApp.Domain;
using HppDonatApp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace HppDonatApp.Views
{
    /// <summary>
    /// Halaman editor resep donat dengan perhitungan HPP real-time
    /// Fitur: Input resep, manajemen bahan, perhitungan biaya, strategi pricing
    /// </summary>
    public sealed partial class RecipeEditorPage : Page
    {
        private RecipeEditorViewModel _viewModel;

        public RecipeEditorViewModel ViewModel => _viewModel;

        public RecipeEditorPage()
        {
            this.InitializeComponent();
            InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            var serviceProvider = ((App)Application.Current).ServiceProvider;
            _viewModel = new RecipeEditorViewModel(
                serviceProvider.GetRequiredService<RecipeRepository>(),
                serviceProvider.GetRequiredService<IngredientRepository>(),
                serviceProvider.GetRequiredService<PricingEngine>(),
                serviceProvider.GetRequiredService<PricingStrategy>(),
                serviceProvider.GetRequiredService<ILogger>()
            );
            this.DataContext = _viewModel;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await _viewModel.LoadInitialDataAsync();
        }
    }

    /// <summary>
    /// ViewModel untuk editor resep dengan perhitungan HPP otomatis
    /// Menangani input resep, bahan, dan strategi pricing
    /// </summary>
    public class RecipeEditorViewModel : ObservableObject
    {
        private readonly RecipeRepository _recipeRepository;
        private readonly IngredientRepository _ingredientRepository;
        private readonly PricingEngine _pricingEngine;
        private readonly PricingStrategy _pricingStrategy;
        private readonly ILogger _logger;

        private Recipe _currentRecipe;
        private ObservableCollection<RecipeIngredientViewModel> _recipeIngredients;
        private ObservableCollection<string> _categories;
        private RecipeIngredientViewModel _selectedIngredient;
        private decimal _totalCost;
        private decimal _costPerUnit;
        private decimal _recommendedPrice;
        private decimal _projectedMargin;

        public Recipe CurrentRecipe
        {
            get => _currentRecipe;
            set => SetProperty(ref _currentRecipe, value);
        }

        public ObservableCollection<RecipeIngredientViewModel> RecipeIngredients
        {
            get => _recipeIngredients;
            set => SetProperty(ref _recipeIngredients, value);
        }

        public ObservableCollection<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public RecipeIngredientViewModel SelectedIngredient
        {
            get => _selectedIngredient;
            set => SetProperty(ref _selectedIngredient, value);
        }

        public decimal TotalCost
        {
            get => _totalCost;
            set => SetProperty(ref _totalCost, value);
        }

        public decimal CostPerUnit
        {
            get => _costPerUnit;
            set => SetProperty(ref _costPerUnit, value);
        }

        public decimal RecommendedPrice
        {
            get => _recommendedPrice;
            set => SetProperty(ref _recommendedPrice, value);
        }

        public decimal ProjectedMargin
        {
            get => _projectedMargin;
            set => SetProperty(ref _projectedMargin, value);
        }

        [RelayCommand]
        public async Task AddIngredient()
        {
            try
            {
                _logger.Information("Add ingredient command triggered");
                // Show dialog to select ingredient
                var viewModel = new RecipeIngredientViewModel 
                { 
                    IngredientName = "",
                    Quantity = 0,
                    UnitOfMeasure = "kg"
                };
                RecipeIngredients?.Add(viewModel);
                await CalculateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding ingredient");
            }
        }

        [RelayCommand]
        public async Task RemoveIngredient()
        {
            if (SelectedIngredient == null)
                return;

            try
            {
                _logger.Information("Removing ingredient: {IngredientName}", SelectedIngredient.IngredientName);
                RecipeIngredients.Remove(SelectedIngredient);
                await CalculateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing ingredient");
            }
        }

        [RelayCommand]
        public async Task Calculate()
        {
            await CalculateAsync();
        }

        [RelayCommand]
        public async Task Save()
        {
            try
            {
                if (CurrentRecipe == null)
                {
                    _logger.Warning("Cannot save null recipe");
                    return;
                }

                _logger.Information("Saving recipe: {RecipeName}", CurrentRecipe.RecipeName);

                // Update recipe with calculated values
                CurrentRecipe.EstimatedCostPerBatch = TotalCost;
                CurrentRecipe.UpdatedAt = DateTime.Now;

                // Save recipe
                if (CurrentRecipe.Id == 0)
                {
                    await _recipeRepository.CreateAsync(CurrentRecipe);
                }
                else
                {
                    await _recipeRepository.UpdateAsync(CurrentRecipe);
                }

                _logger.Information("Recipe saved successfully: {RecipeId}", CurrentRecipe.Id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving recipe");
            }
        }

        [RelayCommand]
        public Task Cancel()
        {
            _logger.Information("Recipe edit cancelled");
            return Task.CompletedTask;
        }

        public void OnIngredientSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is RecipeIngredientViewModel ingredient)
            {
                SelectedIngredient = ingredient;
            }
        }

        private async Task CalculateAsync()
        {
            try
            {
                _logger.Information("Calculating recipe costs");

                if (RecipeIngredients == null || !RecipeIngredients.Any())
                {
                    TotalCost = 0;
                    CostPerUnit = 0;
                    RecommendedPrice = 0;
                    ProjectedMargin = 0;
                    return;
                }

                // Calculate total cost
                TotalCost = RecipeIngredients.Sum(i => i.TotalCost);

                // Calculate cost per unit
                if (CurrentRecipe?.DonutsPerBatch > 0)
                {
                    CostPerUnit = TotalCost / (decimal)CurrentRecipe.DonutsPerBatch;
                }

                // Calculate recommended price with margin
                var targetMargin = CurrentRecipe?.TargetMarginPercent ?? 30m;
                RecommendedPrice = CostPerUnit * (1 + targetMargin / 100);

                // Calculate projected margin
                if (RecommendedPrice > 0)
                {
                    ProjectedMargin = ((RecommendedPrice - CostPerUnit) / RecommendedPrice) * 100;
                }

                _logger.Information("Calculation complete - Total: {Total}, CostPerUnit: {CostPerUnit}, RecPrice: {RecPrice}",
                    TotalCost, CostPerUnit, RecommendedPrice);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating recipe costs");
            }
        }

        public async Task LoadInitialDataAsync()
        {
            try
            {
                _logger.Information("Loading recipe editor initial data");

                // Initialize new recipe
                CurrentRecipe = new Recipe
                {
                    RecipeName = "",
                    Description = "",
                    Category = "Donat Standar",
                    DonutsPerBatch = 12,
                    TargetMarginPercent = 30,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                RecipeIngredients = new ObservableCollection<RecipeIngredientViewModel>();

                // Load categories
                var categories = new List<string>
                {
                    "Donat Standar",
                    "Donat Premium",
                    "Donat Spesial",
                    "Donat Gluten Free",
                    "Donat Vegan"
                };
                Categories = new ObservableCollection<string>(categories);

                _logger.Information("Recipe editor initialized");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading initial data for recipe editor");
            }
        }

        public RecipeEditorViewModel(
            RecipeRepository recipeRepository,
            IngredientRepository ingredientRepository,
            PricingEngine pricingEngine,
            PricingStrategy pricingStrategy,
            ILogger logger)
        {
            _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
            _ingredientRepository = ingredientRepository ?? throw new ArgumentNullException(nameof(ingredientRepository));
            _pricingEngine = pricingEngine ?? throw new ArgumentNullException(nameof(pricingEngine));
            _pricingStrategy = pricingStrategy ?? throw new ArgumentNullException(nameof(pricingStrategy));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _recipeIngredients = new ObservableCollection<RecipeIngredientViewModel>();
            _categories = new ObservableCollection<string>();
        }
    }

    /// <summary>
    /// Service untuk validasi dan kalkulasi resep
    /// Menyediakan metode untuk validasi bahan, perhitungan biaya, dan optimasi harga
    /// </summary>
    public class RecipeCalculationService
    {
        private readonly ILogger _logger;

        public RecipeCalculationService(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Memvalidasi resep berdasarkan aturan bisnis
        /// </summary>
        public async Task<RecipeValidationResult> ValidateRecipeAsync(Recipe recipe)
        {
            try
            {
                var result = new RecipeValidationResult { IsValid = true };

                // Validasi nama resep
                if (string.IsNullOrWhiteSpace(recipe.RecipeName))
                {
                    result.IsValid = false;
                    result.Errors.Add("Nama resep tidak boleh kosong");
                }

                // Validasi batch size
                if ((recipe.DonutsPerBatch ?? 0) <= 0)
                {
                    result.IsValid = false;
                    result.Errors.Add("Jumlah donat per batch harus lebih dari 0");
                }

                // Validasi margin target
                if ((recipe.TargetMarginPercent ?? 0) < 0)
                {
                    result.IsValid = false;
                    result.Errors.Add("Target margin tidak boleh negatif");
                }

                if ((recipe.TargetMarginPercent ?? 0) > 300)
                {
                    result.Warnings.Add("Target margin sangat tinggi, mungkin tidak realistis");
                }

                _logger.Information("Recipe validation complete: Valid={Valid}, Errors={ErrorCount}", 
                    result.IsValid, result.Errors.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating recipe");
                return new RecipeValidationResult { IsValid = false, Errors = { "Terjadi kesalahan saat validasi" } };
            }
        }

        /// <summary>
        /// Mengoptimalkan harga jual berdasarkan berbagai strategi pricing
        /// </summary>
        public PricingOptimizationResult OptimizePrice(Recipe recipe, List<RecipeIngredientViewModel> ingredients)
        {
            try
            {
                var result = new PricingOptimizationResult();
                
                // Hitung total cost
                decimal totalCost = ingredients?.Sum(i => i.TotalCost) ?? 0;
                decimal costPerUnit = recipe.DonutsPerBatch > 0 
                    ? totalCost / (decimal)recipe.DonutsPerBatch 
                    : 0;

                // Strategi 1: Fixed Markup
                decimal fixedMarkupPrice = costPerUnit * 1.4m; // 40% markup
                
                // Strategi 2: Target Margin
                decimal targetMargin = recipe.TargetMarginPercent ?? 30;
                decimal targetMarginPrice = costPerUnit * (1 + targetMargin / 100);

                // Strategi 3: Competitive Pricing (rounded ke nilai psikologis)
                decimal competitivePrice = RoundToNearestPsychologicalPrice(targetMarginPrice);

                // Strategi 4: Premium Pricing (dengan packaging cost)
                decimal premiumPrice = targetMarginPrice * 1.15m; // 15% premium

                result.CostPerUnit = costPerUnit;
                result.FixedMarkupPrice = fixedMarkupPrice;
                result.TargetMarginPrice = targetMarginPrice;
                result.CompetitivePrice = competitivePrice;
                result.PremiumPrice = premiumPrice;
                result.RecommendedPrice = targetMarginPrice; // Default recommendation

                _logger.Information("Price optimization complete: Cost={Cost:C}, RecBP={RecBP:C}", 
                    costPerUnit, result.RecommendedPrice);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error optimizing price");
                return new PricingOptimizationResult();
            }
        }

        /// <summary>
        /// Menganalisis profitabilitas resep dengan berbagai pricing scenarios
        /// </summary>
        public RecipeProfitabilityAnalysis AnalyzeProfitability(Recipe recipe, List<RecipeIngredientViewModel> ingredients, decimal sellingPrice, int unitsSold)
        {
            try
            {
                var analysis = new RecipeProfitabilityAnalysis();
                
                decimal totalCost = ingredients?.Sum(i => i.TotalCost) ?? 0;
                decimal totalRevenue = sellingPrice * unitsSold;
                decimal totalProfit = totalRevenue - totalCost;
                decimal profitMargin = totalRevenue > 0 ? (totalProfit / totalRevenue) * 100 : 0;

                analysis.TotalCost = totalCost;
                analysis.TotalRevenue = totalRevenue;
                analysis.TotalProfit = totalProfit;
                analysis.ProfitMarginPercent = profitMargin;
                analysis.CostPerUnit = recipe.DonutsPerBatch > 0 ? totalCost / (decimal)recipe.DonutsPerBatch : 0;
                analysis.RevenuePerUnit = sellingPrice;
                analysis.ProfitPerUnit = analysis.RevenuePerUnit - analysis.CostPerUnit;

                // Hitung break-even point
                if (analysis.ProfitPerUnit > 0)
                {
                    analysis.BreakEvenUnits = (int)Math.Ceiling(totalCost / analysis.ProfitPerUnit);
                }

                // Hitung ROI
                if (totalCost > 0)
                {
                    analysis.ROI = (totalProfit / totalCost) * 100;
                }

                _logger.Information("Profitability analysis: Profit={Profit:C}, Margin={Margin:P}, ROI={ROI:P}", 
                    totalProfit, profitMargin, analysis.ROI);

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing profitability");
                return new RecipeProfitabilityAnalysis();
            }
        }

        /// <summary>
        /// Membandingkan resep berdasarkan efisiensi biaya
        /// </summary>
        public List<RecipeComparison> CompareRecipes(List<Recipe> recipes)
        {
            try
            {
                var comparisons = new List<RecipeComparison>();

                foreach (var recipe in recipes ?? new List<Recipe>())
                {
                    comparisons.Add(new RecipeComparison
                    {
                        RecipeName = recipe.RecipeName,
                        TotalCost = recipe.EstimatedCostPerBatch ?? 0,
                        CostPerUnit = recipe.DonutsPerBatch > 0 
                            ? (recipe.EstimatedCostPerBatch ?? 0) / (decimal)recipe.DonutsPerBatch 
                            : 0,
                        TargetMargin = recipe.TargetMarginPercent ?? 0,
                        Efficiency = CalculateEfficiency(recipe)
                    });
                }

                // Sort by efficiency
                comparisons = comparisons.OrderByDescending(c => c.Efficiency).ToList();

                _logger.Information("Recipe comparison complete: {Count} recipes analyzed", comparisons.Count);
                return comparisons;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error comparing recipes");
                return new List<RecipeComparison>();
            }
        }

        private decimal RoundToNearestPsychologicalPrice(decimal price)
        {
            // Round ke harga yang lebih menarik (berakhir dengan 9 atau 5)
            if (price < 10000)
                return (decimal)Math.Ceiling(price / 1000) * 1000 - 100;
            else if (price < 100000)
                return (decimal)Math.Ceiling(price / 5000) * 5000 - 500;
            else
                return (decimal)Math.Ceiling(price / 10000) * 10000 - 1000;
        }

        private decimal CalculateEfficiency(Recipe recipe)
        {
            // Efisiensi = berapa banyak margin yang bisa diambil per unit biaya
            decimal cost = recipe.EstimatedCostPerBatch ?? 0;
            decimal margin = recipe.TargetMarginPercent ?? 0;
            
            if (cost <= 0)
                return 0;

            return (margin * 100) / cost;
        }
    }

    /// <summary>
    /// Model untuk hasil validasi resep
    /// </summary>
    public class RecipeValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Model untuk hasil optimasi harga
    /// </summary>
    public class PricingOptimizationResult
    {
        public decimal CostPerUnit { get; set; }
        public decimal FixedMarkupPrice { get; set; }
        public decimal TargetMarginPrice { get; set; }
        public decimal CompetitivePrice { get; set; }
        public decimal PremiumPrice { get; set; }
        public decimal RecommendedPrice { get; set; }
    }

    /// <summary>
    /// Model untuk analisis profitabilitas
    /// </summary>
    public class RecipeProfitabilityAnalysis
    {
        public decimal TotalCost { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalProfit { get; set; }
        public decimal ProfitMarginPercent { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal RevenuePerUnit { get; set; }
        public decimal ProfitPerUnit { get; set; }
        public int BreakEvenUnits { get; set; }
        public decimal ROI { get; set; }
    }

    /// <summary>
    /// Model untuk perbandingan resep
    /// </summary>
    public class RecipeComparison
    {
        public string RecipeName { get; set; }
        public decimal TotalCost { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal TargetMargin { get; set; }
        public decimal Efficiency { get; set; }
    }
}
