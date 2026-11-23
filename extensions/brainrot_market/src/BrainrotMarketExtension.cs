using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("brainrot_market", Version = "0.1.0", Category = "Game", Title = "Brainrot Market", Icon = "📈",
    Resources = [typeof(MemeStock), typeof(Holding), typeof(Farm), typeof(StorefrontConfig), typeof(StorefrontReceipt)])]
public class BrainrotMarketExtension
{
    public const int IdleCapSeconds = 8 * 3600;
    public const int FarmBaseCost = 200;

    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    public static Random Rng { get; set; } = new();

    public void OnExtensionStart()
    {
        if (Exo.Database.All<MemeStock>().Count == 0)
        {
            Exo.Database.Put(new MemeStock("SKBD", "Skibidi Futures", 120, 120, 20, 400, 8));
            Exo.Database.Put(new MemeStock("SIGMA", "Sigma Grindset Holdings", 300, 300, 50, 900, 5));
            Exo.Database.Put(new MemeStock("OHIO", "Ohio Logistics", 80, 80, 10, 250, 12));
        }

        if (Exo.Database.First<StorefrontConfig>() == null)
            // Sample-only: this secret is public. A real storefront secret is set in Studio and never committed.
            Exo.Database.Put(new StorefrontConfig("main", "arcade-demo-secret"));
    }

    [ExoAction(Scope = "player")]
    public Portfolio GetPortfolio()
    {
        var playerId = Caller();
        var farm = Exo.Database.Get<Farm>(playerId);
        var holdings = Exo.Database.All<Holding>().Where(h => h.PlayerId == playerId && h.Shares > 0).ToList();
        return new Portfolio(holdings, farm?.Level ?? 0, PendingIdle(farm));
    }

    [ExoAction(Scope = "player")]
    public async Task<IdleCollected> CollectIdle()
    {
        var playerId = Caller();
        var farm = Exo.Database.Get<Farm>(playerId);
        var pending = PendingIdle(farm);
        if (farm == null || pending <= 0) return new IdleCollected(0, farm?.Level ?? 0);

        await CreditAsync(playerId, pending, "idle_income");
        Exo.Database.Put(farm with { LastCollectedAt = Clock() });
        return new IdleCollected(pending, farm.Level);
    }

    [ExoAction(Scope = "player")]
    public async Task<Portfolio> BuyFarm()
    {
        var playerId = Caller();
        var farm = Exo.Database.Get<Farm>(playerId) ?? new Farm(playerId, 0, Clock());
        var pending = PendingIdle(farm.Level == 0 ? null : farm);

        await DebitAsync(playerId, FarmBaseCost * (1 << farm.Level), "buy_farm");
        if (pending > 0) await CreditAsync(playerId, pending, "idle_income");
        Exo.Database.Put(new Farm(playerId, farm.Level + 1, Clock()));
        return GetPortfolio();
    }

    [ExoAction(Scope = "player")]
    public async Task<Trade> BuyShares(string symbol, int shares)
    {
        var playerId = Caller();
        var stock = Stock(symbol, shares);

        var cost = stock.Price * shares;
        await DebitAsync(playerId, cost, $"buy_{stock.Symbol}");
        var holding = Holding(playerId, stock.Symbol);
        Exo.Database.Put(holding with { Shares = holding.Shares + shares });
        await Exo.Events.EmitAsync(new TradeMade(playerId, stock.Symbol, "buy", shares));
        return new Trade(stock.Symbol, "buy", shares, stock.Price, cost);
    }

    [ExoAction(Scope = "player")]
    public async Task<Trade> SellShares(string symbol, int shares)
    {
        var playerId = Caller();
        var stock = Stock(symbol, shares);

        var holding = Holding(playerId, stock.Symbol);
        if (holding.Shares < shares)
            throw new ExoActionException("not_enough_shares", $"Holds {holding.Shares} {stock.Symbol}, tried to sell {shares}.");

        var proceeds = stock.Price * shares;
        Exo.Database.Put(holding with { Shares = holding.Shares - shares });
        await CreditAsync(playerId, proceeds, $"sell_{stock.Symbol}");
        await Exo.Events.EmitAsync(new TradeMade(playerId, stock.Symbol, "sell", shares));
        return new Trade(stock.Symbol, "sell", shares, stock.Price, proceeds);
    }

    [ExoCron("* * * * *", "tick_prices", Description = "Random-walks every meme stock and broadcasts the new prices.")]
    public async Task TickPrices()
    {
        var quotes = new List<Quote>();
        foreach (var stock in Exo.Database.All<MemeStock>())
        {
            var next = Walk(stock, Rng);
            Exo.Database.Put(next);
            quotes.Add(new Quote(next.Symbol, next.Price, stock.Price));
        }

        await Exo.Events.EmitAsync(new PricesTicked(quotes));
    }

