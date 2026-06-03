namespace RmaWorker.DTOs;

public sealed record RmaValidationResultDto(
    string Status,
    string? Reason,
    bool IsUnderWarranty,
    DateOnly? WarrantyUntil,
    SerialValidationResultDto? SerialValidation,
    InvoiceDataDto? Invoice);
