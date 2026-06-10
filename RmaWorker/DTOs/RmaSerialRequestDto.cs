namespace RmaWorker.DTOs;

public sealed record RmaSerialRequestDto(
    string? Serial,
    IReadOnlyCollection<string>? Serials,
    string? Cnpj,
    string? DefectReported,
    bool MaintenanceInWarranty = false);
