using System.Text.Json;
using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;
using Xunit;

public class BrainrotMarketTests : IDisposable
{
    private readonly ExoTestContext _host = ExoTestHost.Start("brainrot_market");
    private readonly BrainrotMarketExtension _market;
    private readonly FakeWallet _wallet = new();
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public BrainrotMarketTests()
    {
        _host.CallerPlayerId = "p1";
        _market = _host.Create<BrainrotMarketExtension>();
        _market.OnExtensionStart();
        BrainrotMarketExtension.Clock = () => _now;
        BrainrotMarketExtension.Rng = new Random(7);
        ExoServices.Wallet = _wallet;
        _wallet.Coins["p1"] = 1000;
    }

    public void Dispose()
    {
        BrainrotMarketExtension.Clock = () => DateTime.UtcNow;
        BrainrotMarketExtension.Rng = new Random();
        ExoServices.Wallet = null!;
        _host.Dispose();
    }

    private sealed class FakeWallet : IWalletService
    {
        public readonly Dictionary<string, long> Coins = new();

        public Task<WalletBalance?> CreditAsync(WalletCreditRequest request)
        {
            Coins[request.PlayerId] = Coins.GetValueOrDefault(request.PlayerId) + request.Amount;
            return Task.FromResult<WalletBalance?>(new WalletBalance { PlayerId = request.PlayerId, Coins = Coins[request.PlayerId] });
        }

        public Task<WalletBalance?> DebitAsync(WalletDebitRequest request)
        {
            var balance = Coins.GetValueOrDefault(request.PlayerId);
            if (balance < request.Amount)
                throw new ExoActionException("insufficient_funds", "no");

            Coins[request.PlayerId] = balance - request.Amount;
            return Task.FromResult<WalletBalance?>(new WalletBalance { PlayerId = request.PlayerId, Coins = Coins[request.PlayerId] });
        }

        public Task<WalletBalance?> GetBalanceAsync() => throw new NotImplementedException();
        public Task<WalletBalance?> GetBalanceAsync(WalletGetBalanceRequest request) => throw new NotImplementedException();
    }

    private static WebhookPayload Signed(string body, string secret) => new()
    {
        RawBody = body, Json = JsonDocument.Parse(body).RootElement,
        Headers = new Dictionary<string, string> { ["x-storefront-signature"] = BrainrotMarketExtension.Sign(secret, body) }
    };

    [Fact]
    public void Startup_seeds_stocks_and_the_storefront_secret_once()
    {
        _market.OnExtensionStart();

        Assert.Equal(3, _host.Database.All<MemeStock>("meme_stocks").Count);
        Assert.Single(_host.Database.All<StorefrontConfig>("storefront_config"));
    }

    [Fact]
    public async Task Buying_with_funds_spends_coins_and_holds_shares()
    {
        var trade = await _market.BuyShares("skbd", 5);

        Assert.Equal(600, trade.Coins);
        Assert.Equal(400, _wallet.Coins["p1"]);
        Assert.Equal(5, _market.GetPortfolio().Holdings.Single().Shares);
        Assert.True(_host.Events.HasEmitted("trade_made", "market:trades"));
    }

    [Fact]
    public async Task Buying_without_funds_fails_and_holds_nothing()
    {
        var error = await Assert.ThrowsAsync<ExoActionException>(() => _market.BuyShares("SKBD", 1000));

        Assert.Equal("insufficient_funds", error.Code);
        Assert.Equal(1000, _wallet.Coins["p1"]);
        Assert.Empty(_market.GetPortfolio().Holdings);
        Assert.False(_host.Events.HasEmitted("trade_made", "market:trades"));
    }

    [Fact]
    public async Task Selling_pays_the_current_price()
    {
        await _market.BuyShares("SKBD", 4);
        _host.Database.Put("meme_stocks", "SKBD", _host.Database.Get<MemeStock>("meme_stocks", "SKBD")! with { Price = 200 });

        var trade = await _market.SellShares("SKBD", 3);

        Assert.Equal(600, trade.Coins);
        Assert.Equal(1000 - 480 + 600, _wallet.Coins["p1"]);
        Assert.Equal(1, _market.GetPortfolio().Holdings.Single().Shares);
    }

