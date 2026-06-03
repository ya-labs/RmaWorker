namespace RmaWorker.Configuration;

public sealed class InvoiceOptions
{
    public const string SectionName = "Invoice";

    public bool EnablePdfExtraction { get; init; } = true;

    public int TimeoutSeconds { get; init; } = 30;
}
