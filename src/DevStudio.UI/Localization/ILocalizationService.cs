using System.ComponentModel;
using System.Globalization;

namespace DevStudio.UI.Localization;

/// <summary>
/// The single, consistent way UI code obtains localized text (SKILL.md §4–§5 [Phase 12]) — never
/// scattered <c>if (language == "zh-TW")</c> checks, never a duplicated ViewModel per language.
/// Implemented in <see cref="DevStudio.UI"/> (an application/UI-layer concern, not Core — SKILL.md
/// explicitly permits keeping this out of the platform-neutral Core) over the real, standard .NET
/// resource/satellite-assembly mechanism, so culture fallback (a specific culture missing a key
/// falls back to the neutral/default resource) is the runtime's own well-tested behavior, not a
/// hand-rolled substitute.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }

    /// <summary>Exactly the two cultures this phase implements — <c>en-US</c> (default/fallback)
    /// and <c>zh-TW</c> (SKILL.md §2, §28: no other language is in scope this phase, though
    /// nothing here prevents a future one from being added the same way).</summary>
    IReadOnlyList<CultureInfo> SupportedCultures { get; }

    /// <summary>Indexer form of <see cref="GetString"/>, for Avalonia binding
    /// (<c>{Binding Loc[Menu.File]}</c>) — raises the real indexer-changed notification on
    /// <see cref="SetCulture"/> so bound UI text refreshes without an application restart
    /// (SKILL.md §9).</summary>
    string this[string key] { get; }

    /// <summary>Never returns null or an empty string for a real key (SKILL.md §6) — a key
    /// missing from the current culture resolves through the real .NET resource fallback chain
    /// to <c>en-US</c>; a key missing from *every* culture (an authoring bug) is recorded in
    /// <see cref="MissingKeys"/> and returns a visibly-wrong, never-silently-blank placeholder
    /// instead of hiding the gap.</summary>
    string GetString(string key);

    /// <summary>Composes <see cref="GetString"/> with <see cref="string.Format(IFormatProvider?,string,object?[])"/>
    /// for parameterized strings (e.g. <c>"Save changes to {0}?"</c>) — the substituted values
    /// are always real, untranslated data (a file path, a project name, a branch name), never
    /// themselves treated as a resource key or as code (SKILL.md §24–§25).</summary>
    string Format(string key, params object?[] args);

    /// <summary>An unsupported culture is never silently accepted — it is normalized to
    /// <see cref="CultureInfo"/> en-US instead (SKILL.md §5's "validate supported cultures").</summary>
    void SetCulture(CultureInfo culture);

    /// <summary>Every key requested so far that resolved to neither the requested culture nor
    /// its en-US fallback — a real authoring gap, never hidden (SKILL.md §6, §14). Always empty
    /// in ordinary operation, since every key this phase defines exists in both shipped
    /// resources; exists for development/test diagnostics, not end-user display.</summary>
    IReadOnlyList<string> MissingKeys { get; }
}
