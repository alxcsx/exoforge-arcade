using System;
using System.Linq;
using System.Threading.Tasks;
using Arcade.Common;
using Exoforge.Client;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.DoodleDash
{
    [RequireComponent(typeof(DoodleGame))]
    public class DoodleBridge : MonoBehaviour
    {
        private DoodleGame _game = null!;

        private void Awake() => _game = GetComponent<DoodleGame>();

        private void OnEnable()
        {
            _game.Cleared += OnCleared;
            _game.PublishRequested += OnPublish;
            _game.BrowseRequested += OnBrowse;
            _game.PlayRequested += OnPlay;
            _game.RateRequested += OnRate;
        }

        private void OnDisable()
        {
            _game.Cleared -= OnCleared;
            _game.PublishRequested -= OnPublish;
            _game.BrowseRequested -= OnBrowse;
            _game.PlayRequested -= OnPlay;
            _game.RateRequested -= OnRate;
        }

        private void Start()
        {
            SamplePlayer.WhenReady(session =>
            {
                _game.Online = true;
                _game.Status = $"Signed in as {session.DisplayName}.";
            });
        }

        private async void OnCleared(string? levelId, Tile[] tiles)
        {
            if (!_game.Online) return;
            string key = levelId ?? LevelImage.Sha256Hex(LevelImage.ToPng(tiles));
            await Run("record clear", () => ExoServices.LevelShare.RecordClearAsync(key));
        }

        private async void OnPublish(LevelDraft draft)
        {
            await Run("publish", async () =>
            {
                var upload = await ExoServices.LevelShare.UploadLevelAsync(Convert.ToBase64String(LevelImage.ToPng(draft.Tiles)));
                var result = await ExoServices.LevelShare.PublishAsync(draft.Title, draft.Difficulty, upload.FileId);
                _game.Status = $"Published \"{draft.Title}\" ({result.LevelId[..6]}).";
            });
        }

        private async void OnBrowse(string tag, string sort)
        {
            await Run("browse", async () =>
            {
                var levels = await ExoServices.LevelShare.BrowseAsync(tag, sort, 20);
                _game.ShowLevels(levels.Select(l => new LevelRow(l.Id, l.Title, l.Author, l.Tag, (int)l.Plays, l.Rating, (int)l.RatingCount)).ToList());
            });
        }

        private async void OnPlay(LevelRow row)
        {
            await Run("download", async () =>
            {
                var signed = await ExoServices.LevelShare.GetLevelUrlAsync(row.Id);
                var client = ExoforgeSDK.Client;
                var png = await client.HttpClient.GetByteArrayAsync(new Uri(client.HttpBaseUri!, signed.Url));
                var tiles = LevelImage.FromPng(png) ?? throw new InvalidOperationException("The level image is not 32x18.");
                _game.PlayLevel(row, tiles);
            });
        }

        private async void OnRate(string levelId, int stars)
        {
            await Run("rate", async () =>
            {
                var result = await ExoServices.LevelShare.RateAsync(levelId, stars);
                _game.Status = $"Rated. Average is now {result.Rating:0.0} from {result.RatingCount}.";
                OnBrowse("", "newest");
            });
        }

        private async Task Run(string what, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (ExoActionException ex)
            {
                _game.Status = $"{what} failed [{ex.Code}]: {ex.Message}";
                Debug.LogWarning($"[Arcade] doodle {what}: {ex.Code} {ex.Message}");
            }
            catch (Exception ex)
            {
                _game.Status = $"{what} failed: {ex.Message}";
                Debug.LogError($"[Arcade] doodle {what}: {ex}");
            }
        }
    }
}
