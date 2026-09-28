using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace DevStudio.UI.Localization;

/// <summary>
/// Real implementation over <c>Strings.resx</c>/<c>Strings.zh-TW.resx</c> (embedded resources +
/// a real satellite assembly the .NET SDK generates automatically from the culture-suffixed
/// filename — no custom build step). Missing-key fallback is the runtime's own
/// <see cref="ResourceManager"/> culture-fallback chain (requested culture → parent culture →
/// neutral resource), not reimplemented here.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    public static readonly CultureInfo EnglishUnitedStates = new("en-US");
    public static readonly CultureInfo TraditionalChinese = new("zh-TW");

    private static readonly IReadOnlyList<CultureInfo> Cultures = new[] { EnglishUnitedStates, TraditionalChinese };

    private readonly ResourceManager _resourceManager;
    private readonly HashSet<string> _missingKeys = new(StringComparer.Ordinal);
    private CultureInfo _currentCulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService(CultureInfo? initialCulture = null)
    {
        _resourceManager = new ResourceManager("DevStudio.UI.Localization.Strings", typeof(LocalizationService).Assembly);
        _currentCulture = Normalize(initialCulture ?? EnglishUnitedStates);
    }

    public CultureInfo CurrentCulture => _currentCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures => Cultures;

    public IReadOnlyList<string> MissingKeys => _missingKeys.ToList();

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        var value = _resourceManager.GetString(key, _currentCulture);
        if (value is not null) return value;

        // Not found anywhere in the real fallback chain (requested culture -> ... -> the
        // neutral/en-US resource) — a genuine authoring gap, never hidden as null/empty
        // (SKILL.md §6). Recorded for development/test diagnostics; the placeholder is
        // deliberately visible, never blank, so a missing key is never mistaken for "no text
        // needed here."
        _missingKeys.Add(key);
        return $"[[{key}]]";
    }

    public string Format(string key, params object?[] args) =>
        string.Format(_currentCulture, GetString(key), args);

    public void SetCulture(CultureInfo culture)
    {
        var normalized = Normalize(culture);
        if (normalized.Equals(_currentCulture)) return;

        _currentCulture = normalized;

        // The real WPF/Avalonia indexer-changed convention: an empty-string PropertyChangedEventArgs
        // name plus "Item[]" tells every {Binding Loc[SomeKey]} to re-evaluate, refreshing bound
        // UI text without restarting the application (SKILL.md §9).
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
    }

    private static CultureInfo Normalize(CultureInfo culture) =>
        Cultures.Contains(culture) ? culture : EnglishUnitedStates;
}
