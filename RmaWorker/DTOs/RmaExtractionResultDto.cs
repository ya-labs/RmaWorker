using System.Text.Json.Serialization;

namespace RmaWorker.DTOs;

public sealed record RmaExtractionResultDto(
    [property: JsonPropertyName("rmas")] IReadOnlyCollection<OllamaRmaExtractionDto> Rmas);
