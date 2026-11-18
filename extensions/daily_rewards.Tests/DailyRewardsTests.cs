using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;
using Xunit;

public class DailyRewardsTests : IDisposable
{
    private readonly ExoTestContext _host = ExoTestHost.Start("daily_rewards");
    private readonly DailyRewardsExtension _rewards;
    private readonly FakeWallet _wallet = new();
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public DailyRewardsTests()
    {
        _host.CallerPlayerId = "p1";
        _rewards = _host.Create<DailyRewardsExtension>();
        _rewards.OnExtensionStart();
        DailyRewardsExtension.Clock = () => _now;
        ExoServices.Wallet = _wallet;
    }

    public void Dispose()
    {
        DailyRewardsExtension.Clock = () => DateTime.UtcNow;
        ExoServices.Wallet = null!;
        _host.Dispose();
    }

    private sealed class FakeWallet : IWalletService
    {
        public readonly Dictionary<string, long> Coins = new();
        public ExoActionException? NextError;

        public Task<WalletBalance?> CreditAsync(WalletCreditRequest request)
        {
            if (NextError != null) throw NextError;
            Coins[request.PlayerId] = Coins.GetValueOrDefault(request.PlayerId) + request.Amount;
            return Task.FromResult<WalletBalance?>(new WalletBalance { PlayerId = request.PlayerId, Coins = Coins[request.PlayerId] });
        }

        public Task<WalletBalance?> DebitAsync(WalletDebitRequest request) => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync() => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync(WalletGetBalanceRequest request) => throw new NotImplementedException();
    }

    [Fact]
    public void Startup_seeds_the_reward_table_once()
    {
        Assert.Equal(7, _host.Database.All<DailyReward>("daily_reward_table").Count);
        _rewards.OnExtensionStart();
        Assert.Equal(7, _host.Database.All<DailyReward>("daily_reward_table").Count);
    }

    [Fact]
    public async Task First_claim_pays_day_one()
    {
        var result = await _rewards.Claim();

        Assert.Equal(1, result.Day);
        Assert.Equal(100, result.Coins);
        Assert.Equal(100, _wallet.Coins["p1"]);
    }

    [Fact]
    public async Task Claiming_twice_on_the_same_day_is_refused()
    {
        await _rewards.Claim();

        var error = await Assert.ThrowsAsync<ExoActionException>(() => _rewards.Claim());

        Assert.Equal("already_claimed", error.Code);
        Assert.Equal(100, _wallet.Coins["p1"]);
    }

    [Fact]
    public async Task Claiming_on_consecutive_days_grows_the_streak()
    {
        await _rewards.Claim();
        _now = _now.AddDays(1);
        var second = await _rewards.Claim();
        _now = _now.AddDays(1);
        var third = await _rewards.Claim();

        Assert.Equal(2, second.Day);
        Assert.Equal(150, second.Coins);
        Assert.Equal(3, third.Day);
        Assert.Equal(450, _wallet.Coins["p1"]);
    }

    [Fact]
    public async Task Missing_a_day_resets_the_streak()
    {
        await _rewards.Claim();
        _now = _now.AddDays(1);
        await _rewards.Claim();
        _now = _now.AddDays(3);

        Assert.Equal(0, _rewards.GetStatus().Streak);
        Assert.Equal(1, (await _rewards.Claim()).Day);
    }

    [Fact]
    public async Task Streak_wraps_after_the_last_table_row()
    {
        for (var day = 1; day <= 7; day++)
        {
            await _rewards.Claim();
            _now = _now.AddDays(1);
        }

        var eighth = await _rewards.Claim();

        Assert.Equal(8, eighth.Day);
        Assert.Equal(100, eighth.Coins);
    }

    [Fact]
    public async Task Editing_the_table_changes_the_payout()
    {
        _host.Database.Put("daily_reward_table", "1", new DailyReward(1, 777));

        Assert.Equal(777, (await _rewards.Claim()).Coins);
    }

    [Fact]
    public async Task A_refused_wallet_credit_does_not_record_the_claim()
    {
        _wallet.NextError = new ExoActionException("wallet_down", "no");

        var error = await Assert.ThrowsAsync<ExoActionException>(() => _rewards.Claim());

        Assert.Equal("wallet_down", error.Code);
        Assert.True(_rewards.GetStatus().CanClaim);
    }
}
