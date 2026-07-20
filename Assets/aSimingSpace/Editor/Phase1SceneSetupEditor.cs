using SortingFactory.Phase1;
using SortingFactory.Step2;
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
                if (GUILayout.Button("Build / Rebuild Steps 1-2 Scene", GUILayout.Height(30f)))
                {
                    Phase1SceneBuilder.Build((Phase1SceneSetup)target, true);
                }

                if (GUILayout.Button("Build Closed Conveyor Loop", GUILayout.Height(26f)))
                {
                    ConveyorLoopSceneBuilder.BuildLoop(true);
                }

                if (GUILayout.Button("Upgrade Placeholder Arms to SO-101", GUILayout.Height(26f)))
                {
                    Phase1SceneBuilder.UpgradePlaceholderArmsToSo101(
                        (Phase1SceneSetup)target,
                        true);
                }
            }
        }
    }

    [InitializeOnLoad]
    public static class Phase1SceneBuilder
    {
        private const string MainScenePath = "Assets/aSimingSpace/MainScene.unity";
        private const string MaterialFolder = "Assets/aSimingSpace/Generated/Step1Materials";
        private const string ObjectPhotoFolder = "Assets/aSimingSpace/objectPhoto";
        private static readonly string[] DetectionClasses =
        {
            "apple",
            "banana",
            "bottle",
            "cup",
            "orange"
        };

        static Phase1SceneBuilder()
        {
            EditorApplication.delayCall += BuildMissingMainSceneContent;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Sorting Factory/Build Steps 1-2 Scene")]
        public static void BuildFromMenu()
        {
            Phase1SceneSetup setup = Object.FindFirstObjectByType<Phase1SceneSetup>();
            if (setup == null)
            {
                Debug.LogError("Open MainScene and select Phase1 Scene Setup before building Steps 1-2.");
                return;
            }

            Build(setup, true);
            Selection.activeGameObject = setup.gameObject;
        }

        [MenuItem("Tools/Sorting Factory/Replace Pickables With Detection Boxes")]
        public static void ReplacePickablesFromMenu()
        {
            Phase1SceneSetup setup = Object.FindFirstObjectByType<Phase1SceneSetup>();
            if (setup == null)
            {
                Debug.LogError("Open MainScene before replacing the pickable objects.");
                return;
            }

            ReplaceExistingPickables(setup, true);
            Selection.activeGameObject = setup.gameObject;
        }

        [MenuItem("Tools/Sorting Factory/Upgrade Placeholder Arms to SO-101")]
        public static void UpgradePlaceholderArmsFromMenu()
        {
            Phase1SceneSetup setup = Object.FindFirstObjectByType<Phase1SceneSetup>();
            if (setup == null)
            {
                Debug.LogError("Open MainScene before upgrading the placeholder robot arms.");
                return;
            }

            UpgradePlaceholderArmsToSo101(setup, true);
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
            Material feederBeltMaterial = GetOrCreateMaterial("Feeder_Belt", new Color(0.12f, 0.14f, 0.16f));
            Material[] detectionLabelMaterials = GetDetectionLabelMaterials();
            Material detectionBoxBodyMaterial = GetOrCreateMaterial(
                "Detection_Box_Body",
                new Color(0.34f, 0.37f, 0.4f));

            setup.ConfigureGeneratedMaterials(
                stationMaterials,
                objectMaterials,
                guideMaterial,
                feederBeltMaterial);
            setup.ConfigureDetectionBoxes(
                DetectionClasses,
                detectionLabelMaterials,
                detectionBoxBodyMaterial);
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

        public static void UpgradePlaceholderArmsToSo101(
            Phase1SceneSetup setup,
            bool registerUndo)
        {
            if (setup == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (registerUndo)
            {
                Undo.RegisterFullObjectHierarchyUndo(
                    setup.gameObject,
                    "Upgrade Placeholder Arms to SO-101");
            }

            int upgradedCount = setup.UpgradePlaceholderRobotArmsToSo101();
            if (upgradedCount == 0)
            {
                return;
            }

            EditorUtility.SetDirty(setup);
            EditorSceneManager.MarkSceneDirty(setup.gameObject.scene);
            EditorSceneManager.SaveScene(setup.gameObject.scene);
            SceneView.RepaintAll();
            Debug.Log($"Upgraded {upgradedCount} placeholder robot arms to the SO-101 joint hierarchy.", setup);
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
            bool needsSortingArea = setup != null &&
                setup.GetComponentInChildren<SortingAreaFeedObject>(true) == null;
            bool needsStep2Cameras = setup != null &&
                setup.GetComponentInChildren<WorkstationCameraController>(true) == null;
            bool needsDetectionBoxes = setup != null &&
                NeedsDetectionBoxUpgrade(setup);
            bool needsSo101Arms = setup != null &&
                setup.NeedsSo101RobotArmUpgrade;
            if (setup != null &&
                (!setup.HasSceneContent ||
                 needsSortingArea ||
                 needsStep2Cameras ||
                 setup.NeedsGeneratedContentUpgrade))
            {
                Build(setup, false);
            }
            else if (needsDetectionBoxes)
            {
                ReplaceExistingPickables(setup, false);
            }

            if (setup != null && needsSo101Arms)
            {
                UpgradePlaceholderArmsToSo101(setup, false);
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

        private static Material[] GetDetectionLabelMaterials()
        {
            Material[] materials = new Material[DetectionClasses.Length];
            for (int index = 0; index < DetectionClasses.Length; index++)
            {
                string className = DetectionClasses[index];
                string photoFileName = className == "banana"
                    ? "banana 1"
                    : className == "orange" ? "orange 1" : className;
                Texture2D photo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"{ObjectPhotoFolder}/{photoFileName}.png");
                if (photo == null)
                {
                    Debug.LogError($"Missing YOLO object photo: {photoFileName}.png");
                    continue;
                }

                string materialPath = $"{MaterialFolder}/Detection_Label_{className}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null)
                    {
                        shader = Shader.Find("Unlit/Texture");
                    }
                    material = new Material(shader) { name = $"Detection_Label_{className}" };
                    AssetDatabase.CreateAsset(material, materialPath);
                }

                material.mainTexture = photo;
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", photo);
                }
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", Color.white);
                }
                if (material.HasProperty("_Cull"))
                {
                    material.SetFloat("_Cull", 0f);
                }
                EditorUtility.SetDirty(material);
                materials[index] = material;
            }
            return materials;
        }

        private static void ReplaceExistingPickables(
            Phase1SceneSetup setup,
            bool registerUndo)
        {
            SortingAreaFeedObject[] pickables =
                setup.GetComponentsInChildren<SortingAreaFeedObject>(true);
            if (pickables.Length == 0)
            {
                Debug.LogWarning("No initial sorting-area objects were found to replace.", setup);
                return;
            }

            System.Array.Sort(
                pickables,
                (left, right) => string.CompareOrdinal(left.name, right.name));
            if (registerUndo)
            {
                Undo.RegisterFullObjectHierarchyUndo(
                    setup.gameObject,
                    "Replace Pickables With Detection Boxes");
            }

            EnsureFolder(MaterialFolder);
            Material[] labelMaterials = GetDetectionLabelMaterials();
            Material bodyMaterial = GetOrCreateMaterial(
                "Detection_Box_Body",
                new Color(0.34f, 0.37f, 0.4f));
            setup.ConfigureDetectionBoxes(DetectionClasses, labelMaterials, bodyMaterial);

            for (int index = 0; index < pickables.Length; index++)
            {
                GameObject pickable = pickables[index].gameObject;
                pickables[index].SetReleaseDelay(
                    index * Phase1SceneSetup.DetectionBoxReleaseInterval);
                DetectionLabeledBox labeledBox =
                    pickable.GetComponent<DetectionLabeledBox>();
                if (labeledBox == null)
                {
                    labeledBox = pickable.AddComponent<DetectionLabeledBox>();
                }

                int classIndex = index % DetectionClasses.Length;
                labeledBox.Configure(
                    DetectionClasses[classIndex],
                    bodyMaterial,
                    labelMaterials[classIndex],
                    index + 1);
                EditorUtility.SetDirty(pickable);
            }

            SeparateOverlappingPickables(pickables);

            foreach (WorkstationCameraController cameraController in
                setup.GetComponentsInChildren<WorkstationCameraController>(true))
            {
                cameraController.SetOverheadHeightAboveBelt(5f);
                EditorUtility.SetDirty(cameraController);
                EditorUtility.SetDirty(cameraController.transform);
            }

            EnsureDropZoneColliders(setup);

            EditorUtility.SetDirty(setup);
            EditorSceneManager.MarkSceneDirty(setup.gameObject.scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log(
                $"Replaced {pickables.Length} initial objects with single-face YOLO detection boxes.",
                setup);
        }

        private static void SeparateOverlappingPickables(
            SortingAreaFeedObject[] pickables)
        {
            const float minimumSpacing = 1.05f;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                for (int leftIndex = 0; leftIndex < pickables.Length; leftIndex++)
                {
                    Transform left = pickables[leftIndex].transform;
                    for (int rightIndex = leftIndex + 1; rightIndex < pickables.Length; rightIndex++)
                    {
                        Transform right = pickables[rightIndex].transform;
                        Vector3 offset = right.position - left.position;
                        offset.y = 0f;
                        float distance = offset.magnitude;
                        if (distance >= minimumSpacing)
                        {
                            continue;
                        }

                        Vector3 direction = distance > 0.001f
                            ? offset / distance
                            : Quaternion.Euler(0f, (leftIndex * 47f + rightIndex * 83f) % 360f, 0f) *
                                Vector3.forward;
                        Vector3 correction = direction * ((minimumSpacing - distance) * 0.5f);
                        left.position -= correction;
                        right.position += correction;
                    }
                }
            }
        }

        private static bool NeedsDetectionBoxUpgrade(Phase1SceneSetup setup)
        {
            DetectionLabeledBox[] boxes =
                setup.GetComponentsInChildren<DetectionLabeledBox>(true);
            if (boxes.Length < 15)
            {
                return true;
            }

            foreach (DetectionLabeledBox box in boxes)
            {
                if (box.NeedsVisualUpgrade)
                {
                    return true;
                }
            }
            return false;
        }

        private static void EnsureDropZoneColliders(Phase1SceneSetup setup)
        {
            foreach (RobotWorkstation workstation in
                setup.GetComponentsInChildren<RobotWorkstation>(true))
            {
                if (workstation.DropZone == null)
                {
                    continue;
                }

                Transform dropSurface = workstation.DropZone.Find("DropSurface");
                if (dropSurface != null && dropSurface.GetComponent<Collider>() == null)
                {
                    dropSurface.gameObject.AddComponent<BoxCollider>();
                    EditorUtility.SetDirty(dropSurface.gameObject);
                }
            }
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
