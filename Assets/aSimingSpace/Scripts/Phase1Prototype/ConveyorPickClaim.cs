using UnityEngine;

namespace SortingFactory.Phase1
{
    [DisallowMultipleComponent]
    public sealed class ConveyorPickClaim : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string ownerArmId = string.Empty;

        public string OwnerArmId => ownerArmId;
        public bool IsClaimed => !string.IsNullOrEmpty(ownerArmId);

        public bool TryAcquire(string armId)
        {
            if (string.IsNullOrEmpty(armId))
            {
                return false;
            }

            if (IsClaimed && ownerArmId != armId)
            {
                return false;
            }

            ownerArmId = armId;
            return true;
        }

        public void Release(string armId)
        {
            if (!IsClaimed || ownerArmId != armId)
            {
                return;
            }

            ownerArmId = string.Empty;
        }

        public bool IsClaimedByAnotherArm(string armId)
        {
            return IsClaimed && ownerArmId != armId;
        }
    }
}
