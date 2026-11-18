using System;
using System.Threading.Tasks;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.Common
{
    [DefaultExecutionOrder(-800)]
    public class SamplePlayer : MonoBehaviour
    {
        public static SamplePlayer? Instance { get; private set; }

        public static event Action<ExoSession>? Ready;

        public ExoSession? Session { get; private set; }
        public string Status { get; private set; } = "signing in…";
        public bool IsReady => Session is { HasDisplayName: true };

        private string _nameField = "";

        public static SamplePlayer Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SamplePlayer");
            DontDestroyOnLoad(go);
            return go.AddComponent<SamplePlayer>();
        }

        public static void WhenReady(Action<ExoSession> onReady)
        {
            var player = Ensure();
            if (player.IsReady) onReady(player.Session!);
            else Ready += onReady;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private async void Start() => await SignInAsync();

        private async Task SignInAsync()
        {
            try
            {
                Session = await ExoforgeSDK.Auth.LoginAnonymously();
                if (IsReady) Announce();
                else Status = "choose a name";
            }
            catch (Exception ex)
            {
                Status = $"offline: {ex.Message}";
                Debug.LogError($"[Arcade] sign-in failed: {ex.Message}");
            }
        }

        private async Task NameAsync(string displayName)
        {
            try
            {
                Session = await ExoforgeSDK.Auth.SetDisplayName(displayName);
                Announce();
            }
            catch (Exception ex)
            {
                Status = $"could not set name: {ex.Message}";
                Debug.LogError($"[Arcade] {Status}");
            }
        }

        private void Announce()
        {
            Status = $"{Session!.DisplayName}";
            Debug.Log($"[Arcade] ready: {Session.DisplayName} ({Session.PlayerId})");
            Ready?.Invoke(Session);
        }

        private void OnGUI()
        {
            if (Session == null || IsReady) return;

            GUILayout.BeginArea(new Rect(Screen.width / 2f - 160, Screen.height / 2f - 60, 320, 120), GUI.skin.box);
            GUILayout.Label("Pick a display name");
            _nameField = GUILayout.TextField(_nameField, 20);
            GUI.enabled = _nameField.Trim().Length >= 2;
            if (GUILayout.Button("Play")) _ = NameAsync(_nameField.Trim());
            GUI.enabled = true;
            GUILayout.EndArea();
        }
    }
}
