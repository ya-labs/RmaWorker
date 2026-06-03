namespace RmaWorker.DTOs;

public sealed record RmaAssistantRequestDto(
    string EmailBody,
    string? From,
    string? Subject);
