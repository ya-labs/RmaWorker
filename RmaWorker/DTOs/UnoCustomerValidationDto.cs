namespace RmaWorker.DTOs;

public sealed record UnoCustomerValidationDto(
    bool Exists,
    string? Code,
    string? Name,
    string? Cnpj,
    string? Status,
    string? Message);
