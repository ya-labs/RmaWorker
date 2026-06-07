namespace RmaWorker.DTOs;

public sealed record RmaServiceOrderResponseDto(
    string Status,
    string Message,
    IReadOnlyCollection<RmaServiceOrderItemResultDto> Items);
