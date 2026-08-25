using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Data;
using WoobackVash.Api.Services;

namespace WoobackVash.Api.Api;

/// <summary>
/// The loot "Hall of shame" — every leaderboard and one-off record the loot-stats page
/// shows, computed server-side from the whole award/roll history. The page used to fetch
/// <c>/api/loot/history</c> and aggregate in the browser; now it fetches assembled cards
/// and does nothing but render them, so all the copy, thresholds, ranking and formatting
/// live here in one place. Session-gated, same as the read-only loot history it draws on.
/// </summary>
public static class LootStatsEndpoints
{
    // A roll needs a real sample before a rate (average, win %, spread) says anything.
    private const int MinRolls = 5;

    // The Google-sheet tabs that list each phase's loot, by document key and tab gid —
    // the same tabs loot-sheet.js's RAID_TABS drives, mirrored here so the stats page can
    // split the hall of shame the way every other page splits everything else. An award
    // carries no phase of its own and item level can't tell them apart (Serpentshrine /
    // Tempest Keep's final bosses drop ilvl-141 gear that Black Temple / Mount Hyjal also
    // sits on), so the sheets — which list each item under its raid — are the only
    // authority. Keep in sync with RAID_TABS: p2 is SSC + TK (one tab per boss, plus the
    // shared Tier Sets tab), p3 is Black Temple and Mount Hyjal (one tab each).
    private static readonly Dictionary<string, string[]> PhaseTabs = new()
    {
        ["p2"] = new[]
        {
            "324929599", "8551419", "420793116", "152003569", "821616370", "1241401949",
            "1405398559", "649478556", "1032392127", "1216905087", "2111072609",
            "1959598972", "1626986966",
        },
        ["p3"] = new[] { "1226096003", "1714599159" },
    };

