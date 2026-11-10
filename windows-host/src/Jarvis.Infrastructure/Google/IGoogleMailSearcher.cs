namespace Jarvis.Infrastructure.Google;

/// <summary>Searches Gmail metadata and loads one selected message body on demand.</summary>
public interface IGoogleMailSearcher
{
    /// <summary>Searches Gmail and returns metadata only, excluding spam.</summary>
    Task<IReadOnlyList<GoogleGmailMessage>> SearchMailAsync(
        string query,
        int maxResults = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the body of one selected Gmail message without loading attachments.</summary>
    Task<GoogleFullMail> GetMailAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>A complete selected Gmail message projection; attachments are never loaded.</summary>
public sealed record GoogleFullMail(
    string Id,
    string Subject,
    string From,
    string Date,
    string Body);

/// <summary>Summarizes text using the local model only.</summary>
public interface IMailSummarizer
{
    /// <summary>Returns a concise summary of the mail text.</summary>
    Task<string> SummarizeAsync(GoogleFullMail mail, CancellationToken cancellationToken = default);
}
