using System;
using System.Threading.Tasks;
using Arcade.Common;
using Exoforge.Client;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.MainMenu
{
    public class MenuBridge : MonoBehaviour
    {
        public string PlayerName { get; private set; } = "";
        public string News { get; private set; } = "Connecting…";
        public long? Coins { get; private set; }
        public long Volume { get; set; } = 80;
        public bool ColorBlind { get; set; }
        public string GiftStatus { get; private set; } = "";
        public bool Busy { get; private set; }

        private void Start() => SamplePlayer.WhenReady(session => _ = LoadAsync(session));

        public async Task LoadAsync(ExoSession session)
        {
            PlayerName = session.DisplayName;
            await Guarded("news", ReadNewsAsync);
            await Guarded("coins", RefreshCoinsAsync);
            await Guarded("settings", LoadSettingsAsync);
        }

        public Task SaveSettingsAsync() => Guarded("save_settings", async () =>
        {
            var saved = await ExoServices.GameMenu.SaveSettingsAsync((int)Volume, ColorBlind);
            Volume = saved.Volume;
            ColorBlind = saved.ColorBlind;
        });

        public Task ClaimGiftAsync() => Guarded("claim_gift", async () =>
        {
            GiftStatus = "claiming…";
            try
            {
                var receipt = await ExoServices.GameMenu.ClaimGiftAsync();
                GiftStatus = $"+{receipt.Coins} coins";
                Coins = receipt.Balance;
            }
            catch (ExoActionException ex)
            {
                GiftStatus = $"gift failed: {ex.Code}";
                throw;
            }
        });

        private async Task ReadNewsAsync()
        {
            var news = await ExoServices.ResourceStore.ListAsync(new ResourceStoreListRequest { Resource = "menu_news" });
            var best = int.MinValue;
            foreach (var row in news.Rows.EnumerateArray())
            {
                var priority = row.GetProperty("priority").GetInt32();
                if (priority <= best) continue;
                best = priority;
                News = row.GetProperty("headline").GetString() ?? "";
            }
        }

        private async Task RefreshCoinsAsync() => Coins = (await ExoServices.Wallet.GetBalanceAsync()).Coins;

        private async Task LoadSettingsAsync()
        {
            var settings = await ExoServices.GameMenu.GetSettingsAsync();
            Volume = settings.Volume;
            ColorBlind = settings.ColorBlind;
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
                Debug.LogError($"[Arcade] {what} failed [{ex.Code}]: {ex.Message} (rid {ex.RequestId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Arcade] {what} failed: {ex.Message}");
            }
            finally
            {
                Busy = false;
            }
        }
    }
}
