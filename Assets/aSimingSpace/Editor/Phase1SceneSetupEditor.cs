using SortingFactory.Phase1;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortingFactory.Editor
{
    [CustomEditor(typeof(Phase1SceneSetup))]
    public sealed class Phase1SceneSetupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Build / Rebuild Step 1 Scene", GUILayout.Height(30f)))
                {
                    Phase1SceneBuilder.Build((Phase1SceneSetup)target, true);
                }
            }
        }
    }

    [InitializeOnLoad]
    public static class Phase1SceneBuilder
    {
        private const string MainScenePath = "Assets/aSimingSpace/MainScene.unity";
        private const string MaterialFolder = "Assets/aSimingSpace/Generated/Step1Materials";

        static Phase1SceneBuilder()
        {
            EditorApplication.delayCall += BuildMissingMainSceneContent;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Sorting Factory/Build Step 1 Scene")]
        public static void BuildFromMenu()
        {
            Phase1SceneSetup setup = Object.FindFirstObjectByType<Phase1SceneSetup>();
            if (setup == null)
            {
                Debug.LogError("Open MainScene and select Phase1 Scene Setup before building Step 1.");
                return;
            }

            Build(setup, true);
            Selection.activeGameObject = setup.gameObject;
        }

        public static void Build(Phase1SceneSetup setup, bool registerUndo)
        {
            if (setup == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (registerUndo)
            {
                Undo.RegisterFullObjectHierarchyUndo(setup.gameObject, "Rebuild Step 1 Scene");
            }

            EnsureFolder(MaterialFolder);
            Material[] stationMaterials =
            {
                GetOrCreateMaterial("Arm_1_Cyan", new Color(0.08f, 0.72f, 0.82f)),
                GetOrCreateMaterial("Arm_2_Amber", new Color(0.95f, 0.58f, 0.08f)),
                GetOrCreateMaterial("Arm_3_Green", new Color(0.18f, 0.72f, 0.34f))
            };
            Material[] objectMaterials =
            {
                GetOrCreateMaterial("Object_Red", new Color(0.82f, 0.12f, 0.13f)),
                GetOrCreateMaterial("Object_Blue", new Color(0.08f, 0.3f, 0.82f)),
                GetOrCreateMaterial("Object_Yellow", new Color(0.96f, 0.76f, 0.08f)),
                GetOrCreateMaterial("Object_Green", new Color(0.08f, 0.58f, 0.25f)),
                GetOrCreateMaterial("Object_White", new Color(0.82f, 0.85f, 0.88f))
            };
            Material guideMaterial = GetOrCreateMaterial("Dispersal_Guide", new Color(0.9f, 0.72f, 0.12f));

            setup.ConfigureGeneratedMaterials(stationMaterials, objectMaterials, guideMaterial);
            Transform contentRoot = setup.BuildSceneContent();
            if (contentRoot == null)
            {
                return;
            }

            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(contentRoot.gameObject, "Rebuild Step 1 Scene");
            }

            EditorUtility.SetDirty(setup);
            EditorSceneManager.MarkSceneDirty(setup.gameObject.scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
        }

        private static void BuildMissingMainSceneContent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != MainScenePath)
            {
                return;
            }

            Phase1SceneSetup setup = Object.FindFirstObjectByType<Phase1SceneSetup>();
            if (setup != null && !setup.HasSceneContent)
            {
                Build(setup, false);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += BuildMissingMainSceneContent;
            }
        }

        private static Material GetOrCreateMaterial(string materialName, Color color)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
