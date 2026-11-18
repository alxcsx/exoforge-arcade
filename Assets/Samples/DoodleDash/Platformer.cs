using System;

namespace Arcade.DoodleDash
{
    public sealed class Platformer
    {
        private const float HalfWidth = 0.35f;
        private const float BodyHeight = 0.9f;
        private const float RunSpeed = 6f;
        private const float JumpSpeed = 12f;
        private const float Gravity = 32f;
        private const float MaxStep = 0.01f;

        private readonly Tile[] _tiles;

        public float X { get; private set; }
        public float Y { get; private set; }
        public float VelocityY { get; private set; }
        public bool Grounded { get; private set; }
        public bool Dead { get; private set; }
        public bool Cleared { get; private set; }

        public Platformer(Tile[] tiles)
        {
            _tiles = tiles;
            Respawn();
        }

        public void Respawn()
        {
            int start = Array.IndexOf(_tiles, Tile.Start);
            X = start % LevelImage.Width + 0.5f;
            Y = start / LevelImage.Width;
            VelocityY = 0;
            Grounded = false;
            Dead = false;
        }

        public void Step(float dt, float move, bool jump)
        {
            if (Dead || Cleared) return;

            if (jump && Grounded) VelocityY = JumpSpeed;
            while (dt > 0 && !Dead && !Cleared)
            {
                float slice = Math.Min(dt, MaxStep);
                dt -= slice;
                Advance(slice, move);
            }
        }

        private void Advance(float dt, float move)
        {
            VelocityY = Math.Max(VelocityY - Gravity * dt, -20f);

            float nx = X + move * RunSpeed * dt;
            if (!Blocked(nx, Y)) X = nx;

            float ny = Y + VelocityY * dt;
            Grounded = false;
            if (Blocked(X, ny))
            {
                Grounded = VelocityY < 0;
                VelocityY = 0;
            }
            else
            {
                Y = ny;
            }

            Touch();
        }

        private bool Blocked(float x, float y)
        {
            for (int ty = (int)Math.Floor(y); ty <= (int)Math.Floor(y + BodyHeight); ty++)
            for (int tx = (int)Math.Floor(x - HalfWidth); tx <= (int)Math.Floor(x + HalfWidth); tx++)
                if (At(tx, ty) == Tile.Ground) return true;

            return false;
        }

        private void Touch()
        {
            if (Y < -2) Dead = true;

            for (int ty = (int)Math.Floor(Y); ty <= (int)Math.Floor(Y + BodyHeight); ty++)
            for (int tx = (int)Math.Floor(X - HalfWidth); tx <= (int)Math.Floor(X + HalfWidth); tx++)
            {
                var tile = At(tx, ty);
                if (tile == Tile.Spike) Dead = true;
                if (tile == Tile.Goal) Cleared = true;
            }
        }

        private Tile At(int x, int y)
        {
            if (x < 0 || x >= LevelImage.Width) return Tile.Ground;
            if (y < 0 || y >= LevelImage.Height) return Tile.Empty;
            return _tiles[y * LevelImage.Width + x];
        }
    }
}
