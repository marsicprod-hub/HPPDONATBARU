using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HppDonatApp.Data;
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
    /// Halaman laporan dan analisis HPP donat
    /// Fitur: Filter tanggal, berbagai jenis laporan, ekspor data, visualisasi trend
    /// Menampilkan: laporan detail batch, ringkasan per resep, analisis trend harga
    /// </summary>
    public sealed partial class ReportsPage : Page
    {
        private ReportsViewModel _viewModel;

        public ReportsViewModel ViewModel => _viewModel;

        public ReportsPage()
        {
            this.InitializeComponent();
            InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            var serviceProvider = ((App)Application.Current).ServiceProvider;
            _viewModel = new ReportsViewModel(
                serviceProvider.GetRequiredService<RecipeRepository>(),
                serviceProvider.GetRequiredService<ILogger>()
            );
            this.DataContext = _viewModel;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await _viewModel.InitializeAsync();
        }
    }

    /// <summary>
    /// ViewModel untuk halaman laporan dengan analisis detail HPP
    /// Menangani filtering, perhitungan statistik, dan export data
    /// </summary>
    public class ReportsViewModel : ObservableObject
    {
        private readonly RecipeRepository _recipeRepository;
        private readonly ILogger _logger;

        private DateTime _startDate;
        private DateTime _endDate;
        private string _selectedReportType;
        private ObservableCollection<string> _reportTypes;
        private ObservableCollection<ReportItemModel> _detailedReportItems;
        private ObservableCollection<SummaryReportModel> _summaryReportItems;
        private ObservableCollection<string> _exportFormats;

        private int _totalBatches;
        private decimal _totalHPP;
        private decimal _averageHPP;
        private string _hppRange;

        public DateTime StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value);
        }

        public DateTime EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value);
        }

        public string SelectedReportType
        {
            get => _selectedReportType;
            set => SetProperty(ref _selectedReportType, value);
        }

        public ObservableCollection<string> ReportTypes
        {
            get => _reportTypes;
            set => SetProperty(ref _reportTypes, value);
        }

        public ObservableCollection<ReportItemModel> DetailedReportItems
        {
            get => _detailedReportItems;
            set => SetProperty(ref _detailedReportItems, value);
        }

        public ObservableCollection<SummaryReportModel> SummaryReportItems
        {
            get => _summaryReportItems;
            set => SetProperty(ref _summaryReportItems, value);
        }

        public ObservableCollection<string> ExportFormats
        {
            get => _exportFormats;
            set => SetProperty(ref _exportFormats, value);
        }

        public int TotalBatches
        {
            get => _totalBatches;
            set => SetProperty(ref _totalBatches, value);
        }

        public decimal TotalHPP
        {
            get => _totalHPP;
            set => SetProperty(ref _totalHPP, value);
        }

        public decimal AverageHPP
        {
            get => _averageHPP;
            set => SetProperty(ref _averageHPP, value);
        }

        public string HPPRange
        {
            get => _hppRange;
            set => SetProperty(ref _hppRange, value);
        }

        [RelayCommand]
        public async Task GenerateReport()
        {
            try
            {
                _logger.Information("Generating report from {StartDate} to {EndDate}", StartDate, EndDate);
                
                var recipes = await _recipeRepository.GetAllAsync();
                if (recipes == null || !recipes.Any())
                {
                    _logger.Warning("No recipes found for report generation");
                    return;
                }

                var reportItems = new List<ReportItemModel>();
                var summaryItems = new List<SummaryReportModel>();

                int batchCount = 0;
                decimal totalCost = 0;
                decimal minCost = decimal.MaxValue;
                decimal maxCost = decimal.MinValue;

                // Generate detailed report
                foreach (var recipe in recipes)
                {
                    try
                    {
                        var ingredients = await _recipeRepository.GetRecipeIngredientsAsync(recipe.Id);
                        if (ingredients == null || !ingredients.Any())
                            continue;

                        decimal recipeCost = ingredients.Sum(i => (decimal)(i.Quantity ?? 0) * (i.UnitPrice ?? 0));
                        totalCost += recipeCost;
                        batchCount++;

                        minCost = Math.Min(minCost, recipeCost);
                        maxCost = Math.Max(maxCost, recipeCost);

                        reportItems.Add(new ReportItemModel
                        {
                            DateCreated = recipe.CreatedAt?.ToString("dd/MM/yyyy") ?? DateTime.Now.ToString("dd/MM/yyyy"),
                            RecipeName = recipe.RecipeName,
                            BatchCount = 1,
                            TotalHPP = $"Rp {recipeCost:N0}",
                            HPPPerUnit = $"Rp {(recipeCost / (recipe.DonutsPerBatch ?? 1)):N0}"
                        });

                        // Add to summary by recipe
                        var existing = summaryItems.FirstOrDefault(s => s.RecipeName == recipe.RecipeName);
                        if (existing != null)
                        {
                            existing.BatchCount += 1;
                        }
                        else
                        {
                            summaryItems.Add(new SummaryReportModel
                            {
                                RecipeName = recipe.RecipeName,
                                BatchCount = 1,
                                TotalHPP = $"Rp {recipeCost:N0}",
                                AverageHPP = $"Rp {recipeCost:N0}",
                                Trend = "→"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error processing recipe for report: {RecipeId}", recipe.Id);
                    }
                }

                // Update summary statistics
                TotalBatches = batchCount;
                TotalHPP = totalCost;
                AverageHPP = batchCount > 0 ? totalCost / batchCount : 0;
                HPPRange = $"Rp {minCost:N0} - Rp {maxCost:N0}";

                DetailedReportItems = new ObservableCollection<ReportItemModel>(reportItems);
                SummaryReportItems = new ObservableCollection<SummaryReportModel>(summaryItems);

                _logger.Information("Report generated successfully. Total batches: {BatchCount}, Total HPP: {TotalHPP}",
                    batchCount, totalCost);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating report");
            }
        }

        [RelayCommand]
        public async Task Export()
        {
            try
            {
                _logger.Information("Exporting report in {Format} format", SelectedReportType);
                // Implementation for export functionality
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting report");
            }
        }

        [RelayCommand]
        public async Task Print()
        {
            try
            {
                _logger.Information("Printing report");
                // Implementation for print functionality
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error printing report");
            }
        }

        public async Task InitializeAsync()
        {
            try
            {
                _logger.Information("Initializing reports page");

                // Set default date range (last 30 days)
                EndDate = DateTime.Now;
                StartDate = DateTime.Now.AddDays(-30);

                // Initialize report types
                ReportTypes = new ObservableCollection<string> 
                { 
                    "Laporan Detail", 
                    "Laporan Ringkasan", 
                    "Analisis Trend",
                    "Komparasi Resep"
                };
                SelectedReportType = "Laporan Detail";

                // Initialize export formats
                ExportFormats = new ObservableCollection<string>
                {
                    "Excel (.xlsx)",
                    "PDF (.pdf)",
                    "CSV (.csv)",
                    "JSON (.json)"
                };

                DetailedReportItems = new ObservableCollection<ReportItemModel>();
                SummaryReportItems = new ObservableCollection<SummaryReportModel>();

                _logger.Information("Reports page initialized");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing reports page");
            }
        }

        public ReportsViewModel(RecipeRepository recipeRepository, ILogger logger)
        {
            _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
    }

    /// <summary>
    /// Model untuk item laporan detail
    /// Berisi informasi batch individual dengan detail HPP
    /// </summary>
    public class ReportItemModel
    {
        public string DateCreated { get; set; }
        public string RecipeName { get; set; }
        public int BatchCount { get; set; }
        public string TotalHPP { get; set; }
        public string HPPPerUnit { get; set; }
    }

    /// <summary>
    /// Service untuk generasi dan analisis laporan HPP
    /// Menyediakan berbagai metode untuk analisis data dan export
    /// </summary>
    public class ReportGenerationService
    {
        private readonly RecipeRepository _recipeRepository;
        private readonly ILogger _logger;

        public ReportGenerationService(RecipeRepository recipeRepository, ILogger logger)
        {
            _recipeRepository = recipeRepository;
            _logger = logger;
        }

        /// <summary>
        /// Menghasilkan laporan analisis kelengkapan data per resep
        /// </summary>
        public async Task<DataCompletenessReport> GenerateDataCompletenessReportAsync()
        {
            try
            {
                var report = new DataCompletenessReport();
                var recipes = await _recipeRepository.GetAllAsync();

                if (recipes == null)
                    return report;

                report.TotalRecipes = recipes.Count;
                int completeRecipes = 0;
                int recipesWithMissingCost = 0;
                int recipesWithMissingMargin = 0;

                foreach (var recipe in recipes)
                {
                    bool isComplete = !string.IsNullOrEmpty(recipe.RecipeName)
                        && recipe.DonutsPerBatch > 0
                        && recipe.EstimatedCostPerBatch > 0
                        && recipe.TargetMarginPercent > 0;

                    if (isComplete)
                        completeRecipes++;
                    
                    if (!recipe.EstimatedCostPerBatch.HasValue || recipe.EstimatedCostPerBatch == 0)
                        recipesWithMissingCost++;
                    
                    if (!recipe.TargetMarginPercent.HasValue || recipe.TargetMarginPercent == 0)
                        recipesWithMissingMargin++;
                }

                report.CompleteRecipes = completeRecipes;
                report.CompletenessPercentage = recipes.Count > 0 
                    ? (completeRecipes / (decimal)recipes.Count) * 100 
                    : 0;
                report.RecipesWithMissingCost = recipesWithMissingCost;
                report.RecipesWithMissingMargin = recipesWithMissingMargin;

                _logger.Information("Data completeness report: {Complete}/{Total} recipes complete ({Percentage:P})", 
                    completeRecipes, recipes.Count, report.CompletenessPercentage);

                return report;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating data completeness report");
                return new DataCompletenessReport();
            }
        }

        /// <summary>
        /// Menganalisis variasi harga antar kategori resep
        /// </summary>
        public async Task<List<CategoryPriceAnalysis>> AnalyzePriceByCategoryAsync()
        {
            try
            {
                var analysis = new List<CategoryPriceAnalysis>();
                var recipes = await _recipeRepository.GetAllAsync();

                if (recipes == null)
                    return analysis;

                var grouped = recipes
                    .GroupBy(r => r.Category ?? "Uncategorized")
                    .ToList();

                foreach (var group in grouped)
                {
                    var prices = group
                        .Where(r => r.EstimatedCostPerBatch.HasValue && r.EstimatedCostPerBatch > 0)
                        .Select(r => r.EstimatedCostPerBatch.Value)
                        .ToList();

                    if (prices.Any())
                    {
                        analysis.Add(new CategoryPriceAnalysis
                        {
                            Category = group.Key,
                            RecipeCount = group.Count(),
                            AveragePrice = prices.Average(),
                            MinPrice = prices.Min(),
                            MaxPrice = prices.Max(),
                            StandardDeviation = CalculateStandardDeviation(prices)
                        });
                    }
                }

                _logger.Information("Category price analysis complete: {Count} categories", analysis.Count);
                return analysis.OrderByDescending(a => a.AveragePrice).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing prices by category");
                return new List<CategoryPriceAnalysis>();
            }
        }

        /// <summary>
        /// Menghasilkan laporan tren harga dari waktu ke waktu
        /// </summary>
        public async Task<List<PriceTrendAnalysis>> AnalyzePriceTrendsAsync(int monthsBack = 12)
        {
            try
            {
                var analysis = new List<PriceTrendAnalysis>();
                var recipes = await _recipeRepository.GetAllAsync();

                if (recipes == null)
                    return analysis;

                var cutoffDate = DateTime.Now.AddMonths(-monthsBack);
                var filtered = recipes.Where(r => r.CreatedAt >= cutoffDate).ToList();

                var grouped = filtered
                    .GroupBy(r => new { r.CreatedAt?.Year, r.CreatedAt?.Month })
                    .OrderBy(g => g.Key.Year)
                    .ThenBy(g => g.Key.Month)
                    .ToList();

                foreach (var group in grouped)
                {
                    var prices = group
                        .Where(r => r.EstimatedCostPerBatch.HasValue && r.EstimatedCostPerBatch > 0)
                        .Select(r => r.EstimatedCostPerBatch.Value)
                        .ToList();

                    if (prices.Any())
                    {
                        analysis.Add(new PriceTrendAnalysis
                        {
                            Period = $"{group.Key.Month:D2}/{group.Key.Year}",
                            AveragePrice = prices.Average(),
                            MedianPrice = GetMedian(prices),
                            RecipeCount = group.Count(),
                            TrendIndicator = "->"
                        });
                    }
                }

                // Calculate trend direction
                if (analysis.Count >= 2)
                {
                    for (int i = 1; i < analysis.Count; i++)
                    {
                        if (analysis[i].AveragePrice > analysis[i - 1].AveragePrice)
                            analysis[i].TrendIndicator = "↑";
                        else if (analysis[i].AveragePrice < analysis[i - 1].AveragePrice)
                            analysis[i].TrendIndicator = "↓";
                    }
                }

                _logger.Information("Price trend analysis complete: {Count} periods analyzed", analysis.Count);
                return analysis;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing price trends");
                return new List<PriceTrendAnalysis>();
            }
        }

        /// <summary>
        /// Mengidentifikasi recipe outlier (HPP terlalu tinggi atau rendah)
        /// </summary>
        public async Task<List<OutlierAnalysis>> IdentifyOutliersAsync()
        {
            try
            {
                var outliers = new List<OutlierAnalysis>();
                var recipes = await _recipeRepository.GetAllAsync();

                if (recipes == null || recipes.Count < 3)
                    return outliers;

                var prices = recipes
                    .Where(r => r.EstimatedCostPerBatch.HasValue && r.EstimatedCostPerBatch > 0)
                    .Select(r => r.EstimatedCostPerBatch.Value)
                    .ToList();

                if (!prices.Any())
                    return outliers;

                var mean = prices.Average();
                var stdDev = CalculateStandardDeviation(prices);

                foreach (var recipe in recipes)
                {
                    var cost = recipe.EstimatedCostPerBatch ?? 0;
                    if (cost <= 0)
                        continue;

                    var zScore = (cost - mean) / stdDev;
                    
                    // Identify outliers (|z-score| > 2)
                    if (Math.Abs(zScore) > 2)
                    {
                        outliers.Add(new OutlierAnalysis
                        {
                            RecipeName = recipe.RecipeName,
                            Cost = cost,
                            ZScore = zScore,
                            OutlierType = zScore > 0 ? "Sangat Tinggi" : "Sangat Rendah",
                            DeviasiPersentase = ((cost - mean) / mean) * 100
                        });
                    }
                }

                _logger.Information("Outlier detection complete: {Count} outliers found", outliers.Count);
                return outliers.OrderByDescending(o => Math.Abs(o.ZScore)).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error identifying outliers");
                return new List<OutlierAnalysis>();
            }
        }

        /// <summary>
        /// Export laporan ke format CSV
        /// </summary>
        public string ExportToCSV<T>(List<T> data, string title = "Report")
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# {title}");
                sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                if (!data.Any())
                    return sb.ToString();

                // Get properties
                var props = typeof(T).GetProperties();
                sb.AppendLine(string.Join(",", props.Select(p => p.Name)));

                // Get values
                foreach (var item in data)
                {
                    sb.AppendLine(string.Join(",", props.Select(p => 
                    {
                        var value = p.GetValue(item)?.ToString() ?? "";
                        // Escape quotes in CSV
                        return value.Contains(",") ? $"\"{value}\"" : value;
                    })));
                }

                _logger.Information("CSV export successful: {Count} items", data.Count);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting to CSV");
                return "";
            }
        }

        private decimal CalculateStandardDeviation(List<decimal> values)
        {
            if (values.Count < 2)
                return 0;

            var mean = values.Average();
            var sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
            return (decimal)Math.Sqrt((double)(sumOfSquares / values.Count));
        }

        private decimal GetMedian(List<decimal> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count % 2 == 0)
                return (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            return sorted[sorted.Count / 2];
        }
    }

    /// <summary>
    /// Model untuk laporan kelengkapan data
    /// </summary>
    public class DataCompletenessReport
    {
        public int TotalRecipes { get; set; }
        public int CompleteRecipes { get; set; }
        public decimal CompletenessPercentage { get; set; }
        public int RecipesWithMissingCost { get; set; }
        public int RecipesWithMissingMargin { get; set; }
    }

    /// <summary>
    /// Model untuk analisis harga per kategori
    /// </summary>
    public class CategoryPriceAnalysis
    {
        public string Category { get; set; }
        public int RecipeCount { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal StandardDeviation { get; set; }
    }

    /// <summary>
    /// Model untuk analisis tren harga
    /// </summary>
    public class PriceTrendAnalysis
    {
        public string Period { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MedianPrice { get; set; }
        public int RecipeCount { get; set; }
        public string TrendIndicator { get; set; }
    }

    /// <summary>
    /// Model untuk analisis outlier
    /// </summary>
    public class OutlierAnalysis
    {
        public string RecipeName { get; set; }
        public decimal Cost { get; set; }
        public decimal ZScore { get; set; }
        public string OutlierType { get; set; }
        public decimal DeviasiPersentase { get; set; }
    }
}
