using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Data;

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

    public static void MapLootStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/loot/stats", async (HttpContext ctx, SessionTokenService tokens) =>
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

            return Results.Json(Aggregate(awards));
        });
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
        public double Sum, SumSq;
        public readonly HashSet<Guid> Victims = new();
        public readonly HashSet<string> WonItems = new();
        public readonly Dictionary<Guid, int> LostTo = new();

        public double Avg => Rolls > 0 ? Sum / Rolls : double.NaN;
        public double Std => Rolls > 0 ? Math.Sqrt(Math.Max(0, SumSq / Rolls - Avg * Avg)) : double.NaN;
        public double WinRate => Rolls > 0 ? (double)Wins / Rolls : double.NaN;
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

            foreach (var r in a.Rolls)
            {
                var c = Get(r.PlayerId, r.PlayerName, r.PlayerCls);
                c.Rolls++;
                c.Sum += r.Amount;
                c.SumSq += (double)r.Amount * r.Amount;
                if (r.Amount == 100) c.Hundreds++;
                if (r.Amount == 1) c.Ones++;

                bool won = winnerId is Guid wk && r.PlayerId == wk;
                if (won)
                {
                    c.Streak = 0;
                    // Beating nobody isn't luck, so a lone bid doesn't count as a steal.
                    if (a.Rolls.Count > 1 && (luckiest is null || r.Amount < luckiest.Amount))
                        luckiest = new Luckiest(r.Amount, c.Id, c.Name, c.Cls, a.ItemName, a.ItemId, a.Rolls.Count);
                }
                else
                {
                    c.Losses++;
                    c.Streak++;
                    if (c.Streak > c.WorstStreak) c.WorstStreak = c.Streak;
                    if (r.Amount >= 90) c.NearMiss++;   // a 90-something that still lost
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
        }

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
        object Board(string emoji, string title, string blurb, Func<Agg, bool>? eligible,
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
            return Card(emoji, title, blurb, entries, det);
        }

        cards.Add(Board("💔", "Most rolls lost", "Turned up, rolled, went home empty-handed. Again.",
            null, c => c.Losses, false, false, Int, c => Text($"{Plural(c.Rolls, "roll")} → {Plural(c.Wins, "win")}")));
        cards.Add(Board("🎯", "Most 100s", "The dice gods pick favourites, and it is these people.",
            null, c => c.Hundreds, false, false, Int, c => Text($"in {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("💀", "Most 1s", "A perfect roll, just upside down.",
            null, c => c.Ones, false, false, Int, c => Text($"in {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🗑️", "Most off-spec pieces", "\"It's only OS, I swear\" — someone, every single week.",
            null, c => c.OsWins, false, false, Int, c => Text($"of {Plural(c.Wins, "item")} won")));
        cards.Add(Board("🏆", "Most items won", "Statistically, they are wearing your gear.",
            null, c => c.Wins, false, false, Int, c => Text($"from {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🤲", "Greediest", "Rolls on everything. Absolutely everything.",
            null, c => c.Rolls, false, false, Int, c => Text($"{Plural(c.Wins, "win")}, {Plural(c.Losses, "loss", "losses")}")));
        cards.Add(Board("📉", "Worst average roll", "Cursed. There is no other explanation.",
            Enough, c => c.Avg, true, false, F1, c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("📈", "Suspiciously good average", "Nobody is accusing anyone of anything.",
            Enough, c => c.Avg, false, false, F1, c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🥈", "Perpetual bridesmaid", "Worst win rate of anyone who rolls regularly.",
            Enough, c => c.WinRate, true, true, Pct, c => Text($"{Plural(c.Wins, "win")} from {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🍀", "Best win rate", "Same raid, same boss, completely different luck.",
            Enough, c => c.WinRate, false, false, Pct, c => Text($"{Plural(c.Wins, "win")} from {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("🌵", "Longest dry spell", "Consecutive losing rolls without a single win in between.",
            null, c => c.WorstStreak, false, false, v => Plural((int)v, "loss", "losses"),
            c => Text(c.Wins > 0 ? $"they did eventually win {Plural(c.Wins, "item")}" : "still never won anything")));
        cards.Add(Board("😤", "Biggest nemesis", "The one person who keeps taking their loot.",
            null, c => Nemesis(c)?.count ?? 0, false, false, v => Plural((int)v, "time"),
            c => { var n = Nemesis(c); return Text(n is null ? "" : $"beaten by {n.Value.foe.Name}"); }));
        cards.Add(Board("🎢", "Feast or famine", "No middle gear. A 97 or a 4, and nothing in between.",
            Enough, c => c.Std, false, false, v => "±" + v.ToString("F1", CultureInfo.InvariantCulture),
            c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("📏", "Old reliable", "Rolls the same number every week, with grim certainty.",
            Enough, c => c.Std, true, false, v => "±" + v.ToString("F1", CultureInfo.InvariantCulture),
            c => Text($"over {Plural(c.Rolls, "roll")}")));
        cards.Add(Board("😩", "The 99 club", "A 90-something, and it still wasn't enough. Again.",
            null, c => c.NearMiss, false, false, Int, c => Text($"of {Plural(c.Losses, "loss", "losses")}")));
        cards.Add(Board("🧛", "The taxman", "Everyone pays, sooner or later. No exemptions.",
            null, c => c.Victims.Count, false, false, v => Plural((int)v, "victim"), c => Text($"across {Plural(c.Wins, "win")}")));
        cards.Add(Board("🔭", "The sniper", "Waits for a crowd to gather, then walks off with it.",
            null, c => c.BigWins, false, false, Int, c => Text($"of {Plural(c.Wins, "win")} won")));
        cards.Add(Board("🎒", "The magpie", "Not the most items — the most different ones. A collection.",
            null, c => c.WonItems.Count, false, false, v => Plural((int)v, "item"), c => Text($"from {Plural(c.Wins, "win")}")));

        // ── One-off records ──────────────────────────────────────────────────
        if (luckiest is not null)
            cards.Add(Record("🎲", "Cheekiest win", "The lowest roll that somehow still won a contested item.",
                CharRef(luckiest.Id, luckiest.Name, luckiest.Cls), luckiest.Amount.ToString(),
                new[] { TextSeg("won "), ItemRef(luckiest.ItemId, luckiest.ItemName),
                        TextSeg($" against {luckiest.Field - 1} other {(luckiest.Field == 2 ? "roller" : "rollers")}") }));

        if (contested is not null)
            cards.Add(Record("⚔️", "Most contested item", "The item that started the most arguments.",
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
                CharRef(hi), $"{hiN}–{loN}",
                new[] { TextSeg("over "), CharRef(lo), TextSeg($", across {Plural(riv.LoWins + riv.HiWins, "contest")}") }));
        }

        // The generous hand — the master looter who has handed out the most, with runners-up.
        var topLooters = looters.OrderByDescending(e => e.Value).Take(4).ToList();
        if (topLooters.Count > 0 && topLooters[0].Value > 0)
        {
            var rest = topLooters.Skip(1).Select(l => Entry(LooterRef(l.Key), l.Value.ToString())).ToList();
            cards.Add(RecordWithRest("🎅", "The generous hand", "Hands out everyone else's loot all night. A saint, allegedly.",
                LooterRef(topLooters[0].Key), topLooters[0].Value.ToString(),
                Text($"{Plural(topLooters[0].Value, "item")} handed out"), rest));
        }

        // Teacher's pet — the winner who took the most from one particular master looter.
        Gift? pet = null;
        foreach (var g in gifts.Values) if (pet is null || g.Count > pet.Count) pet = g;
        if (pet is not null && pet.Count >= 3)
            cards.Add(Record("🐶", "Teacher's pet", "Nobody is accusing anyone of anything. Just noting it down.",
                CharRef(pet.WinnerId, pet.WinnerName, pet.WinnerCls), pet.Count.ToString(),
                new[] { TextSeg($"{Plural(pet.Count, "item")} from "), LooterRef(pet.Looter) }));

        // Busiest loot night — the single night the boss table gave up entirely.
        KeyValuePair<DateTime, int>? night = null;
        foreach (var n in nights) if (night is null || n.Value > night.Value.Value) night = n;
        if (night is not null && night.Value.Value > 1)
        {
            var label = night.Value.Key.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture);
            cards.Add(Record("📅", "Busiest loot night", "The night the loot simply would not stop coming.",
                TextRef(label), night.Value.Value.ToString(),
                new[] { TextSeg($"{Plural(night.Value.Value, "item")} awarded in one night") }));
        }

        // Shard bait — the item nobody wanted, disenchanted the most.
        ItemAgg? shard = null;
        foreach (var d in deItems.Values) if (shard is null || d.Count > shard.Count) shard = d;
        if (shard is not null && shard.Count > 1)
            cards.Add(Record("🧲", "Shard bait", "Dropped and dropped, and wanted by absolutely no one.",
                ItemRef(shard.Id, shard.Name), shard.Count.ToString(),
                new[] { TextSeg($"disenchanted {Plural(shard.Count, "time")}") }));

        // The white whale — the item fought over the most, across more than one drop.
        ItemAgg? whale = null;
        foreach (var wI in itemRolls.Values) if (wI.Drops >= 2 && (whale is null || wI.Rolls > whale.Rolls)) whale = wI;
        if (whale is not null)
            cards.Add(Record("🐋", "The white whale", "The item the guild has fought over the most, drop after drop.",
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
    private static object ItemRef(long? id, string name) => new { kind = "item", name, id };
    private static object TextRef(string text) => new { kind = "text", text };
    private static object TextSeg(string text) => new { kind = "text", text };
    private static object[] Text(string text) => new[] { TextSeg(text) };

    private static object Entry(object @ref, string value) => new { @ref, value };
    private static object Card(string emoji, string title, string blurb, List<object> entries, object[]? detail) =>
        new { emoji, title, blurb, entries, detail };
    private static object Record(string emoji, string title, string blurb, object headline, string value, object[] detail) =>
        new { emoji, title, blurb, entries = new List<object> { Entry(headline, value) }, detail };
    private static object RecordWithRest(string emoji, string title, string blurb, object headline, string value, object[] detail, List<object> rest)
    {
        var entries = new List<object> { Entry(headline, value) };
        entries.AddRange(rest);
        return new { emoji, title, blurb, entries, detail };
    }

    private record Luckiest(int Amount, Guid Id, string Name, string? Cls, string ItemName, long? ItemId, int Field);
    private record Contested(int Count, string Name, long? Id, string? Winner);

    private static IResult DbUnavailable() =>
        Results.Json(new { error = "unavailable", detail = "Persistence is not configured." }, statusCode: 503);
}
