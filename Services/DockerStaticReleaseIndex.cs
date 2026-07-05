using System.Text.RegularExpressions;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public static partial class DockerStaticReleaseIndex
{
    public static DockerStaticRelease FindLatestDockerZip(string html, Uri baseUri)
    {
        var releases = DockerZipRegex().Matches(html)
            .Select(match => new
            {
                FileName = match.Groups["file"].Value,
                VersionText = match.Groups["version"].Value
            })
            .Select(candidate => new
            {
                candidate.FileName,
                Version = ParseVersion(candidate.VersionText)
            })
            .Where(candidate => candidate.Version is not null)
            .Select(candidate => new DockerStaticRelease(
                candidate.FileName,
                candidate.Version!,
                new Uri(baseUri, candidate.FileName)))
            .OrderByDescending(release => release.Version)
            .FirstOrDefault();

        return releases ?? throw new InvalidOperationException("Could not find a Docker CLI zip in the Docker static release index.");
    }

    private static Version? ParseVersion(string value)
    {
        string normalized = value.Split('-', 2)[0];
        return Version.TryParse(normalized, out Version? version) ? version : null;
    }

    [GeneratedRegex("(?<file>docker-(?<version>\\d+(?:\\.\\d+){1,3}(?:-\\d+)?)\\.zip)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DockerZipRegex();
}
