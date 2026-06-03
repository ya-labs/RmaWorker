using Microsoft.Extensions.Options;
using RmaWorker.Configuration;
using RmaWorker.Interfaces;

namespace RmaWorker.Workers;

public sealed class RmaEmailWorker : BackgroundService
{
    private readonly IGmailService _gmailService;
    private readonly IRmaProcessorService _rmaProcessorService;
    private readonly WorkerOptions _options;
    private readonly ILogger<RmaEmailWorker> _logger;

    public RmaEmailWorker(
        IGmailService gmailService,
        IRmaProcessorService rmaProcessorService,
        IOptions<WorkerOptions> options,
        ILogger<RmaEmailWorker> logger)
    {
        _gmailService = gmailService;
        _rmaProcessorService = rmaProcessorService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RMA Email Worker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var unreadMessages = await _gmailService.GetUnreadMessagesAsync(stoppingToken);

                foreach (var message in unreadMessages)
                {
                    _logger.LogInformation(
                        "Email nao lido recebido. Id: {MessageId} | De: {From} | Assunto: {Subject} | Data: {ReceivedAt}",
                        message.Id,
                        message.From,
                        message.Subject,
                        message.ReceivedAt);

                    await _rmaProcessorService.ProcessAsync(message, stoppingToken);

                    await _gmailService.MarkAsProcessedAsync(message.Id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar e-mails nao lidos.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("RMA Email Worker finalizado.");
    }
}
