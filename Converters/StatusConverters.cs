using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SentinelX.Models;
namespace SentinelX.Converters;
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value?.ToString()?.ToUpperInvariant() switch
        {
            "VERIFIED" or "ROLLEDBACK" => "SxSuccess",
            "FAILED" => "SxError",
            "WAITINGPERMISSION" or "PENDING" or "UNVERIFIED" => "SxWarning",
            "RUNNING" or "VERIFYING" => "SxAccentCyan",
            _ => "SxTextSecondary"
        };
        return Application.Current.FindResource(key);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class VoiceStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Application.Current.FindResource(value switch
    { VoiceState.Active => "SxVoiceActive", VoiceState.Standby => "SxVoiceStandby", _ => "SxVoiceOff" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class RiskLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Application.Current.FindResource(value switch
    { RiskLevel.Low => "SxSuccess", RiskLevel.Medium => "SxWarning", _ => "SxError" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ReadinessToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Application.Current.FindResource(value switch
    {
        ReadinessState.Ready => "SxSuccess", ReadinessState.NeedsSetup => "SxWarning",
        ReadinessState.Unavailable => "SxError", _ => "SxTextSecondary"
    });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// 0.93 NOWOCZESNE GUI: pokazuje komunikat tylko gdy tekst nie jest pusty.
/// Używany przy błędach walidacji pól ustawień i podpowiedziach stanu.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException("Konwersja tylko w jedną stronę.");
}

/// <summary>
/// 0.93 NOWOCZESNE GUI: <c>SystemSnapshot.Empty</c> ma NaN w Cpu/Gpu/RAM, a RangeBase odrzuca
/// wartości niekończone (ValidateValueCallback IsValidDoubleValue) — wiązanie paska do pustego
/// odczytu kończyłoby się błędem. Konwerter daje 0, a pasek jest jednocześnie chowany przez
/// <see cref="FiniteToVisibilityConverter"/>, więc zero nigdy nie udaje pomiaru.
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class SafePercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double number && double.IsFinite(number) ? number : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException("Konwersja tylko w jedną stronę.");
}

/// <summary>Pokazuje pasek pomiaru tylko wtedy, gdy odczyt jest liczbą — brak pomiaru chowa pasek.</summary>
[ValueConversion(typeof(double), typeof(Visibility))]
public sealed class FiniteToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double number && double.IsFinite(number) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException("Konwersja tylko w jedną stronę.");
}
