using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arcade.EchoDungeon.Editor
{
    public static class EchoDungeonSetup
    {
        [MenuItem("Arcade/Set Up Echo Dungeon")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var world = new GameObject("EchoDungeon");
            var game = world.AddComponent<DungeonGame>();
            AddReference(world.AddComponent<DungeonBridge>(), game);
            AddReference(world.AddComponent<MockGameServer>(), game);

            var toast = Type.GetType("Arcade.Common.AchievementToast, Samples.Common");
            if (toast != null) world.AddComponent(toast);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/EchoDungeon.unity");
        }

        private static void AddReference(Component component, DungeonGame game)
        {
            var field = new SerializedObject(component);
            field.FindProperty("game").objectReferenceValue = game;
            field.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
