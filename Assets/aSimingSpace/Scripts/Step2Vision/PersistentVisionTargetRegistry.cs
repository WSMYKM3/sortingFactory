using System;
using System.Collections.Generic;
using UnityEngine;

namespace SortingFactory.Step2
{
    public enum PersistentVisionTargetState
    {
        Tentative,
        Confirmed,
        Coasting,
        Lost
    }

    public sealed class PersistentVisionTarget
    {
        private VisionDetectionResult lastObservedDetection;
        private float velocityCenterX;
        private float velocityCenterY;
        private float velocityWidth;
        private float velocityHeight;
        private double firstSeenAt;
        private double lastObservedAt;
        private double lostAt = -1d;
        private bool wasConfirmed;

        internal bool SeenInFrame { get; private set; }
        internal bool ObservedInFrame { get; private set; }

        public int LogicalId { get; }
        public int SourceTrackId { get; private set; }
        public int ClassId { get; private set; }
        public string ClassName { get; private set; }
        public float Confidence { get; private set; }
        public PersistentVisionTargetState State { get; private set; }
        public int TotalObservedFrames { get; private set; }
        public int ConsecutiveObservedFrames { get; private set; }
        public int MissedFrames { get; private set; }
        public int TrackIdSwitches { get; private set; }
        public float ObservationGapSeconds { get; private set; }
        public float MaximumObservationGapSeconds { get; private set; }
        public bool IsLocked { get; internal set; }
        public bool HasConveyorPosition { get; private set; }
        public float LastObservedSplineDistance { get; private set; }
        public float PredictedSplineDistance { get; private set; }
        public float ConveyorLateralOffset { get; private set; }
        public Vector3 PredictedBeltPosition { get; private set; }

        internal double LastObservedAt => lastObservedAt;
        internal double LostAt => lostAt;

        internal PersistentVisionTarget(
            int logicalId,
            VisionDetectionResult detection,
            double observationTime,
            ConveyorProjection? conveyorProjection)
        {
            LogicalId = logicalId;
            SourceTrackId = detection.track_id;
            ClassId = detection.class_id;
            ClassName = detection.class_name ?? string.Empty;
            firstSeenAt = observationTime;
            lastObservedAt = observationTime;
            State = PersistentVisionTargetState.Tentative;
            Observe(detection, observationTime, conveyorProjection);
        }

        internal void BeginFrame()
        {
            SeenInFrame = false;
            ObservedInFrame = false;
        }

        internal void Observe(
            VisionDetectionResult detection,
            double observationTime,
            ConveyorProjection? conveyorProjection)
        {
            SeenInFrame = true;
            ObservedInFrame = true;

            if (lastObservedDetection != null)
            {
                double elapsed = observationTime - lastObservedAt;
                if (elapsed > 0.001d)
                {
                    float inverseElapsed = 1f / (float)elapsed;
                    float blend = TotalObservedFrames <= 1 ? 1f : 0.45f;
                    velocityCenterX = Mathf.Lerp(
                        velocityCenterX,
                        (detection.bbox_center_x - lastObservedDetection.bbox_center_x) * inverseElapsed,
                        blend);
                    velocityCenterY = Mathf.Lerp(
                        velocityCenterY,
                        (detection.bbox_center_y - lastObservedDetection.bbox_center_y) * inverseElapsed,
                        blend);
                    velocityWidth = Mathf.Lerp(
                        velocityWidth,
                        (detection.bbox_width - lastObservedDetection.bbox_width) * inverseElapsed,
                        blend);
                    velocityHeight = Mathf.Lerp(
                        velocityHeight,
                        (detection.bbox_height - lastObservedDetection.bbox_height) * inverseElapsed,
                        blend);
                    MaximumObservationGapSeconds = Mathf.Max(
                        MaximumObservationGapSeconds,
                        (float)elapsed);
                }
            }

            AdoptSourceTrackId(detection.track_id);
            ClassId = detection.class_id;
            ClassName = detection.class_name ?? string.Empty;
            Confidence = detection.confidence;
            lastObservedDetection = CloneDetection(detection);
            lastObservedAt = observationTime;
            TotalObservedFrames++;
            ConsecutiveObservedFrames++;
            MissedFrames = 0;
            lostAt = -1d;

            if (conveyorProjection.HasValue)
            {
                ConveyorProjection projection = conveyorProjection.Value;
                HasConveyorPosition = true;
                LastObservedSplineDistance = projection.Distance;
                PredictedSplineDistance = projection.Distance;
                ConveyorLateralOffset = projection.LateralOffset;
                PredictedBeltPosition = projection.WorldPosition;
            }
        }

