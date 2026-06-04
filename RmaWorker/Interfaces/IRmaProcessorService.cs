using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IRmaProcessorService
{
    Task<RmaAssistantResponseDto> AnalyzeAsync(EmailMessageDto message, CancellationToken cancellationToken);

    Task<RmaAssistantResponseDto> GenerateFromSerialAsync(
        string? serial,
        IReadOnlyCollection<string>? serials,
        CancellationToken cancellationToken);

    Task ProcessAsync(EmailMessageDto message, CancellationToken cancellationToken);
}
