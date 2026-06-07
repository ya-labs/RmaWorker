namespace RmaWorker.Configuration;

public sealed class UnoErpOptions
{
    public const string SectionName = "UnoErp";

    public string BaseUrl { get; set; } = "http://uno.controlid.com.br:8080/Controlid/";

    public string Login { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 300;

    public bool BrowserHeadless { get; set; } = true;

    public int BrowserSlowMoMs { get; set; } = 0;

    public string ArtifactsPath { get; set; } = "temp/uno-browser";
}