        internal void ApplyServerPrediction(VisionDetectionResult detection)
        {
            SeenInFrame = true;
            AdoptSourceTrackId(detection.track_id);
        }

        internal void EndFrame()
        {
            if (ObservedInFrame)
            {
                return;
            }

            ConsecutiveObservedFrames = 0;
            MissedFrames++;
        }

        internal void Tick(
            double now,
            int confirmationHits,
            float coastingDelaySeconds,
            float tentativeTimeoutSeconds,
            float lostTimeoutSeconds,
            float conveyorSpeed,
            float conveyorLength,
            Func<float, float, Vector3> evaluateConveyorPosition)
        {
            ObservationGapSeconds = Mathf.Max(0f, (float)(now - lastObservedAt));

            if (!wasConfirmed && TotalObservedFrames >= confirmationHits)
            {
                wasConfirmed = true;
            }

            if (!wasConfirmed)
            {
                State = ObservationGapSeconds <= tentativeTimeoutSeconds
                    ? PersistentVisionTargetState.Tentative
                    : PersistentVisionTargetState.Lost;
            }
            else if (ObservationGapSeconds <= coastingDelaySeconds)
            {
                State = PersistentVisionTargetState.Confirmed;
            }
            else if (ObservationGapSeconds <= lostTimeoutSeconds)
            {
                State = PersistentVisionTargetState.Coasting;
            }
            else
            {
                State = PersistentVisionTargetState.Lost;
            }

            if (State == PersistentVisionTargetState.Lost)
            {
                if (lostAt < 0d)
                {
                    lostAt = now;
                }
            }
            else
            {
                lostAt = -1d;
            }

            if (!HasConveyorPosition || conveyorLength <= 0.001f)
            {
                return;
            }

            PredictedSplineDistance = Mathf.Repeat(
                LastObservedSplineDistance + conveyorSpeed * ObservationGapSeconds,
                conveyorLength);
            if (evaluateConveyorPosition != null)
            {
                PredictedBeltPosition = evaluateConveyorPosition(
                    PredictedSplineDistance,
                    ConveyorLateralOffset);
            }
        }

        internal VisionDetectionResult BuildDisplayDetection(float lostTimeoutSeconds)
        {
            if (lastObservedDetection == null || State == PersistentVisionTargetState.Lost)
            {
                return null;
            }

            float predictionAge = Mathf.Min(ObservationGapSeconds, lostTimeoutSeconds);
            float confidenceScale = ObservationGapSeconds <= 0.001f
                ? 1f
                : Mathf.Max(0.2f, 1f - ObservationGapSeconds / lostTimeoutSeconds);
            return new VisionDetectionResult
            {
                track_id = LogicalId,
                class_id = ClassId,
                class_name = ClassName,
                confidence = Confidence * confidenceScale,
                bbox_center_x = Mathf.Clamp01(
                    lastObservedDetection.bbox_center_x + velocityCenterX * predictionAge),
                bbox_center_y = Mathf.Clamp01(
                    lastObservedDetection.bbox_center_y + velocityCenterY * predictionAge),
                bbox_width = Mathf.Clamp(
                    lastObservedDetection.bbox_width + velocityWidth * predictionAge,
                    0.001f,
                    1f),
                bbox_height = Mathf.Clamp(
                    lastObservedDetection.bbox_height + velocityHeight * predictionAge,
                    0.001f,
                    1f),
                tracking_status = IsLocked ? "locked" : State.ToString().ToLowerInvariant(),
                prediction_age_ms = ObservationGapSeconds * 1000f
            };
        }

