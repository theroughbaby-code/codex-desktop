using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Loupedeck.CodexDesktopPlugin;

internal enum RateLimitState
{
    Loading,
    Available,
    SignInRequired,
    Unavailable,
}

internal sealed record RateLimitStatus(
    RateLimitState State,
    Int32? PrimaryRemainingPercent = null,
    Int32? SecondaryRemainingPercent = null,
    DateTimeOffset? ResetsAt = null,
    String? PlanType = null,
    String? CreditBalance = null);

internal sealed class CodexRateLimitClient : IDisposable
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private Timer? timer;
    private Boolean disposed;

    public RateLimitStatus Status { get; private set; } = new(RateLimitState.Loading);

    public event Action? Updated;

    public void Start()
    {
        this.timer = new Timer(_ => _ = this.RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(2));
    }

    public async Task RefreshAsync()
    {
        if (this.disposed || !await this.refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            this.SetStatus(new RateLimitStatus(RateLimitState.Loading));
            this.SetStatus(await ReadRateLimitsAsync().ConfigureAwait(false));
        }
        catch
        {
            this.SetStatus(new RateLimitStatus(RateLimitState.Unavailable));
        }
        finally
        {
            this.refreshLock.Release();
        }
    }

    private static async Task<RateLimitStatus> ReadRateLimitsAsync()
    {
        if (!CodexLocator.TryFindCodexExecutable(out var codexPath))
        {
            return new RateLimitStatus(RateLimitState.Unavailable);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = codexPath,
                Arguments = "app-server",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        if (!process.Start())
        {
            return new RateLimitStatus(RateLimitState.Unavailable);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await process.StandardInput.WriteLineAsync(
                "{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex-desktop-logi-plugin\",\"title\":\"Codex Desktop for Logi Options+\",\"version\":\"1.1.0\"},\"capabilities\":null}}"
            ).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            var initialized = await ReadResponseAsync(process, 1, timeout.Token).ConfigureAwait(false);
            if (initialized is null || initialized.Value.TryGetProperty("error", out _))
            {
                return new RateLimitStatus(RateLimitState.Unavailable);
            }

            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}").ConfigureAwait(false);
            await process.StandardInput.WriteLineAsync("{\"method\":\"account/rateLimits/read\",\"id\":2}").ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            var response = await ReadResponseAsync(process, 2, timeout.Token).ConfigureAwait(false);
            if (response is null)
            {
                return new RateLimitStatus(RateLimitState.Unavailable);
            }

            if (response.Value.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString() ?? String.Empty
                    : String.Empty;
                return message.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
                    ? new RateLimitStatus(RateLimitState.SignInRequired)
                    : new RateLimitStatus(RateLimitState.Unavailable);
            }

            return ParseStatus(response.Value);
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(250))
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // The short-lived app-server may already have exited.
            }
        }
    }

    private static async Task<JsonElement?> ReadResponseAsync(Process process, Int32 expectedId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var id) && id.TryGetInt32(out var value) && value == expectedId)
            {
                return root.Clone();
            }
        }

        return null;
    }

    internal static RateLimitStatus ParseStatus(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("rateLimits", out var limits))
        {
            return new RateLimitStatus(RateLimitState.Unavailable);
        }

        var primaryRemaining = RemainingPercent(limits, "primary");
        var secondaryRemaining = RemainingPercent(limits, "secondary");
        var resetsAt = ResetTime(limits, "primary");
        var plan = limits.TryGetProperty("planType", out var planElement) ? planElement.GetString() : null;
        String? balance = null;

        if (limits.TryGetProperty("credits", out var credits) &&
            credits.ValueKind == JsonValueKind.Object &&
            credits.TryGetProperty("balance", out var balanceElement))
        {
            balance = balanceElement.ValueKind == JsonValueKind.String
                ? balanceElement.GetString()
                : balanceElement.ToString();
        }

        return primaryRemaining is null
            ? new RateLimitStatus(RateLimitState.Unavailable)
            : new RateLimitStatus(RateLimitState.Available, primaryRemaining, secondaryRemaining, resetsAt, plan, balance);
    }

    private static Int32? RemainingPercent(JsonElement limits, String windowName)
    {
        if (!limits.TryGetProperty(windowName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var used) ||
            !used.TryGetDouble(out var usedPercent))
        {
            return null;
        }

        return (Int32)Math.Round(Math.Clamp(100D - usedPercent, 0D, 100D), MidpointRounding.AwayFromZero);
    }

    private static DateTimeOffset? ResetTime(JsonElement limits, String windowName)
    {
        if (!limits.TryGetProperty(windowName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("resetsAt", out var reset))
        {
            return null;
        }

        if (reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return reset.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private void SetStatus(RateLimitStatus status)
    {
        this.Status = status;
        this.Updated?.Invoke();
    }

    public void Dispose()
    {
        this.disposed = true;
        this.timer?.Dispose();
        this.refreshLock.Dispose();
    }
}
