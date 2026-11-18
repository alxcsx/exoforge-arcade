using System.Collections.Generic;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

[ExoSingletonResource("boss_state")]
public record BossState(string Id /*always "current"*/, string BossId /*"boss-<unixtime>-<gen>"*/, string Name, int MaxHp, int Hp,
    string Status /*"alive"|"defeated"*/, string PayoutState /*"none"|"claimed"|"paid"*/, int Inflight, int Pool, int Generation, long SpawnedAt, long DefeatedAt);

[ExoResource("boss_damage", PrimaryKey = nameof(Id))]
public record BossDamage(string Id /*"{BossId}:{PlayerId}"*/, [property: ExoColumn(Filterable = true)] string BossId, string PlayerId, string Name, int Damage, int Hits, int Coins);

public record HitAck(bool Accepted, int Hp, int MaxHp, string Status);

public record BossLeader(string PlayerId, string Name, int Damage, int Coins);

public record BossView(string BossId, string Name, int MaxHp, int Hp, string Status, string PayoutState, long SpawnedAt, int TotalDamage, int MyDamage, int MyCoins, IReadOnlyList<BossLeader> Top /*5, damage desc then player_id asc*/);

[ExoEvent("boss_spawned", Topic = "world_boss:live")]
public record BossSpawned(string BossId, string Name, int MaxHp, long Timestamp);

[ExoEvent("boss_damaged", Topic = "world_boss:live")]
public record BossDamaged(string BossId, string PlayerId, string Name, int Damage, int Hp, int MaxHp, long Timestamp);

[ExoEvent("boss_defeated", Topic = "world_boss:live")]
public record BossDefeated(string BossId, string Name, int MaxHp, int TotalDamage, int Pool, IReadOnlyList<BossLeader> Payouts, long Timestamp);
