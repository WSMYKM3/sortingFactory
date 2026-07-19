using UnityEngine;

namespace SortingFactory.Phase1
{
    [DisallowMultipleComponent]
    public sealed class So101RobotArmRig : MonoBehaviour
    {
        public const int CurrentRigVersion = 1;

        [SerializeField, HideInInspector] private int rigVersion;
        [SerializeField] private Transform shoulderPan;
        [SerializeField] private Transform shoulderLift;
        [SerializeField] private Transform elbowFlex;
        [SerializeField] private Transform wristFlex;
        [SerializeField] private Transform wristRoll;
        [SerializeField] private Transform gripPoint;
        [SerializeField] private Transform objectHoldPoint;
        [SerializeField] private Transform gripperLeft;
        [SerializeField] private Transform gripperRight;

        public Transform ShoulderPan => shoulderPan;
        public Transform ShoulderLift => shoulderLift;
        public Transform ElbowFlex => elbowFlex;
        public Transform WristFlex => wristFlex;
        public Transform WristRoll => wristRoll;
        public Transform GripPoint => gripPoint;
        public Transform ObjectHoldPoint => objectHoldPoint;
        public Transform GripperLeft => gripperLeft;
        public Transform GripperRight => gripperRight;
        public bool NeedsUpgrade => rigVersion < CurrentRigVersion ||
            shoulderPan == null || shoulderLift == null || elbowFlex == null ||
            wristFlex == null || wristRoll == null || gripPoint == null ||
            objectHoldPoint == null || gripperLeft == null || gripperRight == null;

        public void Configure(
            Transform newShoulderPan,
            Transform newShoulderLift,
            Transform newElbowFlex,
            Transform newWristFlex,
            Transform newWristRoll,
            Transform newGripPoint,
            Transform newObjectHoldPoint,
            Transform newGripperLeft,
            Transform newGripperRight)
        {
            rigVersion = CurrentRigVersion;
            shoulderPan = newShoulderPan;
            shoulderLift = newShoulderLift;
            elbowFlex = newElbowFlex;
            wristFlex = newWristFlex;
            wristRoll = newWristRoll;
            gripPoint = newGripPoint;
            objectHoldPoint = newObjectHoldPoint;
            gripperLeft = newGripperLeft;
            gripperRight = newGripperRight;
        }
    }
}
