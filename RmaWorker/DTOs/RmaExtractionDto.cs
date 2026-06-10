namespace RmaWorker.DTOs;

public sealed record RmaExtractionDto(
    string? Serial,
    string? Cnpj,
    string? Defeito,
    string? Produto,
    string? GarantiaInformada,
    bool EvidenciasInformadas,
    bool TestesInformados,
    bool PossuiSerial,
    bool PossuiCnpj,
    bool PossuiDefeito);
