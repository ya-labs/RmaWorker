using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IRmaProcessorService
{
    Task<RmaAssistantResponseDto> GenerateFromSerialAsync(
        RmaSerialRequestDto request,
        CancellationToken cancellationToken);
}
