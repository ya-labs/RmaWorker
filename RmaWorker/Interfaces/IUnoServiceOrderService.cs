using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IUnoServiceOrderService
{
    Task<UnoCustomerValidationDto> ValidateCustomerAsync(
        string? cnpj,
        CancellationToken cancellationToken);

    Task<RmaServiceOrderResponseDto> OpenAsync(
        RmaServiceOrderRequestDto request,
        CancellationToken cancellationToken);
}
