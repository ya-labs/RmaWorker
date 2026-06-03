using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;
using RmaWorker.Services;
using RmaWorker.Validators;
using RmaWorker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GmailOptions>(
    builder.Configuration.GetSection(GmailOptions.SectionName));
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<SerialValidationOptions>(
    builder.Configuration.GetSection(SerialValidationOptions.SectionName));
builder.Services.Configure<InvoiceOptions>(
    builder.Configuration.GetSection(InvoiceOptions.SectionName));

builder.Services.AddCors(options =>
{
    options.AddPolicy("RmaChatbot", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<IGmailService, GmailService>();
builder.Services.AddSingleton<IOllamaService>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>()
        .Value;
    var logger = serviceProvider.GetRequiredService<ILogger<OllamaService>>();
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(options.BaseUrl)
    };

    return new OllamaService(
        httpClient,
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>(),
        logger);
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
builder.Services.AddSingleton<IEmailBodyCleaner, EmailBodyCleaner>();
builder.Services.AddSingleton<IRmaTechnicalClassifier, RmaTechnicalClassifier>();
builder.Services.AddSingleton<IRmaProcessorService, RmaProcessorService>();

if (builder.Configuration.GetValue("Worker:EnableEmailWorker", false))
{
    builder.Services.AddHostedService<RmaEmailWorker>();
}

var app = builder.Build();

app.UseCors("RmaChatbot");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/rma/analyze", async (
    RmaAssistantRequestDto request,
    IRmaProcessorService processor,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.EmailBody))
    {
        return Results.BadRequest(new { error = "Informe o corpo do e-mail para analise." });
    }

    var message = new EmailMessageDto(
        Id: $"manual-{Guid.NewGuid():N}",
        ThreadId: null,
        MessageIdHeader: null,
        From: request.From,
        Subject: request.Subject ?? "Analise manual RMA",
        ReceivedAt: DateTimeOffset.Now,
        Body: request.EmailBody);

    var response = await processor.AnalyzeAsync(message, cancellationToken);
    return Results.Ok(response);
});

app.Run();
