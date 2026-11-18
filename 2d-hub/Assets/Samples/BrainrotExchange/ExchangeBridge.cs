using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arcade.Common;
using Exoforge.Client;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.BrainrotExchange
{
    public class ExchangeBridge : MonoBehaviour
    {
        public const string TickerTopic = "market:ticker";
        public const string TradesTopic = "market:trades";

        public string PlayerName { get; private set; } = "";
        public long? Coins { get; private set; }
        public Dictionary<string, Stock> Stocks { get; } = new();
        public Dictionary<string, long> Shares { get; } = new();
        public PriceHistory History { get; } = new();
        public long FarmLevel { get; private set; }
        public long PendingIdle { get; private set; }
        public long DailyDay { get; private set; }
        public bool DailyAvailable { get; private set; }
        public List<string> Feed { get; } = new();
        public string Status { get; private set; } = "connecting…";
        public bool Busy { get; private set; }

        private void Start() => SamplePlayer.WhenReady(session => _ = LoadAsync(session));

        private void OnDestroy()
        {
            ExoServices.BrainrotMarket.OnPricesTicked -= OnTicked;
            ExoServices.BrainrotMarket.OnTradeMade -= OnTrade;
        }

        public async Task LoadAsync(ExoSession session)
        {
            PlayerName = session.DisplayName;
            ExoServices.BrainrotMarket.OnPricesTicked += OnTicked;
            ExoServices.BrainrotMarket.OnTradeMade += OnTrade;
            await Guarded("subscribe", async () =>
            {
                await ExoServices.BrainrotMarket.SubscribeAsync(TickerTopic);
                await ExoServices.BrainrotMarket.SubscribeAsync(TradesTopic);
            });
            await RefreshAsync();
        }

        public Task RefreshAsync() => Guarded("refresh", async () =>
        {
            await ReadStocksAsync();
            await ReadPortfolioAsync();
            Coins = (await ExoServices.Wallet.GetBalanceAsync()).Coins;
            var daily = await ExoServices.DailyRewards.GetStatusAsync();
            DailyAvailable = daily.CanClaim;
            DailyDay = daily.NextDay;
            Status = "";
        });

        public Task ClaimDailyAsync() => Guarded("daily", async () =>
        {
            var claim = await ExoServices.DailyRewards.ClaimAsync();
            Coins = claim.Balance;
            DailyAvailable = false;
            Status = $"day {claim.Day} bonus: +{claim.Coins}";
        });

        public Task CollectAsync() => Guarded("collect", async () =>
        {
            var collected = await ExoServices.BrainrotMarket.CollectIdleAsync();
            Status = $"collected {collected.Coins} idle coins";
            await RefreshCoinsAndFarmAsync();
        });

        public Task BuyFarmAsync() => Guarded("buy farm", async () =>
        {
            await ExoServices.BrainrotMarket.BuyFarmAsync();
            Status = "farm upgraded";
            await RefreshCoinsAndFarmAsync();
        });

        public Task BuyAsync(string symbol, int shares) => Guarded("buy", async () =>
        {
            var trade = await ExoServices.BrainrotMarket.BuySharesAsync(symbol, shares);
            Status = $"bought {trade.Shares} {trade.Symbol} for {trade.Coins}";
            await RefreshCoinsAndFarmAsync();
        });

        public Task SellAsync(string symbol, int shares) => Guarded("sell", async () =>
        {
            var trade = await ExoServices.BrainrotMarket.SellSharesAsync(symbol, shares);
            Status = $"sold {trade.Shares} {trade.Symbol} for {trade.Coins}";
            await RefreshCoinsAndFarmAsync();
        });

        private async Task RefreshCoinsAndFarmAsync()
        {
            await ReadPortfolioAsync();
            Coins = (await ExoServices.Wallet.GetBalanceAsync()).Coins;
        }

        private async Task ReadStocksAsync()
        {
            var list = await ExoServices.ResourceStore.ListAsync(new ResourceStoreListRequest { Resource = "meme_stocks" });
            foreach (var row in list.Rows.EnumerateArray())
            {
                var price = row.GetProperty("price").GetInt64();
                var symbol = row.GetProperty("symbol").GetString() ?? "";
                Stocks[symbol] = new Stock(symbol, row.GetProperty("name").GetString() ?? symbol, price, price,
                    row.GetProperty("min_price").GetInt64(), row.GetProperty("max_price").GetInt64());
                History.Add(symbol, price);
            }
        }

        private async Task ReadPortfolioAsync()
        {
            var portfolio = await ExoServices.BrainrotMarket.GetPortfolioAsync();
            FarmLevel = portfolio.FarmLevel;
            PendingIdle = portfolio.PendingIdleCoins;
            Shares.Clear();
            foreach (var holding in portfolio.Holdings.EnumerateArray())
                Shares[holding.GetProperty("symbol").GetString() ?? ""] = holding.GetProperty("shares").GetInt64();
        }

        private void OnTicked(BrainrotMarketPricesTickedEvent tick)
        {
            foreach (var quote in tick.Quotes.EnumerateArray())
            {
                var symbol = quote.GetProperty("symbol").GetString() ?? "";
                if (!Stocks.TryGetValue(symbol, out var stock)) continue;

                var price = quote.GetProperty("price").GetInt64();
                Stocks[symbol] = stock with { Price = price, Previous = quote.GetProperty("previous").GetInt64() };
                History.Add(symbol, price);
            }
        }

        private void OnTrade(BrainrotMarketTradeMadeEvent trade)
        {
            Feed.Insert(0, $"{trade.PlayerId[..Math.Min(6, trade.PlayerId.Length)]} {trade.Side} {trade.Shares} {trade.Symbol}");
            if (Feed.Count > 8) Feed.RemoveAt(Feed.Count - 1);
        }

        private async Task Guarded(string what, Func<Task> call)
        {
            Busy = true;
            try
            {
                await call();
            }
            catch (ExoActionException ex)
            {
                Status = $"{what} failed: {ex.Code}";
                Debug.LogError($"[Arcade] {what} failed [{ex.Code}] {ex.Message} (rid {ex.RequestId})");
            }
            catch (Exception ex)
            {
                Status = $"{what} failed: {ex.Message}";
                Debug.LogError($"[Arcade] {what} failed: {ex}");
            }
            finally
            {
                Busy = false;
            }
        }
    }
}
