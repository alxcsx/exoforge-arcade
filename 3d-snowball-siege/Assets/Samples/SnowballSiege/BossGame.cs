using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arcade.SnowballSiege
{
    public class BossGame : MonoBehaviour
    {
        public bool BossAlive;
        public int Hp;
        public int MaxHp;
        public string BossName = "";
        public event Action<int, string>? SnowballHit;

        private readonly List<string> _feed = new();
        private int _payoutCoins;
        private float _payoutUntil;

        public void ApplyState(string name, int hp, int maxHp, bool alive)
        {
            BossName = name;
            Hp = hp;
            MaxHp = maxHp;
            BossAlive = alive;
        }

        public void PushFeed(string line)
        {
            _feed.Add(line);
            if (_feed.Count > 8) _feed.RemoveAt(0);
        }

        public void ShowPayout(int coins)
        {
            _payoutCoins = coins;
            _payoutUntil = Time.time + 6f;
        }

        public void NotifyHit(int damage, string part) => SnowballHit?.Invoke(damage, part);

        private void OnGUI()
        {
            if (MaxHp > 0)
            {
                var rect = new Rect(20, 20, 300, 24);
                GUI.Box(rect, "");
                var fill = new Rect(20, 20, 300f * Hp / MaxHp, 24);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
                GUI.Label(new Rect(20, 20, 300, 24), $"{BossName} {Hp}/{MaxHp}");
            }

            // crosshair
            var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            GUI.Label(new Rect(c.x - 4, c.y - 4, 8, 8), "+");

            // feed
            for (int i = 0; i < _feed.Count; i++)
                GUI.Label(new Rect(20, Screen.height - 120 + i * 16, 400, 16), _feed[i]);

            if (Time.time < _payoutUntil)
                GUI.Label(new Rect(Screen.width / 2f - 100, 60, 200, 30), $"Payout: {_payoutCoins}");

            GUI.Label(new Rect(Screen.width - 260, 20, 240, 20), "MOCK GAME SERVER (editor only)");
        }
    }
}