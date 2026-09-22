namespace PhpManager;

public static class Urls
{
    private static string Get(string category, string key) =>
        UrlDatabase.GetUrl(category, key) ?? "";

    public static class PhpDownload
    {
        public static string[] Mirrors => UrlDatabase.GetByCategory("PHP Download")
            .Select(u => u.UrlTemplate).ToArray();

        public static string Package(string version) =>
            string.Format(Mirrors.FirstOrDefault() ?? "", version);
    }

    public static class PhpCatalog
    {
        public static string[] Mirrors => UrlDatabase.GetByCategory("PHP Catalog")
            .Select(u => u.UrlTemplate).ToArray();

        public static string RegexPattern => @"php-(\d+\.\d+\.\d+)-nts-Win32-vs17-x64\.zip";
    }

    public static class Redis
    {
        public static string Package(string version, string phpVersion, string threadSafety, string compiler, string architecture) =>
            $"php_redis-{version}-{phpVersion}-{threadSafety}-{compiler}-{architecture}.zip";

        public static string DownloadUrl(string version, string phpVersion, string threadSafety, string compiler, string architecture)
        {
            var baseTemplate = Get("Redis", "Download");
            var package = Package(version, phpVersion, threadSafety, compiler, architecture);
            return baseTemplate.Replace("{version}", version).Replace("{package}", package);
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
}
