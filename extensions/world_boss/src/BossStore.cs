using System.Collections.Generic;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

public interface IBossStore
{
    BossState? Current();
    bool TryApplyHit(string bossId, int damage, long now);
    void AddDamage(string bossId, string playerId, string name, int damage);
    void Release(string bossId);
    bool TryClaimPayout(string bossId, bool force);
    IReadOnlyList<BossDamage> Ledger(string bossId);
    void SetLedgerCoins(string bossId, string playerId, int coins);
    bool MarkPaid(string bossId);
    void Spawn(BossState next);
}

/// <summary>Single conditional statements; <c>Execute</c>'s row count is the winner signal.</summary>
public class SqlBossStore : IBossStore
{
    public BossState? Current() => Exo.Database.First<BossState>();

    public bool TryApplyHit(string bossId, int damage, long now) =>
        Exo.Database.Execute(
            "UPDATE boss_state SET hp = CASE WHEN hp > $2 THEN hp - $2 ELSE 0 END, status = CASE WHEN hp > $2 THEN 'alive' ELSE 'defeated' END, defeated_at = CASE WHEN hp > $2 THEN defeated_at ELSE $3 END, inflight = inflight + 1 WHERE id='current' AND boss_id=$1 AND status='alive'",
            bossId, damage, now) == 1;

    public void AddDamage(string bossId, string playerId, string name, int damage) =>
        Exo.Database.Execute(
            "INSERT INTO boss_damage (id,boss_id,player_id,name,damage,hits,coins) VALUES ($1,$2,$3,$4,$5,1,0) ON CONFLICT(id) DO UPDATE SET damage = boss_damage.damage + excluded.damage, hits = boss_damage.hits + 1, name = excluded.name",
            bossId + ":" + playerId, bossId, playerId, name, damage);

    public void Release(string bossId) =>
        Exo.Database.Execute(
            "UPDATE boss_state SET inflight = inflight - 1 WHERE id='current' AND boss_id=$1",
            bossId);

    public bool TryClaimPayout(string bossId, bool force) =>
        Exo.Database.Execute(
            force
                ? "UPDATE boss_state SET payout_state='claimed' WHERE id='current' AND boss_id=$1 AND status='defeated' AND payout_state='none'"
                : "UPDATE boss_state SET payout_state='claimed' WHERE id='current' AND boss_id=$1 AND status='defeated' AND payout_state='none' AND inflight=0",
            bossId) == 1;

    public IReadOnlyList<BossDamage> Ledger(string bossId) =>
        Exo.Database.Query<BossDamage>(
            "SELECT * FROM boss_damage WHERE boss_id=$1 ORDER BY damage DESC, player_id ASC",
            bossId);

    public void SetLedgerCoins(string bossId, string playerId, int coins) =>
        Exo.Database.Execute(
            "UPDATE boss_damage SET coins = $3 WHERE boss_id=$1 AND player_id=$2",
            bossId, playerId, coins);

    public bool MarkPaid(string bossId) =>
        Exo.Database.Execute(
            "UPDATE boss_state SET payout_state='paid' WHERE id='current' AND boss_id=$1 AND payout_state='claimed'",
            bossId) == 1;

    public void Spawn(BossState next) => Exo.Database.Put(next);
}
