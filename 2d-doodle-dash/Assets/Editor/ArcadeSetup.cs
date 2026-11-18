using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ArcadeSetup
{
    private static readonly string[] SceneOrder = { "DoodleDash" };

    [MenuItem("Arcade/Set Up All Scenes")]
    public static void SetUpAll()
    {
        int failed = 0;

        foreach (var scene in SceneOrder)
        {
            var setup = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType($"Arcade.{scene}.Editor.{scene}Setup"))
                .FirstOrDefault(t => t != null)?
                .GetMethod("SetUp", BindingFlags.Public | BindingFlags.Static);

            if (setup == null)
            {
                Debug.Log($"[Arcade] {scene}: no setup yet, skipped");
                continue;
            }

            try
            {
                setup.Invoke(null, null);
                Debug.Log($"[Arcade] {scene}: set up");
            }
            catch (Exception ex)
            {
                failed++;
                Debug.LogError($"[Arcade] {scene}: setup failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        EditorBuildSettings.scenes = SceneOrder
            .Select(s => $"Assets/Scenes/{s}.unity")
            .Where(System.IO.File.Exists)
            .Select(p => new EditorBuildSettingsScene(p, true))
            .ToArray();

        AssetDatabase.SaveAssets();
        if (Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
