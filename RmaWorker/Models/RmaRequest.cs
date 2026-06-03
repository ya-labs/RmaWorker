namespace RmaWorker.Models;

public sealed class RmaRequest
{
    public string? Serial { get; init; }

    public string? Cnpj { get; init; }

    public string? DefectDescription { get; init; }
}
