using System.Globalization;
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

        var normalizedSerial = serial.Trim();
        var requestUri = $"{_options.BaseUrl}?{EscapeSerialQuery(normalizedSerial)}";
        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = ParseResponse(normalizedSerial, content);

        _logger.LogInformation(
            "Validacao de serial concluida. Serial: {Serial} | Existe: {Exists} | Pedido UNO: {UnoOrder}",
            result.Serial,
            result.Exists,
            result.UnoOrder);

        return result;
    }

    private static string EscapeSerialQuery(string serial)
    {
        return Uri.EscapeDataString(serial).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }

    private static SerialValidationResultDto ParseResponse(string requestedSerial, string content)
    {
        if (content.Contains("Encontrados 0 resultados", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Serial nao encontrado", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Serial não encontrado", StringComparison.OrdinalIgnoreCase))
        {
            return Empty(requestedSerial);
        }

        var firstOutput = ExtractFirstOutput(content);
        if (string.IsNullOrWhiteSpace(firstOutput))
        {
            Console.WriteLine("---------- RESPOSTA UNO NAO INTERPRETADA ----------");
            Console.WriteLine(content.Length > 2000 ? content[..2000] : content);
            Console.WriteLine("---------------------------------------------------");
            return Empty(requestedSerial);
        }

        var serial = GetField(firstOutput, "Serial") ?? requestedSerial;
        var issuedAt = TryParseDate(GetField(firstOutput, "Data de emissão da nota"));

        return new SerialValidationResultDto(
            serial,
            Exists: true,
            ProductCode: GetField(firstOutput, "Código Produto"),
            ProductDescription: GetField(firstOutput, "Descrição Produto"),
            UnoOrder: GetField(firstOutput, "Pedido UNO"),
            InvoiceLink: GetField(firstOutput, "Link Nota Fiscal"),
            CustomerName: GetField(firstOutput, "Razão Social"),
            Cnpj: GetField(firstOutput, "CNPJ"),
            InvoiceIssuedAt: issuedAt,
            City: GetField(firstOutput, "Cidade"),
            ZipCode: GetField(firstOutput, "CEP"));
    }

    private static string? ExtractFirstOutput(string content)
    {
        var start = content.IndexOf("Saída 1:", StringComparison.OrdinalIgnoreCase);
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
            next = content.IndexOf("Saida 2:", start + 1, StringComparison.OrdinalIgnoreCase);
        }

        return next < 0
            ? content[start..]
            : content[start..next];
    }

    private static string? GetField(string content, string fieldName)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var prefix = $"{fieldName}:";

            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
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
