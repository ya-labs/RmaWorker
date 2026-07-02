namespace RmaWorker.DTOs;

public sealed record RmaServiceOrderRequestDto(
    string? Cnpj,
    IReadOnlyCollection<RmaServiceOrderItemRequestDto> Items,
    string? RequestType = null,
    bool MaintenanceInWarranty = false,
    string? PartToSend = null,
    string? UnoObservations = null,
    string? TechnicianCode = null,
    string? UnoLogin = null,
    string? UnoPassword = null);
