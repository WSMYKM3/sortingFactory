using System;
using System.Collections.Generic;
using SortingFactory.Step2;
using UnityEngine;
using UnityEngine.Rendering;

namespace SortingFactory.Phase1
{
    public sealed class FactoryLightingController : MonoBehaviour
    {
        [Header("Factory Environment")]
        [SerializeField, Range(0.2f, 1.25f)] private float sceneBrightness = 0.5f;
        [SerializeField] private Color ambientSkyColor = new Color(0.075f, 0.09f, 0.115f);
        [SerializeField] private Color ambientEquatorColor = new Color(0.035f, 0.045f, 0.055f);
        [SerializeField] private Color ambientGroundColor = new Color(0.012f, 0.015f, 0.02f);

        [Header("Workstation Task Lights")]
        [SerializeField, Range(0.4f, 2f)] private float taskLightPower = 1f;
        [SerializeField, Min(1f)] private float taskLightRange = 10f;
        [SerializeField, Range(25f, 80f)] private float taskLightSpotAngle = 54f;
        [SerializeField, Min(0.1f)] private float focusTrackingSpeed = 7f;
        [SerializeField] private Color idleTaskColor = new Color(0.58f, 0.72f, 0.9f);
        [SerializeField] private Color activeTaskColor = new Color(1f, 0.86f, 0.67f);
        [SerializeField] private Color fillLightColor = new Color(0.2f, 0.55f, 0.9f);

        private const string RuntimeRootName = "Factory Lighting Runtime";
        private readonly List<ExternalLightState> externalLights = new List<ExternalLightState>();
        private readonly List<StationLightRig> stationLightRigs = new List<StationLightRig>();

        private Transform runtimeRoot;
        private Material fixtureMaterial;
        private Material fixtureLensMaterial;
        private Material runtimeSkyboxMaterial;
        private bool environmentCaptured;
        private float nextStationRefreshTime;

        private AmbientMode originalAmbientMode;
        private Color originalAmbientSkyColor;
        private Color originalAmbientEquatorColor;
        private Color originalAmbientGroundColor;
        private float originalAmbientIntensity;
        private float originalReflectionIntensity;
        private Material originalSkyboxMaterial;

        public float SceneBrightness => sceneBrightness;
        public float TaskLightPower => taskLightPower;
        public int ActiveTargetLightCount { get; private set; }
        public int StationLightCount => stationLightRigs.Count;

        private void Awake()
        {
            CaptureEnvironment();
            ApplyEnvironment();
        }

        private void Start()
        {
            RebuildStationLights();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextStationRefreshTime)
            {
                nextStationRefreshTime = Time.unscaledTime + 2f;
                RefreshStationLightsIfNeeded();
            }

            UpdateStationLights(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            RestoreEnvironment();
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(fixtureMaterial);
            DestroyRuntimeObject(fixtureLensMaterial);
            DestroyRuntimeObject(runtimeSkyboxMaterial);
        }

        public void SetSceneBrightness(float value)
        {
            sceneBrightness = Mathf.Clamp(value, 0.2f, 1.25f);
            ApplyEnvironment();
        }

        public void SetTaskLightPower(float value)
        {
            taskLightPower = Mathf.Clamp(value, 0.4f, 2f);
        }

        private void CaptureEnvironment()
        {
            if (environmentCaptured)
            {
                return;
            }

            environmentCaptured = true;
            originalAmbientMode = RenderSettings.ambientMode;
            originalAmbientSkyColor = RenderSettings.ambientSkyColor;
            originalAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            originalAmbientGroundColor = RenderSettings.ambientGroundColor;
            originalAmbientIntensity = RenderSettings.ambientIntensity;
            originalReflectionIntensity = RenderSettings.reflectionIntensity;
            originalSkyboxMaterial = RenderSettings.skybox;

            foreach (Light existingLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                externalLights.Add(new ExternalLightState(existingLight));
            }
        }

