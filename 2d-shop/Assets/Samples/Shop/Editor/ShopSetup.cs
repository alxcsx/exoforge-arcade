using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arcade.Shop.Editor
{
    public static class ShopSetup
    {
        private const string ScenePath = "Assets/Scenes/Shop.unity";

        [MenuItem("Arcade/Set Up Shop")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            var cam = camera.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);

            var game = new GameObject("Shop", typeof(ShopGame), typeof(ShopBridge));
            SceneManager.MoveGameObjectToScene(game, scene);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Arcade] Shop scene saved to {ScenePath}");
        }
    }
}
