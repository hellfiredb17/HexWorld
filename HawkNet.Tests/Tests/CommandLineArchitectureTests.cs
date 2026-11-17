using HawkNet.CommandLine;
using Xunit;

namespace HawkNet.Tests;

public sealed class CommandLineArchitectureTests
{
    [Fact]
    public void CompiledGenericCli_HasNoClanIronAssemblyReferences()
    {
        string[] references = typeof(CommandLineProgram).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("ClanIronSim", references);
        Assert.DoesNotContain("ClanIronSim.HawkNet", references);
        Assert.DoesNotContain("ClanIronSim.CommandLine", references);
    }

    [Fact]
    public void GenericCliProjectTree_HasNoClanIronReferences()
    {
        string projectDirectory = FindProjectDirectory("HawkNet.CommandLine", "HawkNet.CommandLine.csproj");
        string[] extensions = [".cs", ".csproj", ".json", ".md"];
        string[] offenders = Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("ClanIronSim", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(projectDirectory, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindProjectDirectory(string directoryName, string projectName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, directoryName);
            if (File.Exists(Path.Combine(candidate, projectName))) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {projectName}.");
    }
}
