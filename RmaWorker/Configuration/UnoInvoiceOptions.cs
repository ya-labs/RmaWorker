namespace RmaWorker.Configuration;

public sealed class UnoInvoiceOptions
{
    public const string SectionName = "UnoInvoice";

    public string BaseUrl { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 180;

    public bool BrowserHeadless { get; set; } = true;

    public int BrowserSlowMoMs { get; set; } = 0;

    public string ArtifactsPath { get; set; } = "temp/uno-invoice-browser";

    public string SearchPath { get; set; } = "vdw0004.do?method=prepListar";
}
