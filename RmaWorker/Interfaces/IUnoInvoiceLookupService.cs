using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IUnoInvoiceLookupService
{
    Task<InvoiceLookupResponseDto> FindAsync(
        InvoiceLookupRequestDto request,
        CancellationToken cancellationToken);
}
