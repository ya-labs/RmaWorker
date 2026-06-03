using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IOllamaService
{
    Task<IReadOnlyCollection<OllamaRmaExtractionDto>> ExtractRmaDataAsync(string emailContent, CancellationToken cancellationToken);
}
