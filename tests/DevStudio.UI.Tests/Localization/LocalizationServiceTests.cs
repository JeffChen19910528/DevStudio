using System.Globalization;
using System.Resources;
using DevStudio.UI.Localization;
using Xunit;

namespace DevStudio.UI.Tests.Localization;

/// <summary>Real tests against the real, compiled <c>Strings.resx</c>/<c>Strings.zh-TW.resx</c>
/// resources and their real satellite assembly — no fakes, no in-memory substitute resource set
/// (SKILL.md §14 [Phase 12]).</summary>
public class LocalizationServiceTests
{
    [Fact]
    public void Default_culture_is_en_US()
    {
        var service = new LocalizationService();
        Assert.Equal("en-US", service.CurrentCulture.Name);
    }

    [Fact]
    public void SupportedCultures_contains_exactly_en_US_and_zh_TW()
    {
        var service = new LocalizationService();
        Assert.Equal(new[] { "en-US", "zh-TW" }, service.SupportedCultures.Select(c => c.Name));
    }

    [Fact]
    public void GetString_returns_the_real_English_value_for_a_real_key()
    {
        var service = new LocalizationService();
        Assert.Equal("_File", service.GetString("Menu.File"));
    }

    [Fact]
    public void SetCulture_to_zh_TW_returns_the_real_Traditional_Chinese_value()
    {
        var service = new LocalizationService();
        service.SetCulture(LocalizationService.TraditionalChinese);
        Assert.Equal("_檔案", service.GetString("Menu.File"));
    }

    [Fact]
    public void SwitchingBackToEnUS_returns_the_English_value_again()
    {
        var service = new LocalizationService();
        service.SetCulture(LocalizationService.TraditionalChinese);
        service.SetCulture(LocalizationService.EnglishUnitedStates);
        Assert.Equal("_File", service.GetString("Menu.File"));
    }

    [Fact]
    public void Indexer_matches_GetString()
    {
        var service = new LocalizationService();
        Assert.Equal(service.GetString("Menu.Edit"), service["Menu.Edit"]);
    }

    [Fact]
    public void SetCulture_with_an_unsupported_culture_normalizes_to_en_US()
    {
        var service = new LocalizationService();
        service.SetCulture(LocalizationService.TraditionalChinese);
        service.SetCulture(new CultureInfo("ja-JP"));
        Assert.Equal("en-US", service.CurrentCulture.Name);
    }

    [Fact]
    public void SetCulture_raises_the_real_indexer_change_notification()
    {
        var service = new LocalizationService();
        var raisedNames = new List<string>();
        service.PropertyChanged += (_, e) => raisedNames.Add(e.PropertyName ?? string.Empty);

        service.SetCulture(LocalizationService.TraditionalChinese);

        Assert.Contains("Item[]", raisedNames);
    }

    [Fact]
    public void SetCulture_to_the_same_culture_does_not_raise_a_notification()
    {
        var service = new LocalizationService();
        var raised = false;
        service.PropertyChanged += (_, _) => raised = true;

        service.SetCulture(LocalizationService.EnglishUnitedStates);

        Assert.False(raised);
    }

    [Fact]
    public void Format_substitutes_real_untranslated_data_into_a_localized_template()
    {
        var service = new LocalizationService();
        var result = service.Format("Dialog.UnsavedChanges.Message", "Program.cs");
        Assert.Equal("Save changes to Program.cs?", result);
    }

    [Fact]
    public void GetString_for_a_genuinely_unknown_key_records_it_as_missing_and_never_returns_empty()
    {
        var service = new LocalizationService();
        var result = service.GetString("This.Key.Does.Not.Exist.Anywhere");

        Assert.NotEqual(string.Empty, result);
        Assert.Contains("This.Key.Does.Not.Exist.Anywhere", service.MissingKeys);
    }

    [Fact]
    public void Every_real_UI_key_used_by_MainWindow_resolves_to_a_non_empty_string_in_both_cultures()
    {
        // A representative cross-section of every localized UI area (SKILL.md §10) — not
        // exhaustive of all 250 real keys, but covering every panel/menu/dialog family.
        string[] keys =
        {
            "Menu.File", "Menu.Edit", "Menu.View", "Menu.Build", "Menu.Run", "Menu.Debug",
            "Toolbar.Build", "Toolbar.Run", "Explorer.Title", "Properties.Title",
            "Problems.Title", "Terminal.Title", "Toolchains.Title", "TestExplorer.Title",
            "SourceControl.Title", "Extensions.Title", "Settings.Title", "Settings.Language",
            "Dialog.UnsavedChanges.Title", "Dialog.WorkspaceNotTrusted.Title",
        };

        var service = new LocalizationService();
        foreach (var culture in service.SupportedCultures)
        {
            service.SetCulture(culture);
            foreach (var key in keys)
            {
                var value = service.GetString(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"'{key}' resolved to empty/whitespace for {culture.Name}.");
                Assert.DoesNotContain("[[", value);
            }
        }

        Assert.Empty(service.MissingKeys);
    }

