using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IInvoicePdfService
{
    Task<InvoiceDataDto> ExtractAsync(
        string invoiceUrl,
        string productCode,
        CancellationToken cancellationToken);
}
