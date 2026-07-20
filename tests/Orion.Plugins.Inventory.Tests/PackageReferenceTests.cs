namespace Orion.Plugins.Inventory.Tests;

public sealed class PackageReferenceTests
{
    [Fact]
    public void Csproj_Uses_NuGet_Not_OrionServerBE_Paths()
    {
        string csproj = PluginCsprojPath();
        string text = File.ReadAllText(csproj);

        Assert.DoesNotContain("Orion.csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrionServerBERoot", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Orion/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Protocol/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/PluginContracts/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Orion.Api/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Orion.Gameplay.Api/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Binary/", text, StringComparison.Ordinal);
        Assert.Contains("PackageReference", text, StringComparison.Ordinal);
    }

    static string PluginCsprojPath([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        string? dir = Path.GetDirectoryName(sourceFile);
        while (dir is not null)
        {
            string manifest = Path.Combine(dir, "plugin.json");
            if (File.Exists(manifest))
            {
                return Directory.EnumerateFiles(dir, "*.csproj")
                    .Single(path => !path.Contains(".Tests", StringComparison.Ordinal));
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("plugin root not found from test source path.");
    }
}
