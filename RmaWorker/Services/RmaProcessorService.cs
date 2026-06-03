using System.Text.Json;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class RmaProcessorService : IRmaProcessorService
{
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
    private readonly ILogger<RmaProcessorService> _logger;

    public RmaProcessorService(
        IOllamaService ollamaService,
        ICnpjValidator cnpjValidator,
        ISerialValidationService serialValidationService,
        IInvoicePdfService invoicePdfService,
        IEmailResponseService emailResponseService,
        IEmailBodyCleaner emailBodyCleaner,
        IRmaTechnicalClassifier technicalClassifier,
        ILogger<RmaProcessorService> logger)
    {
        _ollamaService = ollamaService;
        _cnpjValidator = cnpjValidator;
        _serialValidationService = serialValidationService;
        _invoicePdfService = invoicePdfService;
        _emailResponseService = emailResponseService;
        _emailBodyCleaner = emailBodyCleaner;
        _technicalClassifier = technicalClassifier;
        _logger = logger;
    }

    public async Task ProcessAsync(EmailMessageDto message, CancellationToken cancellationToken)
    {
        var response = await AnalyzeAsync(message, cancellationToken);
        await _emailResponseService.ReplyProcessingResultsAsync(message, response.Results, cancellationToken);
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

        var extractions = await _ollamaService.ExtractRmaDataAsync(currentMessageBody, cancellationToken);
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

        var serialValidation = await _serialValidationService.ValidateAsync(extraction.Serial!, cancellationToken);
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

        var technicalClassification = _technicalClassifier.Classify(extraction, currentMessageBody);
        if (technicalClassification.Status != "APTO_PARA_ORIENTACAO_NF")
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
        if (!string.IsNullOrWhiteSpace(serialValidation.InvoiceLink)
            && !string.IsNullOrWhiteSpace(serialValidation.ProductCode))
        {
            invoiceData = await _invoicePdfService.ExtractAsync(
                serialValidation.InvoiceLink,
                serialValidation.ProductCode,
                cancellationToken);
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
            extraction,
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
