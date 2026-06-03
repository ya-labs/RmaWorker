using System.Globalization;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Options;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class GmailService : IGmailService
{
    private static readonly string[] Scopes =
    [
        Google.Apis.Gmail.v1.GmailService.Scope.GmailModify,
        Google.Apis.Gmail.v1.GmailService.Scope.GmailSend
    ];

    private readonly GmailOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GmailService> _logger;
    private Google.Apis.Gmail.v1.GmailService? _gmailClient;
    private string? _processedLabelId;

    public GmailService(
        IOptions<GmailOptions> options,
        IHostEnvironment environment,
        ILogger<GmailService> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<EmailMessageDto>> GetUnreadMessagesAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);

        var listRequest = client.Users.Messages.List(_options.UserId);
        listRequest.LabelIds = "UNREAD";
        listRequest.MaxResults = _options.MaxUnreadMessagesPerCycle;
        listRequest.Q = _options.SearchQuery;

        var listResponse = await listRequest.ExecuteAsync(cancellationToken);
        if (listResponse.Messages is null || listResponse.Messages.Count == 0)
        {
            return [];
        }

        var messages = new List<EmailMessageDto>();

        foreach (var message in listResponse.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(message.Id))
            {
                continue;
            }

            var getRequest = client.Users.Messages.Get(_options.UserId, message.Id);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

            var fullMessage = await getRequest.ExecuteAsync(cancellationToken);
            messages.Add(MapMessage(fullMessage));
        }

        return messages;
    }

    public async Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        var processedLabelId = await GetOrCreateProcessedLabelIdAsync(client, cancellationToken);

        var getRequest = client.Users.Messages.Get(_options.UserId, messageId);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        var message = await getRequest.ExecuteAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(message.ThreadId))
        {
            var threadRequest = new ModifyThreadRequest
            {
                AddLabelIds = [processedLabelId],
                RemoveLabelIds = ["UNREAD"]
            };

            await client.Users.Threads.Modify(threadRequest, _options.UserId, message.ThreadId).ExecuteAsync(cancellationToken);

            _logger.LogInformation(
                "Label {LabelName} aplicada e UNREAD removido da thread {ThreadId}.",
                _options.ProcessedLabelName,
                message.ThreadId);

            return;
        }

        var request = new ModifyMessageRequest
        {
            AddLabelIds = [processedLabelId],
            RemoveLabelIds = ["UNREAD"]
        };

        await client.Users.Messages.Modify(request, _options.UserId, messageId).ExecuteAsync(cancellationToken);

        _logger.LogInformation(
            "Label {LabelName} aplicada e UNREAD removido do email {MessageId}.",
            _options.ProcessedLabelName,
            messageId);
    }

    public async Task SendReplyAsync(
        EmailMessageDto originalMessage,
        string body,
        CancellationToken cancellationToken)
    {
        await SendReplyAsync(originalMessage, body, isHtml: false, cancellationToken);
    }

    public async Task SendHtmlReplyAsync(
        EmailMessageDto originalMessage,
        string body,
        CancellationToken cancellationToken)
    {
        await SendReplyAsync(originalMessage, body, isHtml: true, cancellationToken);
    }

    private async Task SendReplyAsync(
        EmailMessageDto originalMessage,
        string body,
        bool isHtml,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        var subject = BuildReplySubject(originalMessage.Subject);
        var rawMessage = BuildRawReply(originalMessage, subject, body, isHtml);

        var message = new Message
        {
            Raw = Base64UrlEncode(rawMessage),
            ThreadId = originalMessage.ThreadId
        };

        await client.Users.Messages.Send(message, _options.UserId).ExecuteAsync(cancellationToken);

        _logger.LogInformation(
            "Resposta enviada para o email {MessageId}. Assunto: {Subject}",
            originalMessage.Id,
            subject);
    }

    private async Task<Google.Apis.Gmail.v1.GmailService> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_gmailClient is not null)
        {
            return _gmailClient;
        }

        var credentialsPath = ResolvePath(_options.CredentialsPath);
        var tokenPath = ResolvePath(_options.TokenPath);

        if (!File.Exists(credentialsPath))
        {
            throw new FileNotFoundException("Arquivo de credenciais do Gmail nao encontrado.", credentialsPath);
        }

        await using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            Scopes,
            _options.UserId,
            cancellationToken,
            new FileDataStore(tokenPath, fullPath: true));

        _gmailClient = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });

        _logger.LogInformation("Cliente Gmail autenticado para o usuario {UserId}", _options.UserId);

        return _gmailClient;
    }

    private async Task<string> GetOrCreateProcessedLabelIdAsync(
        Google.Apis.Gmail.v1.GmailService client,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_processedLabelId))
        {
            return _processedLabelId;
        }

        var labelsResponse = await client.Users.Labels.List(_options.UserId).ExecuteAsync(cancellationToken);
        var existingLabel = labelsResponse.Labels?.FirstOrDefault(label =>
            string.Equals(label.Name, _options.ProcessedLabelName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existingLabel?.Id))
        {
            _processedLabelId = existingLabel.Id;
            return _processedLabelId;
        }

        var label = new Label
        {
            Name = _options.ProcessedLabelName,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show"
        };

        var createdLabel = await client.Users.Labels.Create(label, _options.UserId).ExecuteAsync(cancellationToken);
        _processedLabelId = createdLabel.Id;

        _logger.LogInformation("Label Gmail criada: {LabelName}", _options.ProcessedLabelName);

        return _processedLabelId;
    }

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, path));
    }

    private static EmailMessageDto MapMessage(Message message)
    {
        var headers = message.Payload?.Headers ?? [];

        var from = GetHeader(headers, "From");
        var subject = GetHeader(headers, "Subject");
        var messageIdHeader = GetHeader(headers, "Message-ID");
        var date = ParseDate(GetHeader(headers, "Date"));
        var body = ExtractBody(message.Payload);

        return new EmailMessageDto(
            message.Id,
            message.ThreadId,
            messageIdHeader,
            from,
            subject,
            date,
            body);
    }

    private static string? GetHeader(IList<MessagePartHeader> headers, string name)
    {
        return headers.FirstOrDefault(header =>
            string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;
    }

    private static string ExtractBody(MessagePart? payload)
    {
        if (payload is null)
        {
            return string.Empty;
        }

        if (payload.Body?.Data is not null && IsTextPart(payload.MimeType))
        {
            return DecodeBase64Url(payload.Body.Data);
        }

        if (payload.Parts is null || payload.Parts.Count == 0)
        {
            return string.Empty;
        }

        var plainTextPart = FindPart(payload.Parts, "text/plain");
        if (plainTextPart?.Body?.Data is not null)
        {
            return DecodeBase64Url(plainTextPart.Body.Data);
        }

        var htmlPart = FindPart(payload.Parts, "text/html");
        return htmlPart?.Body?.Data is null
            ? string.Empty
            : DecodeBase64Url(htmlPart.Body.Data);
    }

    private static MessagePart? FindPart(IEnumerable<MessagePart> parts, string mimeType)
    {
        foreach (var part in parts)
        {
            if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }

            if (part.Parts is null)
            {
                continue;
            }

            var child = FindPart(part.Parts, mimeType);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsTextPart(string? mimeType)
    {
        return string.Equals(mimeType, "text/plain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mimeType, "text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;

        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + 4 - padding, '=');
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    private static string BuildReplySubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return "Re: Solicitação de RMA";
        }

        return subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : $"Re: {subject}";
    }

    private static string BuildRawReply(EmailMessageDto originalMessage, string subject, string body, bool isHtml)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"To: {originalMessage.From}");
        builder.AppendLine($"Subject: {subject}");

        if (!string.IsNullOrWhiteSpace(originalMessage.MessageIdHeader))
        {
            builder.AppendLine($"In-Reply-To: {originalMessage.MessageIdHeader}");
            builder.AppendLine($"References: {originalMessage.MessageIdHeader}");
        }

        builder.AppendLine("MIME-Version: 1.0");
        builder.AppendLine($"Content-Type: {(isHtml ? "text/html" : "text/plain")}; charset=\"UTF-8\"");
        builder.AppendLine();
        builder.AppendLine(body);

        return builder.ToString();
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
