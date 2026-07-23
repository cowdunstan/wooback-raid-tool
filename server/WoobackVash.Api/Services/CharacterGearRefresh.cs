using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Data;
using WoobackVash.Api.Models;

namespace WoobackVash.Api.Services;

/// <summary>
/// Refreshes one character's gear from the freshest source available, shared by the
/// character sheet's on-demand "Refresh gear" button and the guild-roster import, which
/// enriches each character it creates the same way. Blizzard's live character-equipment
/// route first — whatever they're wearing right now, no raid needed — falling back to
/// the character's most recent Warcraft Logs report anywhere (a pug or another guild's
/// night still beats the last night they raided with us) when Blizzard has nothing.
/// </summary>
public static class CharacterGearRefresh
{
    /// <param name="Ok">True when a snapshot was written.</param>
    /// <param name="Source">"blizzard" or "wcl" on success, else null.</param>
    /// <param name="Note">A message safe to show, describing what happened.</param>
    /// <param name="Status">200 on success, else the upstream failure status.</param>
    /// <param name="Error">The failure detail when <paramref name="Ok"/> is false.</param>
    public record Result(bool Ok, string? Source, string? Note, int Status, string? Error);

    /// <summary>Pulls gear for <paramref name="ch"/> (which must be tracked by
    /// <paramref name="db"/>) and upserts a snapshot, saving on success. Best-effort:
    /// on failure nothing is written and the reason comes back in the result.</summary>
    public static async Task<Result> RefreshAsync(
        AppDbContext db, BlizzardService blizzard, WarcraftLogsService wcl, Character ch)
    {
        // 1) Blizzard live gear — whatever they're wearing right now, no raid needed.
        var (bStatus, bGear, bErr) = await blizzard.GetCharacterEquipmentAsync(ch.Name);
        if (bStatus == 200 && bGear is not null && bGear.Items.Count > 0)
        {
            await GearSnapshotStore.UpsertAsync(db, ch, "blizzard", "blizzard", null,
                DateTimeOffset.UtcNow, bGear, refreshSetup: false);
            await db.SaveChangesAsync();
            return new Result(true, "blizzard", "Refreshed live gear from Blizzard.", 200, null);
        }

        // 2) Fall back to the character's most recent Warcraft Logs report.
        var (_, report, rErr) = await wcl.GetLatestReportCodeForCharacterAsync(ch.Name);
        if (report is not null)
        {
            var (_, players, _) = await wcl.GetReportGearAsync(report.Value.Code);
            var p = players?.FirstOrDefault(x => string.Equals(x.Name, ch.Name, StringComparison.OrdinalIgnoreCase));
            if (p is not null && p.Items.Count > 0)
            {
                // Link the snapshot to a raid we already know, when the report is one.
                var evId = await db.RaidEvents.Where(e => e.WclReportCode == report.Value.Code)
                    .Select(e => (Guid?)e.Id).FirstOrDefaultAsync();
                await GearSnapshotStore.UpsertAsync(db, ch, "wcl", report.Value.Code, evId,
                    report.Value.StartsAt, p, refreshSetup: true);
                await db.SaveChangesAsync();
                return new Result(true, "wcl",
                    "Blizzard had nothing usable — pulled the latest Warcraft Logs report instead.", 200, null);
            }
        }

        // Neither source had gear. Surface the more actionable of the two reasons.
        var detail = bErr ?? rErr ?? "No gear could be fetched from Blizzard or Warcraft Logs.";
        return new Result(false, null, null, 502, detail);
    }
}
