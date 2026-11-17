using HawkNet.CoreUx.Common.Services;
using Xunit;

namespace HawkNet.Tests;

public sealed class CoreUxArchitectureTests
{
    [Fact]
    public void CompiledCoreUx_HasNoClanIronAssemblyReference()
    {
        string[] references = typeof(ICoreUxTool).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("ClanIronSim", references);
        Assert.DoesNotContain("ClanIronSim.HawkNet", references);
        Assert.DoesNotContain("ClanIronSim.CoreUx", references);
    }

    [Fact]
    public void CoreUxProjectTree_HasNoClanIronReferences()
    {
        string coreUxDirectory = FindCoreUxDirectory();
        string[] extensions = [".cs", ".csproj", ".axaml", ".json", ".md"];
        string[] offenders = Directory.EnumerateFiles(coreUxDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("ClanIronSim", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(coreUxDirectory, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindCoreUxDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "CoreUx");
            if (File.Exists(Path.Combine(candidate, "CoreUx.csproj")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CoreUx project directory.");
    }
}
