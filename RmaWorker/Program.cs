using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;
using RmaWorker.Services;
using RmaWorker.Validators;
using Microsoft.Playwright;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SerialValidationOptions>(
    builder.Configuration.GetSection(SerialValidationOptions.SectionName));
builder.Services.Configure<InvoiceOptions>(
    builder.Configuration.GetSection(InvoiceOptions.SectionName));
builder.Services.Configure<UnoErpOptions>(
    builder.Configuration.GetSection(UnoErpOptions.SectionName));
builder.Services.Configure<SpocOptions>(
    builder.Configuration.GetSection(SpocOptions.SectionName));

builder.Services.AddCors(options =>
{
    options.AddPolicy("RmaChatbot", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? ["*"];

        if (allowedOrigins.Any(origin => origin == "*"))
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.SetIsOriginAllowed(origin =>
                IsAllowedOrigin(origin, allowedOrigins));
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<ISerialValidationService>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SerialValidationOptions>>()
        .Value;
    var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
    };

    return new SerialValidationService(
        httpClient,
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SerialValidationOptions>>(),
        serviceProvider.GetRequiredService<ILogger<SerialValidationService>>());
});
builder.Services.AddSingleton<ISpocSerialResolverService, SpocSerialResolverService>();
builder.Services.AddSingleton<IEmailResponseService, EmailResponseService>();
builder.Services.AddSingleton<IInvoicePdfService>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<InvoiceOptions>>()
        .Value;
    var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
    };

    return new InvoicePdfService(
        httpClient,
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<InvoiceOptions>>(),
        serviceProvider.GetRequiredService<ILogger<InvoicePdfService>>());
});
builder.Services.AddSingleton<ICnpjValidator, CnpjValidator>();
builder.Services.AddSingleton<IRmaProcessorService, RmaProcessorService>();
builder.Services.AddSingleton<IUnoServiceOrderService>(serviceProvider =>
    new UnoServiceOrderService(
        serviceProvider.GetRequiredService<ISerialValidationService>(),
        serviceProvider.GetRequiredService<ICnpjValidator>(),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<UnoErpOptions>>(),
        serviceProvider.GetRequiredService<ILogger<UnoServiceOrderService>>()));

var app = builder.Build();

app.UseCors("RmaChatbot");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/rma/generate-by-serial", async (
    RmaSerialRequestDto request,
    IRmaProcessorService processor,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Serial)
        && (request.Serials is null || request.Serials.Count == 0))
    {
        return Results.BadRequest(new { error = "Informe pelo menos um numero de serie para gerar o e-mail." });
    }

    var response = await processor.GenerateFromSerialAsync(request, cancellationToken);
    return Results.Ok(response);
});

