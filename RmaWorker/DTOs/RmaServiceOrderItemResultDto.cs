namespace RmaWorker.DTOs;

public sealed record RmaServiceOrderItemResultDto(
    string Serial,
    string? Cnpj,
    string? CustomerName,
    string? ProductCode,
    string? ProductDescription,
    string? DefectReported,
    int CostCenterCode,
    int CategoryCode,
    string CategoryDescription,
    int AttendantCode,
    int Quantity,
    bool IsUnderWarranty,
    DateOnly? WarrantyUntil,
    bool ReadyForUnoAutomation,
    string Status,
    string? Reason,
    string? ServiceOrderCode);
