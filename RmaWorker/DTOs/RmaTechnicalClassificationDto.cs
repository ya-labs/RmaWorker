namespace RmaWorker.DTOs;

public sealed record RmaTechnicalClassificationDto(
    string Status,
    string Reason,
    IReadOnlyCollection<string> Instructions);
