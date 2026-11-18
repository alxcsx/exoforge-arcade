using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arcade.MainMenu
{
    public class MainMenuView : MonoBehaviour
    {
        private static readonly (string Scene, string Title)[] Games =
        {
            ("BrainrotExchange", "Brainrot Exchange"),
            ("EchoDungeon", "Echo Dungeon"),
            ("DoodleDash", "Doodle Dash"),
        };

        [SerializeField] private MenuBridge bridge = null!;

        private void OnGUI()
        {
            var accent = bridge.ColorBlind ? new Color(1f, 0.6f, 0f) : Color.green;

            GUILayout.BeginArea(new Rect(Screen.width / 2f - 200, 20, 400, Screen.height - 40));
            GUILayout.Label("<size=28><b>Exoforge Arcade</b></size>");
            GUI.color = accent;
            GUILayout.Label($"<i>{bridge.News}</i>");
            GUI.color = Color.white;

            GUILayout.Label($"{bridge.PlayerName}    coins: {(bridge.Coins?.ToString() ?? "…")}");
            GUILayout.Space(8);

            foreach (var (scene, title) in Games)
            {
                GUI.enabled = Application.CanStreamedLevelBeLoaded(scene);
                if (GUILayout.Button(title, GUILayout.Height(32))) SceneManager.LoadScene(scene);
            }

            GUI.enabled = !bridge.Busy;
            GUILayout.Space(8);
            if (GUILayout.Button("Claim Gift", GUILayout.Height(32))) _ = bridge.ClaimGiftAsync();
            GUILayout.Label(bridge.GiftStatus);

            GUILayout.Space(8);
            GUILayout.Label($"Settings   volume {bridge.Volume}");
            bridge.Volume = (long)GUILayout.HorizontalSlider(bridge.Volume, 0, 100);
            bridge.ColorBlind = GUILayout.Toggle(bridge.ColorBlind, "Colour-blind mode");
            if (GUILayout.Button("Save settings")) _ = bridge.SaveSettingsAsync();
            GUI.enabled = true;
            GUILayout.EndArea();
        }
    }
}
