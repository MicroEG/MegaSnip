using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MegaSnip.Updates;

internal enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    UnknownVersion,
}

internal sealed record UpdateRelease(
    string TagName,
    string? Name,
    string ReleaseUrl,
    string? Notes);

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string LatestVersion,
    UpdateRelease Release);

internal sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal static class UpdateService
{
    internal const string RepositoryUrl = "https://github.com/MicroEG/MicroSnip";
    internal const string ReleasesUrl = RepositoryUrl + "/releases";

    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/MicroEG/MicroSnip/releases/latest";
    private const int MaxNotesLength = 12000;

    private static readonly HttpClient Client = CreateClient();

    internal static async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        try
        {
            using HttpResponseMessage response = await Client
                .GetAsync(LatestReleaseApiUrl, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateCheckException("No published releases were found.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new UpdateCheckException(
                    "GitHub's request limit was reached. Try again later or open the releases page.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateCheckException(
                    "GitHub returned HTTP " +
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                    ".");
            }

            string json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!TryParseRelease(json, out UpdateRelease release))
            {
                throw new UpdateCheckException(
                    "GitHub returned an unexpected release response.");
            }

            return CreateResult(currentVersion, release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new UpdateCheckException(
                "GitHub did not respond in time. Try again later.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateCheckException(
                "GitHub could not be reached. Check your internet connection and try again.",
                ex);
        }
    }

    internal static bool TryParseRelease(string json, out UpdateRelease release)
    {
        release = null!;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                IsTrue(root, "draft") ||
                IsTrue(root, "prerelease"))
            {
                return false;
            }

            if (!root.TryGetProperty("tag_name", out JsonElement tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? tagName = tagElement.GetString()?.Trim();

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return false;
            }

            string? name = GetOptionalString(root, "name");
            string? notes = GetOptionalString(root, "body");

            if (notes is { Length: > MaxNotesLength })
            {
                int cut = char.IsHighSurrogate(notes[MaxNotesLength - 1])
                    ? MaxNotesLength - 1
                    : MaxNotesLength;
                notes = notes[..cut] + "…";
            }

            release = new UpdateRelease(
                tagName,
                string.IsNullOrWhiteSpace(name) ? null : name,
                ResolveReleaseUrl(root, tagName),
                string.IsNullOrWhiteSpace(notes) ? null : notes);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryParseReleaseVersion(string? tagName, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        string value = tagName.Trim();

        if (value.Length > 1 && (value[0] == 'v' || value[0] == 'V'))
        {
            value = value[1..];
        }

        int suffixIndex = value.IndexOfAny(new[] { '-', '+' });

        if (suffixIndex >= 0)
        {
            value = value[..suffixIndex];
        }

        string[] parts = value.Split('.');

        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[4];

        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int number))
            {
                return false;
            }

            numbers[index] = number;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    internal static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        Version normalized = Normalize(version);
        return normalized.Revision > 0
            ? normalized.ToString()
            : normalized.ToString(3);
    }

    internal static UpdateCheckResult CreateResult(
        Version currentVersion,
        UpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(release);

        string current = FormatVersion(currentVersion);

        if (!TryParseReleaseVersion(release.TagName, out Version latest))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UnknownVersion,
                current,
                release.TagName,
                release);
        }

        UpdateCheckStatus status =
            Normalize(latest) > Normalize(currentVersion)
                ? UpdateCheckStatus.UpdateAvailable
                : UpdateCheckStatus.UpToDate;

        return new UpdateCheckResult(
            status,
            current,
            FormatVersion(latest),
            release);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        string version =
            typeof(UpdateService).Assembly.GetName().Version?.ToString() ??
            "unknown";

        client.DefaultRequestHeaders.UserAgent.ParseAdd("MegaSnip/" + version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static Version Normalize(Version version) =>
        new(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));

    private static bool IsTrue(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement element) &&
        element.ValueKind == JsonValueKind.True;

    private static string? GetOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement element) &&
        element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim()
            : null;

    private static string ResolveReleaseUrl(JsonElement root, string tagName)
    {
        if (root.TryGetProperty("html_url", out JsonElement urlElement) &&
            urlElement.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(
                "/MicroEG/MicroSnip/releases/",
                StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        return ReleasesUrl + "/tag/" + Uri.EscapeDataString(tagName);
    }
}