        internal float ImageDistanceTo(VisionDetectionResult detection)
        {
            if (lastObservedDetection == null)
            {
                return float.MaxValue;
            }

            float predictedX = lastObservedDetection.bbox_center_x +
                velocityCenterX * ObservationGapSeconds;
            float predictedY = lastObservedDetection.bbox_center_y +
                velocityCenterY * ObservationGapSeconds;
            return Vector2.Distance(
                new Vector2(predictedX, predictedY),
                new Vector2(detection.bbox_center_x, detection.bbox_center_y));
        }

        private void AdoptSourceTrackId(int trackId)
        {
            if (trackId < 0 || trackId == SourceTrackId)
            {
                return;
            }

            if (SourceTrackId >= 0)
            {
                TrackIdSwitches++;
            }
            SourceTrackId = trackId;
        }

        private static VisionDetectionResult CloneDetection(VisionDetectionResult source)
        {
            return new VisionDetectionResult
            {
                track_id = source.track_id,
                class_id = source.class_id,
                class_name = source.class_name,
                confidence = source.confidence,
                bbox_center_x = source.bbox_center_x,
                bbox_center_y = source.bbox_center_y,
                bbox_width = source.bbox_width,
                bbox_height = source.bbox_height,
                tracking_status = source.tracking_status,
                prediction_age_ms = source.prediction_age_ms
            };
        }
    }

    internal readonly struct ConveyorProjection
    {
        public readonly float Distance;
        public readonly float LateralOffset;
        public readonly Vector3 WorldPosition;

        public ConveyorProjection(float distance, float lateralOffset, Vector3 worldPosition)
        {
            Distance = distance;
            LateralOffset = lateralOffset;
            WorldPosition = worldPosition;
        }
    }

    internal sealed class PersistentVisionTargetRegistry
    {
        private readonly List<PersistentVisionTarget> targets =
            new List<PersistentVisionTarget>();
        private int nextLogicalId = 1;
        private int lockedLogicalId = -1;

        public int ConfirmationHits { get; set; } = 3;
        public float CoastingDelaySeconds { get; set; } = 0.18f;
        public float TentativeTimeoutSeconds { get; set; } = 0.45f;
        public float LostTimeoutSeconds { get; set; } = 0.85f;
        public float LostRetentionSeconds { get; set; } = 1.5f;
        public float ReassociationImageDistance { get; set; } = 0.16f;
        public float ReassociationPathDistance { get; set; } = 1f;

        public PersistentVisionTarget LockedTarget =>
            targets.Find(target => target.LogicalId == lockedLogicalId);

        public PersistentVisionTarget[] Snapshot()
        {
            return targets.ToArray();
        }

        public void ProcessFrame(
            VisionDetectionResult[] detections,
            double observationTime,
            double now,
            float conveyorLength,
            Func<VisionDetectionResult, ConveyorProjection?> projectToConveyor)
        {
            foreach (PersistentVisionTarget target in targets)
            {
                target.BeginFrame();
            }

            if (detections != null)
            {
                foreach (VisionDetectionResult detection in detections)
                {
                    if (detection == null)
                    {
                        continue;
                    }

                    bool isPrediction = string.Equals(
                        detection.tracking_status,
                        "predicted",
                        StringComparison.OrdinalIgnoreCase);
                    ConveyorProjection? projection = isPrediction || projectToConveyor == null
                        ? null
                        : projectToConveyor(detection);
                    PersistentVisionTarget target = FindBySourceTrackId(detection.track_id);

                    if (target == null && !isPrediction)
                    {
                        target = FindReassociationCandidate(
                            detection,
                            projection,
                            conveyorLength);
                    }

                    if (target == null)
                    {
                        if (isPrediction)
                        {
                            continue;
                        }

                        target = new PersistentVisionTarget(
                            nextLogicalId++,
                            detection,
                            observationTime,
                            projection);
                        targets.Add(target);
                        continue;
                    }

                    if (isPrediction)
                    {
                        target.ApplyServerPrediction(detection);
                    }
                    else
                    {
                        target.Observe(detection, observationTime, projection);
                    }
                }
            }

            foreach (PersistentVisionTarget target in targets)
            {
                target.EndFrame();
            }

            RemoveExpiredTargets(now);
        }

