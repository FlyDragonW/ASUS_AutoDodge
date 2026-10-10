using UnityEngine;
namespace TaiwanRider
{
    public enum ScenarioKind { Cruise, CarCutIn, ScooterCrossing }
    [CreateAssetMenu(menuName = "Taiwan Rider/Scenario")]
    public sealed class ScenarioDefinition : ScriptableObject
    {
        public ScenarioKind kind;
        public string title;
        [TextArea] public string description;
        public int seed = 125;
        public float conflictZ = 0f;
        public float triggerTimeToArrival = 1.1f;
    }
    public interface IScooterInput
    {
        float Throttle { get; }
        float Brake { get; }
        float Steer { get; }
    }
    public sealed class TestScooterInput : IScooterInput
    {
        public float throttle, brake, steer;
        public float Throttle => throttle;
        public float Brake => brake;
        public float Steer => steer;
    }
    [System.Serializable]
    public sealed class RunResult
    {
        public string scenario;
        public int seed;
        public string endedAtUtc;
        public bool collision;
        public bool finished;
        public bool hazardTriggered;
        public bool autoAvoidEnabled;
        public int avoidanceInterventions;
        public string hitObject;
        public float elapsedSeconds;
        public float reactionSeconds = -1f;
        public float minimumClearanceMetres = -1f;
        public float peakSpeedKph;
    }
}
