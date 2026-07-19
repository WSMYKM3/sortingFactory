using SortingFactory.Step4;
using UnityEngine;

namespace SortingFactory.Step7
{
    [DisallowMultipleComponent]
    public sealed class MultiRobotOperationMonitor : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;

        private WorkstationPickDecisionController[] controllers =
            System.Array.Empty<WorkstationPickDecisionController>();
        private float nextRefreshTime;
        private bool loggedParallelOperation;

        public int ActiveArmCount { get; private set; }
        public int PeakSimultaneousActiveArms { get; private set; }
        public int TotalClaimConflictsPrevented { get; private set; }
        public bool HasObservedParallelOperation => PeakSimultaneousActiveArms >= 2;

        private void Start()
        {
            RefreshControllers();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + refreshInterval;
            if (controllers.Length == 0)
            {
                RefreshControllers();
            }

            int activeCount = 0;
            int conflictCount = 0;
            foreach (WorkstationPickDecisionController controller in controllers)
            {
                if (controller == null)
                {
                    continue;
                }

                if (controller.ArmState != WorkstationArmState.Idle)
                {
                    activeCount++;
                }
                conflictCount += controller.PhysicalClaimConflictCount;
            }

            ActiveArmCount = activeCount;
            TotalClaimConflictsPrevented = conflictCount;
            PeakSimultaneousActiveArms = Mathf.Max(
                PeakSimultaneousActiveArms,
                ActiveArmCount);

            if (!loggedParallelOperation && HasObservedParallelOperation)
            {
                loggedParallelOperation = true;
                Debug.Log(
                    $"Step 7 parallel operation observed: {PeakSimultaneousActiveArms} arms active simultaneously.",
                    this);
            }
        }

        private void RefreshControllers()
        {
            controllers = GetComponentsInChildren<WorkstationPickDecisionController>(true);
        }
    }
}
