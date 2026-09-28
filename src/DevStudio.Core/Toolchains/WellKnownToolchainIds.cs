namespace DevStudio.Core.Toolchains;

/// <summary>Stable identifiers used across detectors, the registry, and capability matching —
/// never an executable name (SKILL.md §6: "ProjectType = DotNet", not "= dotnet.exe").</summary>
public static class WellKnownToolchainIds
{
    public const string DotNet = "dotnet";
    public const string Python = "python";
    public const string Node = "node";
    public const string Npm = "npm";
    public const string Pnpm = "pnpm";
    public const string Yarn = "yarn";
    public const string Java = "java";
    public const string Maven = "maven";
    public const string Gradle = "gradle";
    public const string CMake = "cmake";
    public const string Gcc = "gcc";
    public const string Clang = "clang";
    public const string Rust = "rust";
    public const string Go = "go";
    public const string Vcpkg = "vcpkg";
    public const string Conan = "conan";
    public const string Git = "git";
    public const string Docker = "docker";
    public const string Msvc = "msvc";
}
