using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface ISpocSerialResolverService
{
    Task<SpocIdBlockNextResolutionDto?> TryResolveIdBlockNextSerialAsync(
        string serial,
        CancellationToken cancellationToken);
}
