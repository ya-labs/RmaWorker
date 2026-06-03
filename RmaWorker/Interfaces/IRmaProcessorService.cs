using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IRmaProcessorService
{
    Task<RmaAssistantResponseDto> AnalyzeAsync(EmailMessageDto message, CancellationToken cancellationToken);

    Task ProcessAsync(EmailMessageDto message, CancellationToken cancellationToken);
}
