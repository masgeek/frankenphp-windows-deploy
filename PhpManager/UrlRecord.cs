namespace PhpManager;

public class UrlRecord
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public string Key { get; set; } = "";
    public string UrlTemplate { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsDefault { get; set; }

    public string Display => $"[{Category}] {Key}";
    public string DefaultLabel => IsDefault ? "Yes" : "";
}
