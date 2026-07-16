using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;
using RmaWorker.Services;
using RmaWorker.Validators;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SerialValidationOptions>(
    builder.Configuration.GetSection(SerialValidationOptions.SectionName));
builder.Services.Configure<InvoiceOptions>(
    builder.Configuration.GetSection(InvoiceOptions.SectionName));
builder.Services.Configure<UnoErpOptions>(
    builder.Configuration.GetSection(UnoErpOptions.SectionName));
builder.Services.Configure<UnoInvoiceOptions>(
    builder.Configuration.GetSection(UnoInvoiceOptions.SectionName));
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
builder.Services.AddSingleton<IUnoInvoiceLookupService, UnoInvoiceLookupService>();
builder.Services.AddSingleton<IUnoOccurrenceService, UnoOccurrenceService>();

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

app.MapPost("/api/rma/invoice/find", async (
    InvoiceLookupRequestDto request,
    IUnoInvoiceLookupService invoiceLookupService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await invoiceLookupService.FindAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha inesperada ao buscar NF no UNO.");
        return Results.Json(
            new InvoiceLookupResponseDto(
                "failed",
                $"Falha ao buscar NF no UNO: {ex.Message}",
                request.InvoiceNumber,
                null,
                null,
                null),
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/rma/occurrence/open", async (
    OccurrenceOpenRequestDto request,
    IUnoOccurrenceService occurrenceService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await occurrenceService.OpenAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha inesperada ao abrir O.C no UNO.");
        return Results.Json(
            new OccurrenceOpenResponseDto(
                "failed",
                $"Falha ao abrir O.C no UNO: {ex.Message}",
                null,
                null,
                null,
                request.CategoryCode,
                request.Title),
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/rma/spoc/idblock-next/resolve", async (
    SpocIdBlockNextRequestDto request,
    ISpocSerialResolverService spocSerialResolverService,
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

        return Results.Ok(new SpocIdBlockNextResponseDto(
                "SPOC_SERIAL_ENCONTRADO",
                "Serial da IDBlock Next encontrado no SPOC.",
                resolution.InputSerial,
                null,
                resolution.NextSerial,
                false,
                $"Serial da IDBlock Next encontrado no SPOC: {resolution.NextSerial}"));
    }
    catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or HttpRequestException)
    {
        logger.LogError(ex, "Falha ao consultar serial IDBlock Next no SPOC.");
        return Results.Json(
            new SpocIdBlockNextResponseDto(
                "SPOC_ERRO",
                $"Falha ao consultar serial IDBlock Next no SPOC: {ex.Message}",
                request.Serial,
                null,
                null,
                false,
                $"Falha ao consultar serial IDBlock Next no SPOC: {ex.Message}"),
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

