using System;
using System.Collections.Concurrent;
using Arcade.Common;
using Exoforge.Client;
using UnityEngine;

namespace Arcade.EchoDungeon
{
    public class DungeonBridge : MonoBehaviour
    {
        [SerializeField] private DungeonGame game = null!;

        private readonly ConcurrentQueue<string> _incoming = new();

        private void Start() => SamplePlayer.WhenReady(_ => Connect());

        private async void Connect()
        {
            var world = ExoServices.AdventureWorld;
            world.OnEnemySlain += e => _incoming.Enqueue($"{e.Name} slew a {e.Enemy} in {e.Zone}");
            world.OnChestOpened += e => _incoming.Enqueue($"{e.Name} opened a chest in {e.Zone} (+{e.Gold})");
            world.OnZoneEntered += e => _incoming.Enqueue($"{e.Name} entered {e.Zone}");

            try
            {
                await world.SubscribeAsync();
                var progress = await world.GetProgressAsync();
                _incoming.Enqueue($"welcome back: {progress.Kills} kills, {progress.Chests} chests");
            }
            catch (ExoActionException ex)
            {
                _incoming.Enqueue($"world unavailable [{ex.Code}]: {ex.Message}");
                Debug.LogWarning($"[Arcade] dungeon connect failed [{ex.Code}]: {ex.Message} (rid {ex.RequestId})");
            }
            catch (Exception ex)
            {
                _incoming.Enqueue($"world unavailable: {ex.Message}");
                Debug.LogError($"[Arcade] dungeon connect failed: {ex}");
            }
        }

        private void Update()
        {
            while (_incoming.TryDequeue(out var line)) game.PushFeed(line);
        }
    }
}
