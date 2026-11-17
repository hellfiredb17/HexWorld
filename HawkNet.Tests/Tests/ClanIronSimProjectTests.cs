using System.Xml.Linq;
using ClanIronSim;
using Xunit;

namespace HawkNet.Tests
{
    public class ClanIronSimProjectTests
    {
        [Fact]
        public void Simulation_CanBeConstructedWithoutHawkNetTypes()
        {
            var simulation = new ClanIronSimulation(new ClanIronSimulationOptions
            {
                Seed = 42
            });

            Assert.Equal(42, simulation.Options.Seed);
        }

        [Fact]
        public void Project_HasNoProjectReferences()
        {
            string projectPath = FindClanIronSimProject();
            XDocument document = XDocument.Load(projectPath);

            Assert.DoesNotContain(
                document.Descendants("ProjectReference"),
                reference => reference.Attribute("Include") is not null);
        }

        private static string FindClanIronSimProject()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string projectPath = Path.Combine(directory.FullName, "ClanIronSim", "ClanIronSim.csproj");
                if (File.Exists(projectPath))
                {
                    return projectPath;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate ClanIronSim.csproj.");
        }
    }
}
