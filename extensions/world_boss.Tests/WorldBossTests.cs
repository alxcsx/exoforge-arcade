using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;
using Xunit;

public class WorldBossTests : IDisposable
{
    private readonly ExoTestContext _host = ExoTestHost.Start("world_boss");
    private readonly WorldBossExtension _boss;
    private readonly MemoryBossStore _store = new();
    private readonly FakeWallet _wallet = new();
    private long _now = 1000;

    public WorldBossTests()
    {
        WorldBossExtension.Store = _store;
        WorldBossExtension.Now = () => _now;
        ExoServices.Wallet = _wallet;
        _host.CallerPlayerId = "p1";
        _boss = _host.Create<WorldBossExtension>();
    }

    public void Dispose()
    {
        WorldBossExtension.Store = new SqlBossStore();
        WorldBossExtension.Now = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ExoServices.Wallet = null!;
        _host.Dispose();
    }

    /// <summary>Reproduces the conditional UPDATEs of <see cref="SqlBossStore"/> under one lock.</summary>
    private sealed class MemoryBossStore : IBossStore
    {
        private readonly object _lock = new();
        private BossState? _current;
        private readonly Dictionary<string, BossDamage> _ledger = new();

        public BossState? Current() { lock (_lock) return _current; }

        public bool TryApplyHit(string bossId, int damage, long now)
        {
            lock (_lock)
            {
                if (_current == null || _current.BossId != bossId || _current.Status != "alive") return false;
                var survives = _current.Hp > damage;
                _current = _current with
                {
                    Hp = survives ? _current.Hp - damage : 0,
                    Status = survives ? "alive" : "defeated",
                    DefeatedAt = survives ? _current.DefeatedAt : now,
                    Inflight = _current.Inflight + 1,
                };
                return true;
            }
        }

        public void AddDamage(string bossId, string playerId, string name, int damage)
        {
            lock (_lock)
            {
                var key = bossId + ":" + playerId;
                _ledger[key] = _ledger.TryGetValue(key, out var row)
                    ? row with { Damage = row.Damage + damage, Hits = row.Hits + 1, Name = name }
                    : new BossDamage(key, bossId, playerId, name, damage, 1, 0);
            }
        }

        public void Release(string bossId)
        {
            lock (_lock)
            {
                if (_current != null && _current.BossId == bossId)
                    _current = _current with { Inflight = _current.Inflight - 1 };
            }
        }

        public bool TryClaimPayout(string bossId, bool force)
        {
            lock (_lock)
            {
                if (_current == null || _current.BossId != bossId || _current.Status != "defeated" || _current.PayoutState != "none") return false;
                if (!force && _current.Inflight != 0) return false;
                _current = _current with { PayoutState = "claimed" };
                return true;
            }
        }

        public IReadOnlyList<BossDamage> Ledger(string bossId)
        {
            lock (_lock) return _ledger.Values.Where(r => r.BossId == bossId).ToList();
        }

        public void SetLedgerCoins(string bossId, string playerId, int coins)
        {
            lock (_lock)
            {
                var key = bossId + ":" + playerId;
                if (_ledger.TryGetValue(key, out var row)) _ledger[key] = row with { Coins = coins };
            }
        }

        public bool MarkPaid(string bossId)
        {
            lock (_lock)
            {
                if (_current == null || _current.BossId != bossId || _current.PayoutState != "claimed") return false;
                _current = _current with { PayoutState = "paid" };
                return true;
            }
        }

        public void Spawn(BossState next) { lock (_lock) _current = next; }
    }

    /// <summary>Dedupes by key like wallet.credit_once, but records EVERY attempt so a double payout is visible.</summary>
    private sealed class FakeWallet : IWalletService
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, int> _attempts = new();
        private readonly Dictionary<string, long> _coins = new();

