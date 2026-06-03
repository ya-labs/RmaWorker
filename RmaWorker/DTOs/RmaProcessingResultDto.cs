namespace RmaWorker.DTOs;

public sealed record RmaProcessingResultDto(
    OllamaRmaExtractionDto Extraction,
    string Status,
    string? Reason,
    IReadOnlyCollection<string> MissingFields,
    RmaTechnicalClassificationDto? TechnicalClassification,
    SerialValidationResultDto? SerialValidation,
    InvoiceDataDto? Invoice,
    bool IsUnderWarranty,
    DateOnly? WarrantyUntil);
