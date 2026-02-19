using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace HppDonatApp.Converters;

/// <summary>
/// Converts numeric values to rounded Rupiah display (e.g. "Rp 12.500") and parses user input back.
/// </summary>
public sealed class RupiahRoundConverter : IValueConverter
{
    private static readonly CultureInfo IndonesianCulture = new("id-ID");
    private const decimal DefaultStep = 1m;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (!TryReadDecimal(value, out var amount))
        {
            return string.Empty;
        }

        var options = ParseOptions(parameter, targetType);
        var rounded = RoundToStep(amount, options.Step);
        return string.Format(IndonesianCulture, "Rp {0:N0}", rounded);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var options = ParseOptions(parameter, targetType);
        var text = value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return options.IsNullable ? null! : 0m;
        }

        var normalized = text
            .Replace("Rp", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, IndonesianCulture, out var parsed) ||
            decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out parsed))
        {
            return RoundToStep(parsed, options.Step);
        }

        return DependencyProperty.UnsetValue;
    }

    private static bool TryReadDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Number, IndonesianCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0m;
                return false;
        }
    }

    private static decimal RoundToStep(decimal value, decimal step)
    {
        if (step <= 0m)
        {
            step = DefaultStep;
        }

        var scaled = value / step;
        var roundedScaled = Math.Round(scaled, 0, MidpointRounding.AwayFromZero);
        return roundedScaled * step;
    }

    private static ConverterOptions ParseOptions(object parameter, Type targetType)
    {
        var isNullable = !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
        var step = DefaultStep;

        if (parameter is string parameterText && !string.IsNullOrWhiteSpace(parameterText))
        {
            var tokens = parameterText.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (string.Equals(token, "nullable", StringComparison.OrdinalIgnoreCase))
                {
                    isNullable = true;
                    continue;
                }

                if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedStep) && parsedStep > 0m)
                {
                    step = parsedStep;
                    continue;
                }

                if (decimal.TryParse(token, NumberStyles.Number, IndonesianCulture, out parsedStep) && parsedStep > 0m)
                {
                    step = parsedStep;
                }
            }
        }

        return new ConverterOptions(step, isNullable);
    }

    private readonly record struct ConverterOptions(decimal Step, bool IsNullable);
}
