using System;
using MicroSnip.Updates;
using Xunit;

namespace MicroSnip.Tests.Updates;

public sealed class UpdateServiceTests
{
    private const string SampleReleaseJson = """
        {
          "tag_name": "v0.2.0",
          "name": "MicroSnip 0.2.0",
          "html_url": "https://github.com/MicroEG/MicroSnip/releases/tag/v0.2.0",
          "body": "Fixes and improvements.",
          "draft": false,
          "prerelease": false
        }
        """;

    [Fact]
    public void TryParseRelease_ReadsPublishedRelease()
    {
        Assert.True(
            UpdateService.TryParseRelease(SampleReleaseJson, out UpdateRelease release));

        Assert.Equal("v0.2.0", release.TagName);
        Assert.Equal("MicroSnip 0.2.0", release.Name);
        Assert.Equal(
            "https://github.com/MicroEG/MicroSnip/releases/tag/v0.2.0",
            release.ReleaseUrl);
        Assert.Equal("Fixes and improvements.", release.Notes);
    }

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("0.2.0", 0, 2, 0)]
    [InlineData("V1.4", 1, 4, 0)]
    [InlineData("v1.2.3-beta.1", 1, 2, 3)]
    [InlineData("v1.2.3+build.7", 1, 2, 3)]
    [InlineData("v1.2.3.4", 1, 2, 3)]
    public void TryParseReleaseVersion_AcceptsCommonTags(
        string tag,
        int major,
        int minor,
        int build)
    {
        Assert.True(UpdateService.TryParseReleaseVersion(tag, out Version version));
        Assert.Equal(new Version(major, minor, build), new Version(version.Major, version.Minor, version.Build));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("v1.x.0")]
    [InlineData("v1.2.3.4.5")]
    [InlineData("release-1.2")]
    public void TryParseReleaseVersion_RejectsUnknownFormats(string tag)
    {
        Assert.False(UpdateService.TryParseReleaseVersion(tag, out _));
    }

    [Theory]
    [InlineData("v0.2.0", 0, 1, 0, "UpdateAvailable")]
    [InlineData("v0.1.0", 0, 1, 0, "UpToDate")]
    [InlineData("v0.1.9", 0, 2, 0, "UpToDate")]
    [InlineData("v1.0", 1, 0, 0, "UpToDate")]
    [InlineData("latest", 0, 1, 0, "UnknownVersion")]
    public void CreateResult_ComparesVersions(
        string tag,
        int currentMajor,
        int currentMinor,
        int currentBuild,
        string expectedStatus)
    {
        var release = new UpdateRelease(tag, null, "https://example.com", null);
        var currentVersion = new Version(currentMajor, currentMinor, currentBuild, 0);

        UpdateCheckResult result = UpdateService.CreateResult(currentVersion, release);

        Assert.Equal(expectedStatus, result.Status.ToString());
        Assert.Equal(UpdateService.FormatVersion(currentVersion), result.CurrentVersion);
    }

    [Fact]
    public void CreateResult_DisplaysNormalizedVersions()
    {
        var release = new UpdateRelease("v0.2", null, "https://example.com", null);

        UpdateCheckResult result = UpdateService.CreateResult(
            new Version(0, 1, 0, 0),
            release);

        Assert.Equal("0.1.0", result.CurrentVersion);
        Assert.Equal("0.2.0", result.LatestVersion);
    }

    [Fact]
    public void TryParseRelease_RejectsDraftAndPrerelease()
    {
        const string draftJson = """
            {
              "tag_name": "v0.2.0",
              "draft": true
            }
            """;
        const string prereleaseJson = """
            {
              "tag_name": "v0.2.0",
              "prerelease": true
            }
            """;

        Assert.False(UpdateService.TryParseRelease(draftJson, out _));
        Assert.False(UpdateService.TryParseRelease(prereleaseJson, out _));
    }

    [Fact]
    public void TryParseRelease_RejectsInvalidJson()
    {
        Assert.False(UpdateService.TryParseRelease("{", out _));
        Assert.False(UpdateService.TryParseRelease("{}", out _));
    }

    [Fact]
    public void TryParseRelease_UsesReleaseUrlFallbackForUntrustedLinks()
    {
        const string json = """
            {
              "tag_name": "v0.2.0",
              "html_url": "https://example.com/releases/tag/v0.2.0"
            }
            """;

        Assert.True(UpdateService.TryParseRelease(json, out UpdateRelease release));
        Assert.Equal(
            "https://github.com/MicroEG/MicroSnip/releases/tag/v0.2.0",
            release.ReleaseUrl);
    }
}
