using System;
using System.Security.Cryptography;
using UnityEngine;

namespace Arcade.DoodleDash
{
    public enum Tile : byte
    {
        Empty,
        Ground,
        Spike,
        Start,
        Goal,
    }

    public static class LevelImage
    {
        public const int Width = 32;
        public const int Height = 18;

        private static readonly Color32[] Palette =
        {
            new(255, 255, 255, 255),
            new(0, 0, 0, 255),
            new(255, 0, 0, 255),
            new(0, 200, 0, 255),
            new(255, 220, 0, 255),
        };

        public static Color32 ColorOf(Tile tile) => Palette[(int)tile];

        public static Tile TileOf(Color32 pixel)
        {
            var best = Tile.Empty;
            var bestDistance = int.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                int dr = pixel.r - Palette[i].r, dg = pixel.g - Palette[i].g, db = pixel.b - Palette[i].b;
                int distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (Tile)i;
                }
            }

            return pixel.a < 128 ? Tile.Empty : best;
        }

        public static Tile[] ToTiles(Color32[] pixels)
        {
            if (pixels.Length != Width * Height)
                throw new ArgumentException($"A level is {Width}x{Height} pixels, got {pixels.Length}.");

            var tiles = new Tile[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) tiles[i] = TileOf(pixels[i]);
            return tiles;
        }

        public static Color32[] ToPixels(Tile[] tiles)
        {
            var pixels = new Color32[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) pixels[i] = ColorOf(tiles[i]);
            return pixels;
        }

        public static bool IsPlayable(Tile[] tiles, out string reason)
        {
            int starts = 0, goals = 0;
            foreach (var tile in tiles)
            {
                if (tile == Tile.Start) starts++;
                if (tile == Tile.Goal) goals++;
            }

            reason = starts != 1 ? "Place exactly one green start." : goals == 0 ? "Place a yellow goal." : "";
            return reason.Length == 0;
        }

        public static string Hash(Tile[] tiles)
        {
            var bytes = new byte[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) bytes[i] = (byte)tiles[i];
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes), 0, 8).Replace("-", "").ToLowerInvariant();
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static Texture2D ToTexture(Tile[] tiles)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(ToPixels(tiles));
            texture.Apply();
            return texture;
        }

        public static byte[] ToPng(Tile[] tiles)
        {
            var texture = ToTexture(tiles);
            var png = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            return png;
        }

        public static Tile[]? FromPng(byte[] png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(png) || texture.width != Width || texture.height != Height) return null;
                return ToTiles(texture.GetPixels32());
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
