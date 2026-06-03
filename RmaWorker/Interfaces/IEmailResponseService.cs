using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IEmailResponseService
{
    Task ReplyMissingDataAsync(EmailMessageDto message, IReadOnlyCollection<string> missingFields, CancellationToken cancellationToken);

    Task ReplySerialNotFoundAsync(EmailMessageDto message, string serial, CancellationToken cancellationToken);

    Task ReplyRmaEligibleAsync(
        EmailMessageDto message,
        SerialValidationResultDto serialValidation,
        InvoiceDataDto? invoiceData,
        bool isUnderWarranty,
        DateOnly? warrantyUntil,
        CancellationToken cancellationToken);

    Task ReplyProcessingResultsAsync(
        EmailMessageDto message,
        IReadOnlyCollection<RmaProcessingResultDto> results,
        CancellationToken cancellationToken);

    RmaAssistantResponseDto BuildProcessingResponse(IReadOnlyCollection<RmaProcessingResultDto> results);
}
