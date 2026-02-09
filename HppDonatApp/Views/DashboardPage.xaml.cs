using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HppDonatApp.Services;
using HppDonatApp.Data;
using HppDonatApp.Core.Services;
using HppDonatApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;

namespace HppDonatApp.Views
{
    /// <summary>
    /// View untuk halaman dashboard aplikasi HPP donat
    /// Menampilkan ringkasan statistik dan resep terbaru
    /// Terdiri dari: summary cards, recent recipes list, navigation buttons
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        private DashboardViewModel _viewModel;

        public DashboardViewModel ViewModel => _viewModel;

        public DashboardPage()
        {
            this.InitializeComponent();
            InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            var serviceProvider = ((App)Application.Current).ServiceProvider;
            _viewModel = new DashboardViewModel(
                serviceProvider.GetRequiredService<RecipeRepository>(),
                serviceProvider.GetRequiredService<PricingEngine>(),
                serviceProvider.GetRequiredService<ILogger>()
            );
            this.DataContext = _viewModel;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await _viewModel.LoadDashboardDataAsync();
        }
    }

    /// <summary>
    /// ViewModel untuk halaman dashboard
    /// Menangani pengambilan data resep, perhitungan statistik, dan pembaruan tampilan
    /// </summary>
    public class DashboardViewModel : ObservableObject
    {
        private readonly RecipeRepository _recipeRepository;
        private readonly PricingEngine _pricingEngine;
        private readonly ILogger _logger;

        private int _totalRecipes;
        private int _totalIngredients;
        private decimal _averageHPP;
        private decimal _recommendedPrice;
        private ObservableCollection<RecipeDisplayModel> _recentRecipes;

        public int TotalRecipes
        {
            get => _totalRecipes;
            set => SetProperty(ref _totalRecipes, value);
        }

        public int TotalIngredients
        {
            get => _totalIngredients;
            set => SetProperty(ref _totalIngredients, value);
        }

        public decimal AverageHPP
        {
            get => _averageHPP;
            set => SetProperty(ref _averageHPP, value);
        }

        public decimal RecommendedPrice
        {
            get => _recommendedPrice;
            set => SetProperty(ref _recommendedPrice, value);
        }

        public ObservableCollection<RecipeDisplayModel> RecentRecipes
        {
            get => _recentRecipes;
            set => SetProperty(ref _recentRecipes, value);
        }

        public DashboardViewModel(RecipeRepository recipeRepository, PricingEngine pricingEngine, ILogger logger)
        {
            _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
            _pricingEngine = pricingEngine ?? throw new ArgumentNullException(nameof(pricingEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _recentRecipes = new ObservableCollection<RecipeDisplayModel>();
        }

        public async Task LoadDashboardDataAsync()
        {
            try
            {
                _logger.Information("Loading dashboard data...");

                // Ambil semua resep
                var recipes = await _recipeRepository.GetAllAsync();
                TotalRecipes = recipes?.Count ?? 0;

                // Hitung rata-rata HPP jika ada resep
                if (recipes != null && recipes.Any())
                {
                    decimal totalHPP = 0;
                    int validRecipes = 0;

                    foreach (var recipe in recipes)
                    {
                        try
                        {
                            var ingredients = await _recipeRepository.GetRecipeIngredientsAsync(recipe.Id);
                            if (ingredients != null && ingredients.Any())
                            {
                                decimal recipeCost = ingredients.Sum(i => (decimal)(i.Quantity ?? 0) * (i.UnitPrice ?? 0));
                                totalHPP += recipeCost;
                                validRecipes++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error calculating cost for recipe: {RecipeId}", recipe.Id);
                        }
                    }

                    if (validRecipes > 0)
                    {
                        AverageHPP = totalHPP / validRecipes;
                        RecommendedPrice = AverageHPP * 1.3m; // 30% markup
                    }

                    // Ambil 5 resep terbaru
                    var recentRecipeEntities = recipes
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(5)
                        .ToList();

                    var displayRecipes = new List<RecipeDisplayModel>();
                    foreach (var recipe in recentRecipeEntities)
                    {
                        try
                        {
                            var cost = await CalculateRecipeCostAsync(recipe);
                            displayRecipes.Add(new RecipeDisplayModel
                            {
                                RecipeName = recipe.RecipeName,
                                LastModified = recipe.UpdatedAt?.ToString("dd MMM yyyy HH:mm") ?? "N/A",
                                EstCostPerBatch = $"Rp {cost:N0}",
                                RecommendedPrice = $"Rp {(cost * 1.3m):N0}"
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error processing recipe: {RecipeId}", recipe.Id);
                        }
                    }

                    RecentRecipes = new ObservableCollection<RecipeDisplayModel>(displayRecipes);
                }

                _logger.Information("Dashboard data loaded successfully. Total recipes: {TotalRecipes}", TotalRecipes);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading dashboard data");
            }
        }

        private async Task<decimal> CalculateRecipeCostAsync(Recipe recipe)
        {
            try
            {
                var ingredients = await _recipeRepository.GetRecipeIngredientsAsync(recipe.Id);
                if (ingredients == null || !ingredients.Any())
                    return 0;

                return ingredients.Sum(i => (decimal)(i.Quantity ?? 0) * (i.UnitPrice ?? 0));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating recipe cost: {RecipeId}", recipe.Id);
                return 0;
            }
        }

        /// <summary>
        /// Memperbarui data dashboard dengan data terbaru dari database
        /// Menghitung ulang semua statistik dan ringkasan
        /// </summary>
        public async Task RefreshDataAsync()
        {
            try
            {
                _logger.Information("Refreshing dashboard data");
                await LoadDashboardDataAsync();
                _logger.Information("Dashboard data refreshed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing dashboard data");
            }
        }
    }

    /// <summary>
    /// Model untuk menampilkan resep di dashboard
    /// Berisi informasi ringkas tentang resep dan estimasi biaya
    /// </summary>
    public class RecipeDisplayModel
    {
        public string RecipeName { get; set; }
        public string LastModified { get; set; }
        public string EstCostPerBatch { get; set; }
        public string RecommendedPrice { get; set; }
    }

    /// <summary>
    /// Service untuk analisis dashboard dan perhitungan statistik HPP
    /// Menyediakan metode untuk menganalisis trend harga, profitabilitas, dan efisiensi produksi
    /// Dengan caching untuk performa optimal pada dataset besar
    /// </summary>
    public class DashboardAnalyticsService
    {
        private readonly RecipeRepository _recipeRepository;
        private readonly ILogger _logger;
        private readonly Dictionary<string, (decimal Value, DateTime CachedAt)> _cache;
        private const int CACHE_DURATION_MINUTES = 15;

        public DashboardAnalyticsService(RecipeRepository recipeRepository, ILogger logger)
        {
            _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = new Dictionary<string, (decimal, DateTime)>();
        }

        /// <summary>
        /// Menghitung total revenue potensial dari semua resep aktif
        /// Dengan margin yang telah dikonfigurasi untuk setiap resep
        /// </summary>
        public async Task<decimal> CalculatePotentialRevenueAsync()
        {
            try
            {
                const string cacheKey = "potential_revenue";
                if (TryGetCachedValue(cacheKey, out var cachedValue))
                {
                    _logger.Information("Retrieved potential revenue from cache: {Value:C}", cachedValue);
                    return cachedValue;
                }

                var recipes = await _recipeRepository.GetAllAsync();
                if (recipes == null || !recipes.Any())
                    return 0;

                decimal totalRevenue = 0;
                foreach (var recipe in recipes)
                {
                    decimal cost = recipe.EstimatedCostPerBatch ?? 0;
                    decimal margin = recipe.TargetMarginPercent ?? 30;
                    decimal revenue = cost * (1 + margin / 100);
                    totalRevenue += revenue;
                }

                CacheValue(cacheKey, totalRevenue);
                _logger.Information("Calculated potential revenue: {Revenue:C}", totalRevenue);
                return totalRevenue;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating potential revenue");
                return 0;
            }
        }

        /// <summary>
        /// Menganalisis trend HPP untuk periode waktu tertentu
        /// Mengembalikan list perubahan HPP dengan percentage change
        /// </summary>
        public async Task<List<HPPTrendItem>> AnalyzePriceTrendAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.Information("Analyzing price trend from {StartDate} to {EndDate}", startDate, endDate);
                
                var recipes = await _recipeRepository.GetAllAsync();
                var trendItems = new List<HPPTrendItem>();

                if (recipes == null || !recipes.Any())
                    return trendItems;

                // Group by month for trend analysis
                var grouped = recipes
                    .Where(r => r.CreatedAt >= startDate && r.CreatedAt <= endDate)
                    .GroupBy(r => new { r.CreatedAt?.Year, r.CreatedAt?.Month })
                    .OrderBy(g => g.Key.Year)
                    .ThenBy(g => g.Key.Month)
                    .ToList();

                decimal previousHPP = 0;
                foreach (var group in grouped)
                {
                    var avgHPP = group.Average(r => r.EstimatedCostPerBatch ?? 0);
                    var percentChange = previousHPP > 0 
                        ? ((avgHPP - previousHPP) / previousHPP) * 100 
                        : 0;

                    trendItems.Add(new HPPTrendItem
                    {
                        Period = $"{group.Key.Month:D2}/{group.Key.Year}",
                        AverageHPP = avgHPP,
                        PercentageChange = percentChange,
                        Direction = percentChange > 0 ? "↑" : percentChange < 0 ? "↓" : "→"
                    });

                    previousHPP = avgHPP;
                }

                _logger.Information("Price trend analysis complete with {Count} periods", trendItems.Count);
                return trendItems;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing price trend");
                return new List<HPPTrendItem>();
            }
        }

        /// <summary>
        /// Menghitung profitabilitas keseluruhan dari semua resep
        /// Membandingkan HPP dengan harga jual yang direkomendasikan
        /// </summary>
        public async Task<ProfitabilityMetrics> CalculateProfitabilityAsync()
        {
            try
            {
                var recipes = await _recipeRepository.GetAllAsync();
                if (recipes == null || !recipes.Any())
                {
                    return new ProfitabilityMetrics { TotalRecipes = 0 };
                }

                decimal totalCost = 0;
                decimal totalRevenue = 0;
                int profitableRecipes = 0;
                int unprofitableRecipes = 0;
                decimal maxProfit = 0;
                decimal minProfit = decimal.MaxValue;

                foreach (var recipe in recipes)
                {
                    decimal cost = recipe.EstimatedCostPerBatch ?? 0;
                    decimal margin = recipe.TargetMarginPercent ?? 30;
                    decimal revenue = cost * (1 + margin / 100);
                    decimal profit = revenue - cost;

                    totalCost += cost;
                    totalRevenue += revenue;

                    if (profit > 0)
                        profitableRecipes++;
                    else
                        unprofitableRecipes++;

                    maxProfit = Math.Max(maxProfit, profit);
                    minProfit = Math.Min(minProfit, profit);
                }

                var metrics = new ProfitabilityMetrics
                {
                    TotalRecipes = recipes.Count,
                    TotalCost = totalCost,
                    TotalRevenue = totalRevenue,
                    TotalProfit = totalRevenue - totalCost,
                    ProfitMargin = totalRevenue > 0 ? ((totalRevenue - totalCost) / totalRevenue) * 100 : 0,
                    ProfitableRecipes = profitableRecipes,
                    UnprofitableRecipes = unprofitableRecipes,
                    MaxProfit = maxProfit,
                    MinProfit = minProfit == decimal.MaxValue ? 0 : minProfit
                };

                _logger.Information("Profitability metrics calculated: Profit={Profit:C}, Margin={Margin:P}", 
                    metrics.TotalProfit, metrics.ProfitMargin);
                
                return metrics;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating profitability");
                return new ProfitabilityMetrics { TotalRecipes = 0 };
            }
        }

        /// <summary>
        /// Mendapatkan resep dengan HPP tertinggi dan terendah
        /// Berguna untuk identifikasi outlier dan analisis
        /// </summary>
        public async Task<(Recipe MostExpensive, Recipe MostAffordable)> GetHPPExtremesAsync()
        {
            try
            {
                var recipes = await _recipeRepository.GetAllAsync();
                if (recipes == null || !recipes.Any())
                    return (null, null);

                var mostExpensive = recipes.OrderByDescending(r => r.EstimatedCostPerBatch ?? 0).FirstOrDefault();
                var mostAffordable = recipes.OrderBy(r => r.EstimatedCostPerBatch ?? 0).FirstOrDefault();

                return (mostExpensive, mostAffordable);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting HPP extremes");
                return (null, null);
            }
        }

        private bool TryGetCachedValue(string key, out decimal value)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if ((DateTime.Now - cached.CachedAt).TotalMinutes < CACHE_DURATION_MINUTES)
                {
                    value = cached.Value;
                    return true;
                }
                else
                {
                    _cache.Remove(key);
                }
            }

            value = 0;
            return false;
        }

        private void CacheValue(string key, decimal value)
        {
            _cache[key] = (value, DateTime.Now);
        }

        public void ClearCache()
        {
            _cache.Clear();
            _logger.Information("Dashboard analytics cache cleared");
        }
    }

    /// <summary>
    /// Service untuk notifikasi dan alert dashboard
    /// Menyediakan monitoring untuk kondisi kritis atau anomali
    /// </summary>
    public class DashboardAlertService
    {
        private readonly RecipeRepository _recipeRepository;
        private readonly ILogger _logger;
        private List<DashboardAlert> _activeAlerts;

        public event EventHandler<DashboardAlertEventArgs> AlertOccurred;

        public DashboardAlertService(RecipeRepository recipeRepository, ILogger logger)
        {
            _recipeRepository = recipeRepository;
            _logger = logger;
            _activeAlerts = new List<DashboardAlert>();
        }

        /// <summary>
        /// Melakukan monitoring kondisi dan menghasilkan alert jika diperlukan
        /// </summary>
        public async Task<List<DashboardAlert>> MonitorAsync()
        {
            try
            {
                _activeAlerts.Clear();
                var recipes = await _recipeRepository.GetAllAsync();

                if (recipes == null)
                    return _activeAlerts;

                // Check for recipes with unusually high HPP
                var avgHPP = recipes.Average(r => r.EstimatedCostPerBatch ?? 0);
                var expensiveRecipes = recipes
                    .Where(r => (r.EstimatedCostPerBatch ?? 0) > avgHPP * 1.5m)
                    .ToList();

                if (expensiveRecipes.Any())
                {
                    _activeAlerts.Add(new DashboardAlert
                    {
                        AlertType = AlertType.Warning,
                        Severity = AlertSeverity.Medium,
                        Title = "HPP Tinggi Terdeteksi",
                        Message = $"{expensiveRecipes.Count} resep memiliki HPP lebih tinggi dari rata-rata",
                        AffectedRecipes = expensiveRecipes.Select(r => r.RecipeName).ToList()
                    });

                    AlertOccurred?.Invoke(this, new DashboardAlertEventArgs 
                    { 
                        Alert = _activeAlerts.Last() 
                    });
                }

                // Check for recipes without margin specified
                var noMarginRecipes = recipes
                    .Where(r => !r.TargetMarginPercent.HasValue || r.TargetMarginPercent == 0)
                    .ToList();

                if (noMarginRecipes.Any())
                {
                    _activeAlerts.Add(new DashboardAlert
                    {
                        AlertType = AlertType.Info,
                        Severity = AlertSeverity.Low,
                        Title = "Margin Tidak Dikonfigurasi",
                        Message = $"{noMarginRecipes.Count} resep belum memiliki target margin",
                        AffectedRecipes = noMarginRecipes.Select(r => r.RecipeName).ToList()
                    });
                }

                // Check for recipes with zero batch size
                var invalidRecipes = recipes
                    .Where(r => !r.DonutsPerBatch.HasValue || r.DonutsPerBatch <= 0)
                    .ToList();

                if (invalidRecipes.Any())
                {
                    _activeAlerts.Add(new DashboardAlert
                    {
                        AlertType = AlertType.Error,
                        Severity = AlertSeverity.High,
                        Title = "Data Resep Tidak Valid",
                        Message = $"{invalidRecipes.Count} resep memiliki data tidak valid",
                        AffectedRecipes = invalidRecipes.Select(r => r.RecipeName).ToList()
                    });
                }

                _logger.Information("Monitoring complete: {AlertCount} alerts generated", _activeAlerts.Count);
                return _activeAlerts;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during monitoring");
                return new List<DashboardAlert>();
            }
        }

        /// <summary>
        /// Mendapatkan alert yang sedang aktif
        /// </summary>
        public List<DashboardAlert> GetActiveAlerts()
        {
            return _activeAlerts.OrderByDescending(a => 
                a.Severity == AlertSeverity.High ? 3 : a.Severity == AlertSeverity.Medium ? 2 : 1
            ).ToList();
        }

        /// <summary>
        /// Menghapus alert tertentu
        /// </summary>
        public void DismissAlert(DashboardAlert alert)
        {
            _activeAlerts.Remove(alert);
            _logger.Information("Alert dismissed: {AlertTitle}", alert.Title);
        }

        /// <summary>
        /// Menghapus semua alert
        /// </summary>
        public void DismissAllAlerts()
        {
            _activeAlerts.Clear();
            _logger.Information("All alerts dismissed");
        }
    }

    /// <summary>
    /// Enum untuk tipe alert
    /// </summary>
    public enum AlertType
    {
        Error,
        Warning,
        Info,
        Success
    }

    /// <summary>
    /// Enum untuk tingkat severity alert
    /// </summary>
    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Model untuk dashboard alert
    /// </summary>
    public class DashboardAlert
    {
        public AlertType AlertType { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public List<string> AffectedRecipes { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string SeverityColor => Severity switch
        {
            AlertSeverity.Critical => "#FF0000",
            AlertSeverity.High => "#FF6600",
            AlertSeverity.Medium => "#FFCC00",
            AlertSeverity.Low => "#00CC00",
            _ => "#000000"
        };
    }

    /// <summary>
    /// Event args untuk dashboard alert
    /// </summary>
    public class DashboardAlertEventArgs : EventArgs
    {
        public DashboardAlert Alert { get; set; }
    }

    /// <summary>
    /// Model untuk item trend HPP
    /// </summary>
    public class HPPTrendItem
    {
        public string Period { get; set; }
        public decimal AverageHPP { get; set; }
        public decimal PercentageChange { get; set; }
        public string Direction { get; set; }
    }

    /// <summary>
    /// Model untuk metrik profitabilitas
    /// </summary>
    public class ProfitabilityMetrics
    {
        public int TotalRecipes { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalProfit { get; set; }
        public decimal ProfitMargin { get; set; }
        public int ProfitableRecipes { get; set; }
        public int UnprofitableRecipes { get; set; }
        public decimal MaxProfit { get; set; }
        public decimal MinProfit { get; set; }
    }
}
