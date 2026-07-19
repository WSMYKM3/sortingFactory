using UnityEngine;
using UnityEngine.Splines;

namespace SortingFactory.Phase1
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SplineConveyorObject : MonoBehaviour
    {
        [SerializeField] private SplineContainer conveyorPath;
        [SerializeField, Min(0f)] private float conveyorSpeed = 1f;
        [SerializeField] private float distanceAlongSpline;
        [SerializeField] private float lateralOffset;
        [SerializeField] private float heightAboveSpline;
        [SerializeField] private bool alignWithConveyor = true;
        [SerializeField] private bool followConveyor = true;

        private Rigidbody body;

        public bool IsFollowingConveyor => followConveyor;

        public void Configure(
            SplineContainer path,
            float normalizedPathPosition,
            float sideOffset,
            float worldHeightAboveSpline,
            float speed = 1f,
            bool shouldAlignWithConveyor = true)
        {
            conveyorPath = path;
            lateralOffset = sideOffset;
            heightAboveSpline = worldHeightAboveSpline;
            conveyorSpeed = speed;
            alignWithConveyor = shouldAlignWithConveyor;

            if (conveyorPath != null && conveyorPath.Spline != null)
            {
                distanceAlongSpline = conveyorPath.Spline.ConvertIndexUnit(
                    normalizedPathPosition,
                    PathIndexUnit.Normalized,
                    PathIndexUnit.Distance);
            }

            PrepareRigidbody();
        }

        public void SetConveyorMotionEnabled(bool shouldFollow)
        {
            followConveyor = shouldFollow;
            PrepareRigidbody();

            body.isKinematic = shouldFollow;
            body.useGravity = !shouldFollow;
            if (!shouldFollow)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        public void SetConveyorSpeed(float speed)
        {
            conveyorSpeed = Mathf.Max(0f, speed);
        }

        private void Awake()
        {
            PrepareRigidbody();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                PrepareRigidbody();
            }
        }

        private void FixedUpdate()
        {
            if (!followConveyor || conveyorPath == null || conveyorPath.Spline == null)
            {
                return;
            }

            Spline spline = conveyorPath.Spline;
            float length = spline.GetLength();
            if (length <= 0.001f)
            {
                return;
            }

            distanceAlongSpline = Mathf.Repeat(distanceAlongSpline + conveyorSpeed * Time.fixedDeltaTime, length);
            float normalizedPosition = SplineUtility.GetNormalizedInterpolation(
                spline,
                distanceAlongSpline,
                PathIndexUnit.Distance);

            Vector3 position = conveyorPath.EvaluatePosition(normalizedPosition);
            Vector3 tangent = conveyorPath.EvaluateTangent(normalizedPosition);
            tangent.y = 0f;
            tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;

            body.MovePosition(position + side * lateralOffset + Vector3.up * heightAboveSpline);
            if (alignWithConveyor)
            {
                body.MoveRotation(Quaternion.LookRotation(tangent, Vector3.up));
            }
        }

        private void PrepareRigidbody()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            if (body == null)
            {
                return;
            }

            if (followConveyor && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.useGravity = !followConveyor;
            body.isKinematic = followConveyor;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = followConveyor
                ? CollisionDetectionMode.ContinuousSpeculative
                : CollisionDetectionMode.Continuous;
        }
    }
}
