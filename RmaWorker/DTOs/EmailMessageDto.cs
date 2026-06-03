namespace RmaWorker.DTOs;

public sealed record EmailMessageDto(
    string Id,
    string? ThreadId,
    string? MessageIdHeader,
    string? From,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    string Body);
