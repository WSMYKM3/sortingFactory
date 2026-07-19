using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using SortingFactory.Step2;
using SortingFactory.Step4;

namespace SortingFactory.Phase1
{
    public sealed class Phase1SceneSetup : MonoBehaviour
    {
        [Header("Workstations")]
        [SerializeField, Min(1)] private int workstationCount = 3;
        [SerializeField] private Vector3 workspaceSize = new Vector3(3.2f, 2.4f, 2.6f);
        [SerializeField, Range(0.5f, 3f)] private float robotAndWorkspaceScale = 1.5f;
        [SerializeField] private float robotSideOffset = 3.5f;
        [SerializeField] private float dropZoneSideOffset = 5.2f;
        [SerializeField] private GameObject robotArmPrefab;
        [SerializeField] private bool placeLastWorkstationOnOppositeSide = true;
        [SerializeField] private Vector3 workstationPathPositions = new Vector3(0.18f, 0.5f, 0.86f);

        [Header("Sorting Feed Area")]
        [SerializeField, Min(1)] private int objectCount = 15;
        [SerializeField] private Vector2 sortingAreaSize = new Vector2(8f, 6f);
        [SerializeField, Min(0.2f)] private float feederBeltWidth = 2.6f;
        [SerializeField, Range(0.2f, 1f)] private float initialObjectScale = 0.62f;
        [SerializeField, Min(0.2f)] private float objectReleaseInterval = DetectionBoxReleaseInterval;
        [SerializeField, Min(0f)] private float conveyorObjectSpeed = 0.5f;
        [SerializeField] private int randomSeed = 2026;
        [SerializeField] private GameObject[] objectPrefabs;

        [SerializeField, HideInInspector] private Material[] stationMaterials;
        [SerializeField, HideInInspector] private Material[] objectMaterials;
        [SerializeField, HideInInspector] private Material guideMaterial;
        [SerializeField, HideInInspector] private Material feederBeltMaterial;
        [SerializeField, HideInInspector] private string[] detectionObjectClasses;
        [SerializeField, HideInInspector] private Material[] detectionLabelMaterials;
        [SerializeField, HideInInspector] private Material detectionBoxBodyMaterial;
        [SerializeField, HideInInspector] private int generatedContentVersion;

        private SplineContainer mainConveyorPath;

        public const string SceneContentRootName = "Phase1 Scene Content";
        public const int CurrentGeneratedContentVersion = 2;
        public const float DetectionBoxReleaseInterval = 2.4f;

        public GameObject RobotArmPrefab => robotArmPrefab;
        public float ConveyorObjectSpeed => conveyorObjectSpeed;
        public SplineContainer ConveyorPath
        {
            get
            {
                if (mainConveyorPath == null)
                {
                    mainConveyorPath = FindConveyorPath();
                }
                return mainConveyorPath;
            }
        }
        public bool HasSceneContent => transform.Find(SceneContentRootName) != null;
        public bool NeedsGeneratedContentUpgrade =>
            generatedContentVersion < CurrentGeneratedContentVersion;

        private void Awake()
        {
            mainConveyorPath = FindConveyorPath();
            ApplyConveyorObjectSpeed();

            if (GetComponent<FactoryLightingController>() == null)
            {
                gameObject.AddComponent<FactoryLightingController>();
            }
        }

        public void SetConveyorObjectSpeed(float speed)
        {
            float clampedSpeed = Mathf.Max(0f, speed);
            if (Mathf.Approximately(conveyorObjectSpeed, clampedSpeed))
            {
                return;
            }

            conveyorObjectSpeed = clampedSpeed;
            ApplyConveyorObjectSpeed();
        }

        public void ConfigureGeneratedMaterials(
            Material[] newStationMaterials,
            Material[] newObjectMaterials,
            Material newGuideMaterial,
            Material newFeederBeltMaterial)
        {
            stationMaterials = newStationMaterials;
            objectMaterials = newObjectMaterials;
            guideMaterial = newGuideMaterial;
            feederBeltMaterial = newFeederBeltMaterial;
        }

        public void ConfigureDetectionBoxes(
            string[] classNames,
            Material[] labelMaterials,
            Material bodyMaterial)
        {
            detectionObjectClasses = classNames;
            detectionLabelMaterials = labelMaterials;
            detectionBoxBodyMaterial = bodyMaterial;
        }

