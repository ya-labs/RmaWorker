namespace RmaWorker.DTOs;

public sealed record SpocIdBlockNextResolutionDto(
    string InputSerial,
    string BaseSerial,
    string NextSerial);
