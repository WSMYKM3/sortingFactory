using UnityEngine;

namespace SortingFactory.Phase1
{
    public sealed class RobotWorkstation : MonoBehaviour
    {
        private const float MinimumDropZoneHorizontalScale = 2f;

        [SerializeField] private string armId;
        [SerializeField] private string cameraId;
        [SerializeField, Range(0f, 1f)] private float conveyorPathPosition;
        [SerializeField] private BoxCollider workspace;
        [SerializeField] private Transform robotMount;
        [SerializeField] private Transform cameraMount;
        [SerializeField] private Transform dropZone;

        public string ArmId => armId;
        public string CameraId => cameraId;
        public BoxCollider Workspace => workspace;
        public Transform RobotMount => robotMount;
        public Transform CameraMount => cameraMount;
        public Transform DropZone => dropZone;

        private void Awake()
        {
            UpgradeDropZone();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ApplyDropZoneScale();
            }
        }

        public void Configure(string newArmId, string newCameraId, float pathPosition)
        {
            armId = newArmId;
            cameraId = newCameraId;
            conveyorPathPosition = pathPosition;
        }

        public void SetReferences(
            BoxCollider workspaceTrigger,
            Transform newRobotMount,
            Transform newCameraMount,
            Transform newDropZone)
        {
            workspace = workspaceTrigger;
            robotMount = newRobotMount;
            cameraMount = newCameraMount;
            dropZone = newDropZone;
            ApplyDropZoneScale();
        }

        private void UpgradeDropZone()
        {
            ApplyDropZoneScale();
            if (dropZone == null)
            {
                return;
            }

            CreateOrUpdateContainmentWall(
                "ContainmentWall_Left",
                new Vector3(-0.84f, 0.42f, 0f),
                new Vector3(0.08f, 0.84f, 1.76f));
            CreateOrUpdateContainmentWall(
                "ContainmentWall_Right",
                new Vector3(0.84f, 0.42f, 0f),
                new Vector3(0.08f, 0.84f, 1.76f));
            CreateOrUpdateContainmentWall(
                "ContainmentWall_Front",
                new Vector3(0f, 0.42f, 0.84f),
                new Vector3(1.76f, 0.84f, 0.08f));
            CreateOrUpdateContainmentWall(
                "ContainmentWall_Back",
                new Vector3(0f, 0.42f, -0.84f),
                new Vector3(1.76f, 0.84f, 0.08f));
        }

        private void ApplyDropZoneScale()
        {
            if (dropZone == null)
            {
                return;
            }

            Vector3 scale = dropZone.localScale;
            scale.x = Mathf.Max(scale.x, MinimumDropZoneHorizontalScale);
            scale.z = Mathf.Max(scale.z, MinimumDropZoneHorizontalScale);
            dropZone.localScale = scale;
        }

        private void CreateOrUpdateContainmentWall(
            string wallName,
            Vector3 localPosition,
            Vector3 localSize)
        {
            Transform wall = dropZone.Find(wallName);
            if (wall == null)
            {
                wall = new GameObject(wallName).transform;
                wall.SetParent(dropZone, false);
            }

            wall.localPosition = localPosition;
            wall.localRotation = Quaternion.identity;
            wall.localScale = Vector3.one;
            BoxCollider collider = wall.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = wall.gameObject.AddComponent<BoxCollider>();
            }
            collider.center = Vector3.zero;
            collider.size = localSize;
        }
    }
}
