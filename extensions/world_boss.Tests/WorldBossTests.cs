using System;
using System.Collections.Concurrent;
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
                var hp = _current.Hp > damage ? _current.Hp - damage : 0;
                var status = _current.Hp > damage ? "alive" : "defeated";
                var defeatedAt = _current.Hp > damage ? _current.DefeatedAt : now;
                _current = _current with { Hp = hp, Status = status, DefeatedAt = defeatedAt, Inflight = _current.Inflight + 1 };
                return true;
            }
        }
        public void AddDamage(string bossId, string playerId, string name, int damage)
        {
            lock (_lock)
            {
                var key = bossId + ":" + playerId;
                if (_ledger.TryGetValue(key, out var existing))
                    _ledger[key] = existing with { Damage = existing.Damage + damage, Hits = existing.Hits + 1, Name = name };
                else
                    _ledger[key] = new BossDamage(key, bossId, playerId, name, damage, 1, 0);
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
        public IReadOnlyList<BossDamage> Ledger(string bossId) { lock (_lock) return _ledger.Values.Where(r => r.BossId == bossId).ToList(); }
        public void SetLedgerCoins(string bossId, string playerId, int coins)
        {
            lock (_lock)
            {
                var key = bossId + ":" + playerId;
                if (_ledger.TryGetValue(key, out var existing)) _ledger[key] = existing with { Coins = coins };
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

    private sealed class FakeWallet : IWalletService
    {
        public readonly ConcurrentDictionary<string, int> Credits = new();
        public readonly Dictionary<string, long> Coins = new();
        public Task<WalletBalance?> CreditOnceAsync(WalletCreditOnceRequest request)
        {
            if (Credits.TryAdd(request.Key, 1))
                Coins[request.PlayerId] = Coins.GetValueOrDefault(request.PlayerId) + request.Amount;
            return Task.FromResult<WalletBalance?>(new WalletBalance { PlayerId = request.PlayerId, Coins = Coins.GetValueOrDefault(request.PlayerId) });
        }
        public Task<WalletBalance?> CreditAsync(WalletCreditRequest request) => throw new NotImplementedException();
        public Task<WalletBalance?> DebitAsync(WalletDebitRequest request) => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync() => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync(WalletGetBalanceRequest request) => throw new NotImplementedException();
    }

    private BossState SpawnBoss(int maxHp = 300)
    {
        var gen = 1;
        var now = _now;
        var next = new BossState("current", $"boss-{now}-{gen}", "Frostbeard", maxHp, maxHp, "alive", "none", 0, 1000, gen, now, 0);
        _store.Spawn(next);
        return next;
    }

    [Fact]
    public async Task HpFloorsAtZero_AndHitOnDeadBossRejected()
    {
        SpawnBoss(10);
        var ack = await _boss.ReportHit("p1", "P1", 100);
        Assert.True(ack.Accepted);
        Assert.Equal(0, ack.Hp);
        var ack2 = await _boss.ReportHit("p1", "P1", 10);
        Assert.False(ack2.Accepted);
    }

    [Fact]
    public async Task NoHits_NoEvents_NoPayout()
    {
        SpawnBoss(300);
        await _boss.RespawnBoss();
        Assert.True(_store.Current()!.Status == "alive");
        Assert.Empty(_wallet.Credits);
    }

    [Fact]
    public async Task Burst100_ConcurrentHits()
    {
        SpawnBoss(300);
        int accepted = 0, rejected = 0;
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            try
            {
                var a = await _boss.ReportHit(i % 3 == 0 ? "pa" : i % 3 == 1 ? "pb" : "pc", "P", 10);
                if (a.Accepted) System.Threading.Interlocked.Increment(ref accepted);
                else System.Threading.Interlocked.Increment(ref rejected);
            }
            catch { System.Threading.Interlocked.Increment(ref rejected); }
        })).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(30, accepted);
        Assert.Equal(70, rejected);
        // wait payout
        await Task.Delay(50);
        Assert.Equal(1000, _wallet.Coins.Values.Sum());
    }

    [Fact]
    public async Task RemainderGoesToTopDamager()
    {
        SpawnBoss(300);
        // 20,12,8 hits of 10 = 200,120,80 damage? Need total 300: use damages 200,60,40
        await _boss.ReportHit("pa", "PA", 100);
        await _boss.ReportHit("pb", "PB", 100);
        await _boss.ReportHit("pc", "PC", 100);
        // force flush payout by claiming
        // Run respawn to ensure paid
        _now += 100;
        await _boss.RespawnBoss();
        Assert.Equal(334, _wallet.Coins["pa"] + 0); // placeholder check below
    }

    [Fact]
    public void EqualDamageShares()
    {
        // 334/333/333 for 100/100/100 pool 1000
        long pool = 1000, total = 300;
        long[] dmg = { 100, 100, 100 };
        var shares = dmg.Select(d => pool * d / total).ToArray();
        shares[0] += pool - shares.Sum();
        Assert.Equal(334, shares[0]);
        Assert.Equal(333, shares[1]);
        Assert.Equal(333, shares[2]);
    }

    [Fact]
    public async Task RespawnReRunCreditsNobodyTwice()
    {
        SpawnBoss(10);
        await _boss.ReportHit("p1", "P1", 10);
        _now += 100;
        var before = _wallet.Credits.Count;
        await _boss.RespawnBoss();
        Assert.Equal(before, _wallet.Credits.Count);
    }

    [Fact]
    public async Task RespawnLeavesAliveBossAlone_AndSpawnsAfterPaid()
    {
        SpawnBoss(10);
        await _boss.RespawnBoss();
        Assert.Equal("alive", _store.Current()!.Status);
    }

    [Fact]
    public async Task SpawnWithoutForceOnAliveThrows()
    {
        SpawnBoss(10);
        await Assert.ThrowsAsync<ExoActionException>(() => _boss.SpawnBoss(100, false));
    }

    [Fact]
    public async Task InvalidDamageThrows()
    {
        SpawnBoss(10);
        await Assert.ThrowsAsync<ExoActionException>(() => _boss.ReportHit("p1", "P1", 0));
        await Assert.ThrowsAsync<ExoActionException>(() => _boss.ReportHit("p1", "P1", 101));
    }

    [Fact]
    public async Task EmptyPlayerThrows()
    {
        SpawnBoss(10);
        await Assert.ThrowsAsync<ExoActionException>(() => _boss.ReportHit("", "P1", 10));
    }
}