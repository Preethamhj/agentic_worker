namespace Jarvis.UI;

/// <summary>Display model for one fetched mail and its local-model summary.</summary>
public sealed record MailResultViewModel(
    string Subject,
    string From,
    string Date,
    string Body,
    string Summary);
