using System;
using System.Net;
using System.Net.Http;
using System.Globalization;

namespace Paperbunkr.Data.Tracking;

/// <summary>Classifies a tracker operation without requiring callers to parse provider error text.</summary>
public enum TrackerOperationKind
{
    Success,
    Missing,
    RetryableFailure,
    PermanentFailure,
}

/// <summary>A tracker lookup or push outcome, including the provider's user-facing error when present.</summary>
public sealed record TrackerOperationResult<T>(
    TrackerOperationKind Kind,
    T? Value,
    string? ErrorDetail = null,
    DateTimeOffset? RetryAfterUtc = null)
{
    public bool Succeeded => Kind == TrackerOperationKind.Success;

    public bool IsRetryable => Kind == TrackerOperationKind.RetryableFailure;

    public bool IsMissing => Kind == TrackerOperationKind.Missing;

    public static TrackerOperationResult<T> Success(T value) => new(TrackerOperationKind.Success, value);

    public static TrackerOperationResult<T> Missing() => new(TrackerOperationKind.Missing, default);

    public static TrackerOperationResult<T> Retryable(string? errorDetail, DateTimeOffset? retryAfterUtc = null) =>
        new(TrackerOperationKind.RetryableFailure, default, errorDetail, retryAfterUtc);

    public static TrackerOperationResult<T> Permanent(string? errorDetail) =>
        new(TrackerOperationKind.PermanentFailure, default, errorDetail);
}

/// <summary>Shared HTTP/network classification for tracker adapters.</summary>
public static class TrackerOperationClassifier
{
    public static TrackerOperationResult<T> FromHttpFailure<T>(
        HttpResponseMessage response,
        string errorDetail,
        bool notFoundMeansMissing = false)
    {
        if (notFoundMeansMissing && response.StatusCode == HttpStatusCode.NotFound)
        {
            return TrackerOperationResult<T>.Missing();
        }

        bool retryable = response.StatusCode == HttpStatusCode.RequestTimeout
            || response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500;
        if (!retryable)
        {
            return TrackerOperationResult<T>.Permanent(errorDetail);
        }

        DateTimeOffset? retryAfter = null;
        if (response.Headers.RetryAfter is { } header)
        {
            retryAfter = header.Date ?? (header.Delta is TimeSpan delay ? DateTimeOffset.UtcNow + delay : null);
        }

        return TrackerOperationResult<T>.Retryable(errorDetail, retryAfter);
    }

    public static TrackerOperationResult<T> FromNetworkFailure<T>(Exception exception) =>
        TrackerOperationResult<T>.Retryable($"Network error: {exception.Message}");

    /// <summary>Compatibility bridge for existing adapters while their HTTP boundary is migrated.
    /// New adapter code should classify from the response/exception directly.</summary>
    public static TrackerOperationResult<T> FromLegacyError<T>(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return TrackerOperationResult<T>.Permanent("Push failed.");
        if (error.StartsWith("Network error:", StringComparison.OrdinalIgnoreCase)
            || error.StartsWith("Request timed out", StringComparison.OrdinalIgnoreCase))
            return TrackerOperationResult<T>.Retryable(error);
        int colon = error.IndexOf(':');
        if (colon > 0 && int.TryParse(error.AsSpan(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
        {
            if (code is 408 or 429 || code >= 500)
                return TrackerOperationResult<T>.Retryable(error);
        }
        return TrackerOperationResult<T>.Permanent(error);
    }

    public static TrackerOperationResult<T> Timeout<T>() =>
        TrackerOperationResult<T>.Retryable("Request timed out.");
}
