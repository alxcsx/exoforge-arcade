using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Arcade.BrainrotExchange
{
    public class ExchangeView : MonoBehaviour
    {
        private const float RefreshSeconds = 10f;
        private const float SparkWidth = 150f;

        [SerializeField] private ExchangeBridge bridge = null!;

        private float _nextRefresh;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SceneManager.LoadScene("MainMenu");
            if (Time.time < _nextRefresh || bridge.Busy) return;

            _nextRefresh = Time.time + RefreshSeconds;
            if (bridge.Coins != null) _ = bridge.RefreshAsync();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Brainrot Exchange</b>   {bridge.PlayerName}   coins: {(bridge.Coins?.ToString() ?? "…")}");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Menu (Esc)", GUILayout.Width(100))) SceneManager.LoadScene("MainMenu");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(Screen.width * 0.6f));
            DrawStocks();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            DrawFarm();
            DrawDaily();
            DrawFeed();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.Label(bridge.Status);
            GUILayout.EndArea();
        }

        private void DrawStocks()
        {
            GUILayout.Label("Market");
            foreach (var stock in bridge.Stocks.Values.OrderBy(s => s.Symbol))
            {
                bridge.Shares.TryGetValue(stock.Symbol, out var held);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{stock.Symbol}  {stock.Name}", GUILayout.Width(220));
                GUILayout.Label($"{stock.Price} {Arrow(stock.Change)}", GUILayout.Width(80));
                Sparkline(stock);
                GUILayout.Label($"held {held}", GUILayout.Width(60));
                GUI.enabled = !bridge.Busy;
                if (GUILayout.Button("Buy 1", GUILayout.Width(55))) _ = bridge.BuyAsync(stock.Symbol, 1);
                if (GUILayout.Button("Buy 10", GUILayout.Width(55))) _ = bridge.BuyAsync(stock.Symbol, 10);
                GUI.enabled = !bridge.Busy && held > 0;
                if (GUILayout.Button("Sell all", GUILayout.Width(60))) _ = bridge.SellAsync(stock.Symbol, (int)held);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void Sparkline(Stock stock)
        {
            var area = GUILayoutUtility.GetRect(SparkWidth, 28, GUILayout.Width(SparkWidth));
            var range = Mathf.Max(1, stock.Max - stock.Min);
            var step = area.width / PriceHistory.Length;
            GUI.color = stock.Change >= 0 ? Color.green : Color.red;
            var x = area.x;
            foreach (var price in bridge.History.Of(stock.Symbol))
            {
                var height = Mathf.Max(2, area.height * (price - stock.Min) / range);
                GUI.DrawTexture(new Rect(x, area.yMax - height, step - 1, height), Texture2D.whiteTexture);
                x += step;
            }

            GUI.color = Color.white;
        }

        private void DrawFarm()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Meme farm: level {bridge.FarmLevel}  idle: {bridge.PendingIdle} coins (8h cap, counted on the server)");
            GUI.enabled = !bridge.Busy;
            if (GUILayout.Button("Collect")) _ = bridge.CollectAsync();
            if (GUILayout.Button($"Upgrade ({200 * (1 << (int)bridge.FarmLevel)})")) _ = bridge.BuyFarmAsync();
            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private void DrawDaily()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.enabled = !bridge.Busy && bridge.DailyAvailable;
            if (GUILayout.Button(bridge.DailyAvailable ? $"Claim daily bonus (day {bridge.DailyDay})" : "Daily bonus claimed")) _ = bridge.ClaimDailyAsync();
            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private void DrawFeed()
        {
            GUILayout.Label("Trades on the exchange");
            foreach (var line in bridge.Feed) GUILayout.Label(line);
        }

        private static string Arrow(long change) => change > 0 ? "▲" : change < 0 ? "▼" : "–";
    }
}