app.MapPost("/api/rma/service-order/open", async (
    RmaServiceOrderRequestDto request,
    IUnoServiceOrderService serviceOrderService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await serviceOrderService.OpenAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao abrir O.S no UNO.");
        return Results.Json(
            new
            {
                status = "failed",
                message = $"Falha ao abrir O.S no UNO: {ex.Message}"
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/rma/spoc/idblock-next/resolve", async (
    SpocIdBlockNextRequestDto request,
    ISpocSerialResolverService spocSerialResolverService,
    ISerialValidationService serialValidationService,
    IEmailResponseService emailResponseService,
    IOptions<SerialValidationOptions> serialValidationOptions,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Serial))
    {
        return Results.BadRequest(new { error = "Informe o numero de serie do IDFace para consultar no SPOC." });
    }

    try
    {
        var resolution = await spocSerialResolverService.TryResolveIdBlockNextSerialAsync(
            request.Serial,
            cancellationToken);

        if (resolution is null)
        {
            return Results.Ok(new SpocIdBlockNextResponseDto(
                "SPOC_SERIAL_NAO_ENCONTRADO",
                "Nao foi encontrado IDBlock Next relacionado ao serial informado no SPOC.",
                request.Serial,
                null,
                null,
                false,
                "Nao foi encontrado IDBlock Next relacionado ao serial informado no SPOC."));
        }

        SerialValidationResultDto serialValidation;
        try
        {
            serialValidation = await serialValidationService.ValidateAsync(
                resolution.NextSerial,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                ex,
                "Falha ao consultar serial IDBlock Next {NextSerial} no UNO apos resolver no SPOC.",
                resolution.NextSerial);
            return Results.Ok(new SpocIdBlockNextResponseDto(
                "UNO_INDISPONIVEL",
                $"A IDBlock Next foi encontrada no SPOC, mas a consulta ao UNO falhou: {ex.Message}",
                resolution.InputSerial,
                null,
                resolution.NextSerial,
                false,
                $"Serial da IDBlock Next encontrado no SPOC: {resolution.NextSerial}\n\nNao foi possivel consultar esse serial no UNO: {ex.Message}"));
        }

        if (!serialValidation.Exists)
        {
            serialValidation = await ReconsultNextSerialDirectAsync(
                resolution.NextSerial,
                serialValidationOptions.Value,
                logger,
                cancellationToken);

            if (!serialValidation.Exists)
            {
                return Results.Ok(new SpocIdBlockNextResponseDto(
                    "SERIAL_NAO_ENCONTRADO",
                    "A IDBlock Next foi encontrada no SPOC, mas o serial nao foi encontrado no UNO.",
                    resolution.InputSerial,
                    null,
                    resolution.NextSerial,
                    false,
                    $"Serial da IDBlock Next encontrado no SPOC: {resolution.NextSerial}\n\nEsse serial nao foi encontrado no UNO para gerar o template de manutencao."));
            }
        }

        var warrantyUntil = serialValidation.InvoiceIssuedAt?.AddYears(1);
        var isUnderWarranty = warrantyUntil.HasValue
            && warrantyUntil.Value >= DateOnly.FromDateTime(DateTime.Today);
        var result = new RmaProcessingResultDto(
            new RmaExtractionDto(
                serialValidation.Serial,
                serialValidation.Cnpj,
                null,
                serialValidation.ProductDescription,
                null,
                false,
                false,
                true,
                !string.IsNullOrWhiteSpace(serialValidation.Cnpj),
                false),
            "APTO",
            null,
            [],
            new RmaTechnicalClassificationDto(
                "APTO_PARA_ORIENTACAO_NF",
                "Serial IDBlock Next localizado no SPOC e validado no UNO.",
                []),
            serialValidation,
            null,
            isUnderWarranty,
            warrantyUntil);

        var template = emailResponseService.BuildProcessingResponse([result]);

        return Results.Ok(new SpocIdBlockNextResponseDto(
            "SPOC_SERIAL_ENCONTRADO",
            "Serial da IDBlock Next encontrado no SPOC e validado no UNO.",
            resolution.InputSerial,
            null,
            resolution.NextSerial,
            template.IsHtml,
            template.ResponseBody));
    }
    catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or HttpRequestException)
    {
        logger.LogError(ex, "Falha ao consultar serial IDBlock Next no SPOC/UNO.");
        return Results.Json(
            new SpocIdBlockNextResponseDto(
                "SPOC_ERRO",
                $"Falha ao consultar serial IDBlock Next no SPOC/UNO: {ex.Message}",
                request.Serial,
                null,
                null,
                false,
                $"Falha ao consultar serial IDBlock Next no SPOC/UNO: {ex.Message}"),
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();

static bool IsAllowedOrigin(string origin, IReadOnlyCollection<string> allowedOrigins)
{
    if (string.IsNullOrWhiteSpace(origin))
    {
        return false;
    }

    if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return uri.Host.EndsWith("github.io", StringComparison.OrdinalIgnoreCase)
        || origin.Equals("http://localhost:5173", StringComparison.OrdinalIgnoreCase)
        || origin.Equals("http://127.0.0.1:5173", StringComparison.OrdinalIgnoreCase);
}

static async Task<SerialValidationResultDto> ReconsultNextSerialDirectAsync(
    string serial,
    SerialValidationOptions options,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var requestUri = SerialValidationService.BuildRequestUri(options.BaseUrl, serial);
    logger.LogWarning(
        "SerialValidationService retornou falso para {Serial}. Reconsultando UNO diretamente no fluxo IDBlock Next. Url: {RequestUri}",
        serial,
        requestUri);

    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
    };

    var response = await httpClient.GetAsync(requestUri, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    logger.LogWarning(
        "Reconsulta direta UNO IDBlock Next. HTTP {StatusCode} | Corpo inicial: {ResponsePreview}",
        (int)response.StatusCode,
        content.Length > 1000 ? content[..1000] : content);

    return SerialValidationService.ParseUnoResponse(serial, content);
}
