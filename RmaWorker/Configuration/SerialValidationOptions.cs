namespace RmaWorker.Configuration;

public sealed class SerialValidationOptions
{
    public const string SectionName = "SerialValidation";

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;
}
