namespace RmaWorker.DTOs;

public sealed record RmaProcessingResultDto(
    RmaExtractionDto Extraction,
    string Status,
    string? Reason,
    IReadOnlyCollection<string> MissingFields,
    RmaTechnicalClassificationDto? TechnicalClassification,
    SerialValidationResultDto? SerialValidation,
    InvoiceDataDto? Invoice,
    bool IsUnderWarranty,
    DateOnly? WarrantyUntil);
