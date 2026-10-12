namespace Jarvis.Infrastructure;

public sealed class JarvisApiException : Exception
{
    public int? StatusCode { get; }

    public JarvisApiException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
