using UnityEngine;
namespace TaiwanRider
{
    [CreateAssetMenu(menuName = "Taiwan Rider/Scooter")]
    public sealed class ScooterDefinition : ScriptableObject
    {
        public float length = 1.790f;
        public float wheelbase = 1.275f;
        public float wheelRadius = .217f;
        public float massWithRider = 170f;
        public float topSpeedKph = 100f;
        public float motorTorque = 68f;
        public float brakeTorque = 245f;
        public float steeringDegrees = 29f;
        public float visualLeanDegrees = 17f;
        [TextArea] public string source = "Yamaha AXIS Z 125 (2025) published dimensions. Original mesh; simplified assisted handling.";
    }
}
