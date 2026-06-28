namespace RimeKit.Windows.Core;

internal static class GitHubProxyHelper
{
    private static readonly string[] GitHubHosts =
    [
        "github.com",
        "codeload.github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "raw.githubusercontent.com",
    ];

    private static readonly string[] ProxyPrefixes =
    [
        "https://gh.llkk.cc/",
        "https://gh-proxy.com/",
    ];

    public const int MaxAttemptsNonGitHub = 3;
    public const int MaxAttemptsGitHubDirect = 1;
    public const int MaxAttemptsProxy = 2;

    public const long SpeedThresholdBytesPerSecond = 50 * 1024;
    public static readonly TimeSpan SpeedSampleWindow = TimeSpan.FromSeconds(8);
    public const long MinFileSizeForSpeedCheck = 1024 * 1024;

    public static bool IsGitHubUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            foreach (string host in GitHubHosts)
            {
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    public static List<string> BuildFallbackUrls(string primaryUrl)
    {
        var urls = new List<string> { primaryUrl };
        if (!IsGitHubUrl(primaryUrl))
        {
            return urls;
        }

        foreach (string prefix in ProxyPrefixes)
        {
            urls.Add($"{prefix}{primaryUrl}");
        }

        return urls;
    }

    public static int GetMaxAttempts(string url, int index)
    {
        if (!IsGitHubUrl(url))
        {
            return MaxAttemptsNonGitHub;
        }

        return index == 0 ? MaxAttemptsGitHubDirect : MaxAttemptsProxy;
    }
}
