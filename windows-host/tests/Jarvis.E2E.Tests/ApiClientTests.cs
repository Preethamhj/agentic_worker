using System.Net;
using System.Text.Json;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Xunit;

namespace Jarvis.E2E.Tests;

public class ApiClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    [Fact]
    public async Task ApiClient_Sends_AuthToken_And_TraceId_Headers()
    {
        string? receivedToken = null;
        string? receivedTrace = null;

        var handler = new MockHttpMessageHandler((req, _) =>
        {
            receivedToken = req.Headers.GetValues("X-Jarvis-Token").FirstOrDefault();
            receivedTrace = req.Headers.GetValues("X-Trace-Id").FirstOrDefault();

            var planResponse = new PlanResponseDto(
                SchemaVersion: 1,
                TraceId: receivedTrace ?? "trace-test",
                Status: "ok",
                Intent: new IntentDto(1, "intent-1", "direct", "create_file", 1.0, new Dictionary<string, object?>(), new RiskDto("low", new List<string>()), "rule"),
                Steps: new List<PlanStepDto>(),
                Message: "ok"
            );

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(planResponse))
            };
            return Task.FromResult(resp);
        });

        using var client = new HttpClient(handler);
        var apiClient = new ApiClient(client, "http://127.0.0.1:8000", "secret-test-token-123");

        var request = new PlanRequestDto(1, "session-1", "test prompt");
        var result = await apiClient.GetPlanAsync(request, traceId: "my-custom-trace");

        receivedToken.Should().Be("secret-test-token-123");
        receivedTrace.Should().Be("my-custom-trace");
        result.Status.Should().Be("ok");
    }

    [Fact]
    public async Task ApiClient_Throws_JarvisApiException_On_401_Unauthorized()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });

        using var client = new HttpClient(handler);
        var apiClient = new ApiClient(client, "http://127.0.0.1:8000", "wrong-token");

        var act = async () => await apiClient.GetPlanAsync(new PlanRequestDto(1, "s1", "p1"));
        var ex = await act.Should().ThrowAsync<JarvisApiException>();
        ex.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ApiClient_Throws_On_Malformed_Json()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\": INVALID_JSON_NOT_CLOSED")
            };
            return Task.FromResult(resp);
        });

        using var client = new HttpClient(handler);
        var apiClient = new ApiClient(client, "http://127.0.0.1:8000", "token");

        var act = async () => await apiClient.GetPlanAsync(new PlanRequestDto(1, "s1", "p1"));
        var ex = await act.Should().ThrowAsync<JarvisApiException>();
        ex.Which.InnerException.Should().BeAssignableTo<JsonException>();
    }

    [Fact]
    public async Task ApiClient_Times_Out_After_Configured_Duration()
    {
        var handler = new MockHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(1000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        // Set short plan timeout of 50ms
        var apiClient = new ApiClient(client, "http://127.0.0.1:8000", "token", planTimeout: TimeSpan.FromMilliseconds(50));

        var act = async () => await apiClient.GetPlanAsync(new PlanRequestDto(1, "s1", "p1"));
        var ex = await act.Should().ThrowAsync<JarvisApiException>();
        ex.Which.InnerException.Should().BeAssignableTo<TimeoutException>();
    }

    [Fact]
    public async Task ApiClient_Retries_Plan_Once_On_Transient_Failure()
    {
        int attemptCount = 0;

        var handler = new MockHttpMessageHandler((_, _) =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                // First attempt returns 503 Service Unavailable
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("temporarily unavailable")
                });
            }

            // Second attempt succeeds
            var planResponse = new PlanResponseDto(
                SchemaVersion: 1,
                TraceId: "retry-trace",
                Status: "ok",
                Intent: new IntentDto(1, "intent-1", "direct", "create_file", 1.0, new Dictionary<string, object?>(), new RiskDto("low", new List<string>()), "rule"),
                Steps: new List<PlanStepDto>(),
                Message: "retry succeeded"
            );

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(planResponse))
            });
        });

        using var client = new HttpClient(handler);
        var apiClient = new ApiClient(client, "http://127.0.0.1:8000", "token");

        var result = await apiClient.GetPlanAsync(new PlanRequestDto(1, "s1", "prompt"));

        attemptCount.Should().Be(2, "Plan request should have retried once on transient 503");
        result.Message.Should().Be("retry succeeded");
    }

    [Fact]
    public async Task ApiClient_SendReport_Includes_IdempotencyKey_Header()
    {
        string? receivedIdempotencyKey = null;

        var handler = new MockHttpMessageHandler((req, _) =>
        {
            receivedIdempotencyKey = req.Headers.GetValues("Idempotency-Key").FirstOrDefault();

            var reportResponse = new ReportResponseDto(
                SchemaVersion: 1,
                Message: "report recorded",
                MemoryUpdated: true
            );

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(reportResponse))
            });
        });

        using var client = new HttpClient(handler);
        var apiClient = new ApiClient(client, "http://127.0.0.1:8000", "token");

        var report = new ReportRequestDto(
            SchemaVersion: 1,
            SessionId: "session-1",
            TaskId: "task-abc-123",
            Steps: new List<ReportStepSummaryDto>()
        );

        var result = await apiClient.SendReportAsync(report);

        receivedIdempotencyKey.Should().Be("task-abc-123", "Idempotency-Key must match task_id");
        result.Message.Should().Be("report recorded");
    }
}
