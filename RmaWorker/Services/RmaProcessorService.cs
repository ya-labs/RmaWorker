using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class RmaProcessorService : IRmaProcessorService
{
    private static readonly Regex SerialSeparatorRegex = new(@"[\s,;]+", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ISerialValidationService _serialValidationService;
    private readonly IInvoicePdfService _invoicePdfService;
    private readonly IEmailResponseService _emailResponseService;
    private readonly IUnoServiceOrderService _unoServiceOrderService;
    private readonly InvoiceOptions _invoiceOptions;
    private readonly ILogger<RmaProcessorService> _logger;

    public RmaProcessorService(
        ISerialValidationService serialValidationService,
        IInvoicePdfService invoicePdfService,
        IEmailResponseService emailResponseService,
        IUnoServiceOrderService unoServiceOrderService,
        IOptions<InvoiceOptions> invoiceOptions,
        ILogger<RmaProcessorService> logger)
    {
        _serialValidationService = serialValidationService;
        _invoicePdfService = invoicePdfService;
        _emailResponseService = emailResponseService;
        _unoServiceOrderService = unoServiceOrderService;
        _invoiceOptions = invoiceOptions.Value;
        _logger = logger;
    }

    public async Task<RmaAssistantResponseDto> GenerateFromSerialAsync(
        RmaSerialRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalizedSerials = NormalizeSerials(request.Serial, request.Serials);
        var missingFields = GetMissingRequestFields(request, normalizedSerials);
        if (missingFields.Count > 0)
        {
            return new RmaAssistantResponseDto(
                "DADOS_AUSENTES",
                false,
                $"Informe os campos obrigatorios: {string.Join(", ", missingFields)}.",
                []);
        }

        var customerValidation = await _unoServiceOrderService.ValidateCustomerAsync(request.Cnpj, cancellationToken);
        if (!customerValidation.Exists)
        {
            return new RmaAssistantResponseDto(
                customerValidation.Status ?? "CLIENTE_NAO_ENCONTRADO",
                false,
                customerValidation.Message ?? "Nao foi possivel validar o CNPJ no UNO.",
                []);
        }

        var results = new List<RmaProcessingResultDto>();
        foreach (var normalizedSerial in normalizedSerials)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extraction = new RmaExtractionDto(
                normalizedSerial,
                customerValidation.Cnpj ?? request.Cnpj,
                request.DefectReported,
                null,
                null,
                false,
                false,
                true,
                true,
                true);

            results.Add(await BuildEligibleResultFromSerialAsync(
                $"serial-{Guid.NewGuid():N}",
                extraction,
                cancellationToken));
        }

        return _emailResponseService.BuildProcessingResponse(results);
    }

    private async Task<RmaProcessingResultDto> BuildEligibleResultFromSerialAsync(
        string messageId,
        RmaExtractionDto extraction,
        CancellationToken cancellationToken)
    {
        var serialStartedAt = DateTimeOffset.UtcNow;
        SerialValidationResultDto serialValidation;
        try
        {
            serialValidation = await _serialValidationService.ValidateAsync(extraction.Serial!, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var reason = $"Timeout ao consultar o UNO para o serial {extraction.Serial}.";
            LogNotEligible(messageId, reason);
            return new RmaProcessingResultDto(
                extraction,
                "UNO_TIMEOUT",
                reason,
                [],
                null,
                null,
                null,
                false,
                null);
        }
        catch (HttpRequestException ex)
        {
            var reason = $"Falha ao consultar o UNO para o serial {extraction.Serial}: {ex.Message}";
            LogNotEligible(messageId, reason);
            return new RmaProcessingResultDto(
                extraction,
                "UNO_INDISPONIVEL",
                reason,
                [],
                null,
                null,
                null,
                false,
                null);
        }

        _logger.LogInformation(
            "Consulta UNO concluida em {ElapsedMs}ms para serial {Serial}.",
            (DateTimeOffset.UtcNow - serialStartedAt).TotalMilliseconds,
            extraction.Serial);
        if (!serialValidation.Exists)
        {
            var reason = $"Serial nao encontrado no UNO: {extraction.Serial}";
            LogNotEligible(messageId, reason);
            return new RmaProcessingResultDto(
                extraction,
                "SERIAL_NAO_ENCONTRADO",
                reason,
                [],
                null,
                serialValidation,
                null,
                false,
                null);
        }

        var responseExtraction = extraction with
        {
            Cnpj = string.IsNullOrWhiteSpace(extraction.Cnpj) ? serialValidation.Cnpj : extraction.Cnpj,
            Produto = string.IsNullOrWhiteSpace(extraction.Produto) ? serialValidation.ProductDescription : extraction.Produto
        };

        InvoiceDataDto? invoiceData = null;
        if (_invoiceOptions.EnablePdfExtraction
            && !string.IsNullOrWhiteSpace(serialValidation.InvoiceLink)
            && !string.IsNullOrWhiteSpace(serialValidation.ProductCode))
        {
            var invoiceStartedAt = DateTimeOffset.UtcNow;
            invoiceData = await _invoicePdfService.ExtractAsync(
                serialValidation.InvoiceLink,
                serialValidation.ProductCode,
                cancellationToken);
            _logger.LogInformation(
                "Extracao PDF concluida em {ElapsedMs}ms para serial {Serial}.",
                (DateTimeOffset.UtcNow - invoiceStartedAt).TotalMilliseconds,
                extraction.Serial);
        }
        else if (!_invoiceOptions.EnablePdfExtraction)
        {
            _logger.LogInformation("Extracao de PDF desabilitada por configuracao.");
        }

        var warrantyUntil = serialValidation.InvoiceIssuedAt?.AddYears(1);
        var isUnderWarranty = warrantyUntil.HasValue
            && warrantyUntil.Value >= DateOnly.FromDateTime(DateTime.Today);

        var technicalClassification = new RmaTechnicalClassificationDto(
            "APTO_PARA_ORIENTACAO_NF",
            "Fluxo estruturado por formulario.",
            []);

        var validationResult = new RmaValidationResultDto(
            Status: "APTO",
            Reason: null,
            IsUnderWarranty: isUnderWarranty,
            WarrantyUntil: warrantyUntil,
            SerialValidation: serialValidation,
            Invoice: invoiceData);

        var serialJson = JsonSerializer.Serialize(serialValidation, JsonOptions);
        var invoiceJson = JsonSerializer.Serialize(invoiceData, JsonOptions);
        var validationJson = JsonSerializer.Serialize(validationResult, JsonOptions);
        _logger.LogInformation(
            "Item apto para RMA. Em garantia: {IsUnderWarranty} | Validade garantia: {WarrantyUntil} | Dados UNO: {SerialValidationJson} | Dados NF: {InvoiceJson}",
            isUnderWarranty,
            warrantyUntil,
            serialJson,
            invoiceJson);

        Console.WriteLine("---------- VALIDACAO RMA ----------");
        Console.WriteLine("Status: APTO");
        Console.WriteLine(validationJson);
        Console.WriteLine("-----------------------------------");

        return new RmaProcessingResultDto(
            responseExtraction,
            "APTO",
            null,
            [],
            technicalClassification,
            serialValidation,
            invoiceData,
            isUnderWarranty,
            warrantyUntil);
    }

    private static IReadOnlyCollection<string> GetMissingRequestFields(
        RmaSerialRequestDto request,
        IReadOnlyCollection<string> normalizedSerials)
    {
        var missingFields = new List<string>();

        if (normalizedSerials.Count == 0)
        {
            missingFields.Add("serial");
        }

        if (string.IsNullOrWhiteSpace(request.Cnpj))
        {
            missingFields.Add("cnpj");
        }

        if (string.IsNullOrWhiteSpace(request.DefectReported))
        {
            missingFields.Add("defeito");
        }

        return missingFields;
    }

    private static IReadOnlyCollection<string> NormalizeSerials(
        string? serial,
        IReadOnlyCollection<string>? serials)
    {
        var values = new List<string>();

        if (!string.IsNullOrWhiteSpace(serial))
        {
            values.AddRange(SplitSerialText(serial));
        }

        foreach (var item in serials ?? [])
        {
            values.AddRange(SplitSerialText(item));
        }

        return values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SplitSerialText(string value)
    {
        return SerialSeparatorRegex.Split(value);
    }

    private void LogNotEligible(string messageId, string reason)
    {
        _logger.LogInformation("Item {MessageId} nao apto para RMA. Motivo: {Reason}", messageId, reason);

        Console.WriteLine("---------- VALIDACAO RMA ----------");
        Console.WriteLine("Status: NAO APTO");
        Console.WriteLine($"Motivo: {reason}");
        Console.WriteLine("-----------------------------------");
    }
}
