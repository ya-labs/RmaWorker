using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class SerialValidationService : ISerialValidationService
{
    private readonly HttpClient _httpClient;
    private readonly SerialValidationOptions _options;
    private readonly ILogger<SerialValidationService> _logger;

    public SerialValidationService(
        HttpClient httpClient,
        IOptions<SerialValidationOptions> options,
        ILogger<SerialValidationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SerialValidationResultDto> ValidateAsync(string serial, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial nao pode ser vazio.", nameof(serial));
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Configure SerialValidation__BaseUrl para consultar o sistema interno.");
        }

        var normalizedSerial = serial.Trim();
        var result = await QueryUnoAsync(normalizedSerial, cancellationToken);

        _logger.LogInformation(
            "Validacao de serial concluida. Serial: {Serial} | Existe: {Exists} | Pedido UNO: {UnoOrder}",
            result.Serial,
            result.Exists,
            result.UnoOrder);

        return result;
    }

    private async Task<SerialValidationResultDto> QueryUnoAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(_options.BaseUrl, serial);
        _logger.LogInformation("Consultando serial no UNO. Url: {RequestUri}", requestUri);

        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = ParseUnoResponse(serial, content);

        if (!result.Exists)
        {
            _logger.LogWarning(
                "UNO nao foi interpretado como serial existente. HTTP {StatusCode} | Serial {Serial} | Corpo inicial: {ResponsePreview}",
                (int)response.StatusCode,
                serial,
                Preview(content));
        }

        return result;
    }

    public static string BuildRequestUri(string baseUrlValue, string serial)
    {
        var baseUrl = Regex.Replace(baseUrlValue, @"\s+", string.Empty);
        if (baseUrl.EndsWith("consultar.sh/", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^1];
        }

        if (baseUrl.EndsWith("?", StringComparison.Ordinal))
        {
            return $"{baseUrl}{EscapeSerialQuery(serial)}";
        }

        var querySeparator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{querySeparator}{EscapeSerialQuery(serial)}";
    }

    private static string EscapeSerialQuery(string serial)
    {
        return Uri.EscapeDataString(serial).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }

    public static SerialValidationResultDto ParseUnoResponse(string requestedSerial, string content)
    {
        var normalizedContent = NormalizeUnoContent(content);
        if (IsSerialNotFoundContent(normalizedContent))
        {
            if (!ContainsFoundSerial(normalizedContent, requestedSerial))
            {
                return Empty(requestedSerial);
            }
        }

        var output = ExtractFirstOutput(normalizedContent);
        if (string.IsNullOrWhiteSpace(output))
        {
            output = normalizedContent;
        }

        var serial = GetField(output, "Serial");
        if (string.IsNullOrWhiteSpace(serial))
        {
            if (normalizedContent.Contains(requestedSerial, StringComparison.OrdinalIgnoreCase))
            {
                serial = requestedSerial;
            }
            else
            {
                Console.WriteLine("---------- RESPOSTA UNO NAO INTERPRETADA ----------");
                Console.WriteLine(Preview(normalizedContent, 2000));
                Console.WriteLine("---------------------------------------------------");
                return Empty(requestedSerial);
            }
        }

        var issuedAt = TryParseDate(GetField(
            output,
            "Data de emissao da nota",
            "Data de emissão da nota",
            "Data de emissÃ£o da nota"));

        return new SerialValidationResultDto(
            serial,
            Exists: true,
            ProductCode: GetField(output, "Codigo Produto", "Código Produto", "CÃ³digo Produto"),
            ProductDescription: GetField(output, "Descricao Produto", "Descrição Produto", "DescriÃ§Ã£o Produto"),
            UnoOrder: GetField(output, "Pedido UNO"),
            InvoiceLink: GetField(output, "Link Nota Fiscal"),
            CustomerName: GetField(output, "Razao Social", "Razão Social", "RazÃ£o Social"),
            Cnpj: GetField(output, "CNPJ"),
            InvoiceIssuedAt: issuedAt,
            City: GetField(output, "Cidade"),
            ZipCode: GetField(output, "CEP"));
    }

    private static string NormalizeUnoContent(string content)
    {
        var normalized = WebUtility.HtmlDecode(content);
        normalized = Regex.Replace(normalized, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"</\s*(div|p|tr|td|li|span|label)\s*>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "<.*?>", string.Empty, RegexOptions.Singleline);
        normalized = normalized.Replace("&nbsp;", " ");
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n\s+", "\n");
        return normalized.Trim();
    }

    private static bool IsSerialNotFoundContent(string content)
    {
        return content.Contains("Encontrados 0 resultados", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Serial nao encontrado", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Serial não encontrado", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Serial nÃ£o encontrado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsFoundSerial(string content, string serial)
    {
        return content.Contains("Encontrados 1 resultados", StringComparison.OrdinalIgnoreCase)
            && content.Contains(serial, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractFirstOutput(string content)
    {
        var start = content.IndexOf("Saída 1:", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            start = content.IndexOf("SaÃ­da 1:", StringComparison.OrdinalIgnoreCase);
        }

        if (start < 0)
        {
            start = content.IndexOf("Saida 1:", StringComparison.OrdinalIgnoreCase);
        }

        if (start < 0)
        {
            return null;
        }

        var next = content.IndexOf("Saída 2:", start + 1, StringComparison.OrdinalIgnoreCase);
        if (next < 0)
        {
            next = content.IndexOf("SaÃ­da 2:", start + 1, StringComparison.OrdinalIgnoreCase);
        }

        if (next < 0)
        {
            next = content.IndexOf("Saida 2:", start + 1, StringComparison.OrdinalIgnoreCase);
        }

        return next < 0
            ? content[start..]
            : content[start..next];
    }

    private static string? GetField(string content, params string[] fieldNames)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            foreach (var fieldName in fieldNames)
            {
                var prefix = $"{fieldName}:";
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[prefix.Length..].Trim();
                }
            }
        }

        return null;
    }

    private static DateOnly? TryParseDate(string? value)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static string Preview(string content, int length = 500)
    {
        return content.Length > length ? content[..length] : content;
    }

    private static SerialValidationResultDto Empty(string serial)
    {
        return new SerialValidationResultDto(
            serial,
            Exists: false,
            ProductCode: null,
            ProductDescription: null,
            UnoOrder: null,
            InvoiceLink: null,
            CustomerName: null,
            Cnpj: null,
            InvoiceIssuedAt: null,
            City: null,
            ZipCode: null);
    }
}
