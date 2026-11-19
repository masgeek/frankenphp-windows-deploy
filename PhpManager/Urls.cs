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

        private static readonly string[] Compilers = ["vs17", "vs16", "vc15", "vc14"];

        /// <summary>
        /// The template itself first, then the same template with each known compiler token swapped in,
        /// so versions built with an older compiler still download.
        /// </summary>
        internal static IEnumerable<string> ExpandCompilers(string template)
        {
            yield return template;
            foreach (var compiler in Compilers)
            {
                var variant = Regex.Replace(template, @"(?i)v[sc]\d+", compiler);
                if (variant != template)
                    yield return variant;
            }
        }

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
                // The catalog URLs are index pages (no {0}); the filename pattern comes from the download template.
                var template = PhpDownload.Mirrors.FirstOrDefault(m => m.Contains("{0}")) ?? "";

                return BuildPatternFromTemplate(template, @"(\d+\.\d+\.\d+)");
            }
        }

        internal static IEnumerable<string> ParseVersions(string html, string pattern) =>
            Regex.Matches(html, pattern, RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value)
                .Distinct();

        internal static string BuildPatternFromTemplate(string template, string capture)
        {
            // Match on the filename only, so every mirror's index page (differing directories) is scraped.
            var path = template.Split('?')[0].Split('#')[0].Trim('/');
            path = path[(path.LastIndexOf('/') + 1)..];

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

                // Older PHP lines were built with other compilers (7.x: vc14/vc15, 8.0-8.3: vs16, 8.4+: vs17),
                // so accept any compiler token or the catalogue would only ever list the newest series.
                return Regex.Replace(string.Join(capture, parts.Select(Regex.Escape)), @"v[sc]\d+", @"v[sc]\d+");
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

    public static class Composer
    {
        public static string DownloadUrl => Get("Composer", "Download");
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