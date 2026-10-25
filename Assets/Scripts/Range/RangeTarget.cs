using UnityEngine;

namespace VRArmory
{
    // Shooting dummy. Weapon code calls RegisterHit with the collider it hit;
    // the zone is resolved from the collider's name (HitZone_Head / HitZone_Torso / anything else = other).
    public class RangeTarget : MonoBehaviour
    {
        public string targetId;
        public float firingLineZ = 9.5f;
        public float distanceMeters = 15f;
        public int headHits;
        public int torsoHits;
        public int otherHits;

        public event System.Action<RangeTarget, string, Vector3> OnHit;

        public int DummyHits => headHits + torsoHits;

        // Moves the dummy along its lane so it stands distanceMeters downrange of the firing line.
        public void SetDistance(float meters)
        {
            distanceMeters = meters;
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, p.y, firingLineZ + meters);
        }

        public void RegisterHit(Collider zone, Vector3 point)
        {
            string zoneName = zone != null ? zone.name : "Unknown";
            if (zoneName == "HitZone_Head") headHits++;
            else if (zoneName == "HitZone_Torso") torsoHits++;
            else otherHits++;

            OnHit?.Invoke(this, zoneName, point);
        }

        public void ResetHits()
        {
            headHits = 0;
            torsoHits = 0;
            otherHits = 0;
        }
    }
}
