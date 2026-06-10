namespace RmaWorker.DTOs;

public sealed record SpocIdBlockNextResponseDto(
    string Status,
    string Message,
    string? InputSerial,
    string? BaseSerial,
    string? NextSerial,
    bool IsHtml,
    string ResponseBody);
