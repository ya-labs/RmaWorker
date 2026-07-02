namespace RmaWorker.DTOs;

public sealed record InvoiceLookupResponseDto(
    string Status,
    string Message,
    string? InvoiceNumber,
    string? FileName,
    string? ContentType,
    string? Base64Pdf);