    public static void MapLootStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/loot/stats", async (HttpContext ctx, SessionTokenService tokens, LootSheetService sheets) =>
        {
            var (_, error) = ctx.RequireSession(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            // The same rows the history page reads: ignored characters (and their rolls)
            // are out of the guild and out of every stat. Oldest first so losing streaks
            // read chronologically.
            var awards = await db.LootAwards.AsNoTracking()
                .Where(l => l.Character == null || !l.Character.Ignored)
                .OrderBy(l => l.AwardedAt)
                .Select(l => new StatAward(
                    l.CharacterId,
                    l.Character != null ? l.Character.Name : null,
                    l.Character != null ? l.Character.Class : null,
                    l.Disenchanted,
                    l.ItemName,
                    l.ItemId,
                    l.AwardedBy,
                    l.AwardedAt,
                    l.OffSpec,
                    l.Rolls
                        .Where(r => r.Character == null || !r.Character.Ignored)
                        .Select(r => new StatRoll(
                            r.CharacterId,
                            r.Character != null ? r.Character.Name : null,
                            r.Character != null ? r.Character.Class : null,
                            r.Amount,
                            r.Classification))
                        .ToList()))
                .ToListAsync();

            // ?phase=p2|p3 narrows the hall of shame to one of the guild's two raids;
            // anything else (including the default) keeps the whole history, so an older
            // client that never asks sees exactly what it always has. Phase is decided by
            // the loot sheets: an award is in a phase when that phase's sheets list its
            // item. An item the sheets don't carry (an off-list drop, or a name typed a
            // little differently) matches neither and shows only in the combined "all"
            // view. If the sheets can't be read at all there is nothing to filter on, so
            // the page is told rather than shown a silently-wrong split.
            var phase = ctx.Request.Query["phase"].ToString().Trim().ToLowerInvariant();
            if (phase is "p2" or "p3")
            {
                var names = await BuildPhaseItemNames(sheets, phase);
                if (names.Count == 0)
                    return Results.Json(new { error = "sheets_unavailable",
                        detail = "Couldn't read the loot sheets to split by phase." }, statusCode: 502);
                awards = awards.Where(a => names.Contains(NameKey(a.ItemName))).ToList();
            }

            return Results.Json(Aggregate(awards));
        });
    }

    // The set of item names one phase's sheets list, normalised for matching. Built from
    // column A of every tab — the item-name column — skipping the header row. Boss banners
    // and stray notes come through too, but they never match a real award's item name, so
    // they cost nothing; only a missing item would, and that just leaves it in "all". Each
    // tab is a ~9 KB CSV the LootSheetService already caches for ten minutes.
    private static async Task<HashSet<string>> BuildPhaseItemNames(LootSheetService sheets, string phase)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!PhaseTabs.TryGetValue(phase, out var gids)) return names;

        var tabs = await Task.WhenAll(gids.Select(g => sheets.GetTabAsync(phase, g, html: false)));
        foreach (var (status, body, _) in tabs)
        {
            if (status != 200 || body is null) continue;
            foreach (var row in ParseCsv(body))
            {
                if (row.Count == 0) continue;
                var a = row[0].Trim();
                if (a.Length == 0) continue;
                var b = row.Count > 1 ? row[1].Trim() : "";
                // The header row — col B "Bias", or col A "Item Name…" — is not an item.
                if (b.Equals("Bias", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("Item Name", StringComparison.OrdinalIgnoreCase)) continue;
                var key = NameKey(a);
                if (key.Length > 0) names.Add(key);
            }
        }
        return names;
    }

    // A minimal RFC-4180 CSV reader (quotes, "" escapes, embedded newlines), matching the
    // one loot-sheet.js parses the same export with. Only the field text matters here.
    private static List<List<string>> ParseCsv(string body)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < body.Length && body[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
                continue;
            }
            if (ch == '"') { quoted = true; continue; }
            if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); continue; }
            if (ch == '\n') { row.Add(cell.ToString()); rows.Add(row); row = new(); cell.Clear(); continue; }
            if (ch == '\r') continue;
            cell.Append(ch);
        }
        row.Add(cell.ToString());
        rows.Add(row);
        return rows;
    }

    // Fold an item name to a match key: lower-case, letters and digits only, single spaces.
    // Punctuation the sheet and Gargul disagree on (apostrophes, hyphens) drops out, so
    // "Kael'thas" and "Kaelthas" meet. Both the sheet cell and the award name run through it.
    private static string NameKey(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { if (pendingSpace) { sb.Append(' '); pendingSpace = false; } sb.Append(ch); }
            else if (char.IsWhiteSpace(ch) || ch == '-') { if (sb.Length > 0) pendingSpace = true; }
        }
        return sb.ToString();
    }

    // ── Loaded shapes ───────────────────────────────────────────────────────
    private record StatRoll(Guid PlayerId, string? PlayerName, string? PlayerCls, int Amount, string? Classification);
    private record StatAward(
        Guid? CharacterId, string? CharacterName, string? CharacterClass, bool Disenchanted,
        string ItemName, long? ItemId, string? AwardedBy, DateTimeOffset AwardedAt, bool OffSpec,
        List<StatRoll> Rolls);

    // ── Accumulators ────────────────────────────────────────────────────────
    private sealed class Agg
    {
        public Guid Id;
        public string Name = "?";
        public string? Cls;
        public int Rolls, Wins, Losses, Hundreds, Ones, OsWins, NearMiss, BigWins, Streak, WorstStreak;
        public int WinStreak, BestWinStreak, LowWins, Robbery, TieWins;
        public double Sum, SumSq, LossMarginSum;
        public int LossMarginCount;
        public readonly HashSet<Guid> Victims = new();
        public readonly HashSet<string> WonItems = new();
        public readonly Dictionary<Guid, int> LostTo = new();
        public readonly HashSet<DateTime> Nights = new();
        // Every item this character rolled on, with how many times — the raw material
        // for their personal white whale (the one they chased most and never won).
        public readonly Dictionary<string, (string Name, long? Id, int Count)> ItemBids = new();
        // Filled once the pass is done: the most-rolled item they never won.
        public int WhaleCount; public string? WhaleName; public long? WhaleId;

        public double Avg => Rolls > 0 ? Sum / Rolls : double.NaN;
        public double Std => Rolls > 0 ? Math.Sqrt(Math.Max(0, SumSq / Rolls - Avg * Avg)) : double.NaN;
        public double WinRate => Rolls > 0 ? (double)Wins / Rolls : double.NaN;
        // Average points the winning roll beat them by, across their losses — how close
        // they habitually come. Lower is crueller.
        public double AvgLossMargin => LossMarginCount > 0 ? LossMarginSum / LossMarginCount : double.NaN;
    }

    private sealed class ItemAgg { public string Name = ""; public long? Id; public int Count, Rolls, Drops; }
    private sealed class Pair { public Guid Lo, Hi; public int LoWins, HiWins; }
    private sealed class Gift { public Guid WinnerId; public string WinnerName = ""; public string? WinnerCls; public string Looter = ""; public int Count; }

    private static string ItemKey(long? id, string name) =>
        id.HasValue ? "i" + id.Value : "n" + name.ToLowerInvariant();

    private static object Aggregate(List<StatAward> awards)
    {
        // Nothing recorded at all — let the page show its friendly empty message, the
        // same as when the old history fetch came back with no rows.
        if (awards.Count == 0)
            return new { summary = Array.Empty<object>(), cards = Array.Empty<object>() };

        var aggs = new Dictionary<Guid, Agg>();
        var order = new List<Agg>();  // first-seen order, so ties rank as they did client-side

        Agg Get(Guid id, string? name, string? cls)
        {
            if (!aggs.TryGetValue(id, out var a))
            {
                a = new Agg { Id = id, Name = name ?? "?", Cls = cls };
                aggs[id] = a;
                order.Add(a);
            }
            if (a.Cls is null && cls is not null) a.Cls = cls;
            if (a.Name == "?" && name is not null) a.Name = name;
            return a;
        }

        var looters = new Dictionary<string, int>(StringComparer.Ordinal);
        var gifts = new Dictionary<(Guid, string), Gift>();
        var nights = new Dictionary<DateTime, int>();
        var deItems = new Dictionary<string, ItemAgg>();
        var itemRolls = new Dictionary<string, ItemAgg>();
        var pairs = new Dictionary<(Guid, Guid), Pair>();

        // Raw pass — every award, including the zero-roll ones the leaderboard pass skips.
        // A master-loot hand-out and a disenchant carry no rolls but *are* the looter,
        // loot-night and shard signals, so those tallies can only be built here.
        foreach (var a in awards)
        {
            var looter = a.AwardedBy?.Trim();
            if (!string.IsNullOrEmpty(looter) && !a.Disenchanted)
            {
                looters[looter] = looters.GetValueOrDefault(looter) + 1;
                // Handing loot to yourself isn't favouritism, so it doesn't feed teacher's pet.
                if (a.CharacterId is Guid wid && a.CharacterName is not null &&
                    !string.Equals(a.CharacterName, looter, StringComparison.OrdinalIgnoreCase))
                {
                    var gk = (wid, looter);
                    if (!gifts.TryGetValue(gk, out var g))
                        gifts[gk] = g = new Gift { WinnerId = wid, WinnerName = a.CharacterName, WinnerCls = a.CharacterClass, Looter = looter };
                    g.Count++;
                }
            }
            var nk = a.AwardedAt.UtcDateTime.Date;
            nights[nk] = nights.GetValueOrDefault(nk) + 1;
            if (a.Disenchanted)
            {
                var ik = ItemKey(a.ItemId, a.ItemName);
                if (!deItems.TryGetValue(ik, out var d))
                    deItems[ik] = d = new ItemAgg { Name = a.ItemName, Id = a.ItemId };
                d.Count++;
            }
        }

        int totalAwards = 0, totalRolls = 0, de = 0;
        Luckiest? luckiest = null;
        Contested? contested = null;
        SoClose? soClose = null;      // the highest roll that still lost
        Blowout? blowout = null;      // the widest margin a contested item was won by
        FirstWin? firstWin = null;    // won the biggest field on a maiden roll

        // Leaderboard pass — only contested awards (a roll happened).
        foreach (var a in awards)
        {
            if (a.Rolls.Count == 0) continue;
            totalAwards++;
            totalRolls += a.Rolls.Count;
            if (a.Disenchanted) de++;

            Guid? winnerId = a.Disenchanted ? null : a.CharacterId;
            var winRoll = winnerId is Guid wid0 ? a.Rolls.FirstOrDefault(r => r.PlayerId == wid0) : null;
            bool isOS = a.OffSpec || (winRoll is not null && string.Equals(winRoll.Classification, "OS", StringComparison.OrdinalIgnoreCase));
            bool contestedAward = a.Rolls.Count > 1;   // more than one bidder — a real contest
            int? winAmount = winRoll?.Amount;          // the winning roll, for margins and ties
            var nightDate = a.AwardedAt.UtcDateTime.Date;

            // The winner's accumulator, hoisted so the roll loop can credit them the people
            // they beat (taxman) and the items they collect (magpie).
            Agg? w = null;
            if (winnerId is Guid wid)
            {
                w = Get(wid, a.CharacterName, a.CharacterClass);
                w.Wins++;
                if (isOS) w.OsWins++;
                if (a.Rolls.Count >= 4) w.BigWins++;
                w.WonItems.Add(ItemKey(a.ItemId, a.ItemName));
            }

            var ik = ItemKey(a.ItemId, a.ItemName);
            if (!itemRolls.TryGetValue(ik, out var wr))
                itemRolls[ik] = wr = new ItemAgg { Name = a.ItemName, Id = a.ItemId };
            wr.Rolls += a.Rolls.Count;
            wr.Drops++;

            if (a.Rolls.Count > (contested?.Count ?? 1))
                contested = new Contested(a.Rolls.Count, a.ItemName, a.ItemId, a.Disenchanted ? "the shard pile" : a.CharacterName);

            // Per-award signals resolved once the whole field is seen: the highest losing
            // roll (for the blowout margin), and whether the winner beat a main-spec roller
            // or survived a tie.
            int maxLoser = -1;
            bool beatAnMs = false, survivedTie = false;

            foreach (var r in a.Rolls)
            {
                var c = Get(r.PlayerId, r.PlayerName, r.PlayerCls);
                bool firstRoll = c.Rolls == 0;  // their maiden roll, before this one is counted
                c.Rolls++;
                c.Sum += r.Amount;
                c.SumSq += (double)r.Amount * r.Amount;
                if (r.Amount == 100) c.Hundreds++;
                if (r.Amount == 1) c.Ones++;
                c.Nights.Add(nightDate);
                c.ItemBids[ik] = c.ItemBids.TryGetValue(ik, out var b)
                    ? (b.Name, b.Id, b.Count + 1) : (a.ItemName, a.ItemId, 1);

                bool won = winnerId is Guid wk && r.PlayerId == wk;
                if (won)
                {
                    c.Streak = 0;
                    c.WinStreak++;
                    if (c.WinStreak > c.BestWinStreak) c.BestWinStreak = c.WinStreak;
                    if (contestedAward && r.Amount <= 50) c.LowWins++;
                    // Beating nobody isn't luck, so a lone bid doesn't count as a steal.
                    if (contestedAward && (luckiest is null || r.Amount < luckiest.Amount))
                        luckiest = new Luckiest(r.Amount, c.Id, c.Name, c.Cls, a.ItemName, a.ItemId, a.Rolls.Count);
                    // Winning the biggest crowd you ever faced, on the first roll you ever placed.
                    if (firstRoll && contestedAward && (firstWin is null || a.Rolls.Count > firstWin.Field))
                        firstWin = new FirstWin(c.Id, c.Name, c.Cls, a.ItemName, a.ItemId, a.Rolls.Count);
                }
                else
                {
                    c.Losses++;
                    c.Streak++;
                    c.WinStreak = 0;
                    if (c.Streak > c.WorstStreak) c.WorstStreak = c.Streak;
                    if (r.Amount >= 90) c.NearMiss++;   // a 90-something that still lost
                    if (r.Amount > maxLoser) maxLoser = r.Amount;
                    if (string.Equals(r.Classification, "MS", StringComparison.OrdinalIgnoreCase)) beatAnMs = true;
                    if (winnerId is not null)
                    {
                        if (winAmount is int wa)
                        {
                            var margin = wa - r.Amount;
                            if (margin >= 0) { c.LossMarginSum += margin; c.LossMarginCount++; }
                            if (r.Amount == wa) survivedTie = true;   // a tie the winner won
                        }
                        // The highest roll anyone has ever lost with, to a real winner.
                        if (soClose is null || r.Amount > soClose.Amount)
                            soClose = new SoClose(r.Amount, c.Id, c.Name, c.Cls, a.ItemName, a.ItemId);
                    }
                    if (winnerId is Guid wk2)
                    {
                        c.LostTo[wk2] = c.LostTo.GetValueOrDefault(wk2) + 1;
                        w!.Victims.Add(r.PlayerId);
                        if (r.PlayerId != wk2)
                        {
                            var (lo, hi) = wk2.CompareTo(r.PlayerId) <= 0 ? (wk2, r.PlayerId) : (r.PlayerId, wk2);
                            if (!pairs.TryGetValue((lo, hi), out var p))
                                pairs[(lo, hi)] = p = new Pair { Lo = lo, Hi = hi };
                            if (wk2 == lo) p.LoWins++; else p.HiWins++;
                        }
                    }
                }
            }

            // Now the field is known: credit the winner an off-spec robbery / tie-break,
            // and see if this was the widest margin any contested item has been won by.
            if (w is not null)
            {
                if (isOS && beatAnMs) w.Robbery++;
                if (survivedTie) w.TieWins++;
                if (contestedAward && winAmount is int wamt && maxLoser >= 0)
                {
                    var margin = wamt - maxLoser;
                    if (margin >= 0 && (blowout is null || margin > blowout.Margin))
                        blowout = new Blowout(margin, wamt, maxLoser, w.Id, w.Name, w.Cls, a.ItemName, a.ItemId);
                }
            }
        }

        // Resolve each character's personal white whale: the item they rolled on the most
        // times and never once won. Needs the whole pass first, so it sits out here.
        foreach (var c in order)
            foreach (var (key, bid) in c.ItemBids)
                if (!c.WonItems.Contains(key) && bid.Count > c.WhaleCount)
                    { c.WhaleCount = bid.Count; c.WhaleName = bid.Name; c.WhaleId = bid.Id; }

        bool Enough(Agg c) => c.Rolls >= MinRolls;

        // A looter is an award-level name string; render it class-coloured only when the
        // roster holds a character by exactly that name, matching the old page's fallback.
        var byName = new Dictionary<string, Agg>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in order) byName.TryAdd(c.Name, c);
        object LooterRef(string name) => byName.TryGetValue(name, out var c) ? CharRef(c) : TextRef(name);

        // Who has beaten this character the most, resolved late so every name is known.
        (Agg foe, int count)? Nemesis(Agg c)
        {
            Guid best = default; int bestN = 0;
            foreach (var (k, n) in c.LostTo) if (n > bestN) { bestN = n; best = k; }
            return bestN > 0 && aggs.TryGetValue(best, out var foe) ? (foe, bestN) : null;
        }

        var cards = new List<object>();

        // ── Leaderboards ────────────────────────────────────────────────────
        object Board(string emoji, string title, string blurb, string desc, Func<Agg, bool>? eligible,
            Func<Agg, double> value, bool asc, bool keepZero, Func<double, string> fmt,
            Func<Agg, object[]>? detail)
        {
            var filtered = order
                .Where(c => eligible is null || eligible(c))
                .Select(c => (c, v: value(c)))
                .Where(e => !double.IsNaN(e.v) && (keepZero || e.v > 0));
            var ranked = (asc ? filtered.OrderBy(e => e.v) : filtered.OrderByDescending(e => e.v))
                .Take(3).ToList();
            var entries = ranked.Select(e => Entry(CharRef(e.c), fmt(e.v))).ToList();
            var det = entries.Count > 0 && detail is not null ? detail(ranked[0].c) : null;
            return Card(emoji, title, blurb, desc, entries, det);
        }

        cards.Add(Board("💔", "Most rolls lost", "Turned up, rolled, went home empty-handed. Again.",
            "Rolls placed that didn't win the item.",
            null, c => c.Losses, false, false, Int, c => Text($"{Plural(c.Rolls, "roll")} → {Plural(c.Wins, "win")}")));
        cards.Add(Board("🎯", "Most 100s", "The dice gods pick favourites, and it is these people.",
            "Rolls that came up a natural 100.",
            null, c => c.Hundreds, false, false, Int, c => Text($"in {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("💀", "Most 1s", "A perfect roll, just upside down.",
            "Rolls that came up a natural 1.",
            null, c => c.Ones, false, false, Int, c => Text($"in {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🗑️", "Most off-spec pieces", "\"It's only OS, I swear\" — someone, every single week.",
            "Items won and taken as off-spec.",
            null, c => c.OsWins, false, false, Int, c => Text($"of {Plural(c.Wins, "item")} won")));
        cards.Add(Board("🏆", "Most items won", "Statistically, they are wearing your gear.",
            "Contested items won outright.",
            null, c => c.Wins, false, false, Int, c => Text($"from {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🤲", "Greediest", "Rolls on everything. Absolutely everything.",
            "Total rolls placed, win or lose.",
            null, c => c.Rolls, false, false, Int, c => Text($"{Plural(c.Wins, "win")}, {Plural(c.Losses, "loss", "losses")}")));
        cards.Add(Board("📉", "Worst average roll", "Cursed. There is no other explanation.",
            "Lowest mean roll (min 5 rolls).",
            Enough, c => c.Avg, true, false, F1, c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("📈", "Suspiciously good average", "Nobody is accusing anyone of anything.",
            "Highest mean roll (min 5 rolls).",
            Enough, c => c.Avg, false, false, F1, c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🥈", "Perpetual bridesmaid", "Worst win rate of anyone who rolls regularly.",
            "Lowest win rate (min 5 rolls).",
            Enough, c => c.WinRate, true, true, Pct, c => Text($"{Plural(c.Wins, "win")} from {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🍀", "Best win rate", "Same raid, same boss, completely different luck.",
            "Highest win rate (min 5 rolls).",
            Enough, c => c.WinRate, false, false, Pct, c => Text($"{Plural(c.Wins, "win")} from {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🌵", "Longest dry spell", "Consecutive losing rolls without a single win in between.",
            "Longest run of losing rolls in a row.",
            null, c => c.WorstStreak, false, false, v => Plural((int)v, "loss", "losses"),
            c => Text(c.Wins > 0 ? $"they did eventually win {Plural(c.Wins, "item")}" : "still never won anything")));
        cards.Add(Board("🔥", "On fire", "Quit while you're ahead. Nobody ever does.",
            "Longest run of winning rolls in a row.",
            null, c => c.BestWinStreak, false, false, v => Plural((int)v, "win"),
            c => Text($"of {Plural(c.Wins, "win")} total")));
        cards.Add(Board("😤", "Biggest nemesis", "The one person who keeps taking their loot.",
            "Most losses to one other player.",
            null, c => Nemesis(c)?.count ?? 0, false, false, v => Plural((int)v, "time"),
            c => { var n = Nemesis(c); return Text(n is null ? "" : $"beaten by {n.Value.foe.Name}"); }));
        cards.Add(Board("🎢", "Feast or famine", "No middle gear. A 97 or a 4, and nothing in between.",
            "Highest roll-to-roll spread (min 5 rolls).",
            Enough, c => c.Std, false, false, v => "±" + v.ToString("F1", CultureInfo.InvariantCulture),
            c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("📏", "Old reliable", "Rolls the same number every week, with grim certainty.",
            "Lowest roll-to-roll spread (min 5 rolls).",
            Enough, c => c.Std, true, false, v => "±" + v.ToString("F1", CultureInfo.InvariantCulture),
            c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("😩", "The 99 club", "A 90-something, and it still wasn't enough. Again.",
            "Losing rolls of 90 or more.",
            null, c => c.NearMiss, false, false, Int, c => Text($"of {Plural(c.Losses, "loss", "losses")}")));
        cards.Add(Board("💢", "Death by inches", "Always close. Never quite close enough.",
            "Smallest average losing margin (min 5 losses).",
            c => c.LossMarginCount >= MinRolls, c => c.AvgLossMargin, true, true, F1,
            c => Text($"points, averaged over {Plural(c.LossMarginCount, "loss", "losses")}")));
        cards.Add(Board("🧛", "The taxman", "Everyone pays, sooner or later. No exemptions.",
            "Number of different players beaten.",
            null, c => c.Victims.Count, false, false, v => Plural((int)v, "victim"), c => Text($"across {Plural(c.Wins, "win")}")));
        cards.Add(Board("🦅", "Master-spec robbery", "Grabbed it for off-spec, right out from under someone's main.",
            "Off-spec wins that beat a main-spec roller.",
            null, c => c.Robbery, false, false, Int, c => Text($"of {Plural(c.Wins, "win")} won")));
        cards.Add(Board("🃏", "Winning ugly", "No style, no shame, still your loot.",
            "Contested wins on a roll of 50 or less.",
            null, c => c.LowWins, false, false, Int, c => Text($"of {Plural(c.Wins, "win")} won")));
        cards.Add(Board("🏇", "Photo finish", "Rolled the exact same number, and still walked off with it.",
            "Contested wins decided on a tied roll.",
            null, c => c.TieWins, false, false, v => Plural((int)v, "tie"), c => Text($"from {Plural(c.Wins, "win")}")));
        cards.Add(Board("🔭", "The sniper", "Waits for a crowd to gather, then walks off with it.",
            "Wins where four or more people rolled.",
            null, c => c.BigWins, false, false, Int, c => Text($"of {Plural(c.Wins, "win")} won")));
        cards.Add(Board("🎒", "The magpie", "Not the most items — the most different ones. A collection.",
            "Number of different items won.",
            null, c => c.WonItems.Count, false, false, v => Plural((int)v, "item"), c => Text($"from {Plural(c.Wins, "win")}")));
        cards.Add(Board("🎣", "Personal white whale", "One item, chased drop after drop, never once won.",
            "Most rolls on a single item they never won.",
            c => c.WhaleCount >= 3, c => c.WhaleCount, false, false, v => Plural((int)v, "roll"),
            c => new[] { TextSeg("on "), ItemRef(c.WhaleId, c.WhaleName ?? "?"), TextSeg(", still nothing") }));
        cards.Add(Board("🗓️", "The regular", "Never misses a raid. Rarely troubles the loot.",
            "Number of separate nights they rolled on.",
            null, c => c.Nights.Count, false, false, v => Plural((int)v, "night"),
            c => Text($"{Plural(c.Rolls, "roll")}, {Plural(c.Wins, "win")}")));

        // ── Class leaderboards — the same questions asked of a whole class ────
        var classAgg = new Dictionary<string, (string Disp, int Rolls, int Wins, int Raiders)>();
        foreach (var c in order)
        {
            if (string.IsNullOrWhiteSpace(c.Cls)) continue;
            var key = ClassKey(c.Cls!);
            if (key.Length == 0) continue;
            if (!classAgg.TryGetValue(key, out var ca)) ca = (c.Cls!, 0, 0, 0);
            ca.Rolls += c.Rolls; ca.Wins += c.Wins; if (c.Rolls > 0) ca.Raiders++;
            classAgg[key] = ca;
        }

        var luckyClasses = classAgg
            .Where(kv => kv.Value.Rolls >= 20)
            .OrderByDescending(kv => (double)kv.Value.Wins / kv.Value.Rolls)
            .Take(3).ToList();
        if (luckyClasses.Count > 0)
        {
            var entries = luckyClasses
                .Select(kv => Entry(ClassRef(kv.Value.Disp, kv.Key), Pct((double)kv.Value.Wins / kv.Value.Rolls)))
                .ToList();
            var top = luckyClasses[0].Value;
            cards.Add(Card("🎰", "Luckiest class", "The dice clearly have a favourite. Statistically speaking.",
                "Win rate by class (min 20 rolls).", entries,
                Text($"{Plural(top.Wins, "win")} from {Plural(top.Rolls, "roll")}")));
        }

        var greedyClasses = classAgg
            .Where(kv => kv.Value.Raiders > 0 && kv.Value.Rolls > 0)
            .OrderByDescending(kv => (double)kv.Value.Rolls / kv.Value.Raiders)
            .Take(3).ToList();
        if (greedyClasses.Count > 0)
        {
            var entries = greedyClasses
                .Select(kv => Entry(ClassRef(kv.Value.Disp, kv.Key), F1((double)kv.Value.Rolls / kv.Value.Raiders)))
                .ToList();
            var top = greedyClasses[0].Value;
            cards.Add(Card("🐷", "Greediest class", "Some classes roll on everything, as a bloc.",
                "Average rolls per raider, by class.", entries,
                Text($"{Plural(top.Rolls, "roll")} across {Plural(top.Raiders, "raider")}")));
        }

        // ── One-off records ──────────────────────────────────────────────────
        if (luckiest is not null)
            cards.Add(Record("🎲", "Cheekiest win", "The lowest roll that somehow still won a contested item.",
                "Lowest roll that won a contested item.",
                CharRef(luckiest.Id, luckiest.Name, luckiest.Cls), luckiest.Amount.ToString(),
                new[] { TextSeg("won "), ItemRef(luckiest.ItemId, luckiest.ItemName),
                        TextSeg($" against {luckiest.Field - 1} other {(luckiest.Field == 2 ? "roller" : "rollers")}") }));

        if (soClose is not null)
            cards.Add(Record("😱", "So close", "The single highest roll that still went home with nothing.",
                "The highest roll that ever lost.",
                CharRef(soClose.Id, soClose.Name, soClose.Cls), soClose.Amount.ToString(),
                new[] { TextSeg("rolled it on "), ItemRef(soClose.ItemId, soClose.ItemName), TextSeg(" — beaten anyway") }));

        if (blowout is not null)
            cards.Add(Record("💥", "The blowout", "Won so hard the next roll wasn't even close.",
                "The widest margin a contested item was won by.",
                CharRef(blowout.Id, blowout.Name, blowout.Cls), blowout.Margin.ToString(),
                new[] { TextSeg("won "), ItemRef(blowout.ItemId, blowout.ItemName),
                        TextSeg($" — {blowout.WinRoll} to {blowout.NextRoll}") }));

        if (firstWin is not null)
            cards.Add(Record("🍼", "One and done", "Beginner's luck, immortalised.",
                "Won on the very first roll they ever placed.",
                CharRef(firstWin.Id, firstWin.Name, firstWin.Cls), "1st",
                new[] { TextSeg("won "), ItemRef(firstWin.ItemId, firstWin.ItemName),
                        TextSeg($" against {firstWin.Field - 1} {(firstWin.Field == 2 ? "other" : "others")}, first roll ever") }));

        if (contested is not null)
            cards.Add(Record("⚔️", "Most contested item", "The item that started the most arguments.",
                "The item with the most rolls on a single drop.",
                ItemRef(contested.Id, contested.Name), contested.Count.ToString(),
                new[] { TextSeg($"rolls — it went to {contested.Winner ?? "nobody"}") }));

        // Bitterest rivalry — the pair who contested each other the most, and the split.
        Pair? riv = null;
        foreach (var p in pairs.Values) if (riv is null || p.LoWins + p.HiWins > riv.LoWins + riv.HiWins) riv = p;
        if (riv is not null && riv.LoWins + riv.HiWins >= 4)
        {
            bool loLeads = riv.LoWins >= riv.HiWins;
            var hi = aggs[loLeads ? riv.Lo : riv.Hi];
            var lo = aggs[loLeads ? riv.Hi : riv.Lo];
            int hiN = loLeads ? riv.LoWins : riv.HiWins, loN = loLeads ? riv.HiWins : riv.LoWins;
            cards.Add(Record("🥊", "Bitterest rivalry", "Two names that turn up in each other's losses again and again.",
                "The pair who beat each other most often.",
                CharRef(hi), $"{hiN}–{loN}",
                new[] { TextSeg("over "), CharRef(lo), TextSeg($", across {Plural(riv.LoWins + riv.HiWins, "contest")}") }));
        }

        // The generous hand — the master looter who has handed out the most, with runners-up.
        var topLooters = looters.OrderByDescending(e => e.Value).Take(4).ToList();
        if (topLooters.Count > 0 && topLooters[0].Value > 0)
        {
            var rest = topLooters.Skip(1).Select(l => Entry(LooterRef(l.Key), l.Value.ToString())).ToList();
            cards.Add(RecordWithRest("🎅", "The generous hand", "Hands out everyone else's loot all night. A saint, allegedly.",
                "Master looters who handed out the most items.",
                LooterRef(topLooters[0].Key), topLooters[0].Value.ToString(),
                Text($"{Plural(topLooters[0].Value, "item")} handed out"), rest));
        }

        // Teacher's pet — the winner who took the most from one particular master looter.
        Gift? pet = null;
        foreach (var g in gifts.Values) if (pet is null || g.Count > pet.Count) pet = g;
        if (pet is not null && pet.Count >= 3)
            cards.Add(Record("🐶", "Teacher's pet", "Nobody is accusing anyone of anything. Just noting it down.",
                "Most items received from one master looter.",
                CharRef(pet.WinnerId, pet.WinnerName, pet.WinnerCls), pet.Count.ToString(),
                new[] { TextSeg($"{Plural(pet.Count, "item")} from "), LooterRef(pet.Looter) }));

        // Busiest loot night — the single night the boss table gave up entirely.
        KeyValuePair<DateTime, int>? night = null;
        foreach (var n in nights) if (night is null || n.Value > night.Value.Value) night = n;
        if (night is not null && night.Value.Value > 1)
        {
            var label = night.Value.Key.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture);
            cards.Add(Record("📅", "Busiest loot night", "The night the loot simply would not stop coming.",
                "The single night the most items were awarded.",
                TextRef(label), night.Value.Value.ToString(),
                new[] { TextSeg($"{Plural(night.Value.Value, "item")} awarded in one night") }));
        }

        // Shard bait — the item nobody wanted, disenchanted the most.
        ItemAgg? shard = null;
        foreach (var d in deItems.Values) if (shard is null || d.Count > shard.Count) shard = d;
        if (shard is not null && shard.Count > 1)
            cards.Add(Record("🧲", "Shard bait", "Dropped and dropped, and wanted by absolutely no one.",
                "The item disenchanted the most times.",
                ItemRef(shard.Id, shard.Name), shard.Count.ToString(),
                new[] { TextSeg($"disenchanted {Plural(shard.Count, "time")}") }));

        // The white whale — the item fought over the most, across more than one drop.
        ItemAgg? whale = null;
        foreach (var wI in itemRolls.Values) if (wI.Drops >= 2 && (whale is null || wI.Rolls > whale.Rolls)) whale = wI;
        if (whale is not null)
            cards.Add(Record("🐋", "The white whale", "The item the guild has fought over the most, drop after drop.",
                "The item with the most rolls across all its drops.",
                ItemRef(whale.Id, whale.Name), whale.Rolls.ToString(),
                new[] { TextSeg($"rolls across {Plural(whale.Drops, "drop")}") }));

        var summary = new object[]
        {
            new { label = "Items rolled for", value = totalAwards.ToString() },
            new { label = "Rolls placed", value = totalRolls.ToString() },
            new { label = "Characters rolling", value = order.Count(c => c.Rolls > 0).ToString() },
            new { label = "Disenchanted", value = de.ToString() },
        };

        return new { summary, cards };
    }

    // ── Formatting helpers (kept identical to the old client-side ones) ──────
    private static string Int(double v) => ((int)v).ToString(CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
    private static string Pct(double v) => Math.Round(v * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
    // `many` covers the words a trailing "s" gets wrong (loss → losses).
    private static string Plural(int n, string word, string? many = null) => $"{n} {(n == 1 ? word : many ?? word + "s")}";

    // ── Card / ref builders — the JSON the dumb renderer consumes ────────────
    // A ref is one of: {kind:"char",name,cls,id} · {kind:"item",name,id} · {kind:"text",text}.
    private static object CharRef(Agg c) => new { kind = "char", name = c.Name, cls = c.Cls, id = c.Id };
    private static object CharRef(Guid id, string? name, string? cls) => new { kind = "char", name = name ?? "?", cls, id };
    // A class tile borrows the char ref: the same class-colouring, but no id so the page
    // renders it as plain coloured text rather than a link to a character sheet.
    private static object ClassRef(string display, string key) => new { kind = "char", name = display, cls = key, id = (Guid?)null };
    // Normalised class key — letters only, lower-case — so "Death Knight" and "deathknight"
    // fold together, matching how the page keys its class colours.
    private static string ClassKey(string cls) => new string(cls.ToLowerInvariant().Where(char.IsLetter).ToArray());
    private static object ItemRef(long? id, string name) => new { kind = "item", name, id };
    private static object TextRef(string text) => new { kind = "text", text };
    private static object TextSeg(string text) => new { kind = "text", text };
    private static object[] Text(string text) => new[] { TextSeg(text) };

    private static object Entry(object @ref, string value) => new { @ref, value };
    // `desc` is the plain-English "what this measures" line; `blurb` stays the joke.
    private static object Card(string emoji, string title, string blurb, string desc, List<object> entries, object[]? detail) =>
        new { emoji, title, blurb, desc, entries, detail };
    private static object Record(string emoji, string title, string blurb, string desc, object headline, string value, object[] detail) =>
        new { emoji, title, blurb, desc, entries = new List<object> { Entry(headline, value) }, detail };
    private static object RecordWithRest(string emoji, string title, string blurb, string desc, object headline, string value, object[] detail, List<object> rest)
    {
        var entries = new List<object> { Entry(headline, value) };
        entries.AddRange(rest);
        return new { emoji, title, blurb, desc, entries, detail };
    }

    private record Luckiest(int Amount, Guid Id, string Name, string? Cls, string ItemName, long? ItemId, int Field);
    private record Contested(int Count, string Name, long? Id, string? Winner);
    private record SoClose(int Amount, Guid Id, string Name, string? Cls, string ItemName, long? ItemId);
    private record Blowout(int Margin, int WinRoll, int NextRoll, Guid Id, string Name, string? Cls, string ItemName, long? ItemId);
    private record FirstWin(Guid Id, string Name, string? Cls, string ItemName, long? ItemId, int Field);

    private static IResult DbUnavailable() =>
        Results.Json(new { error = "unavailable", detail = "Persistence is not configured." }, statusCode: 503);
}
