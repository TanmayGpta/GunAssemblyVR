using UnityEngine;

namespace VRArmory
{
    // An emptied magazine that drops out of the pistol: a look-alike copy of the magazine's meshes with real physics,
    // which shrinks away after a few seconds (the real magazine part goes back to the bench, fresh).
    public class SpentMagazine : MonoBehaviour
    {
        private float lifetime;
        private float age;
        private Vector3 scale;
        private const float ShrinkTime = 0.35f;

        // ignoreUnder: colliders under this (the pistol it drops out of) never push it.
        public static SpentMagazine Spawn(Transform magazine, Vector3 velocity, float lifetime, Transform ignoreUnder = null)
        {
            var root = new GameObject("Spent_Magazine");
            root.transform.SetPositionAndRotation(magazine.position, magazine.rotation);
            foreach (MeshFilter mf in magazine.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled) continue;
                var piece = new GameObject(mf.name);
                piece.transform.SetParent(root.transform, false);
                piece.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                piece.transform.localScale = mf.transform.lossyScale; // root is unscaled
                piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                piece.AddComponent<BoxCollider>(); // fits the mesh bounds
            }
            if (ignoreUnder != null)
            {
                Collider[] own = root.GetComponentsInChildren<Collider>();
                foreach (Collider other in ignoreUnder.GetComponentsInChildren<Collider>())
                    foreach (Collider c in own) Physics.IgnoreCollision(c, other, true);
            }
            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // thin and falling: never through the floor
            body.velocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 2f;

            var spent = root.AddComponent<SpentMagazine>();
            spent.lifetime = lifetime;
            spent.scale = root.transform.localScale;
            return spent;
        }

        void Update()
        {
            age += Time.deltaTime;
            float left = lifetime - age;
            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (left < ShrinkTime) transform.localScale = scale * (left / ShrinkTime);
        }
    }
}
