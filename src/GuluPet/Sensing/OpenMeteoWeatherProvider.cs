using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GuluPet.Diagnostics;

namespace GuluPet.Sensing;

internal sealed record OpenMeteoWeatherOptions
{
    internal required Uri RequestUri { get; init; }

    internal TimeSpan RefreshInterval { get; init; } =
        TimeSpan.FromMinutes(45);

    internal TimeSpan FailureRetryInterval { get; init; } =
        TimeSpan.FromMinutes(5);

    internal TimeSpan FailureRetryMaximumInterval { get; init; } =
        TimeSpan.FromMinutes(45);

    internal TimeSpan FreshFor { get; init; } = TimeSpan.FromMinutes(60);

    internal TimeSpan MaximumStaleAge { get; init; } =
        TimeSpan.FromHours(12);

    internal TimeSpan RequestTimeout { get; init; } =
        TimeSpan.FromSeconds(12);

    internal int MaximumResponseBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        if (!string.Equals(
                RequestUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || RefreshInterval <= TimeSpan.Zero
            || FailureRetryInterval <= TimeSpan.Zero
            || FailureRetryMaximumInterval < FailureRetryInterval
            || FreshFor <= TimeSpan.Zero
            || MaximumStaleAge < FreshFor
            || RequestTimeout <= TimeSpan.Zero
            || MaximumResponseBytes <= 0)
        {
            throw new InvalidOperationException(
                "Open-Meteo weather options are invalid.");
        }
    }
}

