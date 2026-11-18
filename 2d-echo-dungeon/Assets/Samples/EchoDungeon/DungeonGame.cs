using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arcade.EchoDungeon
{
    public class DungeonGame : MonoBehaviour
    {
        private const float RoomWidth = 640f;
        private const float RoomHeight = 400f;
        private const float PlayerSpeed = 180f;
        private const float SwordReach = 46f;
        private const float SwingTime = 0.2f;
        private const int FeedLines = 8;

        private static readonly string[] Zones = { "Mossy Hall", "Slime Pit", "Treasury" };

        private sealed class Slime
        {
            public Vector2 Position;
            public Vector2 Heading;
            public int Health = 2;
        }

        private sealed class Chest
        {
            public Vector2 Position;
            public bool Open;
        }

        public event Action<string, string>? EnemySlain;
        public event Action<string, int>? ChestOpened;
        public event Action<string>? ZoneEntered;

        public int Kills { get; private set; }
        public int Gold { get; private set; }
        public string Zone => Zones[_room];

        private readonly List<Slime> _slimes = new();
        private readonly List<Chest>[] _chests = { new(), new(), new() };
        private readonly Queue<string> _feed = new();
        private Vector2 _player;
        private Vector2 _facing = Vector2.right;
        private float _swing;
        private int _room;

        public void PushFeed(string line)
        {
            _feed.Enqueue(line);
            while (_feed.Count > FeedLines) _feed.Dequeue();
        }

        private void Start()
        {
            _chests[0].Add(new Chest { Position = new Vector2(120, 90) });
            _chests[1].Add(new Chest { Position = new Vector2(520, 320) });
            _chests[2].Add(new Chest { Position = new Vector2(200, 200) });
            _chests[2].Add(new Chest { Position = new Vector2(440, 200) });
            Enter(0, new Vector2(60, RoomHeight / 2));
        }

        private void Enter(int room, Vector2 spawn)
        {
            _room = room;
            _player = spawn;
            _slimes.Clear();
            for (int i = 0; i < 2 + room; i++)
            {
                _slimes.Add(new Slime
                {
                    Position = new Vector2(UnityEngine.Random.Range(220, 560), UnityEngine.Random.Range(40, 360)),
                    Heading = UnityEngine.Random.insideUnitCircle.normalized,
                });
            }

            ZoneEntered?.Invoke(Zone);
        }

        private void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;

            var move = Vector2.zero;
            if (keys.wKey.isPressed || keys.upArrowKey.isPressed) move.y += 1;
            if (keys.sKey.isPressed || keys.downArrowKey.isPressed) move.y -= 1;
            if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) move.x -= 1;
            if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) move.x += 1;
            if (move != Vector2.zero) _facing = move.normalized;

            _player += move.normalized * (PlayerSpeed * Time.deltaTime);
            _player.y = Mathf.Clamp(_player.y, 12, RoomHeight - 12);

            if (_player.x > RoomWidth - 8 && _room < Zones.Length - 1) Enter(_room + 1, new Vector2(30, RoomHeight / 2));
            else if (_player.x < 8 && _room > 0) Enter(_room - 1, new Vector2(RoomWidth - 30, RoomHeight / 2));
            _player.x = Mathf.Clamp(_player.x, 8, RoomWidth - 8);

            _swing = Mathf.Max(0, _swing - Time.deltaTime);
            if (keys.spaceKey.wasPressedThisFrame && _swing <= 0) Swing();
            if (keys.eKey.wasPressedThisFrame) OpenNearbyChest();

            foreach (var slime in _slimes)
            {
                if (UnityEngine.Random.value < Time.deltaTime) slime.Heading = UnityEngine.Random.insideUnitCircle.normalized;
                slime.Position += slime.Heading * (40f * Time.deltaTime);
                slime.Position.x = Mathf.Clamp(slime.Position.x, 160, RoomWidth - 20);
                slime.Position.y = Mathf.Clamp(slime.Position.y, 20, RoomHeight - 20);
            }
        }

        private void Swing()
        {
            _swing = SwingTime;
            var tip = _player + _facing * SwordReach * 0.6f;
            for (int i = _slimes.Count - 1; i >= 0; i--)
            {
                if (Vector2.Distance(_slimes[i].Position, tip) > SwordReach * 0.7f) continue;
                if (--_slimes[i].Health > 0) continue;

                _slimes.RemoveAt(i);
                Kills++;
                EnemySlain?.Invoke("slime", Zone);
            }
        }

        private void OpenNearbyChest()
        {
            foreach (var chest in _chests[_room])
            {
                if (chest.Open || Vector2.Distance(chest.Position, _player) > 36f) continue;

                chest.Open = true;
                int gold = UnityEngine.Random.Range(10, 50);
                Gold += gold;
                ChestOpened?.Invoke(Zone, gold);
            }
        }

        private void OnGUI()
        {
            var origin = new Vector2((Screen.width - RoomWidth) / 2f, (Screen.height - RoomHeight) / 2f);

            Fill(origin, new Vector2(RoomWidth / 2f, RoomHeight / 2f), new Vector2(RoomWidth, RoomHeight), new Color(0.12f, 0.12f, 0.16f));
            if (_room < Zones.Length - 1) Fill(origin, new Vector2(RoomWidth - 4, RoomHeight / 2f), new Vector2(8, 70), new Color(0.55f, 0.35f, 0.15f));
            if (_room > 0) Fill(origin, new Vector2(4, RoomHeight / 2f), new Vector2(8, 70), new Color(0.55f, 0.35f, 0.15f));

            foreach (var chest in _chests[_room])
                Fill(origin, chest.Position, new Vector2(26, 18), chest.Open ? new Color(0.35f, 0.3f, 0.2f) : new Color(0.95f, 0.75f, 0.2f));
            foreach (var slime in _slimes)
                Fill(origin, slime.Position, new Vector2(22, 18), slime.Health > 1 ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.7f, 0.9f, 0.3f));

            if (_swing > 0) Fill(origin, _player + _facing * SwordReach * 0.6f, new Vector2(30, 30), new Color(0.9f, 0.9f, 1f));
            Fill(origin, _player, new Vector2(20, 20), new Color(0.35f, 0.6f, 1f));

            GUI.Label(new Rect(origin.x, origin.y - 24, 460, 22), $"{Zone}   kills {Kills}   gold {Gold}   [WASD] move  [Space] sword  [E] chest  doors at the room edges");

            var lines = string.Join("\n", _feed);
            GUI.Box(new Rect(origin.x + RoomWidth + 12, origin.y, 280, 190), "World feed\n" + lines);
        }

        private static void Fill(Vector2 origin, Vector2 center, Vector2 size, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(origin.x + center.x - size.x / 2f, origin.y + (RoomHeight - center.y) - size.y / 2f, size.x, size.y), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
