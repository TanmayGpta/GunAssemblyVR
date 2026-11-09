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

        // Scoring (RangeScoring). Matches the rings painted on the chest (ArmorySetupHelper's Target_Rings texture:
        // 1 px = 1 mm in this object's space): bullseye 10, then each ring outward 9 .. 4, rest of the torso 3, head 10.
        [Header("Scoring")]
        public Vector3 ringCentreLocal = new Vector3(0f, 1.3625f, 0f);
        public float bullRadius = 0.018f;
        public float ringStep = 0.036f;
        public int ringCount = 6;
        public int headScore = 10;
        public int torsoOutsideRingsScore = 3;

        // Farther dummies score more: x1 at 6 m, x1.5 at 10 m, x2 at 15 m (rounded to halves).
        public float DistanceMultiplier => Mathf.Max(1f, Mathf.Round((1f + (distanceMeters - 6f) / 9f) * 2f) * 0.5f);

        // Points for a hit before the distance multiplier (0: stand or anything that is not a scoring zone).
        public int BaseScore(Collider zone, Vector3 point)
        {
            if (zone == null) return 0;
            if (zone.name == "HitZone_Head") return headScore;
            if (zone.name != "HitZone_Torso") return 0;
            Vector3 local = transform.InverseTransformPoint(point) - ringCentreLocal;
            float d = new Vector2(local.x, local.y).magnitude;
            if (d <= bullRadius) return 10;
            int ring = Mathf.CeilToInt(d / ringStep); // 1 = innermost ring
            return ring <= ringCount ? 10 - ring : torsoOutsideRingsScore;
        }

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
