using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;
using Xunit;

namespace GameMenu.Tests;

public class GameMenuTests : IDisposable
{
    private readonly ExoTestContext _ctx = ExoTestHost.Start("game_menu");
    private readonly FakeWallet _wallet = new();
    private readonly GameMenuExtension _menu = new();

    public GameMenuTests()
    {
        _ctx.CallerPlayerId = "p1";
        ExoServices.Wallet = _wallet;
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public void StartSeedsNewsAndABrokenGiftOnce()
    {
        _menu.OnExtensionStart();
        Exo.Database.Put(new MenuNews("welcome", "edited in Studio", 1));
        _menu.OnExtensionStart();

        Assert.Equal("edited in Studio", Exo.Database.Get<MenuNews>("welcome")!.Headline);
        Assert.Equal(0, Exo.Database.Get<MenuGift>("welcome")!.Coins);
    }

    [Fact]
    public void SettingsDefaultThenSavedPerPlayer()
    {
        Assert.Equal(new PlayerSettings("p1", 80, false), _menu.GetSettings());

        _menu.SaveSettings(30, true);
        Assert.Equal(new PlayerSettings("p1", 30, true), _menu.GetSettings());

        _ctx.CallerPlayerId = "p2";
        Assert.Equal(80, _menu.GetSettings().Volume);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void SaveSettingsRejectsOutOfRangeVolume(int volume)
    {
        var ex = Assert.Throws<ExoActionException>(() => _menu.SaveSettings(volume, false));
        Assert.Equal("invalid_volume", ex.Code);
    }

    [Fact]
    public void SettingsNeedACaller()
    {
        _ctx.CallerPlayerId = null;
        var ex = Assert.Throws<ExoActionException>(() => _menu.GetSettings());
        Assert.Equal("unauthorized", ex.Code);
    }

    [Fact]
    public async Task ClaimGiftWithAZeroRowFailsAndRecordsNothing()
    {
        _menu.OnExtensionStart();

        var ex = await Assert.ThrowsAsync<ExoActionException>(() => _menu.ClaimGift());

        Assert.Equal("wallet_rejected", ex.Code);
        Assert.Contains("invalid_amount", ex.Message);
        Assert.Empty(_wallet.Credits);
        Assert.Null(Exo.Database.Get<GiftClaim>("p1"));
    }

    [Fact]
    public async Task ClaimGiftWhenTheWalletFailsForAnotherReasonRecordsNothing()
    {
        _menu.OnExtensionStart();
        Exo.Database.Put(new MenuGift("welcome", 100, "fixed"));
        _wallet.FailWith = new ExoActionException("wallet_down", "The wallet is unavailable.");

        var ex = await Assert.ThrowsAsync<ExoActionException>(() => _menu.ClaimGift());

        Assert.Equal("wallet_rejected", ex.Code);
        Assert.Contains("wallet_down", ex.Message);
        Assert.Null(Exo.Database.Get<GiftClaim>("p1"));
    }

    [Fact]
    public async Task ClaimGiftAfterFixingTheRowPaysOnce()
    {
        _menu.OnExtensionStart();
        Exo.Database.Put(new MenuGift("welcome", 100, "fixed"));

        var receipt = await _menu.ClaimGift();

        Assert.Equal(new GiftReceipt(100, 100), receipt);
        Assert.Equal(("p1", 100L), _wallet.Credits.Single());
        var again = await Assert.ThrowsAsync<ExoActionException>(() => _menu.ClaimGift());
        Assert.Equal("already_claimed", again.Code);
        Assert.Single(_wallet.Credits);
    }

    [Fact]
    public async Task ClaimGiftWithoutARowSaysSo()
    {
        var ex = await Assert.ThrowsAsync<ExoActionException>(() => _menu.ClaimGift());
        Assert.Equal("gift_missing", ex.Code);
    }

    private sealed class FakeWallet : IWalletService
    {
        public List<(string PlayerId, long Amount)> Credits { get; } = [];
        public ExoActionException? FailWith { get; set; }

        public Task<WalletBalance?> CreditAsync(WalletCreditRequest request)
        {
            if (FailWith != null) throw FailWith;
            if (request.Amount <= 0) throw new ExoActionException("invalid_amount", "Amount must be positive.");
            Credits.Add((request.PlayerId, request.Amount));
            return Task.FromResult<WalletBalance?>(new WalletBalance { PlayerId = request.PlayerId, Coins = Credits.Sum(c => c.Amount) });
        }

        public Task<WalletBalance?> GetBalanceAsync(WalletGetBalanceRequest request) => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync() => throw new NotImplementedException();
        public Task<WalletBalance?> DebitAsync(WalletDebitRequest request) => throw new NotImplementedException();
    }
}
