using System;
using System.IO;
using MicroSnip.Infrastructure;
using Xunit;

namespace MicroSnip.Tests.Infrastructure;

public sealed class AppDataPathsTests : IDisposable
{
    private readonly string _root;
    private readonly string _target;
    private readonly string _legacy;

    public AppDataPathsTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "MicroSnip.Tests",
            Guid.NewGuid().ToString("N"));
        _target = Path.Combine(_root, "MicroSnip");
        _legacy = Path.Combine(_root, "MegaSnip");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void MigrateLegacyDirectory_MovesContents_WhenTargetIsMissing()
    {
        Directory.CreateDirectory(_legacy);
        File.WriteAllText(Path.Combine(_legacy, "settings.json"), "{}");

        AppDataPaths.MigrateLegacyDirectory(_target, _legacy);

        Assert.False(Directory.Exists(_legacy));
        Assert.Equal(
            "{}",
            File.ReadAllText(Path.Combine(_target, "settings.json")));
    }

    [Fact]
    public void MigrateLegacyDirectory_LeavesBothDirectories_WhenTargetExists()
    {
        Directory.CreateDirectory(_target);
        Directory.CreateDirectory(_legacy);
        File.WriteAllText(Path.Combine(_target, "settings.json"), "new");
        File.WriteAllText(Path.Combine(_legacy, "settings.json"), "old");

        AppDataPaths.MigrateLegacyDirectory(_target, _legacy);

        Assert.Equal(
            "new",
            File.ReadAllText(Path.Combine(_target, "settings.json")));
        Assert.Equal(
            "old",
            File.ReadAllText(Path.Combine(_legacy, "settings.json")));
    }

    [Fact]
    public void MigrateLegacyDirectory_DoesNothing_WhenLegacyIsMissing()
    {
        Directory.CreateDirectory(_root);

        AppDataPaths.MigrateLegacyDirectory(_target, _legacy);

        Assert.False(Directory.Exists(_target));
        Assert.False(Directory.Exists(_legacy));
    }
}
