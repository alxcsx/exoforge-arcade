using Exoforge.Unity.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arcade.MainMenu.Editor
{
    public static class MainMenuSetup
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";

        [MenuItem("Arcade/Set Up Main Menu")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            camera.GetComponent<Camera>().backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;

            ExoforgeSceneSetup.AddToScene();

            var menu = new GameObject("MainMenu", typeof(MenuBridge), typeof(MainMenuView));
            new SerializedObject(menu.GetComponent<MainMenuView>())
                .FindProperty("bridge").objectReferenceValue = menu.GetComponent<MenuBridge>();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
