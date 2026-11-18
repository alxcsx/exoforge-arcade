using System;
using UnityEditor;
using UnityEngine;

namespace Arcade.DoodleDash.Editor
{
    // tools/unity.sh Arcade.DoodleDash.Editor.LevelImageCheck.Run
    public static class LevelImageCheck
    {
        public static void Run()
        {
            int failed = 0;
            void Check(string name, bool ok)
            {
                if (!ok) failed++;
                Debug.Log($"[Arcade] {(ok ? "ok  " : "FAIL")} {name}");
            }

            var tiles = Flat();
            Check("pixels round trip to the same tiles", Same(tiles, LevelImage.ToTiles(LevelImage.ToPixels(tiles))));
            Check("png round trip keeps every tile", LevelImage.FromPng(LevelImage.ToPng(tiles)) is { } back && Same(tiles, back));
            Check("a wrong sized png is refused", LevelImage.FromPng(new Texture2D(8, 8).EncodeToPNG()) == null);
            Check("near colours snap to the closest tile", LevelImage.TileOf(new Color32(250, 10, 10, 255)) == Tile.Spike && LevelImage.TileOf(new Color32(20, 20, 20, 255)) == Tile.Ground);
            Check("transparent pixels are empty", LevelImage.TileOf(new Color32(0, 0, 0, 0)) == Tile.Empty);

            Check("playable needs one start and a goal", LevelImage.IsPlayable(tiles, out _));
            var noGoal = (Tile[])tiles.Clone();
            noGoal[At(29, 1)] = Tile.Empty;
            Check("no goal is not playable", !LevelImage.IsPlayable(noGoal, out _));
            var twoStarts = (Tile[])tiles.Clone();
            twoStarts[At(5, 1)] = Tile.Start;
            Check("two starts are not playable", !LevelImage.IsPlayable(twoStarts, out _));

            var other = (Tile[])tiles.Clone();
            other[At(10, 1)] = Tile.Ground;
            Check("hash is stable and tracks changes", LevelImage.Hash(tiles) == LevelImage.Hash((Tile[])tiles.Clone()) && LevelImage.Hash(tiles) != LevelImage.Hash(other));

            Check("running right along flat ground reaches the goal", Simulate(tiles, 1, 15f).Cleared);
            Check("standing still never clears", !Simulate(tiles, 0, 5f).Cleared);

            var spiked = (Tile[])tiles.Clone();
            spiked[At(10, 1)] = Tile.Spike;
            Check("a spike on the way kills", Simulate(spiked, 1, 15f).Dead);

            var walled = (Tile[])tiles.Clone();
            for (int y = 1; y < 6; y++) walled[At(10, y)] = Tile.Ground;
            Check("a tall wall stops the runner", !Simulate(walled, 1, 15f).Cleared);

            var jumper = new Platformer(tiles);
            for (int i = 0; i < 20; i++) jumper.Step(0.05f, 0, false);
            jumper.Step(0.02f, 0, true);
            Check("jumping leaves the ground and lands again", jumper.Y > 1.05f && Land(jumper));

            var pit = (Tile[])tiles.Clone();
            for (int x = 6; x < 12; x++) pit[At(x, 0)] = Tile.Empty;
            Check("falling into a pit is death", Simulate(pit, 1, 15f).Dead);

            Debug.Log($"[Arcade] LevelImage check: {(failed == 0 ? "passed" : failed + " failed")}");
            if (Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        private static int At(int x, int y) => y * LevelImage.Width + x;

        private static Tile[] Flat()
        {
            var tiles = new Tile[LevelImage.Width * LevelImage.Height];
            for (int x = 0; x < LevelImage.Width; x++) tiles[At(x, 0)] = Tile.Ground;
            tiles[At(2, 1)] = Tile.Start;
            tiles[At(29, 1)] = Tile.Goal;
            return tiles;
        }

        private static bool Same(Tile[] a, Tile[] b) => System.Linq.Enumerable.SequenceEqual(a, b);

        private static bool Land(Platformer runner)
        {
            for (int i = 0; i < 40; i++) runner.Step(0.05f, 0, false);
            return runner.Grounded && Math.Abs(runner.Y - 1f) < 0.05f;
        }

        private static Platformer Simulate(Tile[] tiles, float move, float seconds)
        {
            var runner = new Platformer(tiles);
            for (float t = 0; t < seconds && !runner.Cleared && !runner.Dead; t += 0.02f) runner.Step(0.02f, move, false);
            return runner;
        }
    }
}