        private void ApplyEnvironment()
        {
            if (!environmentCaptured)
            {
                return;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;
            RenderSettings.ambientIntensity = sceneBrightness;
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.25f, 0.55f, sceneBrightness / 1.25f);
            RenderSettings.skybox = GetRuntimeSkyboxMaterial();

            foreach (ExternalLightState state in externalLights)
            {
                if (state.Light == null)
                {
                    continue;
                }

                float lightScale;
                switch (state.Light.type)
                {
                    case LightType.Directional:
                        lightScale = sceneBrightness;
                        break;
                    case LightType.Point:
                        lightScale = sceneBrightness * 0.12f;
                        break;
                    default:
                        lightScale = sceneBrightness * 0.45f;
                        break;
                }

                state.Light.intensity = state.Intensity * lightScale;
            }
        }

        private void RestoreEnvironment()
        {
            if (!environmentCaptured)
            {
                return;
            }

            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientSkyColor = originalAmbientSkyColor;
            RenderSettings.ambientEquatorColor = originalAmbientEquatorColor;
            RenderSettings.ambientGroundColor = originalAmbientGroundColor;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
            RenderSettings.reflectionIntensity = originalReflectionIntensity;
            RenderSettings.skybox = originalSkyboxMaterial;

            foreach (ExternalLightState state in externalLights)
            {
                state.Restore();
            }
        }

        private void RefreshStationLightsIfNeeded()
        {
            RobotWorkstation[] workstations =
                FindObjectsByType<RobotWorkstation>(FindObjectsSortMode.None);
            if (workstations.Length != stationLightRigs.Count)
            {
                RebuildStationLights();
            }
        }

        private void RebuildStationLights()
        {
            if (runtimeRoot != null)
            {
                DestroyRuntimeObject(runtimeRoot.gameObject);
            }

            stationLightRigs.Clear();
            ActiveTargetLightCount = 0;
            runtimeRoot = new GameObject(RuntimeRootName).transform;
            runtimeRoot.SetParent(transform, false);

            RobotWorkstation[] workstations =
                FindObjectsByType<RobotWorkstation>(FindObjectsSortMode.None);
            Array.Sort(
                workstations,
                (left, right) => string.CompareOrdinal(left.ArmId, right.ArmId));
            foreach (RobotWorkstation workstation in workstations)
            {
                if (workstation == null || workstation.Workspace == null)
                {
                    continue;
                }

                StationLightRig rig = CreateStationLightRig(workstation);
                if (rig != null)
                {
                    stationLightRigs.Add(rig);
                }
            }
        }

        private StationLightRig CreateStationLightRig(RobotWorkstation workstation)
        {
            BoxCollider workspace = workstation.Workspace;
            Vector3 focus = GetWorkspaceFocus(workspace);
            Vector3 cameraDirection = Vector3.forward;
            WorkstationCameraController cameraController = null;
            if (workstation.CameraMount != null)
            {
                cameraController = workstation.CameraMount.GetComponent<WorkstationCameraController>();
                cameraDirection = Flatten(workstation.CameraMount.position - focus);
            }
            if (cameraDirection.sqrMagnitude < 0.01f)
            {
                cameraDirection = Flatten(workstation.transform.right);
            }
            cameraDirection.Normalize();

            Transform rigRoot = new GameObject($"Task Lights {workstation.ArmId}").transform;
            rigRoot.SetParent(runtimeRoot, false);

            Vector3 keyPosition = focus + Vector3.up * 4.2f - cameraDirection * 2.1f;
            Light keyLight = CreateSpotLight(
                rigRoot,
                "Responsive Task Light",
                keyPosition,
                focus,
                activeTaskColor,
                8.5f,
                taskLightRange,
                taskLightSpotAngle,
                true);

            Vector3 fillPosition = focus + Vector3.up * 2.6f - cameraDirection * 2.6f;
            Light fillLight = CreateSpotLight(
                rigRoot,
                "Cool Fill Light",
                fillPosition,
                focus,
                fillLightColor,
                2.4f,
                taskLightRange * 0.9f,
                70f,
                false);

            CreateTaskLightFixture(rigRoot, keyPosition, workstation.transform.rotation);
            return new StationLightRig(
                workstation,
                cameraController,
                keyLight,
                fillLight,
                focus);
        }

