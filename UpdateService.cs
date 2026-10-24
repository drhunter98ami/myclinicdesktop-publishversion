using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MyClinic.Services
{
    public sealed class UpdateInfo
    {
        public required string Version { get; init; }
        public required string SizeText { get; init; }
        public required string DownloadUrl { get; init; }
        public required string AssetName { get; init; }
    }

    public static class UpdateService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/drhunter98ami/myclinicdesktop-publishversion/releases/latest";

        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(3, 0, 0);

        public static async Task<UpdateInfo?> GetLatestUpdateAsync(CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                responseStream,
                cancellationToken: cancellationToken);

            if (release is null)
                throw new JsonException("GitHub returned an empty release response.");

            if (release.Draft || release.Prerelease)
                return null;

            if (!TryParseVersion(release.TagName, out Version latestVersion))
                throw new JsonException("GitHub returned a release with an invalid version tag.");

            if (latestVersion <= CurrentVersion)
                return null;

            GitHubAsset? asset = release.Assets
                .Where(candidate => candidate.State.Equals("uploaded", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => IsInstallerAsset(candidate.Name))
                .ThenByDescending(candidate => candidate.Size)
                .FirstOrDefault();

            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) || asset.Size <= 0)
            {
                throw new InvalidDataException(
                    $"GitHub release {release.TagName} is newer, but it does not have a downloadable installer asset.");
            }

            return new UpdateInfo
            {
                Version = latestVersion.ToString(),
                SizeText = FormatFileSize(asset.Size),
                DownloadUrl = asset.BrowserDownloadUrl,
                AssetName = asset.Name
            };
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyClinic", CurrentVersion.ToString(3)));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        private static bool TryParseVersion(string? tag, out Version version)
        {
            string value = (tag ?? string.Empty).Trim().TrimStart('v', 'V');
            if (Version.TryParse(value, out Version? parsed) && parsed is not null)
            {
                version = parsed;
                return true;
            }

            version = new Version(0, 0);
            return false;
        }

        private static bool IsInstallerAsset(string name)
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatFileSize(long bytes)
        {
            const double megabyte = 1024d * 1024d;
            if (bytes >= megabyte)
                return $"{bytes / megabyte:0.##} ميغابايت";

            return $"{Math.Max(1, bytes / 1024d):0.##} كيلوبايت";
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("state")]
            public string State { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}
