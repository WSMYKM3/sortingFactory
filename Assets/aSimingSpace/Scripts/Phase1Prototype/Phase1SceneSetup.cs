using UnityEngine;
using UnityEngine.Splines;

namespace SortingFactory.Phase1
{
    public sealed class Phase1SceneSetup : MonoBehaviour
    {
        [Header("Workstations")]
        [SerializeField, Min(1)] private int workstationCount = 3;
        [SerializeField] private Vector3 workspaceSize = new Vector3(3.2f, 2.4f, 2.6f);
        [SerializeField] private float robotSideOffset = 3.5f;
        [SerializeField] private float dropZoneSideOffset = 5.2f;
        [SerializeField] private GameObject robotArmPrefab;
        [SerializeField] private bool placeLastWorkstationOnOppositeSide = true;
        [SerializeField] private Vector3 workstationPathPositions = new Vector3(0.18f, 0.5f, 0.86f);

        [Header("Initial Objects")]
        [SerializeField, Min(1)] private int objectCount = 15;
        [SerializeField, Range(0f, 1f)] private float firstObjectPathPosition = 0.035f;
        [SerializeField, Range(0f, 1f)] private float lastObjectPathPosition = 0.63f;
        [SerializeField] private float laneSpread = 0.18f;
        [SerializeField] private int randomSeed = 2026;
        [SerializeField] private GameObject[] objectPrefabs;

        [SerializeField, HideInInspector] private Material[] stationMaterials;
        [SerializeField, HideInInspector] private Material[] objectMaterials;
        [SerializeField, HideInInspector] private Material guideMaterial;

        public const string SceneContentRootName = "Phase1 Scene Content";

        public GameObject RobotArmPrefab => robotArmPrefab;
        public bool HasSceneContent => transform.Find(SceneContentRootName) != null;

        public void ConfigureGeneratedMaterials(
            Material[] newStationMaterials,
            Material[] newObjectMaterials,
            Material newGuideMaterial)
        {
            stationMaterials = newStationMaterials;
            objectMaterials = newObjectMaterials;
            guideMaterial = newGuideMaterial;
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

            if (stationMaterials == null || stationMaterials.Length == 0 ||
                objectMaterials == null || objectMaterials.Length == 0 || guideMaterial == null)
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

            CreateEntrance(contentRoot, conveyorPath, beltSurfaceY);
            CreateInitialObjects(contentRoot, conveyorPath, beltSurfaceY);

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

            float workspaceCenterY = beltSurfaceY + workspaceSize.y * 0.5f;
            GameObject workspace = new GameObject("WorkspaceVolume");
            workspace.transform.SetParent(stationObject.transform, false);
            workspace.transform.localPosition = new Vector3(0f, workspaceCenterY, 0f);
            BoxCollider workspaceTrigger = workspace.AddComponent<BoxCollider>();
            workspaceTrigger.isTrigger = true;
            workspaceTrigger.size = workspaceSize;
            PrototypeVisualFactory.CreateWireBox(workspace.transform, workspaceSize, stationMaterial, 0.045f);

            Transform robotMount = new GameObject("RobotMount").transform;
            robotMount.SetParent(stationObject.transform, false);
            robotMount.localPosition = new Vector3(sideSign * robotSideOffset, 0f, 0f);
            robotMount.localRotation = sideSign > 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            CreateRobotArm(robotMount, stationMaterial);

            Transform cameraMount = new GameObject("CameraMount_Step2").transform;
            cameraMount.SetParent(stationObject.transform, false);
            cameraMount.localPosition = new Vector3(sideSign * robotSideOffset * 0.45f, beltSurfaceY + 4.2f, 0f);
            cameraMount.localRotation = Quaternion.Euler(68f, sideSign > 0f ? -90f : 90f, 0f);

            Transform dropZone = new GameObject($"DropZone_{index + 1}").transform;
            dropZone.SetParent(stationObject.transform, false);
            dropZone.localPosition = new Vector3(sideSign * dropZoneSideOffset, 0.06f, 0f);
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

        private void CreateEntrance(Transform parent, SplineContainer conveyorPath, float beltSurfaceY)
        {
            Vector3 position = conveyorPath.EvaluatePosition(firstObjectPathPosition);
            Vector3 tangent = Flatten(conveyorPath.EvaluateTangent(firstObjectPathPosition));

            Transform entrance = new GameObject("ObjectDispersalGuide").transform;
            entrance.SetParent(parent, false);
            entrance.SetPositionAndRotation(
                new Vector3(position.x, beltSurfaceY + 0.18f, position.z),
                Quaternion.LookRotation(tangent, Vector3.up));

            PrototypeVisualFactory.CreateGuideRail(entrance, -0.88f, guideMaterial);
            PrototypeVisualFactory.CreateGuideRail(entrance, 0.88f, guideMaterial);
        }

        private void CreateInitialObjects(Transform parent, SplineContainer conveyorPath, float beltSurfaceY)
        {
            Transform objectsRoot = new GameObject($"InitialObjects_{objectCount}").transform;
            objectsRoot.SetParent(parent, false);
            System.Random random = new System.Random(randomSeed);

            for (int i = 0; i < objectCount; i++)
            {
                float progress = objectCount == 1 ? 0f : i / (objectCount - 1f);
                float pathT = Mathf.Lerp(firstObjectPathPosition, lastObjectPathPosition, progress);
                Vector3 pathPosition = conveyorPath.EvaluatePosition(pathT);
                Vector3 tangent = Flatten(conveyorPath.EvaluateTangent(pathT));
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                float sideOffset = Mathf.Lerp(-laneSpread, laneSpread, (float)random.NextDouble());

                GameObject item = CreateInitialObject(i, random);
                item.transform.SetParent(objectsRoot, true);
                item.transform.SetPositionAndRotation(
                    new Vector3(pathPosition.x, beltSurfaceY + 0.08f, pathPosition.z) + side * sideOffset,
                    Quaternion.LookRotation(tangent, Vector3.up));
            }
        }

        private GameObject CreateInitialObject(int index, System.Random random)
        {
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

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }
}
