using System.Globalization;
using Avalonia.Data.Converters;

namespace DevStudio.UI.Localization;

/// <summary>Applies <see cref="string.Format"/> to a localized template string bound as the
/// first <see cref="Avalonia.Data.MultiBinding"/> value, with the remaining bound values as its
/// <c>{0}</c>/<c>{1}</c>.../ arguments (SKILL.md §7 [Phase 12]). Exists only because XAML has no
/// other way to combine a runtime-resolved <see cref="ILocalizationService"/> template with
/// dynamic data without either a computed ViewModel property per message or scattered imperative
/// <c>string.Format</c> calls — this converter is the single, reusable binding-only mechanism.
/// Never used to format code, paths as commands, or anything that could be interpreted as
/// executable input; every argument here is display text only.</summary>
public sealed class LocFormatConverter : IMultiValueConverter
{
    public static readonly LocFormatConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] is not string format)
        {
            return Avalonia.AvaloniaProperty.UnsetValue;
        }

        var args = values.Skip(1).Select(v => v ?? string.Empty).ToArray();
        return string.Format(culture, format, args);
    }
}
