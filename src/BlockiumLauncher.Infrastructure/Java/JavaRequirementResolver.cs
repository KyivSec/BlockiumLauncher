using System.Globalization;
using System.Text.RegularExpressions;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class JavaRequirementResolver : IJavaRequirementResolver
{
    private static readonly Regex ReleaseRegex = new(@"^(?<major>\d+)(?:\.(?<minor>\d+))?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int GetRequiredJavaMajor(VersionId gameVersion, LoaderType loaderType)
    {
        return GetRequiredJavaMajor(gameVersion.ToString(), loaderType);
    }

    public int GetRequiredJavaMajor(string gameVersion, LoaderType loaderType)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return 17;
        }

        var normalizedVersion = NormalizeGameVersion(gameVersion);

        if (!TryParseReleaseFamily(normalizedVersion, out var releaseMajor, out var releaseMinor))
        {
            return 17;
        }

        if (releaseMajor == 1 && releaseMinor <= 16)
        {
            return 8;
        }

        if (releaseMajor == 1 && releaseMinor == 17)
        {
            return 16;
        }

        if (releaseMajor >= 1 && releaseMinor >= 18)
        {
            return 17;
        }

        if (releaseMajor >= 18)
        {
            return 17;
        }

        return 17;
    }

    public bool IsSatisfiedBy(int installedJavaMajor, VersionId gameVersion, LoaderType loaderType)
    {
        return IsSatisfiedBy(installedJavaMajor, gameVersion.ToString(), loaderType);
    }

    public bool IsSatisfiedBy(int installedJavaMajor, string gameVersion, LoaderType loaderType)
    {
        if (installedJavaMajor <= 0)
        {
            return false;
        }

        var requiredJavaMajor = GetRequiredJavaMajor(gameVersion, loaderType);
        return installedJavaMajor == requiredJavaMajor;
    }

    private static string NormalizeGameVersion(string gameVersion)
    {
        var value = gameVersion.Trim();

        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            value = value[..dashIndex];
        }

        return value;
    }

    private static bool TryParseReleaseFamily(string version, out int releaseMajor, out int releaseMinor)
    {
        releaseMajor = 0;
        releaseMinor = 0;

        var match = ReleaseRegex.Match(version);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out releaseMajor))
        {
            return false;
        }

        if (match.Groups["minor"].Success)
        {
            return int.TryParse(match.Groups["minor"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out releaseMinor);
        }

        releaseMinor = 0;
        return true;
    }
}