using System.Text.RegularExpressions;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

/// <summary>Detects .NET project files (SKILL.md §3). One <see cref="ProjectInfo"/> per
/// .csproj/.fsproj/.vbproj found directly in a directory; a directory rarely has more than one,
/// but nothing here assumes exactly one.</summary>
public sealed class DotNetProjectDetector : IProjectDetector
{
    public string Name => "DotNet";

    private static readonly IReadOnlyDictionary<string, string> LanguageByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".csproj"] = "C#",
        [".fsproj"] = "F#",
        [".vbproj"] = "VB.NET",
    };

    private static readonly Regex SdkAttributeRegex = new("Sdk\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex OutputTypeRegex = new("<OutputType>\\s*([^<]+?)\\s*</OutputType>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProjectTypeGuidsRegex = new("<ProjectTypeGuids>([^<]+)</ProjectTypeGuids>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Real, conservative test-SDK/framework evidence (SKILL.md §11 [Phase 8]) — a
    /// <c>PackageReference Include="..."</c> for the real .NET test SDK or a real test
    /// framework's own package, never a name DevStudio invented.</summary>
    private static readonly Regex TestPackageReferenceRegex = new(
        "PackageReference\\s+Include\\s*=\\s*\"(Microsoft\\.NET\\.Test\\.Sdk|xunit(\\.core)?|xunit\\.v3|NUnit3?TestAdapter|NUnit|MSTest\\.TestAdapter|MSTest\\.TestFramework|MSTest)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProjectReferenceRegex = new(
        "<ProjectReference\\s+Include\\s*=\\s*\"([^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var projectFile = fileNamesInDirectory.FirstOrDefault(f => LanguageByExtension.ContainsKey(Path.GetExtension(f)));
        if (projectFile is null) return null;

        var language = LanguageByExtension[Path.GetExtension(projectFile)];
        var projectFilePath = Path.Combine(directoryPath, projectFile);

        var content = await ConfigFileReading.TryReadHeadAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
        var isExecutable = DetermineIsExecutable(content);
        var isTestProject = content is not null && TestPackageReferenceRegex.IsMatch(content);
        var projectReferencePaths = ParseProjectReferencePaths(content, directoryPath);

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: Path.GetFileNameWithoutExtension(projectFile),
            RootPath: directoryPath,
            ProjectType: ProjectType.DotNet,
            ProjectFile: projectFilePath,
            Languages: new[] { language },
            ConfigurationFiles: new[] { projectFilePath },
            Capabilities: Array.Empty<ProjectCapability>(),
            IsExecutable: isExecutable,
            IsTestProject: isTestProject,
            ProjectReferenceFilePaths: projectReferencePaths);

        return new ProjectDetectionResult(project, Solution: null);
    }

    /// <summary>
    /// Reads the real project file rather than assuming from project type (SKILL.md §8–§9
    /// [Phase 5]): an explicit <c>&lt;OutputType&gt;</c> of <c>Exe</c>/<c>WinExe</c> means
    /// runnable; <c>Library</c> (or omitted, which is the SDK default) means not. A project
    /// using the <c>Microsoft.NET.Sdk.Web</c> SDK is runnable by convention even without an
    /// explicit <c>OutputType</c> — the Web SDK sets one internally. Legacy ASP.NET projects
    /// (non-SDK format) are identified by <c>&lt;ProjectTypeGuids&gt;</c> containing known web
    /// GUIDs and are also treated as runnable. Unreadable/unparsable content defaults to
    /// not-runnable rather than guessing.
    /// </summary>
    private static IReadOnlyList<string> ParseProjectReferencePaths(string? content, string projectDirectory)
    {
        if (content is null) return Array.Empty<string>();

        var paths = new List<string>();
        foreach (Match match in ProjectReferenceRegex.Matches(content))
        {
            var rawPath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            try
            {
                paths.Add(Path.GetFullPath(Path.Combine(projectDirectory, rawPath)));
            }
            catch (Exception)
            {
                // Malformed path in project file — skip silently; detection stays conservative.
            }
        }
        return paths;
    }

    private static bool DetermineIsExecutable(string? content)
    {
        if (content is null) return false;

        var outputTypeMatch = OutputTypeRegex.Match(content);
        if (outputTypeMatch.Success)
        {
            var value = outputTypeMatch.Groups[1].Value;
            if (value.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("WinExe", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var sdkMatch = SdkAttributeRegex.Match(content);
        if (sdkMatch.Success && sdkMatch.Groups[1].Value.Contains("Web", StringComparison.OrdinalIgnoreCase))
            return true;

        // Legacy ASP.NET Web Application / MVC projects use ProjectTypeGuids instead of Sdk.
        // {349c5851-...} = ASP.NET MVC, {E24C65DC-...} = ASP.NET Web Application (Web Forms).
        var guidsMatch = ProjectTypeGuidsRegex.Match(content);
        if (guidsMatch.Success)
        {
            var guids = guidsMatch.Groups[1].Value;
            if (guids.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase) ||
                guids.Contains("{E24C65DC-7377-472b-9ABA-BC803B73C61A}", StringComparison.OrdinalIgnoreCase) ||
                guids.Contains("{E3E379DF-F4C6-4180-9B81-6769533ABE47}", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