    [Fact]
    public async Task Selling_shares_not_held_fails()
    {
        await _market.BuyShares("SKBD", 1);

        var error = await Assert.ThrowsAsync<ExoActionException>(() => _market.SellShares("SKBD", 2));

        Assert.Equal("not_enough_shares", error.Code);
    }

    [Fact]
    public async Task Unknown_stocks_and_bad_share_counts_are_refused()
    {
        Assert.Equal("unknown_stock", (await Assert.ThrowsAsync<ExoActionException>(() => _market.BuyShares("NOPE", 1))).Code);
        Assert.Equal("invalid_amount", (await Assert.ThrowsAsync<ExoActionException>(() => _market.BuyShares("SKBD", 0))).Code);
    }

    [Fact]
    public async Task Ticking_keeps_every_price_inside_its_bounds_and_broadcasts()
    {
        _host.CallerPlayerId = null;
        for (var i = 0; i < 500; i++) await _market.TickPrices();

        foreach (var stock in _host.Database.All<MemeStock>("meme_stocks"))
            Assert.InRange(stock.Price, stock.MinPrice, stock.MaxPrice);
        Assert.True(_host.Events.HasEmitted("prices_ticked", "market:ticker"));
    }

    [Fact]
    public void A_walk_cannot_leave_the_bounds_even_from_the_edge()
    {
        var edge = new MemeStock("X", "X", 10, 10, 10, 12, 90);
        var rng = new Random(1);

        for (var i = 0; i < 200; i++)
            Assert.InRange(BrainrotMarketExtension.Walk(edge, rng).Price, 10, 12);
    }

    [Fact]
    public async Task Idle_income_is_capped_at_eight_hours_of_server_time()
    {
        await _market.BuyFarm();
        _now = _now.AddDays(3);

        var pending = _market.GetPortfolio().PendingIdleCoins;
        var collected = await _market.CollectIdle();

        Assert.Equal(BrainrotMarketExtension.IdleCapSeconds / 5, pending);
        Assert.Equal(pending, collected.Coins);
        Assert.Equal(0, _market.GetPortfolio().PendingIdleCoins);
    }

    [Fact]
    public async Task A_clock_set_back_earns_nothing()
    {
        await _market.BuyFarm();
        _now = _now.AddHours(-5);

        Assert.Equal(0, (await _market.CollectIdle()).Coins);
    }

    [Fact]
    public async Task A_second_farm_costs_double()
    {
        await _market.BuyFarm();
        await _market.BuyFarm();

        Assert.Equal(1000 - 200 - 400, _wallet.Coins["p1"]);
        Assert.Equal(2, _market.GetPortfolio().FarmLevel);
    }

    [Fact]
    public async Task A_signed_receipt_credits_the_wallet_once()
    {
        const string body = "{\"receipt_id\":\"r1\",\"player_id\":\"p2\",\"coins\":500}";
        var request = Signed(body, "arcade-demo-secret");

        Assert.Equal(200, (await _market.StorefrontPurchase(request)).StatusCode);
        var replay = await _market.StorefrontPurchase(request);

        Assert.Contains("duplicate", replay.Body);
        Assert.Equal(500, _wallet.Coins["p2"]);
    }

    [Fact]
    public async Task A_wrong_signature_is_rejected_and_pays_nothing()
    {
        const string body = "{\"receipt_id\":\"r2\",\"player_id\":\"p2\",\"coins\":500}";
        var request = Signed(body, "some-other-secret");

        var result = await _market.StorefrontPurchase(request);

        Assert.Equal(401, result.StatusCode);
        Assert.False(_wallet.Coins.ContainsKey("p2"));
    }

    [Fact]
    public async Task A_missing_signature_is_rejected()
    {
        const string body = "{\"receipt_id\":\"r3\",\"player_id\":\"p2\",\"coins\":500}";
        var request = Signed(body, "arcade-demo-secret");
        request.Headers = new Dictionary<string, string>();

        Assert.Equal(401, (await _market.StorefrontPurchase(request)).StatusCode);
    }

    [Fact]
    public async Task A_tampered_body_is_rejected()
    {
        var request = Signed("{\"receipt_id\":\"r4\",\"player_id\":\"p2\",\"coins\":5}", "arcade-demo-secret");
        request.RawBody = "{\"receipt_id\":\"r4\",\"player_id\":\"p2\",\"coins\":5000}";

        var result = await _market.StorefrontPurchase(request);

        Assert.Equal(401, result.StatusCode);
    }
}
