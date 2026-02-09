using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using HppDonatApp.Core.Services;
using HppDonatApp.Data;
using HppDonatApp.Data.Repositories;
using HppDonatApp.Services.Mvvm;
using HppDonatApp.Services.ViewModels;

namespace HppDonatApp;

/// <summary>
/// Main application class for HppDonatApp.
/// Initializes WinUI, dependency injection, database, and logging infrastructure.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    public IServiceProvider? Services { get; private set; }

    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize host and services
        await InitializeAsync();

        // Create main window
        var window = _host?.Services.GetRequiredService<MainWindow>();
        if (window != null)
        {
            window.Activate();
        }
    }

    /// <summary>
    /// Initializes the host with DI container and services.
    /// Sets up all necessary dependencies, database, logging, and repositories.
    /// </summary>
    private async Task InitializeAsync()
    {
        // Create and configure host
        _host = new HostBuilder()
            .ConfigureServices((context, services) =>
            {
                // Configure Logging with Serilog
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File("logs/hppdonat-.txt", 
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                services.AddSerilog();

                // Configure Database Context
                var dbPath = GetDatabasePath();
                services.AddDbContext<HppDonatDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}")
                );

                // Configure Caching
                services.AddMemoryCache();

                // Register Core Services
                services.AddSingleton<IPricingEngine>(provider =>
                    new PricingEngine(
                        provider.GetRequiredService<IMemoryCache>(),
                        provider.GetRequiredService<ILogger>(),
                        new FixedMarkupPricingStrategy(provider.GetRequiredService<ILogger>())
                    )
                );

                services.AddSingleton<IRoundingEngine>(provider =>
                    new RoundingEngine(provider.GetRequiredService<ILogger>())
                );

                // Register Repository Services
                services.AddScoped<IIngredientRepository>(provider =>
                    new IngredientRepository(
                        provider.GetRequiredService<HppDonatDbContext>(),
                        provider.GetRequiredService<IMemoryCache>(),
                        provider.GetRequiredService<ILogger>()
                    )
                );

                services.AddScoped<IRecipeRepository>(provider =>
                    new RecipeRepository(
                        provider.GetRequiredService<HppDonatDbContext>(),
                        provider.GetRequiredService<ILogger>()
                    )
                );

                // Register Settings Service
                services.AddSingleton<ISettingsService, DefaultSettingsService>();

                // Register ViewModels
                services.AddTransient<RecipeEditorViewModel>();
                services.AddTransient<DashboardViewModel>();

                // Register UI Windows
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Initialize database
        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HppDonatDbContext>();
            await dbContext.Database.MigrateAsync();
            
            // Seed initial data if database is empty
            await SeedDatabaseAsync(dbContext);
        }

        Services = _host.Services;

        var logger = _host.Services.GetRequiredService<ILogger>();
        logger.Information("=== HppDonatApp Started ===");
        logger.Information("Database initialized at: {DbPath}", GetDatabasePath());
    }

    /// <summary>
    /// Gets the path where the SQLite database file will be stored.
    /// Uses LocalAppData folder for user-specific storage.
    /// </summary>
    private string GetDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "HppDonatApp");

        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        return Path.Combine(appFolder, "hppdonat.db");
    }

    /// <summary>
    /// Seeds the database with sample data if it's empty.
    /// Creates sample recipes and ingredients for demonstration.
    /// </summary>
    private async Task SeedDatabaseAsync(HppDonatDbContext context)
    {
        var logger = _host?.Services.GetRequiredService<ILogger>();

        // Check if already seeded
        if (await context.Ingredients.AnyAsync())
        {
            logger?.Debug("Database already seeded, skipping seed data");
            return;
        }

        logger?.Information("Seeding database with sample data");

        // Create sample ingredients
        var ingredients = new List<Data.Entities.IngredientEntity>
        {
            new() { Name = "Flour (Terigu)", Unit = "kg", CurrentPrice = 3.00m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Sugar (Gula)", Unit = "kg", CurrentPrice = 8.00m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Egg (Telur)", Unit = "piece", CurrentPrice = 0.50m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Cooking Oil", Unit = "liter", CurrentPrice = 12.00m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Baking Powder", Unit = "kg", CurrentPrice = 25.00m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Vanilla Extract", Unit = "liter", CurrentPrice = 80.00m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Salt", Unit = "kg", CurrentPrice = 2.00m, CreatedAt = DateTime.UtcNow },
            new() { Name = "Chocolate (Dark)", Unit = "kg", CurrentPrice = 45.00m, CreatedAt = DateTime.UtcNow }
        };

        context.Ingredients.AddRange(ingredients);
        await context.SaveChangesAsync();
        logger?.Information("Added {Count} ingredients", ingredients.Count);

        // Create sample recipes
        var recipes = new List<Data.Entities.RecipeEntity>
        {
            new()
            {
                Name = "Donat Original",
                Description = "Classic plain donut recipe with light dusting of sugar",
                TheoreticalOutput = 100,
                WastePercent = 0.10m,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Name = "Donat Cokelat",
                Description = "Rich chocolate donut with chocolate topping",
                TheoreticalOutput = 80,
                WastePercent = 0.15m,
                CreatedAt = DateTime.UtcNow
            }
        };

        context.Recipes.AddRange(recipes);
        await context.SaveChangesAsync();
        logger?.Information("Added {Count} recipes", recipes.Count);

        // Add ingredients to recipes
        var donatOriginal = recipes[0];
        var donatCokelat = recipes[1];

        var recipeIngredients = new List<Data.Entities.RecipeIngredientEntity>
        {
            // Donat Original
            new() { RecipeId = donatOriginal.Id, IngredientId = 1, Quantity = 5m, DisplayOrder = 1 },
            new() { RecipeId = donatOriginal.Id, IngredientId = 2, Quantity = 1m, DisplayOrder = 2 },
            new() { RecipeId = donatOriginal.Id, IngredientId = 3, Quantity = 12m, DisplayOrder = 3 },
            new() { RecipeId = donatOriginal.Id, IngredientId = 5, Quantity = 0.2m, DisplayOrder = 4 },
            new() { RecipeId = donatOriginal.Id, IngredientId = 7, Quantity = 0.05m, DisplayOrder = 5 },

            // Donat Cokelat
            new() { RecipeId = donatCokelat.Id, IngredientId = 1, Quantity = 4.5m, DisplayOrder = 1 },
            new() { RecipeId = donatCokelat.Id, IngredientId = 2, Quantity = 2m, DisplayOrder = 2 },
            new() { RecipeId = donatCokelat.Id, IngredientId = 3, Quantity = 10m, DisplayOrder = 3 },
            new() { RecipeId = donatCokelat.Id, IngredientId = 8, Quantity = 1.5m, DisplayOrder = 4 },
            new() { RecipeId = donatCokelat.Id, IngredientId = 5, Quantity = 0.2m, DisplayOrder = 5 },
            new() { RecipeId = donatCokelat.Id, IngredientId = 7, Quantity = 0.05m, DisplayOrder = 6 }
        };

        context.RecipeIngredients.AddRange(recipeIngredients);
        await context.SaveChangesAsync();
        logger?.Information("Added recipe ingredients");

        // Add price history for ingredients
        var priceHistory = new List<Data.Entities.PriceHistoryEntity>
        {
            new() { IngredientId = 1, Price = 3.00m, RecordedDate = DateTime.UtcNow.AddDays(-30) },
            new() { IngredientId = 1, Price = 3.10m, RecordedDate = DateTime.UtcNow.AddDays(-15) },
            new() { IngredientId = 1, Price = 3.00m, RecordedDate = DateTime.UtcNow },

            new() { IngredientId = 2, Price = 7.80m, RecordedDate = DateTime.UtcNow.AddDays(-30) },
            new() { IngredientId = 2, Price = 8.00m, RecordedDate = DateTime.UtcNow.AddDays(-15) },
            new() { IngredientId = 2, Price = 8.00m, RecordedDate = DateTime.UtcNow },

            new() { IngredientId = 4, Price = 11.50m, RecordedDate = DateTime.UtcNow.AddDays(-30) },
            new() { IngredientId = 4, Price = 12.00m, RecordedDate = DateTime.UtcNow.AddDays(-15) },
            new() { IngredientId = 4, Price = 12.00m, RecordedDate = DateTime.UtcNow }
        };

        context.PriceHistories.AddRange(priceHistory);
        await context.SaveChangesAsync();
        logger?.Information("Added price history data");

        logger?.Information("Database seeding completed successfully");
    }
}

/// <summary>
/// Placeholder dashboard view model for future implementation.
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    public DashboardViewModel(ILogger? logger = null) : base(logger)
    {
    }
}
