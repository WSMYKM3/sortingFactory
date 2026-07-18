using SortingFactory.Phase1;
using SplineMeshTools.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace SortingFactory.Editor
{
    [InitializeOnLoad]
    public static class ConveyorLoopSceneBuilder
    {
        private const string MainScenePath = "Assets/aSimingSpace/MainScene.unity";
        private const int LoopResolution = 128;

        private static readonly Vector3[] LoopPoints =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(7f, 0f, -2f),
            new Vector3(10f, 0f, -7f),
            new Vector3(10f, 0f, -17f),
            new Vector3(7f, 0f, -22f),
            new Vector3(0f, 0f, -24f),
            new Vector3(-7f, 0f, -22f),
            new Vector3(-10f, 0f, -17f),
            new Vector3(-10f, 0f, -7f),
            new Vector3(-7f, 0f, -2f)
        };

        static ConveyorLoopSceneBuilder()
        {
            EditorApplication.delayCall += BuildLoopIfMissing;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Sorting Factory/Build Closed Conveyor Loop")]
        public static void BuildLoopFromMenu()
        {
            BuildLoop(true);
        }

        public static void BuildLoop(bool registerUndo)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SplineContainer conveyorPath = FindConveyorPath();
            if (conveyorPath == null)
            {
                Debug.LogError("No conveyor SplineContainer was found in MainScene.");
                return;
            }

            if (registerUndo)
            {
                Undo.RegisterFullObjectHierarchyUndo(conveyorPath.gameObject, "Build Closed Conveyor Loop");
            }

            Spline loop = new Spline();
            foreach (Vector3 point in LoopPoints)
            {
                loop.Add((float3)point, TangentMode.AutoSmooth);
            }
            loop.Closed = true;
            conveyorPath.Spline = loop;
            ConfigureMeshResolution(conveyorPath);
            RebuildConveyorMesh(conveyorPath);
            ConfigureLoopMover(conveyorPath, registerUndo);

            EditorUtility.SetDirty(conveyorPath);
            EditorSceneManager.MarkSceneDirty(conveyorPath.gameObject.scene);

            Phase1SceneSetup setup = Object.FindFirstObjectByType<Phase1SceneSetup>();
            if (setup != null)
            {
                Phase1SceneBuilder.Build(setup, registerUndo);
            }

            SceneView.RepaintAll();
            Debug.Log($"Closed conveyor loop built with {LoopPoints.Length} control points.", conveyorPath);
        }

        private static void BuildLoopIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().path != MainScenePath)
            {
                return;
            }

            SplineContainer conveyorPath = FindConveyorPath();
            if (conveyorPath != null && conveyorPath.Spline != null && !conveyorPath.Spline.Closed)
            {
                BuildLoop(false);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += BuildLoopIfMissing;
            }
        }

        private static SplineContainer FindConveyorPath()
        {
            SplineContainer[] containers = Object.FindObjectsByType<SplineContainer>(FindObjectsSortMode.None);
            SplineContainer best = null;
            float bestScore = float.MinValue;

            foreach (SplineContainer candidate in containers)
            {
                if (candidate == null || candidate.Splines.Count == 0)
                {
                    continue;
                }

                float score = candidate.CalculateLength();
                if (candidate.GetComponent("ConveyorBeltMover") != null ||
                    candidate.GetComponent<ClosedLoopConveyorMover>() != null)
                {
                    score += 1000f;
                }

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static void ConfigureMeshResolution(SplineContainer conveyorPath)
        {
            foreach (SplineMeshResolution meshGenerator in conveyorPath.GetComponents<SplineMeshResolution>())
            {
                SerializedObject serializedGenerator = new SerializedObject(meshGenerator);
                SerializedProperty resolution = serializedGenerator.FindProperty("meshResolution");
                resolution.arraySize = 1;
                resolution.GetArrayElementAtIndex(0).intValue = LoopResolution;
                serializedGenerator.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(meshGenerator);
            }
        }

        private static void RebuildConveyorMesh(SplineContainer conveyorPath)
        {
            foreach (SplineMeshTools.Core.SplineMesh meshGenerator in
                     conveyorPath.GetComponents<SplineMeshTools.Core.SplineMesh>())
            {
                meshGenerator.GenerateMeshAlongSpline();
                EditorUtility.SetDirty(meshGenerator);
            }

            MeshFilter meshFilter = conveyorPath.GetComponent<MeshFilter>();
            MeshCollider meshCollider = conveyorPath.GetComponent<MeshCollider>();
            if (meshFilter != null && meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                EditorUtility.SetDirty(meshCollider);
            }
        }

        private static void ConfigureLoopMover(SplineContainer conveyorPath, bool registerUndo)
        {
            foreach (MonoBehaviour component in conveyorPath.GetComponents<MonoBehaviour>())
            {
                if (component != null && component.GetType().Name == "ConveyorBeltMover")
                {
                    component.enabled = false;
                    EditorUtility.SetDirty(component);
                }
            }

            ClosedLoopConveyorMover loopMover = conveyorPath.GetComponent<ClosedLoopConveyorMover>();
            if (loopMover == null)
            {
                loopMover = registerUndo
                    ? Undo.AddComponent<ClosedLoopConveyorMover>(conveyorPath.gameObject)
                    : conveyorPath.gameObject.AddComponent<ClosedLoopConveyorMover>();
            }

            loopMover.Configure(1f, 0f, true);
            EditorUtility.SetDirty(loopMover);
        }
    }
}
