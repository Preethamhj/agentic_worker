using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jarvis.Contracts;

namespace Jarvis.Infrastructure;

public interface IApiClient
{
    Task<PlanResponseDto> GetPlanAsync(PlanRequestDto request, string? traceId = null, CancellationToken cancellationToken = default);
    Task<ReportResponseDto> SendReportAsync(ReportRequestDto report, string? traceId = null, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP client communicating with the local AI server (127.0.0.1).
/// Invariants:
/// - X-Jarvis-Token on every authenticated request.
/// - X-Trace-Id header forwarded or generated.
/// - Plan timeout default 90 seconds (real model p95 around 28 s).
/// - Exactly one retry for plan on transient failure.
/// - Idempotency-Key header set to task_id for report requests.
/// - Strictly local loopback only.
/// </summary>
public sealed class ApiClient : IApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly TimeSpan _planTimeout;
    private readonly TimeSpan _reportTimeout;

    public ApiClient(
        HttpClient httpClient,
        string baseUrl,
        string token,
        TimeSpan? planTimeout = null,
        TimeSpan? reportTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = (baseUrl ?? "http://127.0.0.1:8000").TrimEnd('/');
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _planTimeout = planTimeout ?? TimeSpan.FromSeconds(90);
        _reportTimeout = reportTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<PlanResponseDto> GetPlanAsync(PlanRequestDto request, string? traceId = null, CancellationToken cancellationToken = default)
    {
        var trace = traceId ?? $"trace-{Guid.NewGuid():N}"[..12];
        var url = $"{_baseUrl}/v1/plan";

        Exception? lastException = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(_planTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Headers.Add("X-Jarvis-Token", _token);
                httpRequest.Headers.Add("X-Trace-Id", trace);
                httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

                using var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new JarvisApiException("Unauthorized: Invalid or missing token (401).", 401);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new JarvisApiException($"AI server returned {(int)response.StatusCode}: {errorBody}", (int)response.StatusCode);
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var plan = JsonSerializer.Deserialize<PlanResponseDto>(content, JsonOptions);
                    if (plan == null)
                    {
                        throw new JarvisApiException("AI server returned empty plan response.", (int)response.StatusCode);
                    }
                    return plan;
                }
                catch (JsonException jex)
                {
                    throw new JarvisApiException($"AI server returned malformed JSON response: {jex.Message}", (int)response.StatusCode, jex);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Plan generation timed out after {_planTimeout.TotalSeconds:F1}s (attempt {attempt}/2).");
            }
            catch (JarvisApiException jae) when (jae.StatusCode == 401)
            {
                // Never retry 401 unauthorized
                throw;
            }
            catch (JarvisApiException jae) when (jae.InnerException is JsonException)
            {
                // Never retry malformed JSON from server
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (attempt == 1)
            {
                // Wait briefly before 1 retry on transient error
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new JarvisApiException($"Plan generation failed after 2 attempts: {lastException?.Message}", innerException: lastException);
    }

    public async Task<ReportResponseDto> SendReportAsync(ReportRequestDto report, string? traceId = null, CancellationToken cancellationToken = default)
    {
        var trace = traceId ?? $"trace-{Guid.NewGuid():N}"[..12];
        var url = $"{_baseUrl}/v1/report";

        using var timeoutCts = new CancellationTokenSource(_reportTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("X-Jarvis-Token", _token);
        httpRequest.Headers.Add("X-Trace-Id", trace);
        httpRequest.Headers.Add("Idempotency-Key", report.TaskId);
        httpRequest.Content = JsonContent.Create(report, options: JsonOptions);

        using var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new JarvisApiException("Unauthorized: Invalid or missing token (401).", 401);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new JarvisApiException($"AI server returned {(int)response.StatusCode}: {errorBody}", (int)response.StatusCode);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var res = JsonSerializer.Deserialize<ReportResponseDto>(content, JsonOptions);
            return res ?? throw new JarvisApiException("AI server returned empty report response.");
        }
        catch (JsonException jex)
        {
            throw new JarvisApiException($"Malformed JSON response from report: {jex.Message}", (int)response.StatusCode, jex);
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/health";
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using var response = await _httpClient.GetAsync(url, linkedCts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