internal sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    private const string GuangzhouRequest =
        "https://api.open-meteo.com/v1/forecast" +
        "?latitude=23.1291&longitude=113.2644" +
        "&current=temperature_2m%2Cweather_code" +
        "&timezone=Asia%2FShanghai&forecast_days=1";

    internal static Uri DefaultGuangzhouRequestUri { get; } =
        new(GuangzhouRequest, UriKind.Absolute);

    private readonly object _gate = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly OpenMeteoWeatherOptions _options;
    private readonly IContextHealthSink _healthSink;
    private WeatherCacheEntry? _cache;
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastClockObservation;
    private DateTimeOffset? _lastSuccessAt;
    private int _consecutiveFailures;
    private bool _disposed;

    internal OpenMeteoWeatherProvider(
        HttpClient httpClient,
        OpenMeteoWeatherOptions options,
        bool ownsHttpClient = false,
        IContextHealthSink? healthSink = null)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options
            ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _ownsHttpClient = ownsHttpClient;
        _healthSink = healthSink ?? NullContextHealthSink.Instance;
    }

    internal static OpenMeteoWeatherProvider CreateGuangzhouDefault(
        IContextHealthSink? healthSink = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GuluPetCommunity", "0.1"));
        return new OpenMeteoWeatherProvider(
            client,
            new OpenMeteoWeatherOptions
            {
                RequestUri = DefaultGuangzhouRequestUri,
            },
            ownsHttpClient: true,
            healthSink: healthSink);
    }

    public WeatherReading GetCurrent(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_cache is null)
            {
                return WeatherReading.Unavailable;
            }

            TimeSpan age = now - _cache.FetchedAt;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            if (age > _options.MaximumStaleAge)
            {
                return WeatherReading.Unavailable;
            }

            WeatherDataQuality quality = age <= _options.FreshFor
                ? WeatherDataQuality.Fresh
                : WeatherDataQuality.Stale;
            return new WeatherReading(
                quality,
                _cache.Kind,
                _cache.TemperatureCelsius,
                _cache.WeatherCode,
                _cache.ObservedAt);
        }
    }

    public async Task RefreshIfDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_lastClockObservation != default
                && now < _lastClockObservation)
            {
                _nextAttemptAt = DateTimeOffset.MinValue;
            }

            _lastClockObservation = now;
            if (_disposed || now < _nextAttemptAt)
            {
                return;
            }

            // Reserve the retry slot before leaving the lock. This also
            // deduplicates concurrent callers.
            _nextAttemptAt = now + _options.FailureRetryInterval;
        }

        WeatherCacheEntry? fetched = null;
        ContextHealthErrorCategory? failureCategory = null;
        try
        {
            fetched = await FetchAsync(now, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _nextAttemptAt = DateTimeOffset.MinValue;
                }
            }

            RecordHealth(
                now,
                ContextHealthOutcome.Failure,
                ContextHealthErrorCategory.Cancel,
                incrementFailure: false);
            return;
        }
        catch (WeatherFetchException exception)
        {
            failureCategory = exception.Category;
        }
        catch
        {
            failureCategory = ContextHealthErrorCategory.Unexpected;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (fetched is not null)
            {
                _cache = fetched;
                _nextAttemptAt = now + _options.RefreshInterval;
            }
        }

        if (fetched is not null)
        {
            RecordHealth(
                now,
                ContextHealthOutcome.Success,
                ContextHealthErrorCategory.None,
                incrementFailure: false);
        }
        else
        {
            int failureCount = RecordHealth(
                now,
                ContextHealthOutcome.Failure,
                failureCategory
                    ?? ContextHealthErrorCategory.Unexpected,
                incrementFailure: true);
            lock (_gate)
            {
                if (!_disposed)
                {
                    _nextAttemptAt = now
                        + CalculateFailureDelay(failureCount);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cache = null;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    internal static string MapWeatherCode(int weatherCode) =>
        weatherCode switch
        {
            0 => "clear",
            >= 1 and <= 3 => "cloudy",
            45 or 48 => "fog",
            >= 51 and <= 67 => "rain",
            >= 71 and <= 77 => "cloudy",
            >= 80 and <= 82 => "rain",
            85 or 86 => "cloudy",
            >= 95 and <= 99 => "storm",
            _ => "unknown",
        };

    private async Task<WeatherCacheEntry> FetchAsync(
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(
                    _options.RequestUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherFetchException(
                ContextHealthErrorCategory.Timeout,
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WeatherFetchException(
                ContextHealthErrorCategory.Unexpected,
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherFetchException(
                    ContextHealthErrorCategory.HttpStatus);
            }

            if (response.Content.Headers.ContentLength
                > _options.MaximumResponseBytes)
            {
                throw new WeatherFetchException(
                    ContextHealthErrorCategory.InvalidData);
            }

            byte[] payload;
            try
            {
                payload = await ReadLimitedAsync(
                        response.Content,
                        _options.MaximumResponseBytes,
                        timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new WeatherFetchException(
                    ContextHealthErrorCategory.Timeout,
                    exception);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidDataException exception)
            {
                throw new WeatherFetchException(
                    ContextHealthErrorCategory.InvalidData,
                    exception);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload);
            }
            catch (JsonException exception)
            {
                throw new WeatherFetchException(
                    ContextHealthErrorCategory.InvalidJson,
                    exception);
            }

            using (document)
            {
                return ParseWeather(document, fetchedAt);
            }
        }
    }

    private static WeatherCacheEntry ParseWeather(
        JsonDocument document,
        DateTimeOffset fetchedAt)
    {
        if (!document.RootElement.TryGetProperty(
                "current",
                out JsonElement current)
            || !current.TryGetProperty(
                "temperature_2m",
                out JsonElement temperatureElement)
            || !temperatureElement.TryGetDouble(out double temperature)
            || !double.IsFinite(temperature)
            || !current.TryGetProperty(
                "weather_code",
                out JsonElement weatherCodeElement)
            || !weatherCodeElement.TryGetInt32(out int weatherCode))
        {
            throw new WeatherFetchException(
                ContextHealthErrorCategory.InvalidData);
        }

        DateTimeOffset? observedAt = null;
        if (current.TryGetProperty("time", out JsonElement timeElement)
            && timeElement.ValueKind == JsonValueKind.String)
        {
            string? rawTime = timeElement.GetString();
            if (DateTimeOffset.TryParse(
                    rawTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out DateTimeOffset parsed))
            {
                observedAt = parsed;
            }
        }

        return new WeatherCacheEntry(
            MapWeatherCode(weatherCode),
            temperature,
            weatherCode,
            observedAt,
            fetchedAt);
    }

    private int RecordHealth(
        DateTimeOffset now,
        ContextHealthOutcome requestedOutcome,
        ContextHealthErrorCategory errorCategory,
        bool incrementFailure)
    {
        ContextHealthEvent healthEvent;
        lock (_gate)
        {
            if (incrementFailure)
            {
                _consecutiveFailures = checked(_consecutiveFailures + 1);
            }

            ContextHealthOutcome outcome = requestedOutcome;
            if (requestedOutcome == ContextHealthOutcome.Success)
            {
                outcome = _consecutiveFailures > 0
                    ? ContextHealthOutcome.Recovered
                    : ContextHealthOutcome.Success;
                _consecutiveFailures = 0;
                _lastSuccessAt = now;
            }

            healthEvent = new ContextHealthEvent(
                now,
                ContextHealthSource.Weather,
                outcome,
                errorCategory,
                _consecutiveFailures,
                LastSuccessAgeSeconds(now, _lastSuccessAt));
        }

        try
        {
            _healthSink.Write(healthEvent);
        }
        catch
        {
            // Diagnostics must never alter sensing availability.
        }

        return healthEvent.ConsecutiveFailures;
    }

    private TimeSpan CalculateFailureDelay(int consecutiveFailures)
    {
        double exponent = Math.Min(
            Math.Max(0, consecutiveFailures - 1),
            20);
        double milliseconds = Math.Min(
            _options.FailureRetryMaximumInterval.TotalMilliseconds,
            _options.FailureRetryInterval.TotalMilliseconds
                * Math.Pow(2, exponent));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static double? LastSuccessAgeSeconds(
        DateTimeOffset now,
        DateTimeOffset? lastSuccessAt)
    {
        if (lastSuccessAt is not DateTimeOffset success)
        {
            return null;
        }

        return Math.Max(0, (now - success).TotalSeconds);
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using Stream source = await content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream(
            Math.Min(maximumBytes, 8 * 1024));
        byte[] buffer = new byte[8 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "Open-Meteo response exceeded the size limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private sealed record WeatherCacheEntry(
        string Kind,
        double TemperatureCelsius,
        int WeatherCode,
        DateTimeOffset? ObservedAt,
        DateTimeOffset FetchedAt);

    private sealed class WeatherFetchException(
        ContextHealthErrorCategory category,
        Exception? innerException = null)
        : Exception("Weather refresh failed.", innerException)
    {
        internal ContextHealthErrorCategory Category { get; } = category;
    }
}
