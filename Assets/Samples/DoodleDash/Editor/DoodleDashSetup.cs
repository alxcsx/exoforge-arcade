using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arcade.DoodleDash.Editor
{
    public static class DoodleDashSetup
    {
        private const string ScenePath = "Assets/Scenes/DoodleDash.unity";

        [MenuItem("Arcade/Set Up Doodle Dash")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            var cam = camera.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);

            var game = new GameObject("DoodleDash", typeof(DoodleGame), typeof(DoodleBridge));
            SceneManager.MoveGameObjectToScene(game, scene);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Arcade] DoodleDash scene saved to {ScenePath}");
        }
    }
}
