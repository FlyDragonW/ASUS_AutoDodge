using UnityEngine;

namespace TaiwanRider
{
    // Deliberately simple monocular baseline for the fusion comparison.
    // Hazard decisions use RGB detections only. The known route is shared by
    // both controllers for steering; no radar contacts or collider data enter here.
    [DefaultExecutionOrder(-140)]
    public sealed class VisionOnlyWarningBaseline : MonoBehaviour, IScooterInput
    {
        public ScenarioRunner session;
        public RiderVisionSensor vision;
        public float cruiseSpeedKph = 40;
        public float minimumConfidence = .60f, minimumHeightFraction = .07f;
        public float roiLeft = .40f, roiRight = .60f, minimumBottomFraction = .50f;
        public int requiredObservations = 2;
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Steer { get; private set; }
        public bool WarningActive { get; private set; }
        public int WarningEvents { get; private set; }
        public float FirstWarningAt { get; private set; } = -1;
        public RiderVisionSensor.Detection LastWarningDetection { get; private set; }
        int lastCompleted, consecutive;
        float lastQualified = -100;
        RiderVisionSensor.Detection previous;

        public void ResetBaseline()
        {
            Throttle = Brake = Steer = 0; WarningActive = false;
            WarningEvents = 0; FirstWarningAt = -1; lastCompleted = consecutive = 0;
            lastQualified = -100; previous = LastWarningDetection = default;
        }

        void FixedUpdate()
        {
            if (!session || session.rider.InputOverride != this || !session.IsRunning || session.IsPaused)
            { Throttle = Brake = Steer = 0; return; }
            bool fresh = vision && vision.Ready && vision.FusionEnabled &&
                Time.realtimeSinceStartupAsDouble - vision.LastCapturedRealtime <= .35;
            if (fresh && vision.CompletedFrames != lastCompleted)
            {
                lastCompleted = vision.CompletedFrames;
                bool found = false;
                foreach (var detection in vision.Detections)
                {
                    var b = detection.box;
                    if (detection.confidence < minimumConfidence ||
                        b.height < RiderVisionSensor.ImageHeight * minimumHeightFraction ||
                        b.yMax < RiderVisionSensor.ImageHeight * minimumBottomFraction ||
                        b.center.x < RiderVisionSensor.ImageWidth * roiLeft ||
                        b.center.x > RiderVisionSensor.ImageWidth * roiRight) continue;
                    consecutive = previous.classId == detection.classId && RiderVisionSensor.IoU(previous.box, b) >= .25f ? consecutive + 1 : 1;
                    previous = detection; found = true;
                    if (consecutive >= requiredObservations)
                    { lastQualified = Time.time; LastWarningDetection = detection; }
                    break;
                }
                if (!found) consecutive = 0;
            }
            bool warning = fresh && Time.time - lastQualified <= .35f;
            if (warning && !WarningActive)
            { WarningEvents++; if (FirstWarningAt < 0) FirstWarningAt = session.elapsed; }
            WarningActive = warning;
            var rider = session.rider;
            float speed = rider.SpeedKph / 3.6f;
            float feedForward = (.12f + speed * speed * .002f) * rider.definition.wheelRadius * rider.definition.massWithRider / rider.definition.motorTorque;
            Throttle = Mathf.Clamp01(feedForward + (cruiseSpeedKph - rider.SpeedKph) * .22f);
            Brake = Mathf.Clamp01((rider.SpeedKph - cruiseSpeedKph) / 8f);
            if (warning) { Throttle = 0; Brake = 1; }
            float lookAhead = 6f + speed * .35f;
            Vector3 target = session.route.Position(rider.transform.position.z + lookAhead, 2.65f, rider.transform.position.y);
            float angle = Vector3.SignedAngle(rider.transform.forward, target - rider.transform.position, Vector3.up) * Mathf.Deg2Rad;
            float wheelAngle = Mathf.Atan2(2 * rider.definition.wheelbase * Mathf.Sin(angle), lookAhead) * Mathf.Rad2Deg;
            float available = Mathf.Lerp(rider.definition.steeringDegrees, 10f, rider.SpeedKph / rider.definition.topSpeedKph);
            Steer = Mathf.Clamp(wheelAngle / available, -.8f, .8f);
        }
    }
}
