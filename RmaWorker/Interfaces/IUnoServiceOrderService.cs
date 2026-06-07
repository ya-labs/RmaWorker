using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IUnoServiceOrderService
{
    Task<RmaServiceOrderResponseDto> OpenAsync(
        RmaServiceOrderRequestDto request,
        CancellationToken cancellationToken);
}
