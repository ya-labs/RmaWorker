namespace RmaWorker.Configuration;

public sealed class SpocOptions
{
    public const string SectionName = "Spoc";

    public string BaseUrl { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public bool BrowserHeadless { get; set; } = true;

    public int BrowserSlowMoMs { get; set; } = 0;
}