        public IReadOnlyDictionary<string, int> Attempts { get { lock (_lock) return new Dictionary<string, int>(_attempts); } }
        public IReadOnlyDictionary<string, long> Coins { get { lock (_lock) return new Dictionary<string, long>(_coins); } }

        public Task<WalletBalance?> CreditOnceAsync(WalletCreditOnceRequest request)
        {
            lock (_lock)
            {
                _attempts[request.Key] = _attempts.GetValueOrDefault(request.Key) + 1;
                if (_attempts[request.Key] == 1)
                    _coins[request.PlayerId] = _coins.GetValueOrDefault(request.PlayerId) + request.Amount;
                return Task.FromResult<WalletBalance?>(new WalletBalance { PlayerId = request.PlayerId, Coins = (int)_coins.GetValueOrDefault(request.PlayerId) });
            }
        }

        public Task<WalletBalance?> CreditAsync(WalletCreditRequest request) => throw new NotImplementedException();
        public Task<WalletBalance?> DebitAsync(WalletDebitRequest request) => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync() => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync(WalletGetBalanceRequest request) => throw new NotImplementedException();
    }

    private BossState Seed(int maxHp = 300, int generation = 1)
    {
        var next = new BossState("current", $"boss-{_now}-{generation}", "Frostbeard", maxHp, maxHp, "alive", "none", 0, 1000, generation, _now, 0);
        _store.Spawn(next);
        return next;
    }

    private IEnumerable<T> Emitted<T>(string name) =>
        _host.Events.EmittedEvents.Where(e => e.EventName == name).Select(e => (T)e.Payload);

    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<ExoActionException>(act)).Code;

    [Fact]
    public async Task HpFloorsAtZero_AndHitOnDeadBossIsRejected()
    {
        Seed(10);
        var ack = await _boss.ReportHit("p1", "P1", 100);
        Assert.True(ack.Accepted);
        Assert.Equal(0, ack.Hp);
        Assert.Equal("defeated", ack.Status);

        var late = await _boss.ReportHit("p1", "P1", 10);
        Assert.False(late.Accepted);
        Assert.Equal(0, _store.Current()!.Hp);
        Assert.Single(Emitted<BossDamaged>("boss_damaged"));
    }

    [Fact]
    public async Task HitWithoutABossIsNotAccepted()
    {
        var ack = await _boss.ReportHit("p1", "P1", 10);
        Assert.False(ack.Accepted);
        Assert.Empty(_host.Events.EmittedEvents);
    }

    [Fact]
    public async Task NoHits_MeansNoEventsAndNoPayout()
    {
        var boss = Seed(300);
        _now += 10_000;
        await _boss.RespawnBoss();

        var after = _store.Current()!;
        Assert.Equal(boss.BossId, after.BossId);
        Assert.Equal(300, after.Hp);
        Assert.Equal("alive", after.Status);
        Assert.Empty(_host.Events.EmittedEvents);
        Assert.Empty(_wallet.Attempts);
    }

    [Fact]
    public async Task ConcurrentBurst_PaysExactlyOnce()
    {
        for (var round = 0; round < 25; round++)
        {
            _now += 1000;
            var boss = Seed(300);
            var players = new[] { "pa", "pb", "pc" };

            // Exceptions are NOT swallowed: any failure surfaces through WhenAll.
            var acks = await Task.WhenAll(Enumerable.Range(0, 100)
                .Select(i => Task.Run(() => _boss.ReportHit(players[i % 3], "P" + (i % 3), 10))));

            Assert.Equal(30, acks.Count(a => a.Accepted));
            Assert.Equal(70, acks.Count(a => !a.Accepted));

            var final = _store.Current()!;
            Assert.Equal(0, final.Hp);
            Assert.Equal("defeated", final.Status);
            Assert.Equal("paid", final.PayoutState);
            Assert.Equal(0, final.Inflight);

            Assert.Equal(30, Emitted<BossDamaged>("boss_damaged").Count(e => e.BossId == boss.BossId));

            // Exactly one defeat event, carrying the complete ledger and the whole pool.
            var defeated = Emitted<BossDefeated>("boss_defeated").Where(e => e.BossId == boss.BossId).ToList();
            Assert.Single(defeated);
            Assert.Equal(300, defeated[0].Payouts.Sum(p => p.Damage));
            Assert.Equal(1000, defeated[0].Payouts.Sum(p => p.Coins));

            // Every payout key was attempted exactly once: no second payer.
            var attempts = _wallet.Attempts.Where(kv => kv.Key.StartsWith("payout:" + boss.BossId + ":")).ToList();
            Assert.Equal(defeated[0].Payouts.Count, attempts.Count);
            Assert.All(attempts, kv => Assert.Equal(1, kv.Value));
        }

        Assert.Equal(25 * 1000, _wallet.Coins.Values.Sum());
    }

    [Fact]
    public async Task EqualDamage_SplitsTheRemainderToTheFirstPlayer()
    {
        Seed(300);
        await _boss.ReportHit("pa", "PA", 100);
        await _boss.ReportHit("pb", "PB", 100);
        await _boss.ReportHit("pc", "PC", 100);

        Assert.Equal("paid", _store.Current()!.PayoutState);
        Assert.Equal(334, _wallet.Coins["pa"]);
        Assert.Equal(333, _wallet.Coins["pb"]);
        Assert.Equal(333, _wallet.Coins["pc"]);
    }

    [Fact]
    public async Task UnequalDamage_RemainderGoesToTheTopDamager()
    {
        Seed(300);
        await _boss.ReportHit("pa", "PA", 100);
        await _boss.ReportHit("pb", "PB", 100);
        await _boss.ReportHit("pc", "PC", 50);
        await _boss.ReportHit("pd", "PD", 50);

        // 333 + 333 + 166 + 166 = 998; the 2 left over go to the top damager (damage tie: player id order).
        Assert.Equal(335, _wallet.Coins["pa"]);
        Assert.Equal(333, _wallet.Coins["pb"]);
        Assert.Equal(166, _wallet.Coins["pc"]);
        Assert.Equal(166, _wallet.Coins["pd"]);
        Assert.Equal(1000, _wallet.Coins.Values.Sum());

        var payouts = Emitted<BossDefeated>("boss_defeated").Single().Payouts;
        Assert.Equal(new[] { "pa", "pb", "pc", "pd" }, payouts.Select(p => p.PlayerId).ToArray());
    }

    [Fact]
    public async Task CrashedHit_IsRecoveredByTheCronAfterTheStaleWindow_AndNoOneIsPaidTwice()
    {
        var boss = Seed(100);
        // A hit that landed and wrote its ledger row but died before Release: inflight stays 1.
        Assert.True(_store.TryApplyHit(boss.BossId, 100, _now));
        _store.AddDamage(boss.BossId, "p1", "P1", 100);

        // Inside the stale window the cron must not claim (the hit may still finish).
        _now += WorldBossExtension.StaleSeconds - 1;
        await _boss.RespawnBoss();
        Assert.Equal("none", _store.Current()!.PayoutState);
        Assert.Empty(_wallet.Attempts);

        // Past it, the cron force-claims and pays once.
        _now += 2;
        await _boss.RespawnBoss();
        Assert.Equal("paid", _store.Current()!.PayoutState);
        Assert.Equal(1000, _wallet.Coins["p1"]);
        Assert.Equal(1, _wallet.Attempts["payout:" + boss.BossId + ":p1"]);
        Assert.Single(Emitted<BossDefeated>("boss_defeated"));
    }

    [Fact]
    public async Task ClaimedButUnpaidBoss_IsPaidByTheCron_AfterTheStaleWindow()
    {
        var boss = Seed(100);
        Assert.True(_store.TryApplyHit(boss.BossId, 100, _now));
        _store.AddDamage(boss.BossId, "p1", "P1", 100);
        _store.Release(boss.BossId);
        Assert.True(_store.TryClaimPayout(boss.BossId, false)); // the claimer then "died"

        _now += WorldBossExtension.StaleSeconds + 1;
        await _boss.RespawnBoss();

        Assert.Equal("paid", _store.Current()!.PayoutState);
        Assert.Equal(1000, _wallet.Coins["p1"]);
        Assert.Equal(1, _wallet.Attempts["payout:" + boss.BossId + ":p1"]);
    }

    [Fact]
    public async Task RespawnLeavesAnAliveBossAlone_AndSpawnsTheNextOnceThePreviousIsPaid()
    {
        var first = Seed(100);
        _now += 5;
        await _boss.RespawnBoss();
        Assert.Equal(first.BossId, _store.Current()!.BossId);

        await _boss.ReportHit("p1", "P1", 100); // defeats and pays inline
        Assert.Equal("paid", _store.Current()!.PayoutState);

        _now += 5;
        await _boss.RespawnBoss();

        var next = _store.Current()!;
        Assert.NotEqual(first.BossId, next.BossId);
        Assert.Equal(2, next.Generation);
        Assert.Equal("alive", next.Status);
        Assert.Equal("none", next.PayoutState);
        Assert.Equal(next.MaxHp, next.Hp);
        Assert.Single(Emitted<BossSpawned>("boss_spawned"));
    }

    [Fact]
    public async Task SpawnBoss_RequiresForceOverAnAliveBoss()
    {
        var first = Seed(300);
        Assert.Equal("boss_alive", await CodeOf(() => _boss.SpawnBoss(100, false)));
        Assert.Equal(first.BossId, _store.Current()!.BossId);

        _now += 1;
        var view = await _boss.SpawnBoss(500, true);
        Assert.Equal(500, view.MaxHp);
        Assert.NotEqual(first.BossId, view.BossId);
        Assert.Equal("alive", view.Status);
    }

    [Fact]
    public async Task SpawnBoss_ValidatesMaxHp()
    {
        Assert.Equal("invalid_hp", await CodeOf(() => _boss.SpawnBoss(99, true)));
        Assert.Equal("invalid_hp", await CodeOf(() => _boss.SpawnBoss(100_001, true)));
    }

    [Fact]
    public async Task ReportHit_ValidatesItsArguments()
    {
        Seed(10);
        Assert.Equal("invalid_damage", await CodeOf(() => _boss.ReportHit("p1", "P1", 0)));
        Assert.Equal("invalid_damage", await CodeOf(() => _boss.ReportHit("p1", "P1", 101)));
        Assert.Equal("invalid_player", await CodeOf(() => _boss.ReportHit("", "P1", 10)));
        Assert.Equal(10, _store.Current()!.Hp);
    }

    [Fact]
    public async Task GetState_ReportsTheCallersOwnDamageAndTheTopFive()
    {
        Seed(300);
        await _boss.ReportHit("p1", "P1", 30);
        await _boss.ReportHit("p2", "P2", 90);
        await _boss.ReportHit("p1", "P1", 20);

        var view = await _boss.GetState();
        Assert.Equal(160, view.Hp);
        Assert.Equal(140, view.TotalDamage);
        Assert.Equal(50, view.MyDamage);
        Assert.Equal(new[] { "p2", "p1" }, view.Top.Select(t => t.PlayerId).ToArray());
    }

    [Fact]
    public async Task GetState_RequiresAPlayerSession()
    {
        Seed(300);
        _host.CallerPlayerId = "";
        Assert.Equal("unauthorized", await CodeOf(() => _boss.GetState()));
    }

    [Fact]
    public async Task GetState_SpawnsTheFirstBossWhenNoneExists()
    {
        var view = await _boss.GetState();
        Assert.Equal("alive", view.Status);
        Assert.Equal(WorldBossExtension.DefaultMaxHp, view.MaxHp);
        Assert.NotNull(_store.Current());
    }
}
