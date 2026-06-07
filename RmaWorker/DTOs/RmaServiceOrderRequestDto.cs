namespace RmaWorker.DTOs;

public sealed record RmaServiceOrderRequestDto(
    string? Cnpj,
    IReadOnlyCollection<RmaServiceOrderItemRequestDto> Items);
