namespace RmaWorker.DTOs;

public sealed record SerialValidationResultDto(
    string Serial,
    bool Exists,
    string? ProductCode,
    string? ProductDescription,
    string? UnoOrder,
    string? InvoiceLink,
    string? CustomerName,
    string? Cnpj,
    DateOnly? InvoiceIssuedAt,
    string? City,
    string? ZipCode);