        private void UpdateStationLights(float deltaTime)
        {
            ActiveTargetLightCount = 0;
            float blend = 1f - Mathf.Exp(-focusTrackingSpeed * Mathf.Max(0f, deltaTime));
            foreach (StationLightRig rig in stationLightRigs)
            {
                if (rig.Workstation == null || rig.Workstation.Workspace == null ||
                    rig.KeyLight == null || rig.FillLight == null)
                {
                    continue;
                }

                PersistentVisionTarget target = SelectLightingTarget(rig);
                bool hasTarget = target != null;
                Vector3 desiredFocus = hasTarget
                    ? GetTargetFocus(target, rig.Workstation.Workspace)
                    : GetWorkspaceFocus(rig.Workstation.Workspace);
                rig.SmoothedFocus = Vector3.Lerp(rig.SmoothedFocus, desiredFocus, blend);

                Quaternion desiredRotation = Quaternion.LookRotation(
                    rig.SmoothedFocus - rig.KeyLight.transform.position,
                    Vector3.up);
                rig.KeyLight.transform.rotation = Quaternion.Slerp(
                    rig.KeyLight.transform.rotation,
                    desiredRotation,
                    blend);

                float keyIntensity = (hasTarget ? 11.5f : 6.5f) * taskLightPower;
                rig.KeyLight.intensity = Mathf.Lerp(rig.KeyLight.intensity, keyIntensity, blend);
                rig.KeyLight.color = Color.Lerp(
                    rig.KeyLight.color,
                    hasTarget ? activeTaskColor : idleTaskColor,
                    blend);
                rig.FillLight.intensity = 2.4f * taskLightPower;

                if (hasTarget)
                {
                    ActiveTargetLightCount++;
                }
            }
        }

        private static PersistentVisionTarget SelectLightingTarget(StationLightRig rig)
        {
            if (rig.CameraController == null)
            {
                return null;
            }

            PersistentVisionTarget lockedTarget = rig.CameraController.LockedTarget;
            if (IsUsableTarget(lockedTarget))
            {
                return lockedTarget;
            }

            PersistentVisionTarget bestTarget = null;
            float bestScore = float.PositiveInfinity;
            Vector3 workspaceCenter = rig.Workstation.Workspace.bounds.center;
            foreach (PersistentVisionTarget target in rig.CameraController.PersistentTargets)
            {
                if (!IsUsableTarget(target))
                {
                    continue;
                }

                Vector3 localPosition = rig.Workstation.Workspace.transform.InverseTransformPoint(
                    target.PredictedBeltPosition);
                Vector3 halfSize = rig.Workstation.Workspace.size * 0.65f;
                if (Mathf.Abs(localPosition.x - rig.Workstation.Workspace.center.x) > halfSize.x ||
                    Mathf.Abs(localPosition.z - rig.Workstation.Workspace.center.z) > halfSize.z)
                {
                    continue;
                }

                float score = HorizontalSqrDistance(target.PredictedBeltPosition, workspaceCenter);
                if (target.State == PersistentVisionTargetState.Coasting)
                {
                    score += 1f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = target;
                }
            }

            return bestTarget;
        }

        private static bool IsUsableTarget(PersistentVisionTarget target)
        {
            return target != null &&
                target.HasConveyorPosition &&
                target.State != PersistentVisionTargetState.Lost;
        }

        private static Vector3 GetTargetFocus(
            PersistentVisionTarget target,
            BoxCollider workspace)
        {
            Vector3 position = target.PredictedBeltPosition;
            position.y = Mathf.Max(position.y + 0.45f, workspace.bounds.min.y + 0.3f);
            return position;
        }

        private static Vector3 GetWorkspaceFocus(BoxCollider workspace)
        {
            Vector3 focus = workspace.bounds.center;
            focus.y = workspace.bounds.min.y + 0.4f;
            return focus;
        }

        private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
        {
            float x = left.x - right.x;
            float z = left.z - right.z;
            return x * x + z * z;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
        }

        private static Light CreateSpotLight(
            Transform parent,
            string lightName,
            Vector3 position,
            Vector3 focus,
            Color color,
            float intensity,
            float range,
            float spotAngle,
            bool castShadows)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(focus - position, Vector3.up));

