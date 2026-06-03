using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IGmailService
{
    Task<IReadOnlyCollection<EmailMessageDto>> GetUnreadMessagesAsync(CancellationToken cancellationToken);

    Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken);

    Task SendReplyAsync(
        EmailMessageDto originalMessage,
        string body,
        CancellationToken cancellationToken);

    Task SendHtmlReplyAsync(
        EmailMessageDto originalMessage,
        string body,
        CancellationToken cancellationToken);
}
