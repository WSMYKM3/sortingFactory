using System;
using UnityEngine;

namespace SortingFactory.Phase1
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DetectionLabeledBox : MonoBehaviour
    {
        private const int CurrentVisualVersion = 6;
        private static readonly Vector3 DetectionLabelScale = Vector3.one * 1.6f;

        [SerializeField] private string detectionClass;
        [SerializeField] private Renderer labelRenderer;
        [SerializeField, HideInInspector] private int visualVersion;

        public string DetectionClass => detectionClass;
        public Renderer LabelRenderer => labelRenderer;
        public bool NeedsVisualUpgrade => visualVersion < CurrentVisualVersion;

        public bool MatchesVisionClass(string className)
        {
            return string.Equals(
                detectionClass,
                className,
                StringComparison.OrdinalIgnoreCase);
        }

        public static GameObject Create(
            string className,
            Material bodyMaterial,
            Material labelMaterial,
            int index)
        {
            GameObject root = new GameObject($"Pickable_{index:00}_{className}");
            DetectionLabeledBox labeledBox = root.AddComponent<DetectionLabeledBox>();
            labeledBox.Configure(className, bodyMaterial, labelMaterial, index);
            return root;
        }

        public void Configure(
            string className,
            Material bodyMaterial,
            Material labelMaterial,
            int index)
        {
            detectionClass = className ?? string.Empty;
            visualVersion = CurrentVisualVersion;
            gameObject.name = $"Pickable_{index:00}_{detectionClass}";
            RemoveExistingVisuals();
            ConfigureRigidbody();

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "DetectionBoxBody";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            body.transform.localScale = new Vector3(1.6f, 0.56f, 1.6f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

            GameObject label = GameObject.CreatePrimitive(PrimitiveType.Quad);
            label.name = $"SingleFaceLabel_{detectionClass}";
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, 0.585f, 0f);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            label.transform.localScale = DetectionLabelScale;
            labelRenderer = label.GetComponent<Renderer>();
            labelRenderer.sharedMaterial = labelMaterial;

            Collider labelCollider = label.GetComponent<Collider>();
            if (labelCollider != null)
            {
                DestroyGeneratedObject(labelCollider);
            }
        }

        private void ConfigureRigidbody()
        {
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.mass = 0.65f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.linearDamping = 0.12f;
            body.angularDamping = 0.8f;
        }

        private void RemoveExistingVisuals()
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                DestroyGeneratedObject(transform.GetChild(index).gameObject);
            }
            labelRenderer = null;
        }

        private static void DestroyGeneratedObject(UnityEngine.Object target)
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
    }
}
