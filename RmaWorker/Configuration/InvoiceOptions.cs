namespace RmaWorker.Configuration;

public sealed class InvoiceOptions
{
    public const string SectionName = "Invoice";

    public int TimeoutSeconds { get; init; } = 30;
}
