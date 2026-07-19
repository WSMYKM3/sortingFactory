using SplineMeshTools.Core;
using UnityEngine;
using UnityEngine.Splines;

namespace SortingFactory.Phase1
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class ClosedLoopConveyorMover : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float conveyorSpeed = 1f;
        [SerializeField] private float conveyorHeightOffset;
        [SerializeField] private bool snapRotation = true;

        private SplineContainer splineContainer;

        public void Configure(float speed, float heightOffset, bool shouldSnapRotation)
        {
            conveyorSpeed = speed;
            conveyorHeightOffset = heightOffset;
            snapRotation = shouldSnapRotation;
        }

        public void SetConveyorSpeed(float speed)
        {
            conveyorSpeed = Mathf.Max(0f, speed);
        }

        private void Awake()
        {
            splineContainer = GetComponent<SplineContainer>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0 ||
                collision.GetContact(0).point.y <= transform.position.y + conveyorHeightOffset)
            {
                return;
            }

            Rigidbody body = collision.rigidbody;
            if (body == null)
            {
                return;
            }

            SplineConveyorObject conveyorObject = body.GetComponent<SplineConveyorObject>();
            if (conveyorObject != null && conveyorObject.IsFollowingConveyor)
            {
                return;
            }

            (Spline closestSpline, float closestDistance) =
                SplineMeshUtils.FindClosestSplineAndPosition(splineContainer, body.position);
            if (closestSpline == null || closestSpline.GetLength() <= 0.001f)
            {
                return;
            }

            float normalizedPosition = SplineUtility.GetNormalizedInterpolation(
                closestSpline,
                closestDistance,
                PathIndexUnit.Distance);
            Vector3 pathPosition = splineContainer.EvaluatePosition(normalizedPosition);
            Vector3 tangent = splineContainer.EvaluateTangent(normalizedPosition);
            tangent.y = 0f;
            tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : body.transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            float lateralOffset = Vector3.Dot(body.position - pathPosition, side);
            float heightAboveSpline = body.position.y - pathPosition.y;

            if (conveyorObject == null)
            {
                conveyorObject = body.gameObject.AddComponent<SplineConveyorObject>();
            }

            conveyorObject.Configure(
                splineContainer,
                normalizedPosition,
                lateralOffset,
                heightAboveSpline,
                conveyorSpeed,
                snapRotation);
            conveyorObject.SetConveyorMotionEnabled(true);
        }
    }
}
