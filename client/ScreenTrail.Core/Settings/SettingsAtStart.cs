using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Core.Settings;

/// <summary>
/// What the service records at start about the settings it is about to run under (A-03 (c), ST-045): one
/// <c>settings_changed</c> row per setting that differs from what it last saved or applied, the setting's
/// fixed label and never its value (INV-10). That is a change made to the files while the service was not
/// running, by hand, by another program, or by damage that was read as the strictest document.
///
/// The rows go before the copy beside each file is brought up to date, so a start that could not write them
/// finds the same change again next time rather than losing it. A first start after the upgrade has no copy
/// to compare against and says nothing.
/// </summary>
public static class SettingsAtStart
{
    /// <param name="loaded">What <paramref name="settings"/> gave the applier at this start.</param>
    /// <param name="loadedCapture">What <paramref name="capture"/> gave the applier at this start.</param>
    /// <returns>False when a row could not be written; that file's copy is then left as it was.</returns>
    public static async Task<bool> RecordAsync(
        IAuditLog audit,
        ClientSettingsStore settings,
        ClientSettings loaded,
        CaptureSettingsStore capture,
        CaptureSettings loadedCapture,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(loadedCapture);

        var all = true;
        var lastSettings = settings.LoadApplied();
        if (await RecordAsync(audit, lastSettings is null ? [] : SettingChanges.Between(lastSettings, loaded), ct).ConfigureAwait(false))
        {
            // Only once there is a file or an earlier copy: a technician who never saved keeps no copy, so a
            // later start still reads a missing file as the first start it is.
            if (lastSettings is not null || File.Exists(settings.Path))
            {
                _ = settings.RecordApplied(loaded);
            }
        }
        else
        {
            all = false;
        }

        var lastCapture = capture.LoadApplied();
        if (await RecordAsync(audit, lastCapture is null ? [] : SettingChanges.Between(lastCapture, loadedCapture), ct).ConfigureAwait(false))
        {
            if (lastCapture is not null || File.Exists(capture.Path))
            {
                _ = capture.RecordApplied(loadedCapture);
            }
        }
        else
        {
            all = false;
        }

        return all;
    }

    private static async Task<bool> RecordAsync(IAuditLog audit, IReadOnlyList<(SettingChange Change, long? Count)> changes, CancellationToken ct)
    {
        try
        {
            foreach (var (change, count) in changes)
            {
                await audit.RecordAsync(AuditTypes.SettingsChanged, null, count, AuditDetail.Of(change), ct).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The settings are already in force; the start goes on, and the next one tries the rows again.
            return false;
        }
    }
}
