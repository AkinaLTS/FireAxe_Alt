using Avalonia.Data.Converters;
using Avalonia.Media;
using FireAxe;
using System;
using System.Globalization;

namespace FireAxe.ValueConverters;

public static class ReplacementStateConverters
{
    public static readonly IValueConverter ToBrush = new ReplacementStateToBrushConverter();
}

public sealed class ReplacementStateToBrushConverter : IValueConverter
{
    public static readonly IBrush VanillaBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    public static readonly IBrush InstalledButDisabledBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
    public static readonly IBrush ReplacedBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22));
    public static readonly IBrush OverriddenBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0x7A, 0x00));
    public static readonly IBrush ConflictBrush = new SolidColorBrush(Color.FromRgb(0xD2, 0x3D, 0x0A));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ReplacementSlotState.Vanilla => VanillaBrush,
            ReplacementSlotState.InstalledButDisabled => InstalledButDisabledBrush,
            ReplacementSlotState.Replaced => ReplacedBrush,
            ReplacementSlotState.Overridden => OverriddenBrush,
            ReplacementSlotState.Conflict => ConflictBrush,
            _ => VanillaBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