        public Transform BuildSceneContent()
        {
            RemoveExistingContent();

            SplineContainer conveyorPath = FindConveyorPath();
            if (conveyorPath == null || conveyorPath.Splines.Count == 0)
            {
                Debug.LogError("Step 1 could not find a conveyor SplineContainer in MainScene.", this);
                return null;
            }
            mainConveyorPath = conveyorPath;

            if (stationMaterials == null || stationMaterials.Length == 0 ||
                objectMaterials == null || objectMaterials.Length == 0 ||
                guideMaterial == null || feederBeltMaterial == null ||
                detectionObjectClasses == null || detectionObjectClasses.Length == 0 ||
                detectionLabelMaterials == null || detectionLabelMaterials.Length == 0 ||
                detectionBoxBodyMaterial == null)
            {
                Debug.LogError("Step 1 materials are missing. Use the Build Step 1 Scene button in the Inspector.", this);
                return null;
            }

            Transform contentRoot = new GameObject(SceneContentRootName).transform;
            contentRoot.SetParent(transform, false);

            float beltSurfaceY = FindBeltSurfaceY(conveyorPath);
            for (int i = 0; i < workstationCount; i++)
            {
                float pathT = GetWorkstationPathPosition(i);
                float sideSign = GetWorkstationSide(i);
                CreateWorkstation(
                    contentRoot,
                    conveyorPath,
                    pathT,
                    beltSurfaceY,
                    i,
                    sideSign,
                    stationMaterials[i % stationMaterials.Length]);
            }

            contentRoot.gameObject.AddComponent<Step2CameraDebugPanel>();

            SortingAreaLayout sortingArea = CreateSortingArea(contentRoot, conveyorPath, beltSurfaceY);
            CreateInitialObjects(contentRoot, conveyorPath, sortingArea, beltSurfaceY);
            generatedContentVersion = CurrentGeneratedContentVersion;

            Debug.Log($"Step 1 scene content built: {workstationCount} workstations and {objectCount} placed objects.", this);
            return contentRoot;
        }

        public void RemoveExistingContent()
        {
            Transform existingRoot = transform.Find(SceneContentRootName);
            if (existingRoot == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(existingRoot.gameObject);
            }
            else
            {
                DestroyImmediate(existingRoot.gameObject);
            }
        }

