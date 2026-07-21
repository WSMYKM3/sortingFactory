using System;
using UnityEngine;

namespace SortingFactory.Showcase
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ProjectShowcaseCameraTrack : MonoBehaviour
    {
        [Serializable]
        private struct CameraKey
        {
            public string name;
            public Vector3 position;
            public Vector3 lookAt;
            [Min(0.1f)] public float travelSeconds;
            [Min(0f)] public float holdSeconds;
            [Range(20f, 80f)] public float fieldOfView;
        }

        [SerializeField] private bool playOnStart = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool useUnscaledTime;
        [SerializeField, Min(0f)] private float startDelay = 1.5f;
        [SerializeField] private CameraKey[] keys = Array.Empty<CameraKey>();

        private Camera controlledCamera;
        private int currentKeyIndex;
        private float phaseElapsed;
        private float delayRemaining;
        private bool isPlaying;
        private bool isHolding;

        public bool IsPlaying => isPlaying;
        public int CurrentKeyIndex => currentKeyIndex;

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayFromStart();
            }
        }

        private void LateUpdate()
        {
            if (!isPlaying || keys == null || keys.Length == 0)
            {
                return;
            }

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (delayRemaining > 0f)
            {
                delayRemaining -= deltaTime;
                ApplyKey(keys[0]);
                return;
            }

            phaseElapsed += deltaTime;
            if (isHolding)
            {
                ApplyKey(keys[currentKeyIndex]);
                if (phaseElapsed >= keys[currentKeyIndex].holdSeconds)
                {
                    phaseElapsed = 0f;
                    isHolding = false;
                }
                return;
            }

            int nextKeyIndex = currentKeyIndex + 1;
            if (nextKeyIndex >= keys.Length)
            {
                if (!loop)
                {
                    ApplyKey(keys[currentKeyIndex]);
                    isPlaying = false;
                    return;
                }
                nextKeyIndex = 0;
            }

            CameraKey current = keys[currentKeyIndex];
            CameraKey next = keys[nextKeyIndex];
            float duration = Mathf.Max(0.1f, current.travelSeconds);
            float t = Mathf.Clamp01(phaseElapsed / duration);
            float easedT = t * t * (3f - 2f * t);

            ApplyPose(
                Vector3.Lerp(current.position, next.position, easedT),
                Vector3.Lerp(current.lookAt, next.lookAt, easedT),
                Mathf.Lerp(current.fieldOfView, next.fieldOfView, easedT));

            if (t >= 1f)
            {
                currentKeyIndex = nextKeyIndex;
                phaseElapsed = 0f;
                isHolding = keys[currentKeyIndex].holdSeconds > 0f;
            }
        }

        public void PlayFromStart()
        {
            if (keys == null || keys.Length == 0)
            {
                return;
            }

            currentKeyIndex = 0;
            phaseElapsed = 0f;
            delayRemaining = startDelay;
            isHolding = keys[0].holdSeconds > 0f;
            isPlaying = true;
            ApplyKey(keys[0]);
        }

        public void Pause()
        {
            isPlaying = false;
        }

        public void Resume()
        {
            if (keys != null && keys.Length > 0)
            {
                isPlaying = true;
            }
        }

        public void Stop()
        {
            isPlaying = false;
            currentKeyIndex = 0;
            phaseElapsed = 0f;
            delayRemaining = 0f;
            if (keys != null && keys.Length > 0)
            {
                ApplyKey(keys[0]);
            }
        }

        private void ApplyKey(CameraKey key)
        {
            ApplyPose(key.position, key.lookAt, key.fieldOfView);
        }

        private void ApplyPose(Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            transform.position = position;
            Vector3 viewDirection = lookAt - position;
            if (viewDirection.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            }

            if (controlledCamera != null)
            {
                controlledCamera.fieldOfView = fieldOfView;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (keys == null || keys.Length == 0)
            {
                return;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                CameraKey key = keys[i];
                CameraKey next = keys[(i + 1) % keys.Length];
                Gizmos.color = i == 0
                    ? new Color(0.2f, 0.9f, 1f, 1f)
                    : new Color(1f, 0.78f, 0.2f, 1f);
                Gizmos.DrawWireSphere(key.position, 0.35f);
                Gizmos.DrawLine(key.position, key.lookAt);

                if (loop || i < keys.Length - 1)
                {
                    Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.7f);
                    Gizmos.DrawLine(key.position, next.position);
                }
            }
        }
    }
}
