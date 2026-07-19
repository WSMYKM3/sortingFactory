using UnityEngine;
using UnityEngine.Splines;

namespace SortingFactory.Phase1
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SortingAreaFeedObject : MonoBehaviour
    {
        [SerializeField] private SplineContainer feederPath;
        [SerializeField] private SplineContainer mainConveyorPath;
        [SerializeField, Min(0f)] private float releaseDelay;
        [SerializeField, Min(0f)] private float conveyorSpeed = 1f;
        [SerializeField] private float heightAboveFeeder;
        [SerializeField] private float heightAboveMainConveyor;

        private Rigidbody body;
        private float elapsedTime;
        private float feederDistance;
        private FeedState state;

        private enum FeedState
        {
            Waiting,
            ApproachingFeeder,
            FollowingFeeder,
            OnMainConveyor
        }

        public void Configure(
            SplineContainer feeder,
            SplineContainer mainConveyor,
            float delay,
            float feederHeight,
            float mainConveyorHeight,
            float speed = 1f)
        {
            feederPath = feeder;
            mainConveyorPath = mainConveyor;
            releaseDelay = delay;
            heightAboveFeeder = feederHeight;
            heightAboveMainConveyor = mainConveyorHeight;
            conveyorSpeed = speed;
            state = FeedState.Waiting;
            PrepareRigidbody();
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
            if (feederPath == null || feederPath.Spline == null ||
                mainConveyorPath == null || mainConveyorPath.Spline == null)
            {
                return;
            }

            switch (state)
            {
                case FeedState.Waiting:
                    elapsedTime += Time.fixedDeltaTime;
                    if (elapsedTime >= releaseDelay)
                    {
                        state = FeedState.ApproachingFeeder;
                    }
                    break;

                case FeedState.ApproachingFeeder:
                    MoveToFeederEntrance();
                    break;

                case FeedState.FollowingFeeder:
                    FollowFeeder();
                    break;
            }
        }

        private void MoveToFeederEntrance()
        {
            Vector3 target = (Vector3)feederPath.EvaluatePosition(0f) + Vector3.up * heightAboveFeeder;
            Vector3 nextPosition = Vector3.MoveTowards(body.position, target, conveyorSpeed * Time.fixedDeltaTime);
            Vector3 direction = target - body.position;

            body.MovePosition(nextPosition);
            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.y = 0f;
                body.MoveRotation(Quaternion.LookRotation(direction.normalized, Vector3.up));
            }

            if ((nextPosition - target).sqrMagnitude <= 0.0004f)
            {
                feederDistance = 0f;
                state = FeedState.FollowingFeeder;
            }
        }

        private void FollowFeeder()
        {
            Spline feederSpline = feederPath.Spline;
            float feederLength = feederSpline.GetLength();
            feederDistance = Mathf.Min(feederDistance + conveyorSpeed * Time.fixedDeltaTime, feederLength);

            float normalizedPosition = SplineUtility.GetNormalizedInterpolation(
                feederSpline,
                feederDistance,
                PathIndexUnit.Distance);
            Vector3 position = (Vector3)feederPath.EvaluatePosition(normalizedPosition) +
                Vector3.up * heightAboveFeeder;
            Vector3 tangent = feederPath.EvaluateTangent(normalizedPosition);
            tangent.y = 0f;

            body.MovePosition(position);
            if (tangent.sqrMagnitude > 0.0001f)
            {
                body.MoveRotation(Quaternion.LookRotation(tangent.normalized, Vector3.up));
            }

            if (feederDistance >= feederLength - 0.001f)
            {
                TransferToMainConveyor();
            }
        }

        private void TransferToMainConveyor()
        {
            SplineConveyorObject mainConveyorObject = GetComponent<SplineConveyorObject>();
            if (mainConveyorObject == null)
            {
                mainConveyorObject = gameObject.AddComponent<SplineConveyorObject>();
            }

            mainConveyorObject.Configure(
                mainConveyorPath,
                0f,
                0f,
                heightAboveMainConveyor,
                conveyorSpeed);
            mainConveyorObject.SetConveyorMotionEnabled(true);
            state = FeedState.OnMainConveyor;
            enabled = false;
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

            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
    }
}
