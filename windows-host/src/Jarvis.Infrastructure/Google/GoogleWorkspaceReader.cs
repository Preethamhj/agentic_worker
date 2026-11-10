using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util;

namespace Jarvis.Infrastructure.Google;

/// <summary>Reads a bounded number of Calendar events and Gmail inbox headers.</summary>
public sealed class GoogleWorkspaceReader : IGoogleWorkspaceReader, IGoogleMailSearcher
{
    private const int MaximumResults = 100;

    private readonly IGoogleOAuthService _oauthService;
    private readonly GoogleApiClientFactory _clientFactory;

    /// <summary>Creates a reader that uses the account service and restricted transport.</summary>
    public GoogleWorkspaceReader(
        IGoogleOAuthService oauthService,
        GoogleApiClientFactory clientFactory)
    {
        _oauthService = oauthService ??
            throw new ArgumentNullException(nameof(oauthService));
        _clientFactory = clientFactory ??
            throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleCalendarEvent>> ListUpcomingEventsAsync(
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ValidateResultCount(maxResults);
        _clientFactory.EnsureCalendarEnabled();
        cancellationToken.ThrowIfCancellationRequested();

        var credential = await _oauthService.GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        using var service = _clientFactory.CreateService(
            initializer => new CalendarService(initializer),
            "Jarvis",
            credential);

        var request = service.Events.List("primary");
        request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        request.MaxResults = maxResults;
        var response = await request.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return (response.Items ?? [])
            .Select(item => new GoogleCalendarEvent(
                string.IsNullOrWhiteSpace(item.Summary) ? "(No title)" : item.Summary,
                item.Start?.DateTimeRaw ?? item.Start?.Date ?? string.Empty,
                item.Location,
                item.HtmlLink))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleGmailMessage>> ListInboxMessagesAsync(
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ValidateResultCount(maxResults);
        _clientFactory.EnsureGmailEnabled();
        cancellationToken.ThrowIfCancellationRequested();

        var credential = await _oauthService.GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        using var service = _clientFactory.CreateService(
            initializer => new GmailService(initializer),
            "Jarvis",
            credential);

        var listRequest = service.Users.Messages.List("me");
        listRequest.LabelIds = new Repeatable<string>(["INBOX"]);
        listRequest.MaxResults = maxResults;
        var list = await listRequest.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        var messages = new List<GoogleGmailMessage>();
        foreach (var stub in list.Messages ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(stub.Id))
                continue;

            var getRequest = service.Users.Messages.Get("me", stub.Id);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getRequest.MetadataHeaders = new Repeatable<string>(
                ["From", "Subject", "Date"]);
            var message = await getRequest.ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            messages.Add(new GoogleGmailMessage(
                stub.Id,
                GetHeader(message, "Subject") ?? "(No subject)",
                GetHeader(message, "From") ?? string.Empty,
                GetHeader(message, "Date") ?? string.Empty,
                message.Snippet ?? string.Empty));
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleGmailMessage>> SearchMailAsync(
        string query,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateResultCount(maxResults);
        _clientFactory.EnsureGmailEnabled();
        cancellationToken.ThrowIfCancellationRequested();

        var credential = await _oauthService.GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        using var service = _clientFactory.CreateService(
            initializer => new GmailService(initializer),
            "Jarvis",
            credential);

        var listRequest = service.Users.Messages.List("me");
        listRequest.Q = string.IsNullOrWhiteSpace(query)
            ? "-in:spam"
            : $"{query} -in:spam";
        listRequest.MaxResults = maxResults;
        var list = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        var mails = new List<GoogleGmailMessage>();
        foreach (var stub in list.Messages ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(stub.Id))
                continue;

            var getRequest = service.Users.Messages.Get("me", stub.Id);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getRequest.MetadataHeaders = new Repeatable<string>(["From", "Subject", "Date"]);
            var message = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            mails.Add(new GoogleGmailMessage(
                stub.Id,
                GetHeader(message, "Subject") ?? "(No subject)",
                GetHeader(message, "From") ?? string.Empty,
                GetHeader(message, "Date") ?? string.Empty,
                string.Empty));
        }

        return mails;
    }

    /// <inheritdoc />
    public async Task<GoogleFullMail> GetMailAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _clientFactory.EnsureGmailEnabled();
        cancellationToken.ThrowIfCancellationRequested();

        var credential = await _oauthService.GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        using var service = _clientFactory.CreateService(
            initializer => new GmailService(initializer),
            "Jarvis",
            credential);

        var getRequest = service.Users.Messages.Get("me", id);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var message = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var body = ExtractBody(message.Payload);
        if (string.IsNullOrWhiteSpace(body))
            body = message.Snippet ?? string.Empty;

        return new GoogleFullMail(
            id,
            GetHeader(message, "Subject") ?? "(No subject)",
            GetHeader(message, "From") ?? string.Empty,
            GetHeader(message, "Date") ?? string.Empty,
            body);
    }

    private static string ExtractBody(MessagePart? part)
    {
        var plain = FindPart(part, "text/plain");
        if (plain is not null)
            return Decode(plain.Body?.Data);

        var html = FindPart(part, "text/html");
        if (html is null)
            return string.Empty;

        var text = System.Text.RegularExpressions.Regex.Replace(
            Decode(html.Body?.Data), "(?is)<(script|style).*?</\\1>|<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static MessagePart? FindPart(MessagePart? part, string mimeType)
    {
        if (part is null)
            return null;
        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(part.Body?.Data)
            && string.IsNullOrEmpty(part.Filename))
            return part;

        foreach (var child in part.Parts ?? [])
        {
            var found = FindPart(child, mimeType);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string Decode(string? base64Url)
    {
        if (string.IsNullOrEmpty(base64Url))
            return string.Empty;
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string? GetHeader(Message message, string name) =>
        message.Payload?.Headers?
            .FirstOrDefault(header =>
                string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static void ValidateResultCount(int maxResults)
    {
        if (maxResults is < 1 or > MaximumResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"The result count must be between 1 and {MaximumResults}.");
        }
    }
}
