namespace RmaWorker.DTOs;

public sealed record OccurrenceOpenRequestDto(
    string Title,
    string Description,
    string CategoryCode,
    string? OccurrenceTypeCode = null,
    string? StatusCode = null,
    string? CostCenterCode = null,
    string? Cnpj = null,
    string? UnoLogin = null,
    string? UnoPassword = null);