        public void Tick(
            double now,
            float conveyorSpeed,
            float conveyorLength,
            Func<float, float, Vector3> evaluateConveyorPosition)
        {
            foreach (PersistentVisionTarget target in targets)
            {
                target.Tick(
                    now,
                    ConfirmationHits,
                    CoastingDelaySeconds,
                    TentativeTimeoutSeconds,
                    LostTimeoutSeconds,
                    conveyorSpeed,
                    conveyorLength,
                    evaluateConveyorPosition);
            }

            RemoveExpiredTargets(now);
        }

        public VisionDetectionResult[] BuildDisplayDetections()
        {
            List<VisionDetectionResult> detections = new List<VisionDetectionResult>();
            foreach (PersistentVisionTarget target in targets)
            {
                VisionDetectionResult detection = target.BuildDisplayDetection(LostTimeoutSeconds);
                if (detection != null)
                {
                    detections.Add(detection);
                }
            }
            detections.Sort((left, right) => left.track_id.CompareTo(right.track_id));
            return detections.ToArray();
        }

        public bool TryLockBestTarget(out PersistentVisionTarget lockedTarget)
        {
            if (LockedTarget != null)
            {
                lockedTarget = LockedTarget;
                return true;
            }

            PersistentVisionTarget best = null;
            foreach (PersistentVisionTarget target in targets)
            {
                if (target.State != PersistentVisionTargetState.Confirmed)
                {
                    continue;
                }

                if (best == null || target.TotalObservedFrames > best.TotalObservedFrames)
                {
                    best = target;
                }
            }

            if (best == null)
            {
                lockedTarget = null;
                return false;
            }

            lockedLogicalId = best.LogicalId;
            best.IsLocked = true;
            lockedTarget = best;
            return true;
        }

        public void ReleaseLockedTarget()
        {
            PersistentVisionTarget locked = LockedTarget;
            if (locked != null)
            {
                locked.IsLocked = false;
            }
            lockedLogicalId = -1;
        }

        public void Clear()
        {
            targets.Clear();
            nextLogicalId = 1;
            lockedLogicalId = -1;
        }

        private PersistentVisionTarget FindBySourceTrackId(int sourceTrackId)
        {
            if (sourceTrackId < 0)
            {
                return null;
            }

            return targets.Find(target =>
                target.SourceTrackId == sourceTrackId &&
                target.State != PersistentVisionTargetState.Lost);
        }

        private PersistentVisionTarget FindReassociationCandidate(
            VisionDetectionResult detection,
            ConveyorProjection? projection,
            float conveyorLength)
        {
            PersistentVisionTarget best = null;
            float bestScore = float.MaxValue;

            foreach (PersistentVisionTarget candidate in targets)
            {
                if (candidate.SeenInFrame ||
                    candidate.State == PersistentVisionTargetState.Lost ||
                    candidate.ClassId != detection.class_id ||
                    candidate.ObservationGapSeconds > LostTimeoutSeconds)
                {
                    continue;
                }

                float score;
                if (projection.HasValue && candidate.HasConveyorPosition && conveyorLength > 0.001f)
                {
                    float pathDifference = Mathf.Abs(
                        projection.Value.Distance - candidate.PredictedSplineDistance);
                    pathDifference = Mathf.Min(pathDifference, conveyorLength - pathDifference);
                    if (pathDifference > ReassociationPathDistance)
                    {
                        continue;
                    }
                    score = pathDifference / Mathf.Max(0.001f, ReassociationPathDistance);
                }
                else
                {
                    float imageDistance = candidate.ImageDistanceTo(detection);
                    if (imageDistance > ReassociationImageDistance)
                    {
                        continue;
                    }
                    score = imageDistance / Mathf.Max(0.001f, ReassociationImageDistance);
                }

                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private void RemoveExpiredTargets(double now)
        {
            for (int index = targets.Count - 1; index >= 0; index--)
            {
                PersistentVisionTarget target = targets[index];
                if (target.State != PersistentVisionTargetState.Lost ||
                    target.LostAt < 0d ||
                    now - target.LostAt <= LostRetentionSeconds)
                {
                    continue;
                }

                if (target.LogicalId == lockedLogicalId)
                {
                    lockedLogicalId = -1;
                }
                targets.RemoveAt(index);
            }
        }
    }
}
