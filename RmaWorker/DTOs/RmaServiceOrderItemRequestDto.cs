namespace RmaWorker.DTOs;

public sealed record RmaServiceOrderItemRequestDto(
    string Serial,
    string? DefectReported,
    string? UnoObservations = null);
