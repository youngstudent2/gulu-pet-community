using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GuluPet.Diary;

/// <summary>
/// Keeps half-hour checks tied to their intended wall-clock slot. WPF timers
/// may fire a little late; using the scheduled slot prevents that jitter from
/// skipping the 08:30 closing check or a 30-minute retry.
/// </summary>
public static class DiaryGenerationSchedule
{
    public static readonly TimeSpan ImmediateSlotGrace =
        TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumInstallationJitter =
        TimeSpan.FromMinutes(15);

    /// <summary>
    /// Spreads production installations across the first 15 minutes after a
    /// slot without storing another identifier. The coordinator must still
    /// receive the original slot so the 08:30 boundary remains inclusive.
    /// </summary>
    public static TimeSpan GetStableInstallationJitter(Guid installationId)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(installationId));
        }

        byte[] digest = SHA256.HashData(installationId.ToByteArray());
        uint sample = BinaryPrimitives.ReadUInt32LittleEndian(digest);
        int maximumSeconds = checked(
            (int)MaximumInstallationJitter.TotalSeconds);
        return TimeSpan.FromSeconds(sample % (maximumSeconds + 1U));
    }

    public static DateTimeOffset ApplyInstallationJitter(
        DateTimeOffset intendedCheckUtc,
        TimeSpan jitter)
    {
        if (jitter < TimeSpan.Zero || jitter > MaximumInstallationJitter)
        {
            throw new ArgumentOutOfRangeException(nameof(jitter));
        }

        return intendedCheckUtc + jitter;
    }

    public static DateTimeOffset GetNextSlotUtc(
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        DateTime localTarget = local.Minute < 30
            ? new DateTime(
                local.Year,
                local.Month,
                local.Day,
                local.Hour,
                30,
                0,
                DateTimeKind.Unspecified)
            : new DateTime(
                    local.Year,
                    local.Month,
                    local.Day,
                    local.Hour,
                    0,
                    0,
                    DateTimeKind.Unspecified)
                .AddHours(1);
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(localTarget, timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static DateTimeOffset NormalizeImmediateCheckUtc(
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        int slotMinute = local.Minute < 30 ? 0 : 30;
        var localSlot = new DateTime(
            local.Year,
            local.Month,
            local.Day,
            local.Hour,
            slotMinute,
            0,
            DateTimeKind.Unspecified);
        DateTime slotUtcValue = TimeZoneInfo.ConvertTimeToUtc(
            localSlot,
            timeZone);
        var slotUtc = new DateTimeOffset(slotUtcValue, TimeSpan.Zero);
        TimeSpan lateness = nowUtc - slotUtc;
        return lateness >= TimeSpan.Zero && lateness <= ImmediateSlotGrace
            ? slotUtc
            : nowUtc;
    }
}