        private SplineContainer FindConveyorPath()
        {
            SplineContainer[] containers = FindObjectsByType<SplineContainer>(FindObjectsSortMode.None);
            SplineContainer best = null;
            float bestScore = float.MinValue;

            foreach (SplineContainer candidate in containers)
            {
                if (candidate == null || candidate.Splines.Count == 0)
                {
                    continue;
                }

                float score = candidate.CalculateLength();
                if (candidate.GetComponent("ConveyorBeltMover") != null)
                {
                    score += 1000f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static float FindBeltSurfaceY(SplineContainer conveyorPath)
        {
            Renderer[] renderers = conveyorPath.GetComponentsInChildren<Renderer>();
            float highestPoint = float.MinValue;

            foreach (Renderer beltRenderer in renderers)
            {
                highestPoint = Mathf.Max(highestPoint, beltRenderer.bounds.max.y);
            }

            if (highestPoint > float.MinValue)
            {
                return highestPoint;
            }

            Vector3 pathStart = conveyorPath.EvaluatePosition(0f);
            return pathStart.y + 0.5f;
        }

        private void CreateWorkstation(
            Transform parent,
            SplineContainer conveyorPath,
            float pathT,
            float beltSurfaceY,
            int index,
            float sideSign,
            Material stationMaterial)
        {
            Vector3 pathPosition = conveyorPath.EvaluatePosition(pathT);
            Vector3 tangent = Flatten(conveyorPath.EvaluateTangent(pathT));
            Quaternion pathRotation = Quaternion.LookRotation(tangent, Vector3.up);

            GameObject stationObject = new GameObject($"Workstation_{index + 1}");
            stationObject.transform.SetParent(parent, false);
            stationObject.transform.SetPositionAndRotation(
                new Vector3(pathPosition.x, 0f, pathPosition.z),
                pathRotation);

            RobotWorkstation station = stationObject.AddComponent<RobotWorkstation>();
            station.Configure($"arm_{index + 1}", $"arm_{index + 1}_camera", pathT);

            float stationScale = Mathf.Max(0.1f, robotAndWorkspaceScale);
            Vector3 scaledWorkspaceSize = workspaceSize * stationScale;
            float workspaceCenterY = beltSurfaceY + scaledWorkspaceSize.y * 0.5f;
            GameObject workspace = new GameObject("WorkspaceVolume");
            workspace.transform.SetParent(stationObject.transform, false);
            workspace.transform.localPosition = new Vector3(0f, workspaceCenterY, 0f);
            BoxCollider workspaceTrigger = workspace.AddComponent<BoxCollider>();
            workspaceTrigger.isTrigger = true;
            workspaceTrigger.size = scaledWorkspaceSize;
            PrototypeVisualFactory.CreateWireBox(
                workspace.transform,
                scaledWorkspaceSize,
                stationMaterial,
                0.045f * stationScale);

            Transform robotMount = new GameObject("RobotMount").transform;
            robotMount.SetParent(stationObject.transform, false);
            robotMount.localPosition = new Vector3(sideSign * robotSideOffset, 0f, 0f);
            robotMount.localRotation = sideSign > 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            robotMount.localScale = Vector3.one * stationScale;
            CreateRobotArm(robotMount, stationMaterial);

            Transform cameraMount = new GameObject("CameraMount_Step2").transform;
            cameraMount.SetParent(stationObject.transform, false);
            cameraMount.localPosition = new Vector3(
                sideSign * robotSideOffset * 0.45f * stationScale,
                beltSurfaceY + 4.2f * stationScale,
                0f);
            cameraMount.rotation = Quaternion.LookRotation(
                workspaceTrigger.bounds.center - cameraMount.position,
                Vector3.up);

            Camera stationCamera = cameraMount.gameObject.AddComponent<Camera>();
            stationCamera.enabled = false;
            stationCamera.fieldOfView = 58f;
            stationCamera.nearClipPlane = 0.08f;
            stationCamera.farClipPlane = 40f;
            stationCamera.depth = -10f - index;

            WorkstationCameraController cameraController =
                cameraMount.gameObject.AddComponent<WorkstationCameraController>();
            cameraController.Configure(
                $"arm_{index + 1}",
                $"arm_{index + 1}_camera",
                stationCamera,
                workspaceTrigger,
                1280,
                720,
                10f,
                85);
            cameraMount.gameObject.AddComponent<WorkstationPickDecisionController>();

            Transform dropZone = new GameObject($"DropZone_{index + 1}").transform;
            dropZone.SetParent(stationObject.transform, false);
            dropZone.localPosition = new Vector3(sideSign * dropZoneSideOffset, 1.74f, 0f);
            PrototypeVisualFactory.CreateDropZone(dropZone, stationMaterial);

            station.SetReferences(workspaceTrigger, robotMount, cameraMount, dropZone);
        }

        private void CreateRobotArm(Transform robotMount, Material stationMaterial)
        {
            if (robotArmPrefab == null)
            {
                PrototypeVisualFactory.CreatePlaceholderRobot(robotMount, stationMaterial);
                return;
            }

            GameObject robot = Instantiate(robotArmPrefab, robotMount, false);
            robot.name = robotArmPrefab.name;
            robot.transform.localPosition = Vector3.zero;
            robot.transform.localRotation = Quaternion.identity;
        }

        private SortingAreaLayout CreateSortingArea(
            Transform parent,
            SplineContainer conveyorPath,
            float beltSurfaceY)
        {
            Vector3 mainEntry = conveyorPath.EvaluatePosition(0f);
            Vector3 loopCenter = Vector3.zero;
            const int centerSamples = 32;
            for (int i = 0; i < centerSamples; i++)
            {
                loopCenter += (Vector3)conveyorPath.EvaluatePosition(i / (float)centerSamples);
            }
            loopCenter /= centerSamples;

            Vector3 outward = Flatten(mainEntry - loopCenter);
            Vector3 feedDirection = -outward;
            Vector3 right = Vector3.Cross(Vector3.up, feedDirection).normalized;
            Vector3 sortingCenter = mainEntry + outward * 12f;
            Vector3 feederStart = sortingCenter + feedDirection * (sortingAreaSize.y * 0.5f);
            Vector3 feederMiddleA = mainEntry + outward * 6f;
            Vector3 feederMiddleB = mainEntry + outward * 2.8f;

            Transform sortingRoot = new GameObject("InitialSortingArea").transform;
            sortingRoot.SetParent(parent, false);
            sortingRoot.SetPositionAndRotation(
                new Vector3(sortingCenter.x, beltSurfaceY - 0.09f, sortingCenter.z),
                Quaternion.LookRotation(feedDirection, Vector3.up));
            PrototypeVisualFactory.CreateSortingPlatform(
                sortingRoot,
                sortingAreaSize,
                feederBeltMaterial,
                guideMaterial);

            Transform feederVisuals = new GameObject("FeederBelt_To_MainConveyor").transform;
            feederVisuals.SetParent(parent, false);
            PrototypeVisualFactory.CreateFeederBeltSegment(
                feederVisuals,
                feederStart,
                feederMiddleA,
                beltSurfaceY,
                feederBeltWidth,
                feederBeltMaterial,
                guideMaterial);
            PrototypeVisualFactory.CreateFeederBeltSegment(
                feederVisuals,
                feederMiddleA,
                feederMiddleB,
                beltSurfaceY,
                feederBeltWidth,
                feederBeltMaterial,
                guideMaterial);
            PrototypeVisualFactory.CreateFeederBeltSegment(
                feederVisuals,
                feederMiddleB,
                mainEntry,
                beltSurfaceY,
                feederBeltWidth,
                feederBeltMaterial,
                guideMaterial);

            GameObject feederPathObject = new GameObject("SortingFeederPath");
            feederPathObject.transform.SetParent(parent, false);
            SplineContainer feederPath = feederPathObject.AddComponent<SplineContainer>();
            Spline feederSpline = new Spline();
            feederSpline.Add((float3)feederStart, TangentMode.AutoSmooth);
            feederSpline.Add((float3)feederMiddleA, TangentMode.AutoSmooth);
            feederSpline.Add((float3)feederMiddleB, TangentMode.AutoSmooth);
            feederSpline.Add((float3)mainEntry, TangentMode.AutoSmooth);
            feederSpline.Closed = false;
            feederPath.Spline = feederSpline;

            return new SortingAreaLayout
            {
                FeederPath = feederPath,
                Center = new Vector3(sortingCenter.x, beltSurfaceY + 0.08f, sortingCenter.z),
                Forward = feedDirection,
                Right = right
            };
        }

        private void CreateInitialObjects(
            Transform parent,
            SplineContainer conveyorPath,
            SortingAreaLayout sortingArea,
            float beltSurfaceY)
        {
            Transform objectsRoot = new GameObject($"SortingAreaObjects_{objectCount}").transform;
            objectsRoot.SetParent(parent, false);
            System.Random random = new System.Random(randomSeed);
            Vector2[] positions = CreateScatteredPositions(random);
            Vector3 feederStartPosition = sortingArea.FeederPath.EvaluatePosition(0f);

            for (int i = 0; i < objectCount; i++)
            {
                Vector3 randomOffset =
                    sortingArea.Right * positions[i].x + sortingArea.Forward * positions[i].y;
                GameObject item = CreateInitialObject(i, random);
                item.transform.SetParent(objectsRoot, true);
                item.transform.localScale *= initialObjectScale;
                item.transform.SetPositionAndRotation(
                    sortingArea.Center + randomOffset,
                    Quaternion.Euler(0f, Mathf.Lerp(0f, 360f, (float)random.NextDouble()), 0f));

                SortingAreaFeedObject feedObject = item.GetComponent<SortingAreaFeedObject>();
                if (feedObject == null)
                {
                    feedObject = item.AddComponent<SortingAreaFeedObject>();
                }

                float releaseDelay = i * Mathf.Max(objectReleaseInterval, DetectionBoxReleaseInterval);
                float feederHeight = beltSurfaceY + 0.08f - feederStartPosition.y;
                Vector3 mainEntry = conveyorPath.EvaluatePosition(0f);
                float mainHeight = beltSurfaceY + 0.08f - mainEntry.y;
                feedObject.Configure(
                    sortingArea.FeederPath,
                    conveyorPath,
                    releaseDelay,
                    feederHeight,
                    mainHeight,
                    conveyorObjectSpeed);
            }
        }

        private void ApplyConveyorObjectSpeed()
        {
            foreach (SortingAreaFeedObject feedObject in
                FindObjectsByType<SortingAreaFeedObject>(FindObjectsSortMode.None))
            {
                feedObject.SetConveyorSpeed(conveyorObjectSpeed);
            }

            foreach (SplineConveyorObject conveyorObject in
                FindObjectsByType<SplineConveyorObject>(FindObjectsSortMode.None))
            {
                conveyorObject.SetConveyorSpeed(conveyorObjectSpeed);
            }

            foreach (ClosedLoopConveyorMover conveyorMover in
                FindObjectsByType<ClosedLoopConveyorMover>(FindObjectsSortMode.None))
            {
                conveyorMover.SetConveyorSpeed(conveyorObjectSpeed);
            }
        }

        private Vector2[] CreateScatteredPositions(System.Random random)
        {
            Vector2[] positions = new Vector2[objectCount];
            float halfWidth = sortingAreaSize.x * 0.5f - 0.65f;
            float halfLength = sortingAreaSize.y * 0.5f - 0.65f;
            const float minimumSpacing = 1.05f;

            for (int i = 0; i < positions.Length; i++)
            {
                Vector2 candidate = Vector2.zero;
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    candidate = new Vector2(
                        Mathf.Lerp(-halfWidth, halfWidth, (float)random.NextDouble()),
                        Mathf.Lerp(-halfLength, halfLength, (float)random.NextDouble()));

                    bool overlaps = false;
                    for (int previous = 0; previous < i; previous++)
                    {
                        if ((positions[previous] - candidate).sqrMagnitude < minimumSpacing * minimumSpacing)
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        break;
                    }
                }

                positions[i] = candidate;
            }

            return positions;
        }

        private GameObject CreateInitialObject(int index, System.Random random)
        {
            int detectionTypeCount = Mathf.Min(
                detectionObjectClasses == null ? 0 : detectionObjectClasses.Length,
                detectionLabelMaterials == null ? 0 : detectionLabelMaterials.Length);
            if (detectionTypeCount > 0 && detectionBoxBodyMaterial != null)
            {
                int typeIndex = index % detectionTypeCount;
                return DetectionLabeledBox.Create(
                    detectionObjectClasses[typeIndex],
                    detectionBoxBodyMaterial,
                    detectionLabelMaterials[typeIndex],
                    index + 1);
            }

            if (objectPrefabs != null && objectPrefabs.Length > 0)
            {
                GameObject prefab = objectPrefabs[random.Next(objectPrefabs.Length)];
                if (prefab != null)
                {
                    GameObject customObject = Instantiate(prefab);
                    customObject.name = $"Pickable_{index + 1:00}_{prefab.name}";
                    EnsurePhysics(customObject);
                    return customObject;
                }
            }

            PrototypeObjectKind kind = (PrototypeObjectKind)(index % 3);
            Material material = objectMaterials[random.Next(objectMaterials.Length)];
            return PrototypeVisualFactory.CreatePickableObject(kind, material, index + 1);
        }

        private static void EnsurePhysics(GameObject item)
        {
            Rigidbody rigidbody = item.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = item.AddComponent<Rigidbody>();
            }

            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

            if (item.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider fallbackCollider = item.AddComponent<BoxCollider>();
                fallbackCollider.size = new Vector3(0.6f, 1f, 0.6f);
                fallbackCollider.center = new Vector3(0f, 0.5f, 0f);
                Debug.LogWarning($"{item.name} had no Collider, so Step 1 added a fallback BoxCollider.", item);
            }
        }

        private float GetWorkstationPathPosition(int index)
        {
            if (workstationCount == 3)
            {
                return index == 0
                    ? workstationPathPositions.x
                    : index == 1
                        ? workstationPathPositions.y
                        : workstationPathPositions.z;
            }

            return Mathf.Lerp(0.18f, 0.86f, workstationCount == 1 ? 0.5f : index / (workstationCount - 1f));
        }

        private float GetWorkstationSide(int index)
        {
            return placeLastWorkstationOnOppositeSide && workstationCount > 1 && index == workstationCount - 1
                ? -1f
                : 1f;
        }

        private struct SortingAreaLayout
        {
            public SplineContainer FeederPath;
            public Vector3 Center;
            public Vector3 Forward;
            public Vector3 Right;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }
}
