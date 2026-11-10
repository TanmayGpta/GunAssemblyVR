using UnityEngine;

namespace VRArmory
{
    // Slides a range dummy side to side across its lane. Purely positional: RangeTarget and its hit zones are untouched,
    // and SetDistance keeps working because only X is driven.
    public class RangeTargetMover : MonoBehaviour
    {
        [Tooltip("Metres either side of the starting X.")]
        public float amplitude = 1f;
        [Tooltip("Seconds for one full left-right-left cycle.")]
        public float period = 6f;
        [Tooltip("Where in the cycle it starts, 0..1, so the dummies do not move in step.")]
        [Range(0f, 1f)] public float phase;

        float centerX;

        void Awake()
        {
            centerX = transform.position.x;
        }

        // Back to its lane centre (stationary targets: the three lanes keep every dummy in clear view of the booth).
        public void ResetToLane()
        {
            Vector3 p = transform.position;
            p.x = centerX;
            transform.position = p;
        }

        void Update()
        {
            Vector3 p = transform.position;
            p.x = centerX + amplitude * Mathf.Sin((Time.time / Mathf.Max(0.1f, period) + phase) * Mathf.PI * 2f);
            transform.position = p;
        }
    }
}
