using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IUnoOccurrenceService
{
    Task<OccurrenceOpenResponseDto> OpenAsync(
        OccurrenceOpenRequestDto request,
        CancellationToken cancellationToken);
}
