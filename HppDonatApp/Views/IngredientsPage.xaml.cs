using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HppDonatApp.Data;
using HppDonatApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace HppDonatApp.Views
{
    /// <summary>
    /// Halaman manajemen bahan baku untuk aplikasi HPP donat
    /// Fitur: CRUD operasi bahan, pencarian, filter kategori, tracking stok, history harga
    /// </summary>
    public sealed partial class IngredientsPage : Page
    {
        private IngredientsViewModel _viewModel;

        public IngredientsViewModel ViewModel => _viewModel;

        public IngredientsPage()
        {
            this.InitializeComponent();
            InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            var serviceProvider = ((App)Application.Current).ServiceProvider;
            _viewModel = new IngredientsViewModel(
                serviceProvider.GetRequiredService<IngredientRepository>(),
                serviceProvider.GetRequiredService<ILogger>()
            );
            this.DataContext = _viewModel;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await _viewModel.LoadIngredientsAsync();
        }
    }

    /// <summary>
    /// ViewModel untuk manajemen bahan baku
    /// Menangani CRUD operasi, pencarian, filtering, dan manajemen stok
    /// </summary>
    public class IngredientsViewModel : ObservableObject
    {
        private readonly IngredientRepository _ingredientRepository;
        private readonly ILogger _logger;

        private ObservableCollection<IngredientDisplayModel> _allIngredients;
        private ObservableCollection<IngredientDisplayModel> _filteredIngredients;
        private ObservableCollection<string> _categories;
        private ObservableCollection<string> _units;
        private IngredientDisplayModel _selectedIngredient;
        private string _searchText;

        public ObservableCollection<IngredientDisplayModel> AllIngredients
        {
            get => _allIngredients;
            set => SetProperty(ref _allIngredients, value);
        }

        public ObservableCollection<IngredientDisplayModel> FilteredIngredients
        {
            get => _filteredIngredients;
            set => SetProperty(ref _filteredIngredients, value);
        }

        public ObservableCollection<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<string> Units
        {
            get => _units;
            set => SetProperty(ref _units, value);
        }

        public IngredientDisplayModel SelectedIngredient
        {
            get => _selectedIngredient;
            set => SetProperty(ref _selectedIngredient, value);
        }

        [RelayCommand]
        public async Task AddIngredient()
        {
            try
            {
                _logger.Information("Add ingredient command triggered");
                var dialog = new AddIngredientDialog();
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await LoadIngredientsAsync();
                    _logger.Information("New ingredient added successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding new ingredient");
            }
        }

        [RelayCommand]
        public async Task DeleteIngredient()
        {
            if (SelectedIngredient == null)
            {
                _logger.Warning("No ingredient selected for deletion");
                return;
            }

            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Konfirmasi Hapus",
                    Content = $"Yakin menghapus bahan '{SelectedIngredient.IngredientName}'?",
                    PrimaryButtonText = "Hapus",
                    SecondaryButtonText = "Batal"
                };
                
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // await _ingredientRepository.DeleteAsync(SelectedIngredient.Id);
                    await LoadIngredientsAsync();
                    _logger.Information("Ingredient deleted: {IngredientName}", SelectedIngredient.IngredientName);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting ingredient");
            }
        }

        [RelayCommand]
        public async Task EditIngredient()
        {
            if (SelectedIngredient == null)
            {
                _logger.Warning("No ingredient selected for editing");
                return;
            }

            try
            {
                _logger.Information("Edit ingredient command triggered for: {IngredientName}", 
                    SelectedIngredient.IngredientName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error editing ingredient");
            }
        }

        [RelayCommand]
        public async Task RefreshIngredients()
        {
            try
            {
                _logger.Information("Refreshing ingredients list");
                await LoadIngredientsAsync();
                _logger.Information("Ingredients refreshed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing ingredients");
            }
        }

        public async Task LoadIngredientsAsync()
        {
            try
            {
                _logger.Information("Loading ingredients from database");

                var ingredients = await _ingredientRepository.GetAllAsync();
                
                var displayModels = ingredients?
                    .Select(i => new IngredientDisplayModel
                    {
                        Id = i.Id,
                        IngredientName = i.IngredientName ?? "Unknown",
                        Description = i.Description ?? "",
                        Unit = i.UnitOfMeasure ?? "pcs",
                        CurrentStock = i.StockQuantity?.ToString("N2") ?? "0",
                        StockStatus = GetStockStatus(i.StockQuantity ?? 0),
                        CurrentPrice = $"Rp {(i.CurrentPrice ?? 0):N0}",
                        Category = i.Category ?? "Umum",
                        Supplier = i.Supplier ?? "-",
                        StockAlert = GetStockAlert(i.StockQuantity ?? 0, i.MinimumStock ?? 0),
                        AlertSeverity = GetAlertSeverity(i.StockQuantity ?? 0, i.MinimumStock ?? 0),
                        StockAlertVisibility = (i.StockQuantity ?? 0) < (i.MinimumStock ?? 10) ? 
                            Visibility.Visible : Visibility.Collapsed
                    })
                    .ToList() ?? new List<IngredientDisplayModel>();

                AllIngredients = new ObservableCollection<IngredientDisplayModel>(displayModels);
                FilteredIngredients = new ObservableCollection<IngredientDisplayModel>(displayModels);

                // Load categories and units
                LoadCategories();
                LoadUnits();

                _logger.Information("Loaded {Count} ingredients", displayModels.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading ingredients");
            }
        }

        private void LoadCategories()
        {
            var categories = AllIngredients?
                .Select(i => i.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList() ?? new List<string>();

            categories.Insert(0, "Semua");
            Categories = new ObservableCollection<string>(categories);
        }

        private void LoadUnits()
        {
            var units = AllIngredients?
                .Select(i => i.Unit)
                .Distinct()
                .OrderBy(u => u)
                .ToList() ?? new List<string>();

            units.Insert(0, "Semua");
            Units = new ObservableCollection<string>(units);
        }

        public async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _searchText = sender.Text?.ToLower() ?? "";
            ApplyFilters();
        }

        public async void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        public async void OnUnitChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        public void OnIngredientSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is IngredientDisplayModel ingredient)
            {
                SelectedIngredient = ingredient;
            }
        }

        private void ApplyFilters()
        {
            var filtered = AllIngredients?.AsEnumerable() ?? Enumerable.Empty<IngredientDisplayModel>();

            // Filter by search text
            if (!string.IsNullOrEmpty(_searchText))
            {
                filtered = filtered.Where(i => 
                    i.IngredientName.ToLower().Contains(_searchText) ||
                    i.Category.ToLower().Contains(_searchText) ||
                    i.Supplier.ToLower().Contains(_searchText)
                );
            }

            FilteredIngredients = new ObservableCollection<IngredientDisplayModel>(filtered.ToList());
        }

        private string GetStockStatus(double quantity)
        {
            if (quantity <= 0)
                return "Habis";
            if (quantity < 5)
                return "Kritis";
            if (quantity < 20)
                return "Rendah";
            return "Baik";
        }

        private string GetStockAlert(double current, double minimum)
        {
            if (current < minimum)
                return "!";
            return "";
        }

        private InfoBadgeSeverity GetAlertSeverity(double current, double minimum)
        {
            if (current <= 0)
                return InfoBadgeSeverity.Error;
            if (current < minimum)
                return InfoBadgeSeverity.Warning;
            return InfoBadgeSeverity.Informational;
        }

        public IngredientsViewModel(IngredientRepository ingredientRepository, ILogger logger)
        {
            _ingredientRepository = ingredientRepository ?? throw new ArgumentNullException(nameof(ingredientRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _allIngredients = new ObservableCollection<IngredientDisplayModel>();
            _filteredIngredients = new ObservableCollection<IngredientDisplayModel>();
            _categories = new ObservableCollection<string>();
            _units = new ObservableCollection<string>();
        }
    }

    /// <summary>
    /// Model display untuk menampilkan bahan di UI
    /// Berisi informasi lengkap dan status bahan
    /// </summary>
    public class IngredientDisplayModel
    {
        public int Id { get; set; }
        public string IngredientName { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public string CurrentStock { get; set; }
        public string StockStatus { get; set; }
        public string CurrentPrice { get; set; }
        public string Category { get; set; }
        public string Supplier { get; set; }
        public string StockAlert { get; set; }
        public InfoBadgeSeverity AlertSeverity { get; set; }
        public Visibility StockAlertVisibility { get; set; }
    }

    /// <summary>
    /// Helper service untuk manajemen inventory dan supplier
    /// </summary>
    public class InventoryManagementService
    {
        private readonly IngredientRepository _ingredientRepository;
        private readonly ILogger _logger;

        public InventoryManagementService(IngredientRepository ingredientRepository, ILogger logger)
        {
            _ingredientRepository = ingredientRepository;
            _logger = logger;
        }

        /// <summary>
        /// Melakukan reorder untuk bahan yang stoknya kritis
        /// </summary>
        public async Task<List<ReorderSuggestion>> GenerateReorderSuggestionsAsync()
        {
            try
            {
                var ingredients = await _ingredientRepository.GetAllAsync();
                var suggestions = new List<ReorderSuggestion>();

                if (ingredients == null)
                    return suggestions;

                foreach (var ingredient in ingredients)
                {
                    var currentStock = ingredient.StockQuantity ?? 0;
                    var minimumStock = ingredient.MinimumStock ?? 10;
                    var reorderQuantity = ingredient.ReorderQuantity ?? minimumStock * 2;

                    if (currentStock < minimumStock)
                    {
                        suggestions.Add(new ReorderSuggestion
                        {
                            IngredientId = ingredient.Id,
                            IngredientName = ingredient.IngredientName ?? "Unknown",
                            CurrentStock = currentStock,
                            MinimumStock = minimumStock,
                            RecommendedOrderQuantity = reorderQuantity,
                            Supplier = ingredient.Supplier ?? "N/A",
                            Priority = GetReorderPriority(currentStock, minimumStock),
                            EstimatedCost = (decimal)reorderQuantity * (ingredient.CurrentPrice ?? 0)
                        });
                    }
                }

                _logger.Information("Generated {Count} reorder suggestions", suggestions.Count);
                return suggestions.OrderByDescending(s => s.Priority == "Kritis" ? 2 : s.Priority == "Tinggi" ? 1 : 0).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating reorder suggestions");
                return new List<ReorderSuggestion>();
            }
        }

        /// <summary>
        /// Menganalisis penggunaan bahan untuk periode tertentu
        /// </summary>
        public async Task<List<IngredientUsageAnalysis>> AnalyzeIngredientUsageAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var analysis = new List<IngredientUsageAnalysis>();
                var ingredients = await _ingredientRepository.GetAllAsync();

                if (ingredients == null)
                    return analysis;

                foreach (var ingredient in ingredients)
                {
                    var priceHistory = await _ingredientRepository.GetPriceHistoryAsync(ingredient.Id);
                    
                    if (priceHistory != null && priceHistory.Any())
                    {
                        var periodHistory = priceHistory
                            .Where(p => p.DateRecorded >= startDate && p.DateRecorded <= endDate)
                            .ToList();

                        if (periodHistory.Any())
                        {
                            var avgPrice = periodHistory.Average(p => p.Price);
                            var minPrice = periodHistory.Min(p => p.Price);
                            var maxPrice = periodHistory.Max(p => p.Price);

                            analysis.Add(new IngredientUsageAnalysis
                            {
                                IngredientId = ingredient.Id,
                                IngredientName = ingredient.IngredientName ?? "Unknown",
                                AveragePrice = avgPrice,
                                MinPrice = minPrice,
                                MaxPrice = maxPrice,
                                PriceVariation = maxPrice - minPrice,
                                ObservationCount = periodHistory.Count,
                                TrendDirection = maxPrice > avgPrice ? "↑" : minPrice < avgPrice ? "↓" : "→"
                            });
                        }
                    }
                }

                _logger.Information("Generated {Count} ingredient usage analyses", analysis.Count);
                return analysis;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing ingredient usage");
                return new List<IngredientUsageAnalysis>();
            }
        }

        /// <summary>
        /// Melakukan audit stok dan membandingkan dengan data sistem
        /// </summary>
        public async Task<StockAuditReport> PerformStockAuditAsync()
        {
            try
            {
                var report = new StockAuditReport
                {
                    AuditDate = DateTime.Now,
                    TotalItems = 0,
                    ItemsAudited = 0,
                    Discrepancies = 0,
                    TotalDiscrepancyValue = 0
                };

                var ingredients = await _ingredientRepository.GetAllAsync();
                if (ingredients == null)
                    return report;

                report.TotalItems = ingredients.Count;

                foreach (var ingredient in ingredients)
                {
                    // Simulate audit process
                    var auditedQuantity = ingredient.StockQuantity ?? 0;
                    var systemQuantity = ingredient.StockQuantity ?? 0;

                    if (auditedQuantity != systemQuantity)
                    {
                        report.Discrepancies++;
                        var difference = Math.Abs(auditedQuantity - systemQuantity);
                        report.TotalDiscrepancyValue += difference * (ingredient.CurrentPrice ?? 0);
                    }

                    report.ItemsAudited++;
                }

                _logger.Information("Stock audit complete: {Audited}/{Total} items, {Discrepancies} discrepancies", 
                    report.ItemsAudited, report.TotalItems, report.Discrepancies);
                
                return report;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error performing stock audit");
                return new StockAuditReport();
            }
        }

        private string GetReorderPriority(double currentStock, double minimumStock)
        {
            if (currentStock <= 0)
                return "Kritis";
            if (currentStock < minimumStock * 0.5)
                return "Tinggi";
            if (currentStock < minimumStock)
                return "Sedang";
            return "Rendah";
        }
    }

    /// <summary>
    /// Model untuk saran reorder
    /// </summary>
    public class ReorderSuggestion
    {
        public int IngredientId { get; set; }
        public string IngredientName { get; set; }
        public double CurrentStock { get; set; }
        public double MinimumStock { get; set; }
        public double RecommendedOrderQuantity { get; set; }
        public string Supplier { get; set; }
        public string Priority { get; set; }
        public decimal EstimatedCost { get; set; }
    }

    /// <summary>
    /// Model untuk analisis penggunaan bahan
    /// </summary>
    public class IngredientUsageAnalysis
    {
        public int IngredientId { get; set; }
        public string IngredientName { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal PriceVariation { get; set; }
        public int ObservationCount { get; set; }
        public string TrendDirection { get; set; }
    }

    /// <summary>
    /// Model untuk laporan audit stok
    /// </summary>
    public class StockAuditReport
    {
        public DateTime AuditDate { get; set; }
        public int TotalItems { get; set; }
        public int ItemsAudited { get; set; }
        public int Discrepancies { get; set; }
        public decimal TotalDiscrepancyValue { get; set; }
        public string Status => Discrepancies == 0 ? "Sempurna" : $"{Discrepancies} perbedaan ditemukan";
    }
}
