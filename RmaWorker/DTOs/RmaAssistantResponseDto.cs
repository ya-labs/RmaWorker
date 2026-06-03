namespace RmaWorker.DTOs;

public sealed record RmaAssistantResponseDto(
    string Status,
    bool IsHtml,
    string ResponseBody,
    IReadOnlyCollection<RmaProcessingResultDto> Results);
