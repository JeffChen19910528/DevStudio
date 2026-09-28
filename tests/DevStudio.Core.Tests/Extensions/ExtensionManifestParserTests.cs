using DevStudio.Core.Extensions;
using Xunit;

namespace DevStudio.Core.Tests.Extensions;

public class ExtensionManifestParserTests
{
    private const string ValidRoot = "/extensions/sample";

    private static string ValidManifestJson(string entryPoint = "SampleExtension.dll") => $$"""
        {
          "id": "devstudio.sample",
          "name": "Sample",
          "displayName": "Sample",
          "version": "1.0.0",
          "publisher": "DevStudio",
          "description": "A sample.",
          "hostVersionRange": ">=1.0.0",
          "entryPoint": "{{entryPoint}}",
          "capabilities": ["command"],
          "contributions": { "commands": [{"id": "sample.hello", "title": "Sample: Hello"}] }
        }
        """;

    [Fact]
    public void A_valid_manifest_parses_successfully()
    {
        var result = ExtensionManifestParser.Parse(ValidManifestJson(), ValidRoot);

        Assert.True(result.Succeeded);
        Assert.Equal("devstudio.sample", result.Manifest!.Id.Value);
        Assert.Equal("1.0.0", result.Manifest.Version.ToString());
        Assert.Contains(ExtensionCapability.Command, result.Manifest.Capabilities);
        Assert.Equal("sample.hello", Assert.Single(result.Manifest.Contributions.Commands).Id);
    }

    [Fact]
    public void Malformed_JSON_is_rejected_without_throwing()
    {
        var result = ExtensionManifestParser.Parse("{ not json", ValidRoot);

        Assert.False(result.Succeeded);
        Assert.Null(result.Manifest);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void A_non_object_root_is_rejected()
    {
        var result = ExtensionManifestParser.Parse("[1,2,3]", ValidRoot);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("displayName")]
    [InlineData("publisher")]
    [InlineData("version")]
    [InlineData("hostVersionRange")]
    [InlineData("entryPoint")]
    public void Missing_a_required_field_is_rejected(string fieldToRemove)
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc.Remove(fieldToRemove);

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(fieldToRemove, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_invalid_extension_id_is_rejected()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc["id"] = "Not A Valid Id!";

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void An_invalid_version_is_rejected()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc["version"] = "not-a-version";

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void An_invalid_host_version_range_is_rejected()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc["hostVersionRange"] = "garbage";

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void An_unknown_capability_is_rejected()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc["capabilities"] = new System.Text.Json.Nodes.JsonArray("notARealCapability");

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Unknown capability", StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_command_contribution_id_within_one_manifest_is_rejected()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc["contributions"]!["commands"] = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject { ["id"] = "sample.hello", ["title"] = "A" },
            new System.Text.Json.Nodes.JsonObject { ["id"] = "sample.hello", ["title"] = "B" });

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate command contribution", StringComparison.Ordinal));
    }

    [Fact]
    public void Declaring_command_contributions_without_the_command_capability_is_rejected()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(ValidManifestJson())!.AsObject();
        doc["capabilities"] = new System.Text.Json.Nodes.JsonArray();

        var result = ExtensionManifestParser.Parse(doc.ToJsonString(), ValidRoot);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("nested/../../outside.dll")]
    public void A_relative_traversal_entry_point_is_rejected(string entryPoint)
    {
        var result = ExtensionManifestParser.Parse(ValidManifestJson(entryPoint), ValidRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("traversal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_absolute_entry_point_is_rejected()
    {
        var result = ExtensionManifestParser.Parse(ValidManifestJson("C:\\Windows\\System32\\evil.dll"), ValidRoot);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_UNC_entry_point_is_rejected()
    {
        var result = ExtensionManifestParser.Parse(ValidManifestJson("\\\\server\\share\\evil.dll"), ValidRoot);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_non_dll_entry_point_is_rejected()
    {
        var result = ExtensionManifestParser.Parse(ValidManifestJson("script.sh"), ValidRoot);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_valid_relative_entry_point_resolves_inside_the_extension_root()
    {
        var result = ExtensionManifestParser.Parse(ValidManifestJson("bin/SampleExtension.dll"), ValidRoot);

        Assert.True(result.Succeeded);
        Assert.StartsWith(System.IO.Path.GetFullPath(ValidRoot), result.Manifest!.EntryPoint);
    }
}
