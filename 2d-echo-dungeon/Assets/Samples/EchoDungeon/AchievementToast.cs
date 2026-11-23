using System.Collections.Concurrent;
using System.Collections.Generic;
using Arcade.Common;
using Exoforge.Client;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.EchoDungeon
{
    public class AchievementToast : MonoBehaviour
    {
        private const float Seconds = 4f;

        private readonly ConcurrentQueue<AchievementsAchievementUnlockedEvent> _incoming = new();
        private readonly List<(string Title, float Until)> _shown = new();
        private string _playerId = "";

        private void Start() => SamplePlayer.WhenReady(OnReady);

        private async void OnReady(ExoSession session)
        {
            _playerId = session.PlayerId;
            ExoServices.Achievements.OnAchievementUnlocked += _incoming.Enqueue;
            await ExoServices.Achievements.SubscribeAsync("achievements:feed");
        }

        private void OnDestroy() => ExoServices.Achievements.OnAchievementUnlocked -= _incoming.Enqueue;

        private void Update()
        {
            var now = Time.unscaledTime;
            while (_incoming.TryDequeue(out var unlocked))
                if (unlocked.PlayerId == _playerId) _shown.Add((unlocked.Title, now + Seconds));

            for (var i = _shown.Count - 1; i >= 0; i--)
                if (_shown[i].Until < now) _shown.RemoveAt(i);
        }

        private void OnGUI()
        {
            for (var i = 0; i < _shown.Count; i++)
                GUI.Box(new Rect(Screen.width - 260, 10 + i * 40, 250, 34), $"Achievement unlocked: {_shown[i].Title}");
        }
    }
}
