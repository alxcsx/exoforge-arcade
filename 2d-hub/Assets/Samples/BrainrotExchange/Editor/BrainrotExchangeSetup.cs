using Exoforge.Unity.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arcade.BrainrotExchange.Editor
{
    public static class BrainrotExchangeSetup
    {
        private const string ScenePath = "Assets/Scenes/BrainrotExchange.unity";

        [MenuItem("Arcade/Set Up Brainrot Exchange")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            camera.GetComponent<Camera>().backgroundColor = new Color(0.07f, 0.1f, 0.08f);
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;

            ExoforgeSceneSetup.AddToScene();

            var exchange = new GameObject("BrainrotExchange", typeof(ExchangeBridge), typeof(ExchangeView));
            new SerializedObject(exchange.GetComponent<ExchangeView>())
                .FindProperty("bridge").objectReferenceValue = exchange.GetComponent<ExchangeBridge>();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
