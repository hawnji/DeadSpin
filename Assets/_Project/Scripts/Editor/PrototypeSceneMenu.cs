using System.Collections.Generic;
using System.IO;
using DeadSpin.Runtime.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadSpin.EditorTools
{
    /// <summary>
    /// Menu: Dead Spin > Build Prototype Scene. Generates the Phase 1 scene from code, saves its
    /// materials as assets, saves the scene and adds it to the build list. Safe to run again: it
    /// rebuilds the scene and updates the existing materials in place.
    /// </summary>
    public static class PrototypeSceneMenu
    {
        public const string ScenePath = "Assets/_Project/Scenes/Prototype.unity";
        public const string MaterialFolder = "Assets/_Project/Materials/Prototype";

        [MenuItem("Dead Spin/Build Prototype Scene", priority = 0)]
        public static void BuildPrototypeScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Dead Spin", "Exit Play mode before building the prototype scene.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(MaterialFolder);
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrototypeSceneFactory.Result result = PrototypeSceneFactory.Build(PersistMaterial);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("[Dead Spin] Failed to save " + ScenePath);
                return;
            }

            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = result.Camera.gameObject;
            Debug.Log("[Dead Spin] Prototype scene built and saved to " + ScenePath + ". Press Play to sit at the table.");
        }

        [MenuItem("Dead Spin/Open Prototype Scene", priority = 1)]
        public static void OpenPrototypeScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (EditorUtility.DisplayDialog("Dead Spin", "The prototype scene has not been built yet. Build it now?", "Build", "Cancel"))
                {
                    BuildPrototypeScene();
                }
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        private static Material PersistMaterial(Material material)
        {
            string path = MaterialFolder + "/" + material.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = material.shader;
                existing.CopyPropertiesFromMaterial(material);
                existing.shaderKeywords = material.shaderKeywords;
                existing.globalIlluminationFlags = material.globalIlluminationFlags;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(material);
                return existing;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
