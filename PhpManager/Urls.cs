using System.Text.RegularExpressions;

namespace PhpManager;

public static class Urls
{
    private static bool _regexWarned;

    private static string Get(string category, string key)
    {
        var value = UrlDatabase.GetUrl(category, key);
        if (string.IsNullOrWhiteSpace(value))
            LogWindow.LogWarn($"URL '{category}/{key}' is not configured. Check the URLs page.");
        return value ?? "";
    }

    private static string[] ByCategory(string category) =>
        UrlDatabase.GetByCategory(category)
            .OrderByDescending(u => u.IsDefault)
            .Select(u => u.UrlTemplate)
            .ToArray();

    public static class PhpDownload
    {
        public static string[] Mirrors => ByCategory("PHP Download");

        public static string Package(string version) =>
            string.Format(Mirrors.FirstOrDefault() ?? "", version);
    }

    public static class PhpCatalog
    {
        public static string[] Mirrors => ByCategory("PHP Catalog");

        /// <summary>
        /// Builds the catalogue scraper pattern from the primary download template so the two can never
        /// drift apart. Editing the download URL automatically updates what the catalogue matches on.
        /// </summary>
        public static string RegexPattern
        {
            get
            {
                var template = Mirrors.Length > 0 ? Mirrors[0] : "";
                if (string.IsNullOrEmpty(template))
                    template = PhpDownload.Mirrors.FirstOrDefault() ?? "";

                return BuildPatternFromTemplate(template, @"(\d+\.\d+\.\d+)");
            }
        }

        internal static string BuildPatternFromTemplate(string template, string capture)
        {
            var path = template;

            var hostMatch = Regex.Match(path, @"^https?://[^/]+/(.*)$");
            if (hostMatch.Success)
                path = hostMatch.Groups[1].Value;

            path = path.Split('?')[0].Split('#')[0].Trim('/');

var parts = path.Split(new[] { "{0}" }, StringSplitOptions.None);

                // If the primary download template lost its {0} token the scraper would silently
                // match nothing, so warn loudly rather than returning an empty catalogue.
                if (parts.Length == 1 && !_regexWarned)
                {
                    _regexWarned = true;
                    LogWindow.LogError(
                        "The primary PHP download URL has no {0} version token, so the version catalogue " +
                        "cannot be scraped. Add {0} to the template on the URLs page.");
                }

                return string.Join(capture, parts.Select(Regex.Escape));
        }
    }

    public static class Redis
    {
        public static string VersionIndex => Get("Redis", "Version Index");

        public static string Package(string version, string phpVersion, string threadSafety, string compiler, string architecture) =>
            $"php_redis-{version}-{phpVersion}-{threadSafety}-{compiler}-{architecture}";

        public static string DownloadUrl(string version, string phpVersion, string threadSafety, string compiler, string architecture)
        {
            var template = Get("Redis", "Download");
            var package = Package(version, phpVersion, threadSafety, compiler, architecture);
            return template.Replace("{version}", version).Replace("{package}", package);
        }
    }

    public static class Xdebug
    {
        public static string VersionIndex => Get("Xdebug", "Version Index");

        public static string DownloadUrl(string version, string phpVersion, string threadSafety, string compiler, string architecture)
        {
            var template = Get("Xdebug", "Download");
            var package = $"php_xdebug-{version}-{phpVersion}-{threadSafety}-{compiler}-{architecture}";
            return template.Replace("{version}", version).Replace("{package}", package);
        }
    }

    public static class SqlServer
    {
        public static string DownloadUrl(string driverVersion)
        {
            var template = Get("SQL Server", "Download");
            return template.Replace("{version}", driverVersion);
        }
    }

    public static class FrankenPhp
    {
        public static string DownloadUrl(string version = "latest")
        {
            var template = Get("FrankenPHP", "Download");
            var downloadPath = version == "latest" ? "latest/download" : $"download/v{version}";
            return template.Replace("{path}", downloadPath);
        }
    }

    public static class Servy
    {
        public static string DownloadUrl => Get("Servy", "Download");
    }

    public static class Cacert
    {
        public static string DownloadUrl => Get("CA Certificate", "Download");
    }

    public static class Docs
    {
        public static string Link(string key) => Get("Documentation", key);
    }
}