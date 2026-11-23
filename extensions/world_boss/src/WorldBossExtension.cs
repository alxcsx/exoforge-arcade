using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("world_boss", Version = "0.1.0", Category = "Game", Title = "World Boss", Icon = "❄️", Resources = [typeof(BossState), typeof(BossDamage)])]
public partial class WorldBossExtension
{
    public const int MaxHit = 100;
    public const int DefaultMaxHp = 1000;
    public const int DefaultPool = 1000;
    public const int StaleSeconds = 60;
    public static readonly string[] BossNames = { "Frostbeard", "Sir Meltsalot", "Old Man Winter" };

    /// <summary>All business state goes through the store; tests swap in an in-memory one.</summary>
    public static IBossStore Store { get; set; } = new SqlBossStore();

    public static Func<long> Now { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public Task OnExtensionStart() => RunRespawn();

    [ExoAction(Scope = "service")]
    public async Task<HitAck> ReportHit(string playerId, string name, int damage)
    {
        if (damage < 1 || damage > MaxHit)
            throw new ExoActionException("invalid_damage", $"Damage must be between 1 and {MaxHit}.");
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("invalid_player", "A player id is required.");
        if (string.IsNullOrEmpty(name)) name = playerId;

        var store = Store;
        var boss = store.Current();
        if (boss == null || boss.Status != "alive") return Ack(false, boss);

        // 1. One conditional UPDATE decides whether this hit lands; its row count is the signal.
        if (!store.TryApplyHit(boss.BossId, damage, Now())) return Ack(false, store.Current());

        // 2. + 3. The ledger row must be written before Release, so the payout winner never reads a
        // ledger that is missing an accepted hit. Release runs even if the write throws.
        try { store.AddDamage(boss.BossId, playerId, name, damage); }
        finally { store.Release(boss.BossId); }

        var now = store.Current() ?? boss;
        await Exo.Events.EmitAsync(new BossDamaged(boss.BossId, playerId, name, damage, now.Hp, now.MaxHp, Now()));

        // 4. Every accepted hit tries to claim the payout after its own Release. The claim only
        // succeeds for a defeated boss with inflight == 0, so whichever hit finishes last wins it,
        // and exactly one caller pays.
        if (store.TryClaimPayout(boss.BossId, false))
            await Pay(boss.BossId);

        return Ack(true, store.Current() ?? now);
    }

    [ExoAction(Scope = "player")]
    public async Task<BossView> GetState()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");

        var boss = Store.Current() ?? await SpawnNext(DefaultMaxHp, null);
        return View(boss, playerId);
    }

    [ExoAction(Scope = "service")]
    public async Task<BossView> SpawnBoss(int maxHp, bool force)
    {
        if (maxHp < 100 || maxHp > 100000)
            throw new ExoActionException("invalid_hp", "maxHp must be between 100 and 100000.");

        var current = Store.Current();
        var spawnable = current == null || (current.Status == "defeated" && current.PayoutState == "paid");
        if (!spawnable && !force)
            throw new ExoActionException("boss_alive", "A boss is still active; pass force to replace it.");

        return View(await SpawnNext(maxHp, current), "");
    }

    [ExoCron("*/10 * * * *", "respawn_boss", Description = "Spawns the next boss once the last one is defeated and paid; finishes stale payouts.")]
    public Task RespawnBoss() => RunRespawn();

    private async Task RunRespawn()
    {
        var store = Store;
        var boss = store.Current();
        if (boss == null) { await SpawnNext(DefaultMaxHp, null); return; }
        if (boss.Status == "alive") return;

        var age = Now() - boss.DefeatedAt;
        switch (boss.PayoutState)
        {
            case "none" when age > StaleSeconds:
                // Cron-only force claim: a crashed hit left inflight > 0, so drop that clause.
                if (store.TryClaimPayout(boss.BossId, true)) await Pay(boss.BossId);
                break;
            case "claimed" when age > StaleSeconds:
                // The claimer died mid-payout. Pay is re-runnable: the wallet dedupes by key.
                await Pay(boss.BossId);
                break;
            case "paid":
                await SpawnNext(DefaultMaxHp, boss);
                break;
        }
    }

    /// <summary>
    /// Idempotent payout: shares are floor(Pool * damage / total) in long arithmetic and the remainder
    /// goes to the top damager, so they sum to exactly Pool. Safe to re-run.
    /// </summary>
    private static async Task Pay(string bossId)
    {
        var store = Store;
        var boss = store.Current();
        if (boss == null || boss.BossId != bossId) return;

        var rows = store.Ledger(bossId)
            .OrderByDescending(r => r.Damage)
            .ThenBy(r => r.PlayerId, StringComparer.Ordinal)
            .ToList();
        long total = rows.Sum(r => (long)r.Damage);
        var shares = new long[rows.Count];

        if (total > 0)
        {
            long assigned = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                shares[i] = (long)boss.Pool * rows[i].Damage / total;
                assigned += shares[i];
            }
            shares[0] += boss.Pool - assigned;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (shares[i] <= 0) continue;

            // Crash window: if the process dies after the wallet applied the credit but before its
            // key row is stored (sub-millisecond), a re-run could credit this player twice. Accepted
            // and documented: the single claim winner plus the wallet key make it the only gap.
            await ExoServices.Wallet.CreditOnceAsync(new WalletCreditOnceRequest
            {
                PlayerId = rows[i].PlayerId,
                Amount = shares[i],
                Reason = "world_boss:" + bossId,
                Key = "payout:" + bossId + ":" + rows[i].PlayerId,
            });

            // Crash window (see above): the credit is applied; the ledger copy is informational.
            store.SetLedgerCoins(bossId, rows[i].PlayerId, (int)shares[i]);
        }

        // Crash window: a crash before MarkPaid leaves payout_state 'claimed'; the cron re-runs Pay.
        if (!store.MarkPaid(bossId)) return;

        var payouts = rows.Select((r, i) => new BossLeader(r.PlayerId, r.Name, r.Damage, (int)shares[i])).ToList();
        await Exo.Events.EmitAsync(new BossDefeated(bossId, boss.Name, boss.MaxHp, (int)total, boss.Pool, payouts, Now()));
    }

    private static async Task<BossState> SpawnNext(int maxHp, BossState? previous)
    {
        var generation = (previous?.Generation ?? 0) + 1;
        var now = Now();
        var next = new BossState("current", $"boss-{now}-{generation}", BossNames[generation % BossNames.Length],
            maxHp, maxHp, "alive", "none", 0, DefaultPool, generation, now, 0);
        Store.Spawn(next);
        await Exo.Events.EmitAsync(new BossSpawned(next.BossId, next.Name, next.MaxHp, now));
        return next;
    }

    private static HitAck Ack(bool accepted, BossState? boss) =>
        new(accepted, boss?.Hp ?? 0, boss?.MaxHp ?? 0, boss?.Status ?? "none");

    private static BossView View(BossState boss, string playerId)
    {
        var rows = Store.Ledger(boss.BossId)
            .OrderByDescending(r => r.Damage)
            .ThenBy(r => r.PlayerId, StringComparer.Ordinal)
            .ToList();
        var mine = rows.FirstOrDefault(r => r.PlayerId == playerId);
        var top = rows.Take(5).Select(r => new BossLeader(r.PlayerId, r.Name, r.Damage, r.Coins)).ToList();
        return new BossView(boss.BossId, boss.Name, boss.MaxHp, boss.Hp, boss.Status, boss.PayoutState, boss.SpawnedAt,
            rows.Sum(r => r.Damage), mine?.Damage ?? 0, mine?.Coins ?? 0, top);
    }
}
