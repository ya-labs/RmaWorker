namespace RmaWorker.Configuration;

public sealed class SerialValidationOptions
{
    public const string SectionName = "SerialValidation";

    public string BaseUrl { get; init; } = "http://uno.controlid.com.br/supplychain/consultar.sh";

    public int TimeoutSeconds { get; init; } = 30;
}
