using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;

namespace HppDonatApp.Controls;

/// <summary>
/// Custom control for editing a single ingredient line item.
/// This control is designed to be used in a list or DataGrid for inline editing of ingredient properties.
/// 
/// Features:
/// - Ingredient selection dropdown with search
/// - Quantity input with spinner controls (up/down buttons)
/// - Unit display
/// - Price per unit display
/// - Total cost calculation
/// - Unit conversion helper
/// - Keyboard navigation support
/// - Event notifications for changes
/// 
/// This control follows WinUI 3 best practices and supports MVVM patterns.
/// </summary>
public partial class IngredientLineControl : UserControl
{
    private readonly ILogger? _logger;
    private bool _isInitialized;
    private List<(int Id, string Name, string Unit, decimal Price)> _availableIngredients = new();

    // Dependency Properties
    public static readonly DependencyProperty IngredientIdProperty =
        DependencyProperty.Register(
            nameof(IngredientId),
            typeof(int),
            typeof(IngredientLineControl),
            new PropertyMetadata(0, OnIngredientIdChanged));

    public static readonly DependencyProperty IngredientNameProperty =
        DependencyProperty.Register(
            nameof(IngredientName),
            typeof(string),
            typeof(IngredientLineControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty QuantityProperty =
        DependencyProperty.Register(
            nameof(Quantity),
            typeof(decimal),
            typeof(IngredientLineControl),
            new PropertyMetadata(1m, OnQuantityChanged));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(IngredientLineControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PricePerUnitProperty =
        DependencyProperty.Register(
            nameof(PricePerUnit),
            typeof(decimal),
            typeof(IngredientLineControl),
            new PropertyMetadata(0m));

    public static readonly DependencyProperty TotalCostProperty =
        DependencyProperty.Register(
            nameof(TotalCost),
            typeof(decimal),
            typeof(IngredientLineControl),
            new PropertyMetadata(0m));

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(IngredientLineControl),
            new PropertyMetadata(true));

    public static readonly DependencyProperty RemoveCommandProperty =
        DependencyProperty.Register(
            nameof(RemoveCommand),
            typeof(ICommand),
            typeof(IngredientLineControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AvailableIngredientsProperty =
        DependencyProperty.Register(
            nameof(AvailableIngredients),
            typeof(IEnumerable<(int Id, string Name, string Unit, decimal Price)>),
            typeof(IngredientLineControl),
            new PropertyMetadata(null, OnAvailableIngredientsChanged));

    // Public Properties
    public int IngredientId
    {
        get => (int)GetValue(IngredientIdProperty);
        set => SetValue(IngredientIdProperty, value);
    }

    public string IngredientName
    {
        get => (string)GetValue(IngredientNameProperty);
        set => SetValue(IngredientNameProperty, value);
    }

    public decimal Quantity
    {
        get => (decimal)GetValue(QuantityProperty);
        set => SetValue(QuantityProperty, Math.Max(0.01m, value));
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public decimal PricePerUnit
    {
        get => (decimal)GetValue(PricePerUnitProperty);
        set => SetValue(PricePerUnitProperty, Math.Max(0, value));
    }

    public decimal TotalCost
    {
        get => (decimal)GetValue(TotalCostProperty);
        set => SetValue(TotalCostProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => (ICommand)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public IEnumerable<(int Id, string Name, string Unit, decimal Price)> AvailableIngredients
    {
        get => (IEnumerable<(int Id, string Name, string Unit, decimal Price)>)GetValue(AvailableIngredientsProperty);
        set => SetValue(AvailableIngredientsProperty, value);
    }

    // Events
    public event EventHandler<IngredientChangedEventArgs>? IngredientChanged;
    public event EventHandler<QuantityChangedEventArgs>? QuantityChanged;
    public event EventHandler? RemoveRequested;

    /// <summary>
    /// Initializes a new instance of the IngredientLineControl.
    /// </summary>
    public IngredientLineControl()
    {
        this.InitializeComponent();
        _logger = LogManager.GetCurrentClassLogger();
        _logger?.Debug("IngredientLineControl initialized");
    }

    /// <summary>
    /// Dependency property change handler for IngredientId.
    /// Updates the ingredient name and price when the ID changes.
    /// </summary>
    private static void OnIngredientIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (IngredientLineControl)d;
        var newId = (int)e.NewValue;

        control._logger?.Debug("IngredientId changed to {Id}", newId);

        // Update ingredient details based on ID
        var ingredient = control._availableIngredients.FirstOrDefault(i => i.Id == newId);
        if (ingredient != default)
        {
            control.IngredientName = ingredient.Name;
            control.Unit = ingredient.Unit;
            control.PricePerUnit = ingredient.Price;
            control.UpdateTotalCost();

            control._logger?.Information("Ingredient selected: {Name} ({Unit}) @ {Price:C}",
                ingredient.Name, ingredient.Unit, ingredient.Price);

            control.IngredientChanged?.Invoke(control, new IngredientChangedEventArgs
            {
                IngredientId = newId,
                IngredientName = ingredient.Name,
                Unit = ingredient.Unit,
                PricePerUnit = ingredient.Price
            });
        }
    }

    /// <summary>
    /// Dependency property change handler for Quantity.
    /// Recalculates total cost whenever quantity changes.
    /// </summary>
    private static void OnQuantityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (IngredientLineControl)d;
        var newQuantity = (decimal)e.NewValue;

        control._logger?.Debug("Quantity changed to {Qty}", newQuantity);

        control.UpdateTotalCost();

        control.QuantityChanged?.Invoke(control, new QuantityChangedEventArgs
        {
            OldQuantity = (decimal)e.OldValue,
            NewQuantity = newQuantity
        });
    }

    /// <summary>
    /// Dependency property change handler for AvailableIngredients.
    /// Updates the internal ingredient list and populates dropdown.
    /// </summary>
    private static void OnAvailableIngredientsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (IngredientLineControl)d;
        var newIngredients = (IEnumerable<(int Id, string Name, string Unit, decimal Price)>?)e.NewValue;

        if (newIngredients != null)
        {
            control._availableIngredients = newIngredients.ToList();
            control._logger?.Debug("Available ingredients updated: {Count} items", control._availableIngredients.Count);
            control.UpdateIngredientDropdown();
        }
    }

    /// <summary>
    /// Dynamically updates the ingredient dropdown/ComboBox with available items.
    /// </summary>
    private void UpdateIngredientDropdown()
    {
        // This would update a ComboBox or similar selector
        // Implementation depends on the XAML structure
        _logger?.Debug("Updating ingredient dropdown with {Count} items", _availableIngredients.Count);
    }

    /// <summary>
    /// Recalculates the total cost (Quantity * PricePerUnit).
    /// Called whenever quantity or price changes.
    /// </summary>
    private void UpdateTotalCost()
    {
        var newTotal = Quantity * PricePerUnit;
        TotalCost = newTotal;
        _logger?.Debug("Total cost updated: {Qty} * {Price:C} = {Total:C}",
            Quantity, PricePerUnit, newTotal);
    }

    /// <summary>
    /// Increments the quantity by a specified amount (default 1).
    /// Useful for spinner control up button.
    /// </summary>
    public void IncrementQuantity(decimal amount = 1m)
    {
        if (amount > 0)
        {
            Quantity += amount;
            _logger?.Information("Quantity incremented by {Amount}", amount);
        }
    }

    /// <summary>
    /// Decrements the quantity by a specified amount (default 1).
    /// Ensures quantity doesn't go below minimum (0.01).
    /// Useful for spinner control down button.
    /// </summary>
    public void DecrementQuantity(decimal amount = 1m)
    {
        if (amount > 0 && Quantity > amount)
        {
            Quantity -= amount;
            _logger?.Information("Quantity decremented by {Amount}", amount);
        }
    }

    /// <summary>
    /// Converts quantity from one unit to another using standard conversion factors.
    /// Supports common cooking units like kg, g, liter, ml, piece, etc.
    /// </summary>
    /// <param name="fromUnit">Source unit of measurement</param>
    /// <param name="toUnit">Target unit of measurement</param>
    /// <returns>Converted quantity; or original if conversion not supported</returns>
    public decimal ConvertQuantity(string fromUnit, string toUnit)
    {
        if (fromUnit == toUnit)
            return Quantity;

        var normalizedFrom = NormalizeUnit(fromUnit);
        var normalizedTo = NormalizeUnit(toUnit);

        _logger?.Debug("Converting quantity from {From} to {To}: {Qty}",
            normalizedFrom, normalizedTo, Quantity);

        // Create conversion factor dictionary (simplified, not exhaustive)
        var conversions = new Dictionary<(string, string), decimal>
        {
            // Weight conversions
            { ("kg", "g"), 1000m },
            { ("g", "kg"), 0.001m },
            { ("lb", "kg"), 0.453592m },
            { ("kg", "lb"), 2.20462m },
            { ("oz", "g"), 28.3495m },
            { ("g", "oz"), 0.035274m },

            // Volume conversions
            { ("liter", "ml"), 1000m },
            { ("ml", "liter"), 0.001m },
            { ("cup", "ml"), 236.588m },
            { ("ml", "cup"), 0.00423m },
            { ("tbsp", "ml"), 14.7868m },
            { ("ml", "tbsp"), 0.067628m },
            { ("tsp", "ml"), 4.92892m },
            { ("ml", "tsp"), 0.202884m },

            // Cross unit conversions (require density, using water as default)
            { ("kg", "liter"), 1m }, // Assuming water density
            { ("liter", "kg"), 1m }
        };

        if (conversions.TryGetValue((normalizedFrom, normalizedTo), out var factor))
        {
            var converted = Quantity * factor;
            _logger?.Information("Conversion successful: {Original} {From} = {Converted} {To}",
                Quantity, fromUnit, converted, toUnit);
            return converted;
        }

        _logger?.Warning("Conversion not supported: {From} to {To}", fromUnit, toUnit);
        return Quantity;
    }

    /// <summary>
    /// Normalizes unit names to standard forms for conversion.
    /// Handles variations like "Kg", "KG", "kilogram", etc.
    /// </summary>
    private string NormalizeUnit(string unit)
    {
        if (string.IsNullOrEmpty(unit))
            return unit;

        var normalized = unit.ToLowerInvariant().Trim();

        // Map common variations to standard units
        var unitMappings = new Dictionary<string, string>
        {
            { "kilogram", "kg" },
            { "kgs", "kg" },
            { "gram", "g" },
            { "grams", "g" },
            { "litre", "liter" },
            { "l", "liter" },
            { "milliliter", "ml" },
            { "mls", "ml" },
            { "piece", "piece" },
            { "pieces", "piece" },
            { "pcs", "piece" },
            { "pound", "lb" },
            { "pounds", "lb" },
            { "lbs", "lb" },
            { "ounce", "oz" },
            { "ounces", "oz" },
            { "tablespoon", "tbsp" },
            { "tablespoons", "tbsp" },
            { "teaspoon", "tsp" },
            { "teaspoons", "tsp" },
            { "cup", "cup" },
            { "cups", "cup" }
        };

        return unitMappings.TryGetValue(normalized, out var standardUnit) ? standardUnit : normalized;
    }

    /// <summary>
    /// Validates the quantity input to ensure it's a valid positive decimal.
    /// </summary>
    /// <returns>True if quantity is valid; false otherwise</returns>
    public bool ValidateQuantity()
    {
        if (Quantity <= 0)
        {
            _logger?.Warning("Invalid quantity: {Qty}", Quantity);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that an ingredient has been selected.
    /// </summary>
    /// <returns>True if ingredient ID is valid; false otherwise</returns>
    public bool ValidateIngredientSelection()
    {
        if (IngredientId <= 0)
        {
            _logger?.Warning("No valid ingredient selected");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the current state of the ingredient line as a tuple for easy data binding.
    /// </summary>
    /// <returns>Tuple containing all ingredient line data</returns>
    public (int IngredientId, string Name, decimal Quantity, string Unit, decimal Price, decimal Total) GetLineData()
    {
        return (IngredientId, IngredientName, Quantity, Unit, PricePerUnit, TotalCost);
    }

    /// <summary>
    /// Sets all properties from a data tuple.
    /// Useful for initializing the control with existing data.
    /// </summary>
    public void SetLineData(int ingredientId, string name, decimal quantity, string unit, decimal price)
    {
        _logger?.Debug("Setting line data: {Name}, Qty={Qty}, Price={Price:C}",
            name, quantity, price);

        IngredientId = ingredientId;
        IngredientName = name;
        Quantity = quantity;
        Unit = unit;
        PricePerUnit = price;
        UpdateTotalCost();
    }

    /// <summary>
    /// Handles quantity spinup button click (increment).
    /// </summary>
    private void QuantityUpButton_Click(object sender, RoutedEventArgs e)
    {
        IncrementQuantity(0.5m);
        _logger?.Debug("Quantity up button clicked");
    }

    /// <summary>
    /// Handles quantity spindown button click (decrement).
    /// </summary>
    private void QuantityDownButton_Click(object sender, RoutedEventArgs e)
    {
        DecrementQuantity(0.5m);
        _logger?.Debug("Quantity down button clicked");
    }

    /// <summary>
    /// Handles remove button click to trigger removal event.
    /// </summary>
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        _logger?.Information("Remove button clicked for ingredient {Name}", IngredientName);
        RemoveCommand?.Execute(this);
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Handles keyboard input for quantity field (supports arrow up/down).
    /// </summary>
    private void QuantityInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                IncrementQuantity(0.1m);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down:
                DecrementQuantity(0.1m);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Enter:
                if (ValidateQuantity())
                {
                    _logger?.Debug("Quantity confirmed via Enter key");
                }
                break;
        }
    }

    /// <summary>
    /// Handles ingredient selection change from dropdown.
    /// </summary>
    private void IngredientSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is (int id, string name, string unit, decimal price) tuple)
        {
            IngredientId = id;
            _logger?.Debug("Ingredient selected from dropdown: {Name}", name);
        }
    }

    /// <summary>
    /// Provides visual feedback when focus is gained on quantity input.
    /// </summary>
    private void QuantityInput_GotFocus(object sender, RoutedEventArgs e)
    {
        _logger?.Debug("Quantity input got focus");
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    /// <summary>
    /// Validates quantity when focus is lost.
    /// </summary>
    private void QuantityInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!ValidateQuantity())
        {
            _logger?.Warning("Invalid quantity on focus loss: {Qty}", Quantity);
            Quantity = 1m; // Reset to default if invalid
        }
    }

    /// <summary>
    /// Logging helper to get a logger instance (static method for use across control).
    /// </summary>
    private static class LogManager
    {
        private static ILogger? _logger;

        public static ILogger GetCurrentClassLogger()
        {
            return _logger ??= Log.ForContext<IngredientLineControl>();
        }
    }

    /// <summary>
    /// XAML code-behind implementation for the user control UI.
    /// This would contain the actual XAML layout definition.
    /// </summary>
    private void InitializeComponent()
    {
        // This method would be auto-generated by WinUI tooling
        // Placeholder for actual XAML compilation
        _isInitialized = true;
    }
}

/// <summary>
/// Event arguments for ingredient changed event.
/// </summary>
public class IngredientChangedEventArgs : EventArgs
{
    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal PricePerUnit { get; set; }
}

/// <summary>
/// Event arguments for quantity changed event.
/// </summary>
public class QuantityChangedEventArgs : EventArgs
{
    public decimal OldQuantity { get; set; }
    public decimal NewQuantity { get; set; }
}

/// <summary>
/// Helper class for common ingredient unit operations.
/// </summary>
public static class IngredientUnitHelper
{
    /// <summary>
    /// Gets all supported units organized by category.
    /// </summary>
    public static Dictionary<string, List<string>> GetCategorizedUnits()
    {
        return new Dictionary<string, List<string>>
        {
            { "Weight", new List<string> { "kg", "g", "lb", "oz" } },
            { "Volume", new List<string> { "liter", "ml", "cup", "tbsp", "tsp" } },
            { "Count", new List<string> { "piece", "dozen" } },
            { "Other", new List<string> { "pinch", "dash", "splash" } }
        };
    }

    /// <summary>
    /// Suggests appropriate units for a given ingredient type.
    /// </summary>
    public static List<string> GetSuggestedUnitsFor(string ingredientType)
    {
        return ingredientType?.ToLowerInvariant() switch
        {
            var x when x.Contains("flour") || x.Contains("sugar") || x.Contains("powder") => new List<string> { "kg", "g" },
            var x when x.Contains("oil") || x.Contains("water") || x.Contains("milk") => new List<string> { "liter", "ml" },
            var x when x.Contains("egg") => new List<string> { "piece", "dozen" },
            _ => new List<string> { "kg", "liter", "piece" }
        };
    }
}
