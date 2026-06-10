using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IEmailResponseService
{
    RmaAssistantResponseDto BuildProcessingResponse(IReadOnlyCollection<RmaProcessingResultDto> results);
}
