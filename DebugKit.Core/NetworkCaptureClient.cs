using System.Net;
using System.Net.Http;

namespace DebugKit.Core;

/// <summary>Only HTTP boundary: fixed GET routes, no redirects/auth/cookies/proxy credentials or response text logging.</summary>
public sealed class NetworkCaptureClient : IDisposable
{
    private readonly HttpClient http;
    private readonly IClock clock;
    private readonly TimeSpan timeout;
    private readonly object gate = new();
    private long? lastRequest;
    private long? rateLimitedAt;
    private double cooldownMilliseconds;
    private bool busy, disposed;

    public NetworkCaptureClient(IClock? clock = null, HttpMessageHandler? testHandler = null, TimeSpan? timeout = null)
    {
        this.clock = clock ?? new SystemClock();
        this.timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(timeout));
        http = new(testHandler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
        });
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DebugKit/0.5.3");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<ToolStep> CaptureAsync(Inspector inspector, ToolOptions options, CancellationToken cancellationToken)
    {
        if (!NetworkPolicy.Valid(inspector, options)) return Failure(options.NetworkConsent ? Reason.InvalidRange : Reason.NetworkConsentRequired, Outcome.Blocked);
        lock (gate)
        {
            if (disposed) return Failure(Reason.Disposed, Outcome.Cancelled);
            if (cancellationToken.IsCancellationRequested) return Failure(Reason.UserCancelled, Outcome.Cancelled);
            if (busy || lastRequest is { } last && clock.ElapsedMilliseconds(last) < 10000 ||
                rateLimitedAt is { } limited && clock.ElapsedMilliseconds(limited) < cooldownMilliseconds)
                return Failure(Reason.NetworkRateLimited, Outcome.Blocked);
            busy = true; lastRequest = clock.Timestamp;
        }
        int? httpStatusCode = null;
        ToolStep ResponseFailure(Reason reason, Outcome outcome = Outcome.Unavailable) => Failure(reason, outcome, httpStatusCode);
        try
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            var token = timeoutCancellation.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, NetworkPolicy.RequestUri(inspector, options));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            httpStatusCode = (int)response.StatusCode is >= 100 and <= 599 ? (int)response.StatusCode : null;
            if ((int)response.StatusCode == 429)
            {
                var retry = response.Headers.RetryAfter;
                var wait = retry?.Delta?.TotalMilliseconds ?? (retry?.Date - clock.UtcNow)?.TotalMilliseconds ?? 60000;
                lock (gate) { rateLimitedAt = clock.Timestamp; cooldownMilliseconds = Math.Max(60000, double.IsFinite(wait) ? wait : 60000); }
                return ResponseFailure(Reason.NetworkRateLimited);
            }
            // Includes redirects: never follow a server-supplied location or retain its error body.
            if (!response.IsSuccessStatusCode) return ResponseFailure(Reason.NetworkHttpError);
            if (response.Content.Headers.ContentType?.MediaType is not "application/json") return ResponseFailure(Reason.NetworkSchemaMismatch);
            if (response.Content.Headers.ContentEncoding.Count != 0) return ResponseFailure(Reason.NetworkSchemaMismatch);
            if (response.Content.Headers.ContentLength > NetworkPolicy.MaximumResponseBytes) return ResponseFailure(Reason.ResponseTooLarge);
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                var count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
                if (count == 0) break;
                if (buffer.Length + count > NetworkPolicy.MaximumResponseBytes) return ResponseFailure(Reason.ResponseTooLarge);
                buffer.Write(chunk, 0, count);
            }
            token.ThrowIfCancellationRequested();
            var result = NetworkDecoder.Decode(inspector, options, buffer.ToArray(), clock.UtcNow);
            token.ThrowIfCancellationRequested();
            return result with { HttpStatusCode = httpStatusCode };
        }
        catch (OperationCanceledException) { return cancellationToken.IsCancellationRequested
            ? ResponseFailure(Reason.UserCancelled, Outcome.Cancelled) : ResponseFailure(Reason.NetworkTimeout); }
        catch (Exception) { return ResponseFailure(Reason.NetworkFailed); }
        finally { lock (gate) busy = false; }
    }

    private static ToolStep Failure(Reason reason, Outcome outcome = Outcome.Unavailable, int? httpStatusCode = null) =>
        new(null, Outcome: outcome, Reason: reason, HttpStatusCode: httpStatusCode);
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        http.Dispose();
    }
}
