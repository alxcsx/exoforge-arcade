using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arcade.DoodleDash
{
    public record LevelRow(string Id, string Title, string Author, string Tag, int Plays, double Rating, int RatingCount);

    public record LevelDraft(string Title, string Difficulty, Tile[] Tiles, string Hash);

    public class DoodleGame : MonoBehaviour
    {
        private enum Mode
        {
            Paint,
            Play,
            Browse,
        }

        private static readonly string[] Difficulties = { "easy", "normal", "hard" };
        private static readonly string[] Sorts = { "newest", "top", "popular" };
        private static readonly string[] Brushes = { "Erase", "Ground", "Spikes", "Start", "Goal" };

        public event Action<string?, Tile[]>? Cleared;
        public event Action<LevelDraft>? PublishRequested;
        public event Action<string, string>? BrowseRequested;
        public event Action<LevelRow>? PlayRequested;
        public event Action<string, int>? RateRequested;

        public bool Online { get; set; }
        public string Status { get; set; } = "Paint a level: black ground, red spikes, one green start, a yellow goal.";

        private Mode _mode = Mode.Paint;
        private Tile[] _draft = new Tile[LevelImage.Width * LevelImage.Height];
        private Tile[] _playTiles = Array.Empty<Tile>();
        private Texture2D? _view;
        private Platformer? _runner;
        private LevelRow? _playing;
        private Tile _brush = Tile.Ground;
        private string _draftHash = "";
        private string _clearedHash = "";
        private string _title = "My level";
        private int _difficulty = 1;
        private string _tag = "";
        private int _sort;
        private IReadOnlyList<LevelRow> _rows = Array.Empty<LevelRow>();
        private bool _drewPlayView;

        private void Awake()
        {
            for (int x = 0; x < LevelImage.Width; x++) _draft[x] = Tile.Ground;
            _draft[1 * LevelImage.Width + 2] = Tile.Start;
            _draft[1 * LevelImage.Width + LevelImage.Width - 3] = Tile.Goal;
            _draftHash = LevelImage.Hash(_draft);
        }

        public void ShowLevels(IReadOnlyList<LevelRow> rows)
        {
            _rows = rows;
            Status = rows.Count == 0 ? "No levels here yet." : $"{rows.Count} levels";
        }

        public void PlayLevel(LevelRow row, Tile[] tiles)
        {
            _playing = row;
            StartPlay(tiles);
        }

        private void Update()
        {
            if (_mode == Mode.Paint) PaintWithMouse();
            if (_mode == Mode.Play) RunPlatformer();
        }

        private void PaintWithMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed) return;

            var position = mouse.position.ReadValue();
            var canvas = CanvasRect();
            float guiY = Screen.height - position.y;
            if (!canvas.Contains(new Vector2(position.x, guiY))) return;

            float cell = canvas.width / LevelImage.Width;
            int x = (int)((position.x - canvas.x) / cell);
            int y = (int)((canvas.yMax - guiY) / cell);
            var tile = _brush;
            if (tile == Tile.Start) ClearAll(Tile.Start);
            _draft[y * LevelImage.Width + x] = tile;
            _draftHash = LevelImage.Hash(_draft);
        }

        private void ClearAll(Tile tile)
        {
            for (int i = 0; i < _draft.Length; i++)
                if (_draft[i] == tile) _draft[i] = Tile.Empty;
        }

        private void RunPlatformer()
        {
            var keys = Keyboard.current;
            if (keys == null || _runner == null) return;

            if (keys.escapeKey.wasPressedThisFrame) Leave();
            if (keys.rKey.wasPressedThisFrame || _runner.Dead) _runner.Respawn();

            float move = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1 : 0) - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1 : 0);
            bool jump = keys.spaceKey.isPressed || keys.wKey.isPressed || keys.upArrowKey.isPressed;
            _runner.Step(Time.deltaTime, move, jump);

            if (!_runner.Cleared) return;

            if (_playing == null) _clearedHash = _draftHash;
            Status = _playing == null ? "Cleared! You can publish it now." : "Cleared! Go back and rate it.";
            Cleared?.Invoke(_playing?.Id, _playTiles);
            Leave();
        }

        private void StartPlay(Tile[] tiles)
        {
            if (!LevelImage.IsPlayable(tiles, out var reason))
            {
                Status = reason;
                return;
            }

            _playTiles = tiles;
            if (_view != null) Destroy(_view);
            _view = LevelImage.ToTexture(tiles);
            _runner = new Platformer(tiles);
            _mode = Mode.Play;
            Status = "A/D or arrows to move, Space to jump, R restarts, Esc leaves.";
        }

        private void Leave()
        {
            _mode = _playing == null ? Mode.Paint : Mode.Browse;
            _playing = null;
        }

        private Rect CanvasRect()
        {
            float cell = Mathf.Floor(Mathf.Min((Screen.width - 40f) / LevelImage.Width, (Screen.height - 170f) / LevelImage.Height));
            return new Rect(20, 60, cell * LevelImage.Width, cell * LevelImage.Height);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 10, Screen.width - 40, 40));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Doodle Dash", GUILayout.Width(110));
            if (GUILayout.Button("Paint", GUILayout.Width(80))) { _mode = Mode.Paint; _playing = null; }
            GUI.enabled = Online;
            if (GUILayout.Button("Browse", GUILayout.Width(80))) OpenBrowse();
            GUI.enabled = true;
            GUILayout.Label(Online ? "online" : "offline: publishing and browsing are off");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            var canvas = CanvasRect();
            if (_mode == Mode.Browse) DrawBrowse(canvas);
            else DrawBoard(canvas);

            GUI.Label(new Rect(20, Screen.height - 30, Screen.width - 40, 24), Status);
        }

        private void OpenBrowse()
        {
            _mode = Mode.Browse;
            BrowseRequested?.Invoke(_tag, Sorts[_sort]);
        }

        private void DrawBoard(Rect canvas)
        {
            if (_mode == Mode.Paint)
            {
                if (_drewPlayView || _view == null) _view = LevelImage.ToTexture(_draft);
                _view.SetPixels32(LevelImage.ToPixels(_draft));
                _view.Apply();
                _drewPlayView = false;
            }
            else
            {
                _drewPlayView = true;
            }

            if (_view != null) GUI.DrawTexture(canvas, _view, ScaleMode.StretchToFill);

            if (_mode == Mode.Play && _runner != null)
            {
                float cell = canvas.width / LevelImage.Width;
                var body = new Rect(canvas.x + (_runner.X - 0.35f) * cell, canvas.yMax - (_runner.Y + 0.9f) * cell, 0.7f * cell, 0.9f * cell);
                var previous = GUI.color;
                GUI.color = new Color(0.1f, 0.4f, 1f);
                GUI.DrawTexture(body, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            if (_mode != Mode.Paint) return;

            GUILayout.BeginArea(new Rect(20, canvas.yMax + 8, Screen.width - 40, 100));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Brush", GUILayout.Width(50));
            for (int i = 0; i < Brushes.Length; i++)
                if (GUILayout.Toggle(_brush == (Tile)i, Brushes[i], GUI.skin.button, GUILayout.Width(80))) _brush = (Tile)i;
            if (GUILayout.Button("Clear all", GUILayout.Width(80))) Array.Clear(_draft, 0, _draft.Length);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play it", GUILayout.Width(80))) { _playing = null; StartPlay((Tile[])_draft.Clone()); }
            _title = GUILayout.TextField(_title, 30, GUILayout.Width(200));
            _difficulty = GUILayout.SelectionGrid(_difficulty, Difficulties, 3, GUILayout.Width(210));
            GUI.enabled = Online && _clearedHash == _draftHash && _title.Trim().Length > 0;
            if (GUILayout.Button("Publish", GUILayout.Width(80)))
                PublishRequested?.Invoke(new LevelDraft(_title.Trim(), Difficulties[_difficulty], (Tile[])_draft.Clone(), _draftHash));
            GUI.enabled = true;
            if (_clearedHash != _draftHash) GUILayout.Label("clear it to publish");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawBrowse(Rect canvas)
        {
            GUILayout.BeginArea(new Rect(20, 60, Screen.width - 40, Screen.height - 100));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort", GUILayout.Width(40));
            int sort = GUILayout.SelectionGrid(_sort, Sorts, 3, GUILayout.Width(240));
            GUILayout.Label("Tag", GUILayout.Width(30));
            string[] tags = { "", "difficulty:easy", "difficulty:normal", "difficulty:hard" };
            int tagIndex = GUILayout.SelectionGrid(Mathf.Max(0, Array.IndexOf(tags, _tag)), new[] { "all", "easy", "normal", "hard" }, 4, GUILayout.Width(280));
            GUILayout.EndHorizontal();

            if (sort != _sort || tags[tagIndex] != _tag)
            {
                _sort = sort;
                _tag = tags[tagIndex];
                BrowseRequested?.Invoke(_tag, Sorts[_sort]);
            }

            foreach (var row in _rows)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{row.Title}  by {row.Author}  [{row.Tag}]  {row.Rating:0.0}* ({row.RatingCount})  {row.Plays} plays", GUILayout.Width(Mathf.Max(320, Screen.width - 520)));
                if (GUILayout.Button("Play", GUILayout.Width(60))) PlayRequested?.Invoke(row);
                for (int stars = 1; stars <= 5; stars++)
                    if (GUILayout.Button(stars.ToString(), GUILayout.Width(28))) RateRequested?.Invoke(row.Id, stars);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }
    }
}
