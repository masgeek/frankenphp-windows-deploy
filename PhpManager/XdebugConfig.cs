namespace PhpManager;

public class XdebugConfig
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "off";
    public string StartWithRequest { get; set; } = "trigger";
    public string ClientHost { get; set; } = "127.0.0.1";
    public int ClientPort { get; set; } = 9003;
    public string IdeKey { get; set; } = "phpstorm";
    public string DiscoverClientHost { get; set; } = "0";
    public string LogLevel { get; set; } = "7";
    public string LogFile { get; set; } = "";
    public string ExtensionVersion { get; set; } = "3.4.1";
}