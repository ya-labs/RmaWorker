namespace RmaWorker.DTOs;

public sealed record OccurrenceOpenResponseDto(
    string Status,
    string Message,
    string? OccurrenceCode,
    string? CustomerCode,
    string? CustomerName,
    string? CategoryCode,
    string? Title);