            Light taskLight = lightObject.AddComponent<Light>();
            taskLight.type = LightType.Spot;
            taskLight.color = color;
            taskLight.intensity = intensity;
            taskLight.range = range;
            taskLight.spotAngle = spotAngle;
            taskLight.innerSpotAngle = spotAngle * 0.62f;
            taskLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            taskLight.shadowStrength = castShadows ? 0.65f : 0f;
            taskLight.renderMode = LightRenderMode.ForcePixel;
            return taskLight;
        }

        private void CreateTaskLightFixture(
            Transform parent,
            Vector3 position,
            Quaternion stationRotation)
        {
            EnsureFixtureMaterials();
            GameObject housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.name = "Industrial Task Light Housing";
            housing.transform.SetParent(parent, false);
            housing.transform.SetPositionAndRotation(position + Vector3.up * 0.1f, stationRotation);
            housing.transform.localScale = new Vector3(1.35f, 0.16f, 0.34f);
            RemoveCollider(housing);
            housing.GetComponent<Renderer>().sharedMaterial = fixtureMaterial;

            GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lens.name = "Warm Light Panel";
            lens.transform.SetParent(parent, false);
            lens.transform.SetPositionAndRotation(position - Vector3.up * 0.005f, stationRotation);
            lens.transform.localScale = new Vector3(1.12f, 0.035f, 0.23f);
            RemoveCollider(lens);
            lens.GetComponent<Renderer>().sharedMaterial = fixtureLensMaterial;
        }

        private void EnsureFixtureMaterials()
        {
            if (fixtureMaterial != null && fixtureLensMaterial != null)
            {
                return;
            }

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                litShader = Shader.Find("Standard");
            }

            fixtureMaterial = new Material(litShader)
            {
                name = "Runtime Industrial Light Housing",
                color = new Color(0.055f, 0.065f, 0.075f)
            };
            fixtureLensMaterial = new Material(litShader)
            {
                name = "Runtime Industrial Light Lens",
                color = new Color(0.9f, 0.75f, 0.55f)
            };
            fixtureLensMaterial.EnableKeyword("_EMISSION");
            fixtureLensMaterial.SetColor("_EmissionColor", activeTaskColor * 2.2f);
        }

        private Material GetRuntimeSkyboxMaterial()
        {
            if (runtimeSkyboxMaterial != null)
            {
                runtimeSkyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(0.18f, 0.42f, sceneBrightness / 1.25f));
                return runtimeSkyboxMaterial;
            }

            Shader skyboxShader = Shader.Find("Skybox/Procedural");
            if (skyboxShader == null)
            {
                return originalSkyboxMaterial;
            }

            runtimeSkyboxMaterial = new Material(skyboxShader)
            {
                name = "Runtime Dark Factory Skybox"
            };
            runtimeSkyboxMaterial.SetFloat("_SunDisk", 2f);
            runtimeSkyboxMaterial.SetFloat("_AtmosphereThickness", 0.28f);
            runtimeSkyboxMaterial.SetColor("_SkyTint", new Color(0.12f, 0.15f, 0.19f));
            runtimeSkyboxMaterial.SetColor("_GroundColor", new Color(0.025f, 0.025f, 0.028f));
            runtimeSkyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(0.18f, 0.42f, sceneBrightness / 1.25f));
            return runtimeSkyboxMaterial;
        }

        private static void RemoveCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyRuntimeObject(collider);
            }
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private sealed class ExternalLightState
        {
            public readonly Light Light;
            public readonly float Intensity;

            public ExternalLightState(Light light)
            {
                Light = light;
                Intensity = light.intensity;
            }

            public void Restore()
            {
                if (Light != null)
                {
                    Light.intensity = Intensity;
                }
            }
        }

        private sealed class StationLightRig
        {
            public readonly RobotWorkstation Workstation;
            public readonly WorkstationCameraController CameraController;
            public readonly Light KeyLight;
            public readonly Light FillLight;
            public Vector3 SmoothedFocus;

            public StationLightRig(
                RobotWorkstation workstation,
                WorkstationCameraController cameraController,
                Light keyLight,
                Light fillLight,
                Vector3 initialFocus)
            {
                Workstation = workstation;
                CameraController = cameraController;
                KeyLight = keyLight;
                FillLight = fillLight;
                SmoothedFocus = initialFocus;
            }
        }
    }
}
