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

    private readonly IOllamaService _ollamaService;
    private readonly ICnpjValidator _cnpjValidator;
    private readonly ISerialValidationService _serialValidationService;
    private readonly IInvoicePdfService _invoicePdfService;
    private readonly IEmailResponseService _emailResponseService;
    private readonly IEmailBodyCleaner _emailBodyCleaner;
    private readonly IRmaTechnicalClassifier _technicalClassifier;
    private readonly InvoiceOptions _invoiceOptions;
    private readonly ILogger<RmaProcessorService> _logger;

    public RmaProcessorService(
        IOllamaService ollamaService,
        ICnpjValidator cnpjValidator,
        ISerialValidationService serialValidationService,
        IInvoicePdfService invoicePdfService,
        IEmailResponseService emailResponseService,
        IEmailBodyCleaner emailBodyCleaner,
        IRmaTechnicalClassifier technicalClassifier,
        IOptions<InvoiceOptions> invoiceOptions,
        ILogger<RmaProcessorService> logger)
    {
        _ollamaService = ollamaService;
        _cnpjValidator = cnpjValidator;
        _serialValidationService = serialValidationService;
        _invoicePdfService = invoicePdfService;
        _emailResponseService = emailResponseService;
        _emailBodyCleaner = emailBodyCleaner;
        _technicalClassifier = technicalClassifier;
        _invoiceOptions = invoiceOptions.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(EmailMessageDto message, CancellationToken cancellationToken)
    {
        var response = await AnalyzeAsync(message, cancellationToken);
        await _emailResponseService.ReplyProcessingResultsAsync(message, response.Results, cancellationToken);
    }

    public async Task<RmaAssistantResponseDto> GenerateFromSerialAsync(
        string? serial,
        IReadOnlyCollection<string>? serials,
        CancellationToken cancellationToken)
    {
        var normalizedSerials = NormalizeSerials(serial, serials);
        var results = new List<RmaProcessingResultDto>();

        foreach (var normalizedSerial in normalizedSerials)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extraction = new OllamaRmaExtractionDto(
                normalizedSerial,
                null,
                null,
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
                null,
                cancellationToken));
        }

        return _emailResponseService.BuildProcessingResponse(results);
    }

    public async Task<RmaAssistantResponseDto> AnalyzeAsync(EmailMessageDto message, CancellationToken cancellationToken)
    {
        PrintEmail(message);

        var currentMessageBody = _emailBodyCleaner.ExtractCurrentMessage(message.Body);
        if (!string.Equals(currentMessageBody, message.Body, StringComparison.Ordinal))
        {
            _logger.LogInformation("Historico removido do email {MessageId} antes da extracao.", message.Id);
        }

        if (string.IsNullOrWhiteSpace(currentMessageBody))
        {
            _logger.LogInformation("Email {MessageId} ignorado porque o corpo atual ficou vazio apos limpeza.", message.Id);
            return new RmaAssistantResponseDto(
                "IGNORADO",
                false,
                "O corpo atual do e-mail ficou vazio após a limpeza do histórico.",
                []);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var extractions = await _ollamaService.ExtractRmaDataAsync(currentMessageBody, cancellationToken);
        _logger.LogInformation(
            "Extracao IA concluida em {ElapsedMs}ms para o email {MessageId}.",
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            message.Id);
        var extractionJson = JsonSerializer.Serialize(extractions, JsonOptions);

        _logger.LogInformation(
            "Dados extraidos pela IA para o email {MessageId}: {ExtractionJson}",
            message.Id,
            extractionJson);

        Console.WriteLine("---------- EXTRACAO IA ----------");
        Console.WriteLine(extractionJson);
        Console.WriteLine("---------------------------------");

        var results = new List<RmaProcessingResultDto>();
        foreach (var extraction in extractions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessExtractionAsync(message.Id, extraction, currentMessageBody, cancellationToken));
        }

        _logger.LogInformation(
            "Analise completa concluida em {ElapsedMs}ms para o email {MessageId}.",
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            message.Id);

        return _emailResponseService.BuildProcessingResponse(results);
    }

    private async Task<RmaProcessingResultDto> ProcessExtractionAsync(
        string messageId,
        OllamaRmaExtractionDto extraction,
        string currentMessageBody,
        CancellationToken cancellationToken)
    {
        var missingFields = GetMissingFields(extraction);
        if (missingFields.Count > 0)
        {
            var reason = $"Dados obrigatorios ausentes: {string.Join(", ", missingFields)}";
            LogNotEligible(messageId, reason);
            return new RmaProcessingResultDto(
                extraction,
                "DADOS_AUSENTES",
                reason,
                missingFields,
                null,
                null,
                null,
                false,
                null);
        }

        if (!_cnpjValidator.IsValid(extraction.Cnpj))
        {
            const string reason = "CNPJ em formato invalido.";
            LogNotEligible(messageId, reason);
            return new RmaProcessingResultDto(
                extraction,
                "CNPJ_INVALIDO",
                reason,
                ["CNPJ válido"],
                null,
                null,
                null,
                false,
                null);
        }

        return await BuildEligibleResultFromSerialAsync(messageId, extraction, currentMessageBody, cancellationToken);
    }

    private async Task<RmaProcessingResultDto> BuildEligibleResultFromSerialAsync(
        string messageId,
        OllamaRmaExtractionDto extraction,
        string? currentMessageBody,
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

        var technicalClassification = currentMessageBody is null
            ? new RmaTechnicalClassificationDto(
                "APTO_PARA_ORIENTACAO_NF",
                "Fluxo manual por serial sem validacao tecnica.",
                [])
            : _technicalClassifier.Classify(extraction, currentMessageBody);
        if (currentMessageBody is not null && technicalClassification.Status != "APTO_PARA_ORIENTACAO_NF")
        {
            LogNotEligible(messageId, technicalClassification.Reason);
            return new RmaProcessingResultDto(
                extraction,
                technicalClassification.Status,
                technicalClassification.Reason,
                [],
                technicalClassification,
                serialValidation,
                null,
                false,
                null);
        }

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
            "Email {MessageId} apto para RMA. Em garantia: {IsUnderWarranty} | Validade garantia: {WarrantyUntil} | Dados UNO: {SerialValidationJson} | Dados NF: {InvoiceJson}",
            messageId,
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

    private static IReadOnlyCollection<string> GetMissingFields(OllamaRmaExtractionDto extraction)
    {
        var missingFields = new List<string>();

        if (!extraction.PossuiSerial || string.IsNullOrWhiteSpace(extraction.Serial))
        {
            missingFields.Add("serial");
        }

        if (!extraction.PossuiCnpj || string.IsNullOrWhiteSpace(extraction.Cnpj))
        {
            missingFields.Add("cnpj");
        }

        if (!extraction.PossuiDefeito || string.IsNullOrWhiteSpace(extraction.Defeito))
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
        _logger.LogInformation("Email {MessageId} nao apto para RMA. Motivo: {Reason}", messageId, reason);

        Console.WriteLine("---------- VALIDACAO RMA ----------");
        Console.WriteLine("Status: NAO APTO");
        Console.WriteLine($"Motivo: {reason}");
        Console.WriteLine("-----------------------------------");
    }

    private static void PrintEmail(EmailMessageDto message)
    {
        Console.WriteLine("---------- EMAIL RMA ----------");
        Console.WriteLine($"Id: {message.Id}");
        Console.WriteLine($"De: {message.From}");
        Console.WriteLine($"Assunto: {message.Subject}");
        Console.WriteLine($"Recebido em: {message.ReceivedAt}");
        Console.WriteLine("Conteudo:");
        Console.WriteLine(message.Body);
        Console.WriteLine("-------------------------------");
    }
}