    [ExoWebhook("storefront_purchase")]
    public async Task<WebhookResult> StorefrontPurchase(WebhookPayload payload)
    {
        var secret = Exo.Database.First<StorefrontConfig>()?.Secret ?? "";
        string? signature = null;
        payload.Headers?.TryGetValue("x-storefront-signature", out signature);
        if (!SignatureMatches(secret, payload.RawBody, signature))
            return new WebhookResult(401, "{\"error\":\"bad_signature\"}", false);

        var purchase = Receipt(payload.Json);
        if (Exo.Database.Get<StorefrontReceipt>(purchase.ReceiptId) != null)
            return new WebhookResult(200, "{\"status\":\"duplicate\"}", true);
        if (purchase.Coins <= 0 || string.IsNullOrEmpty(purchase.PlayerId))
            return new WebhookResult(400, "{\"error\":\"bad_receipt\"}", true);

        await CreditAsync(purchase.PlayerId, purchase.Coins, $"storefront_{purchase.ReceiptId}");
        Exo.Database.Put(purchase);
        return new WebhookResult(200, "{\"status\":\"credited\"}", true);
    }

    public static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static bool SignatureMatches(string secret, string body, string? signature) =>
        !string.IsNullOrEmpty(secret) && !string.IsNullOrEmpty(signature) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(secret, body)), Encoding.UTF8.GetBytes(signature));

    public static MemeStock Walk(MemeStock stock, Random rng)
    {
        var swing = (rng.NextDouble() * 2 - 1) * stock.VolatilityPercent / 100.0;
        var price = (int)Math.Round(stock.Price * (1 + swing));
        return stock with { Price = Math.Clamp(price, stock.MinPrice, stock.MaxPrice) };
    }

    private static StorefrontReceipt Receipt(JsonElement json)
    {
        return new StorefrontReceipt(
            json.GetProperty("receipt_id").GetString() ?? "",
            json.GetProperty("player_id").GetString() ?? "",
            json.GetProperty("coins").GetInt32());
    }

    private static int PendingIdle(Farm? farm)
    {
        if (farm == null) return 0;
        var seconds = Math.Clamp((Clock() - farm.LastCollectedAt).TotalSeconds, 0, IdleCapSeconds);
        return (int)(seconds * farm.Level / 5);
    }

    private static MemeStock Stock(string symbol, int shares)
    {
        if (shares <= 0) throw new ExoActionException("invalid_amount", "Shares must be positive.");
        return Exo.Database.Get<MemeStock>(symbol.ToUpperInvariant())
            ?? throw new ExoActionException("unknown_stock", $"No stock called {symbol}.");
    }

    private static Holding Holding(string playerId, string symbol) =>
        Exo.Database.Get<Holding>($"{playerId}:{symbol}") ?? new Holding($"{playerId}:{symbol}", playerId, symbol, 0);

    private static string Caller()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");
        return playerId;
    }

    private static Task CreditAsync(string playerId, int amount, string reason) =>
        ExoServices.Wallet.CreditAsync(new WalletCreditRequest { PlayerId = playerId, Amount = amount, Reason = reason });

    private static Task DebitAsync(string playerId, int amount, string reason) =>
        ExoServices.Wallet.DebitAsync(new WalletDebitRequest { PlayerId = playerId, Amount = amount, Reason = reason });
}

public record Portfolio(IReadOnlyList<Holding> Holdings, int FarmLevel, int PendingIdleCoins);
public record IdleCollected(int Coins, int FarmLevel);
public record Trade(string Symbol, string Side, int Shares, int Price, int Coins);
public record Quote(string Symbol, int Price, int Previous);

[ExoEvent("trade_made", Topic = "market:trades")]
public record TradeMade(string PlayerId, string Symbol, string Side, int Shares);

[ExoEvent("prices_ticked", Topic = "market:ticker")]
public record PricesTicked(IReadOnlyList<Quote> Quotes);

[ExoResource("meme_stocks", PrimaryKey = nameof(Symbol), AllowPlayerRead = true)]
public record MemeStock(
    [property: ExoColumn(Sortable = true)] string Symbol,
    string Name,
    int Price,
    int BasePrice,
    int MinPrice,
    int MaxPrice,
    int VolatilityPercent
);

[ExoResource("portfolios", PrimaryKey = nameof(Id))]
public record Holding(
    string Id,
    [property: ExoColumn(Filterable = true)] string PlayerId,
    string Symbol,
    int Shares
);

[ExoResource("farms", PrimaryKey = nameof(PlayerId))]
public record Farm(
    [property: ExoColumn(Filterable = true)] string PlayerId,
    int Level,
    DateTime LastCollectedAt
);

[ExoSingletonResource("storefront_config")]
public record StorefrontConfig(string Id, string Secret);

[ExoResource("storefront_receipts", PrimaryKey = nameof(ReceiptId))]
public record StorefrontReceipt(string ReceiptId, string PlayerId, int Coins);
