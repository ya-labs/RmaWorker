namespace RmaWorker.Configuration;

public sealed class GmailOptions
{
    public const string SectionName = "Gmail";

    public string ApplicationName { get; init; } = "RMA Worker";

    public string UserId { get; init; } = "me";

    public string CredentialsPath { get; init; } = "credentials.json";

    public string TokenPath { get; init; } = "token.json";

    public string SearchQuery { get; init; } = "is:unread subject:RMA_TESTE";

    public string ProcessedLabelName { get; init; } = "RMA PROCESSADO";

    public int MaxUnreadMessagesPerCycle { get; init; } = 10;
}