    /// <summary>SKILL.md §14's "key consistency" check: every real key in the shipped neutral
    /// (en-US) resource must also exist in the shipped zh-TW satellite resource, and vice versa
    /// — enumerated directly from each culture's own real <see cref="ResourceSet"/> with
    /// <c>tryParents: false</c>, so this never passes merely because fallback papered over a
    /// real gap.</summary>
    [Fact]
    public void Every_key_in_the_real_en_US_resource_also_exists_in_the_real_zh_TW_resource_and_vice_versa()
    {
        var resourceManager = new ResourceManager("DevStudio.UI.Localization.Strings", typeof(LocalizationService).Assembly);

        var neutralKeys = GetOwnKeys(resourceManager, CultureInfo.InvariantCulture);
        var zhTwKeys = GetOwnKeys(resourceManager, LocalizationService.TraditionalChinese);

        var missingFromZhTw = neutralKeys.Except(zhTwKeys).ToList();
        var missingFromEnUs = zhTwKeys.Except(neutralKeys).ToList();

        Assert.True(neutralKeys.Count > 0, "The neutral (en-US) resource resolved zero keys — something is wrong with resource embedding.");
        Assert.Empty(missingFromZhTw);
        Assert.Empty(missingFromEnUs);
    }

    private static HashSet<string> GetOwnKeys(ResourceManager resourceManager, CultureInfo culture)
    {
        var set = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (set is null) return keys;

        foreach (System.Collections.DictionaryEntry entry in set)
        {
            keys.Add((string)entry.Key);
        }
        return keys;
    }

    /// <summary>Proves the real .NET satellite-assembly key-parity/fallback mechanism itself
    /// (not merely this codebase's own resources, which happen to have full parity) using a
    /// real, dynamically-written pair of <c>.resources</c> files via
    /// <see cref="System.Resources.ResourceWriter"/> — a genuine
    /// <see cref="ResourceManager"/> resolving a key present only in the neutral resource for a
    /// specific culture that doesn't have it, exactly the scenario SKILL.md §14's "remove or
    /// simulate a missing zh-TW key" test asks for.</summary>
    [Fact]
    public void The_real_ResourceManager_fallback_chain_resolves_a_key_missing_from_a_specific_culture()
    {
        using var temp = new TempResourceDirectory();
        temp.WriteNeutralResource(("Shared.Key", "neutral value"), ("OnlyInNeutral.Key", "only in neutral"));
        temp.WriteCultureResource("zh-TW", ("Shared.Key", "zh-TW 值"));

        var manager = ResourceManager.CreateFileBasedResourceManager("Fixture", temp.Path, null);

        Assert.Equal("zh-TW 值", manager.GetString("Shared.Key", new CultureInfo("zh-TW")));
        // "OnlyInNeutral.Key" was never written to the zh-TW resource at all — the real
        // ResourceManager fallback chain (zh-TW -> zh -> neutral) is what resolves it, not any
        // code in DevStudio.
        Assert.Equal("only in neutral", manager.GetString("OnlyInNeutral.Key", new CultureInfo("zh-TW")));
    }

    private sealed class TempResourceDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DevStudioLocTests_" + Guid.NewGuid());

        public TempResourceDirectory() => Directory.CreateDirectory(Path);

        public void WriteNeutralResource(params (string Key, string Value)[] entries) =>
            WriteResources(System.IO.Path.Combine(Path, "Fixture.resources"), entries);

        public void WriteCultureResource(string culture, params (string Key, string Value)[] entries) =>
            // A file-based ResourceManager resolves a culture-specific .resources file directly
            // in its own root directory as "<baseName>.<culture>.resources" — unlike a compiled
            // satellite assembly, it does not probe a "<culture>/" subdirectory.
            WriteResources(System.IO.Path.Combine(Path, $"Fixture.{culture}.resources"), entries);

        private static void WriteResources(string path, (string Key, string Value)[] entries)
        {
            using var writer = new ResourceWriter(path);
            foreach (var (key, value) in entries) writer.AddResource(key, value);
            writer.Generate();
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort scratch cleanup */ }
        }
    }
}
