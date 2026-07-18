using UnityEngine;

namespace SortingFactory.Phase1
{
    public sealed class RobotWorkstation : MonoBehaviour
    {
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
        }
    }
}
