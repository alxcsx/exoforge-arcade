#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arcade.Common;
using Exoforge.Client;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.EchoDungeon
{
    // Stands in for the dedicated game server of a real MMO. It is the only holder of a `service` token,
    // which is read from Exoforge/, never from Assets/, and exists only in the Editor.
    public class MockGameServer : MonoBehaviour
    {
        private const string TokenFile = "Exoforge/mock-game-server.token";
        private static readonly string[] BotNames = { "Bot Ada", "Bot Brom", "Bot Cinder" };
        private static readonly string[] Zones = { "Mossy Hall", "Slime Pit", "Treasury" };

        [SerializeField] private DungeonGame game = null!;
        [SerializeField] private float botEverySeconds = 4f;

        private ExoClient? _server;
        private string _playerId = "";
        private string _playerName = "";
        private float _nextBot;

        private void Start()
        {
            var root = Path.GetDirectoryName(Application.dataPath)!;
            var tokenPath = Path.Combine(root, TokenFile);
            if (!File.Exists(tokenPath))
            {
                Debug.LogWarning($"[Arcade] MockGameServer: no service token at {TokenFile}; kills are not reported");
                enabled = false;
                return;
            }

            var token = File.ReadAllText(tokenPath).Trim();
            game.EnemySlain += (enemy, zone) => _ = ReportAsync("report_kill", _playerId, _playerName, new { enemy, zone });
            game.ChestOpened += (zone, gold) => _ = ReportAsync("report_chest", _playerId, _playerName, new { zone, gold });
            game.ZoneEntered += zone => _ = ReportAsync("report_zone", _playerId, _playerName, new { zone });

            SamplePlayer.WhenReady(session =>
            {
                (_playerId, _playerName) = (session.PlayerId, session.DisplayName);
                _server = new ExoClient { HttpBaseUri = ExoforgeSDK.Client.HttpBaseUri };
                _server.UseToken(token);
            });
        }

        private void Update()
        {
            if (_server == null || Time.time < _nextBot) return;

            _nextBot = Time.time + botEverySeconds * UnityEngine.Random.Range(0.5f, 1.5f);
            var index = UnityEngine.Random.Range(0, BotNames.Length);
            var (id, name, zone) = ($"bot-{index}", BotNames[index], Zones[UnityEngine.Random.Range(0, Zones.Length)]);
            _ = UnityEngine.Random.value switch
            {
                < 0.6f => ReportAsync("report_kill", id, name, new { enemy = "slime", zone }),
                < 0.85f => ReportAsync("report_chest", id, name, new { zone, gold = UnityEngine.Random.Range(10, 50) }),
                _ => ReportAsync("report_zone", id, name, new { zone }),
            };
        }

        private async Task ReportAsync(string action, string playerId, string name, object extra)
        {
            if (_server == null || string.IsNullOrEmpty(playerId)) return;

            try
            {
                await _server.SendActionAsync<AdventurerProgress>("adventure_world", action, Merge(playerId, name, extra));
            }
            catch (ExoActionException ex)
            {
                Debug.LogWarning($"[Arcade] MockGameServer {action} rejected [{ex.Code}]: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Arcade] MockGameServer {action} failed: {ex}");
            }
        }

        private static Dictionary<string, object?> Merge(string playerId, string name, object extra)
        {
            var payload = new Dictionary<string, object?> { ["player_id"] = playerId, ["name"] = name };
            foreach (var property in extra.GetType().GetProperties()) payload[property.Name] = property.GetValue(extra);
            return payload;
        }
    }
}
#endif
