namespace RmaWorker.DTOs;

public sealed record RmaSerialRequestDto(
    string? Serial,
    IReadOnlyCollection<string>? Serials);
