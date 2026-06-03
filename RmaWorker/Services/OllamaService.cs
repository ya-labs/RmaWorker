using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class OllamaService : IOllamaService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<OllamaRmaExtractionDto>> ExtractRmaDataAsync(string emailContent, CancellationToken cancellationToken)
    {
        var request = new OllamaGenerateRequest(
            _options.Model,
            BuildPrompt(emailContent),
            Stream: false,
            Format: "json",
            Options: new OllamaRequestOptions(_options.Temperature));

        var response = await _httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(rawResponse, JsonOptions);

        if (!string.IsNullOrWhiteSpace(ollamaResponse?.Error))
        {
            throw new InvalidOperationException($"Ollama retornou erro: {ollamaResponse.Error}");
        }

        var extractedJson = string.IsNullOrWhiteSpace(ollamaResponse?.Response)
            ? ollamaResponse?.Thinking
            : ollamaResponse.Response;

        if (string.IsNullOrWhiteSpace(ollamaResponse?.Response) && !string.IsNullOrWhiteSpace(ollamaResponse?.Thinking))
        {
            _logger.LogWarning("Ollama retornou JSON no campo thinking. Usando thinking como fallback.");
        }

        if (string.IsNullOrWhiteSpace(extractedJson))
        {
            _logger.LogError("Ollama retornou resposta vazia. Payload bruto: {RawResponse}", rawResponse);
            throw new InvalidOperationException("Ollama retornou uma resposta vazia. Verifique se o modelo configurado existe e se ele consegue responder em JSON.");
        }

        try
        {
            var extraction = DeserializeExtraction(extractedJson);
            return NormalizeExtractions(extraction, emailContent);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Falha ao desserializar JSON retornado pelo Ollama: {Response}", extractedJson);
            throw;
        }
    }

    private static string BuildPrompt(string emailContent)
    {
        return $$"""
            /no_think

            Extraia dados de uma ou mais solicitacoes de RMA escritas em portugues.

            Regras:
            - Retorne exclusivamente JSON valido.
            - Nao escreva explicacoes.
            - Nao use markdown.
            - Cada equipamento/serial corresponde a uma RMA separada.
            - Se o e-mail tiver varios seriais/equipamentos, retorne um item em rmas para cada um.
            - Se CNPJ ou defeito forem informados uma unica vez e valerem para todos os equipamentos, repita esses dados em cada item.
            - Extraia produto quando houver coluna, linha ou descricao de produto associada ao serial.
            - Extraia garantiaInformada quando houver texto como GARANTIA ou CONSERTO associado ao item.
            - evidenciasInformadas deve ser true se o e-mail disser que ha videos, fotos, anexos ou evidencias.
            - testesInformados deve ser true se o e-mail disser que testes, atualizacao, reset, recovery ou procedimento tecnico ja foram realizados.
            - Serial pode aparecer como serial, serie, série, numero de serie, numero de série, num. serie, nr serie, n serie, no serie, numero serial, numero do equipamento, codigo do equipamento, NS, N/S ou S/N.
            - Quando encontrar qualquer variacao de serial seguida de um codigo, preencha serial com esse codigo.
            - Se uma informacao nao existir, retorne null no campo textual e false no campo booleano correspondente.
            - O CNPJ deve ser retornado apenas com numeros, sem pontuacao.
            - Defeito so existe se o e-mail informar explicitamente um problema, falha, defeito ou comportamento anormal.
            - Nao invente defeito.
            - Nao preencha defeito com exemplos.
            - Se o e-mail informar apenas CNPJ e serial/NS, retorne defeito null e possuiDefeito false.
            - Preserve a descricao do defeito usando somente texto presente no e-mail.
            - A IA apenas extrai informacoes; nao valide CNPJ, serial ou regra de negocio.

            Formato obrigatorio:
            {
              "rmas": [
                {
                  "serial": null,
                  "cnpj": null,
                  "defeito": null,
                  "produto": null,
                  "garantiaInformada": null,
                  "evidenciasInformadas": false,
                  "testesInformados": false,
                  "possuiSerial": false,
                  "possuiCnpj": false,
                  "possuiDefeito": false
                }
              ]
            }

            Email:
            {{emailContent}}
            """;
    }

    private static IReadOnlyCollection<OllamaRmaExtractionDto> DeserializeExtraction(string extractedJson)
    {
        var result = JsonSerializer.Deserialize<RmaExtractionResultDto>(extractedJson, JsonOptions);
        if (result?.Rmas is { Count: > 0 })
        {
            return result.Rmas;
        }

        var single = JsonSerializer.Deserialize<OllamaRmaExtractionDto>(extractedJson, JsonOptions);
        return single is null ? [] : [single];
    }

    private static IReadOnlyCollection<OllamaRmaExtractionDto> NormalizeExtractions(
        IReadOnlyCollection<OllamaRmaExtractionDto> extractions,
        string emailContent)
    {
        var normalized = extractions
            .Select(extraction => NormalizeExtraction(extraction, emailContent))
            .ToList();

        var knownSerials = normalized
            .Where(extraction => !string.IsNullOrWhiteSpace(extraction.Serial))
            .Select(extraction => extraction.Serial!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cnpj = normalized.FirstOrDefault(extraction => !string.IsNullOrWhiteSpace(extraction.Cnpj))?.Cnpj
            ?? TryExtractCnpj(emailContent);
        var hasEvidence = ContainsEvidence(emailContent);
        var hasTests = ContainsTests(emailContent);

        var structuredExtractions = TryExtractStructuredExtractions(emailContent, cnpj, hasEvidence, hasTests)
            .Select(extraction => NormalizeExtraction(extraction, emailContent))
            .ToList();
        if (structuredExtractions.Count > 0)
        {
            var structuredSerials = structuredExtractions
                .Where(extraction => !string.IsNullOrWhiteSpace(extraction.Serial))
                .Select(extraction => extraction.Serial!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            normalized = normalized
                .Where(extraction =>
                    !string.IsNullOrWhiteSpace(extraction.Serial)
                    && !structuredSerials.Contains(extraction.Serial))
                .ToList();
            knownSerials = normalized
                .Select(extraction => extraction.Serial!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var structuredExtraction in structuredExtractions)
        {
            if (!string.IsNullOrWhiteSpace(structuredExtraction.Serial)
                && knownSerials.Add(structuredExtraction.Serial))
            {
                normalized.Add(structuredExtraction);
            }
        }

        foreach (var serial in TryExtractSerials(emailContent))
        {
            if (knownSerials.Contains(serial))
            {
                continue;
            }

            normalized.Add(new OllamaRmaExtractionDto(
                serial,
                cnpj,
                null,
                null,
                null,
                hasEvidence,
                hasTests,
                true,
                !string.IsNullOrWhiteSpace(cnpj),
                false));
        }

        if (normalized.Count == 0)
        {
            normalized.Add(new OllamaRmaExtractionDto(
                null,
                cnpj,
                null,
                null,
                null,
                hasEvidence,
                hasTests,
                false,
                !string.IsNullOrWhiteSpace(cnpj),
                false));
        }

        return normalized;
    }

    private static OllamaRmaExtractionDto NormalizeExtraction(
        OllamaRmaExtractionDto extraction,
        string emailContent)
    {
        var extractedSerial = string.IsNullOrWhiteSpace(extraction.Serial)
            ? null
            : extraction.Serial.Trim();
        var serial = string.IsNullOrWhiteSpace(extractedSerial) || !IsPlausibleSerial(extractedSerial)
            ? TryExtractSerial(emailContent)
            : extractedSerial;
        var cnpj = string.IsNullOrWhiteSpace(extraction.Cnpj)
            ? TryExtractCnpj(emailContent)
            : Regex.Replace(extraction.Cnpj, @"\D", string.Empty);
        var defect = string.IsNullOrWhiteSpace(extraction.Defeito) ? null : extraction.Defeito.Trim();
        var product = string.IsNullOrWhiteSpace(extraction.Produto) ? null : extraction.Produto.Trim();
        var warrantyInfo = string.IsNullOrWhiteSpace(extraction.GarantiaInformada)
            ? null
            : extraction.GarantiaInformada.Trim();

        if (defect is not null && !ContainsNormalized(emailContent, defect))
        {
            defect = null;
        }

        return new OllamaRmaExtractionDto(
            serial,
            string.IsNullOrWhiteSpace(cnpj) ? null : cnpj,
            defect,
            product,
            warrantyInfo,
            extraction.EvidenciasInformadas || ContainsEvidence(emailContent),
            extraction.TestesInformados || ContainsTests(emailContent),
            !string.IsNullOrWhiteSpace(serial),
            !string.IsNullOrWhiteSpace(cnpj),
            !string.IsNullOrWhiteSpace(defect));
    }

    private static string? TryExtractSerial(string emailContent)
    {
        var match = Regex.Match(
            emailContent,
            @"(?ix)
            \b
            (?:
                ns
                | n\s*/\s*s
                | s\s*/\s*n
                | s\s*n
                | serial
                | s[ée]rie
                | num\.?\s*serie
                | nr\.?\s*serie
                | n[\.º°]?\s*serie
                | no\.?\s*serie
                | numero\s+de\s+serie
                | numero\s+serial
                | numero\s+do\s+equipamento
                | codigo\s+do\s+equipamento
                | cod\.?\s*equipamento
            )
            \s*[:\-]?\s*
            (?<serial>[a-z0-9][a-z0-9./_-]{2,})
            ");

        if (!match.Success)
        {
            return null;
        }

        var serial = match.Groups["serial"].Value.Trim();
        return IsPlausibleSerial(serial) ? serial : null;
    }

    private static IReadOnlyCollection<string> TryExtractSerials(string emailContent)
    {
        return Regex.Matches(
                emailContent,
                @"(?ix)
                \b
                (?:
                    ns
                    | n\s*/\s*s
                    | s\s*/\s*n
                    | s\s*n
                    | serial
                    | s[ée]rie
                    | num\.?\s*serie
                    | nr\.?\s*serie
                    | n[\.ÂºÂ°]?\s*serie
                    | no\.?\s*serie
                    | numero\s+de\s+serie
                    | numero\s+serial
                    | numero\s+do\s+equipamento
                    | codigo\s+do\s+equipamento
                    | cod\.?\s*equipamento
                )
                \s*[:\-]?\s*
                (?<serial>[a-z0-9][a-z0-9./_-]{2,})
                ")
            .Select(match => match.Groups["serial"].Value.Trim())
            .Where(IsPlausibleSerial)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPlausibleSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        var normalized = serial.Trim();
        var blockedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "equipamento",
            "serie",
            "serial",
            "nota",
            "venda",
            "data",
            "produto",
            "servico",
            "serviço",
            "ncm",
            "cfop"
        };

        if (blockedValues.Contains(normalized))
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"(?i)^0[A-Z0-9]0[A-Z0-9][0-9]{2}/[A-Z0-9]{6}$")
            || Regex.IsMatch(normalized, @"(?i)^[A-Z0-9]{4}$");
    }

    private static string? TryExtractCnpj(string emailContent)
    {
        var match = Regex.Match(
            emailContent,
            @"(?<!\d)(?<cnpj>\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})(?!\d)");

        return match.Success
            ? Regex.Replace(match.Groups["cnpj"].Value, @"\D", string.Empty)
            : null;
    }

    private static IReadOnlyCollection<OllamaRmaExtractionDto> TryExtractStructuredExtractions(
        string emailContent,
        string? cnpj,
        bool hasEvidence,
        bool hasTests)
    {
        var results = new List<OllamaRmaExtractionDto>();
        var lines = emailContent
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        for (var index = 0; index < lines.Count; index++)
        {
            var serialMatch = Regex.Match(
                lines[index],
                @"(?i)^(?:s[ée]rie|ns|n\s*/\s*s|s\s*/\s*n)\s*:\s*(?<serial>[a-z0-9][a-z0-9./_-]{2,})$");

            if (!serialMatch.Success)
            {
                continue;
            }

            var serial = serialMatch.Groups["serial"].Value.Trim();
            if (!IsPlausibleSerial(serial))
            {
                continue;
            }

            string? defect = null;
            string? product = null;
            for (var nextIndex = index + 1; nextIndex < Math.Min(index + 5, lines.Count); nextIndex++)
            {
                var defectMatch = Regex.Match(lines[nextIndex], @"(?i)^defeito\s*:\s*(?<value>.+)$");
                if (defectMatch.Success)
                {
                    defect = defectMatch.Groups["value"].Value.Trim();
                    continue;
                }

                var productMatch = Regex.Match(lines[nextIndex], @"(?i)^produto\s*:\s*(?<value>.+)$");
                if (productMatch.Success)
                {
                    product = productMatch.Groups["value"].Value.Trim();
                }
            }

            results.Add(new OllamaRmaExtractionDto(
                serial,
                cnpj,
                string.IsNullOrWhiteSpace(defect) ? null : defect,
                string.IsNullOrWhiteSpace(product) ? null : product,
                null,
                hasEvidence,
                hasTests,
                true,
                !string.IsNullOrWhiteSpace(cnpj),
                !string.IsNullOrWhiteSpace(defect)));
        }

        if (results.Count > 0)
        {
            return results
                .GroupBy(extraction => extraction.Serial, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        foreach (Match match in Regex.Matches(
                     emailContent,
                     @"(?imsx)
                     (?:^|\n)\s*(?:s[ée]rie|ns|n\s*/\s*s|s\s*/\s*n)\s*:\s*(?<serial>[a-z0-9][a-z0-9./_-]{2,})\s*
                     (?:\r?\n)+\s*defeito\s*:\s*(?<defect>[^\r\n]+)
                     (?:\r?\n)+\s*produto\s*:\s*(?<product>[^\r\n]+)?"))
        {
            var serial = match.Groups["serial"].Value.Trim();
            if (!IsPlausibleSerial(serial))
            {
                continue;
            }

            var defect = match.Groups["defect"].Value.Trim();
            var product = match.Groups["product"].Value.Trim();
            results.Add(new OllamaRmaExtractionDto(
                serial,
                cnpj,
                string.IsNullOrWhiteSpace(defect) ? null : defect,
                string.IsNullOrWhiteSpace(product) ? null : product,
                null,
                hasEvidence,
                hasTests,
                true,
                !string.IsNullOrWhiteSpace(cnpj),
                !string.IsNullOrWhiteSpace(defect)));
        }

        if (results.Count > 0)
        {
            return results;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            if (!IsPlausibleSerial(lines[index]))
            {
                continue;
            }

            var defect = index + 1 < lines.Count ? lines[index + 1] : null;
            var product = index + 2 < lines.Count ? lines[index + 2] : null;

            if (string.Equals(defect, "GARANTIA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(defect, "CONSERTO", StringComparison.OrdinalIgnoreCase))
            {
                defect = index + 2 < lines.Count ? lines[index + 2] : null;
                product = index + 3 < lines.Count ? lines[index + 3] : null;
            }

            results.Add(new OllamaRmaExtractionDto(
                lines[index],
                cnpj,
                defect,
                product,
                null,
                hasEvidence,
                hasTests,
                true,
                !string.IsNullOrWhiteSpace(cnpj),
                !string.IsNullOrWhiteSpace(defect)));
        }

        return results
            .GroupBy(extraction => extraction.Serial, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool ContainsEvidence(string emailContent)
    {
        return ContainsAnyNormalized(emailContent, ["anexo", "anexos", "video", "videos", "foto", "fotos", "evidencia", "evidencias"]);
    }

    private static bool ContainsTests(string emailContent)
    {
        return ContainsAnyNormalized(emailContent, ["teste", "testes", "atualizacao", "atualização", "reset", "recovery", "firmware", "persistiu"]);
    }

    private static bool ContainsAnyNormalized(string source, IReadOnlyCollection<string> values)
    {
        var normalizedSource = NormalizeText(source);
        return values.Any(value => normalizedSource.Contains(NormalizeText(value), StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsNormalized(string source, string value)
    {
        return NormalizeText(source).Contains(NormalizeText(value), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("options")] OllamaRequestOptions Options);

    private sealed record OllamaRequestOptions(
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("thinking")] string? Thinking,
        [property: JsonPropertyName("error")] string? Error);
}
