namespace Jarvis.Infrastructure.Google;

/// <summary>Reads limited, read-only Calendar and Gmail data for explicit UI requests.</summary>
public interface IGoogleWorkspaceReader
{
    /// <summary>Lists upcoming primary-calendar events.</summary>
    Task<IReadOnlyList<GoogleCalendarEvent>> ListUpcomingEventsAsync(
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Lists recent inbox messages without loading message bodies or attachments.</summary>
    Task<IReadOnlyList<GoogleGmailMessage>> ListInboxMessagesAsync(
        int maxResults = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>A display-safe projection of a Google Calendar event.</summary>
public sealed record GoogleCalendarEvent(
    string Summary,
    string Start,
    string? Location,
    string? HtmlLink);

/// <summary>A metadata-only display projection of a Gmail inbox message.</summary>
public sealed record GoogleGmailMessage(
    string Id,
    string Subject,
    string From,
    string Date,
    string Snippet);
