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
