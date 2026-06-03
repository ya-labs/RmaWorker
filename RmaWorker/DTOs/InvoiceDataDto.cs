namespace RmaWorker.DTOs;

public sealed record InvoiceDataDto(
    string? Number,
    DateOnly? IssuedAt,
    string? Ncm,
    decimal? UnitValue);
