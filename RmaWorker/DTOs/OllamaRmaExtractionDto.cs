using System.Text.Json.Serialization;

namespace RmaWorker.DTOs;

public sealed record OllamaRmaExtractionDto(
    [property: JsonPropertyName("serial")] string? Serial,
    [property: JsonPropertyName("cnpj")] string? Cnpj,
    [property: JsonPropertyName("defeito")] string? Defeito,
    [property: JsonPropertyName("produto")] string? Produto,
    [property: JsonPropertyName("garantiaInformada")] string? GarantiaInformada,
    [property: JsonPropertyName("evidenciasInformadas")] bool EvidenciasInformadas,
    [property: JsonPropertyName("testesInformados")] bool TestesInformados,
    [property: JsonPropertyName("possuiSerial")] bool PossuiSerial,
    [property: JsonPropertyName("possuiCnpj")] bool PossuiCnpj,
    [property: JsonPropertyName("possuiDefeito")] bool PossuiDefeito);
