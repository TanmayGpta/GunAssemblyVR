using Unity.XR.CoreUtils;
using UnityEngine;

namespace VRArmory
{
    // Live fire for the assembled Beretta 92 FS. Once the function check has passed, every time the hammer falls with the
    // safety off (FunctionCheck.HammerFell), a round is fired:
    //  - the shot is instant along the bore (a 9 mm round crosses the range in well under a tenth of a second), with a
    //    small random spread, and skips the pistol itself and the player;
    //  - gunshot sound, muzzle flash, tracer, bullet hole + dust where it lands (ShotEffects);
    //  - the slide cycles and re-cocks the hammer; the trigger must be let out to its reset before the next shot
    //    (semi-automatic, one round per pull);
    //  - a slight recoil kick (muzzle up, gun back toward the hand) and a strong controller buzz;
    //  - range dummies (RangeTarget) get the hit; loose rigid bodies get a small push.
    // Ammunition: PistolMagazine (chamber + 15-round magazine; without it, unlimited).
    public class PistolFiring : MonoBehaviour
    {
        public FunctionCheck functionCheck;
        [Tooltip("The pistol's grab (frame). Optional: without it (desktop) there is no recoil kick or vibration.")]
        public VRAssemblyGrab frameGrab;
        public ShotEffects effects;
        public PistolMagazine ammo;
        public AudioClip gunshot;

        [Header("Ballistics")]
        public float range = 150f;
        [Tooltip("Random cone (degrees) around the bore: about 5 cm at 25 m, like a service pistol from a steady hand.")]
        public float spreadDegrees = 0.12f;
        public float impactForce = 2f;

        [Header("Recoil")]
        [Tooltip("Muzzle rise per shot, degrees.")]
        public float kickAngle = 4f;
        [Tooltip("How far the pistol moves back into the hand per shot, metres.")]
        public float kickBack = 0.012f;
        public float kickRiseTime = 0.02f;
        public float kickRecoverTime = 0.07f;
        [Range(0f, 1f)] public float shotHaptic = 1f;
        public float shotHapticTime = 0.1f;

        [Header("Sound")]
        [Range(0f, 1f)] public float volume = 1f;

        public event System.Action<RaycastHit> Hit;
        public event System.Action Fired;
        public int ShotsFired { get; private set; }

        private AudioSource audioSource;
        private float sinceShot = 10f;
        private readonly RaycastHit[] hits = new RaycastHit[16];

        void Awake()
        {
            var go = new GameObject("Gunshot_Audio");
            go.transform.SetParent(transform, false);
            audioSource = go.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.7f; // mostly in the head (it is in the hand), a little placed at the muzzle
            audioSource.minDistance = 1.5f;
            audioSource.maxDistance = 60f;
            audioSource.dopplerLevel = 0f;
        }

        void OnEnable()
        {
            if (functionCheck != null) functionCheck.HammerFell += OnHammerFell;
        }

        void OnDisable()
        {
            if (functionCheck != null) functionCheck.HammerFell -= OnHammerFell;
            SetKick(0f);
        }

        private void OnHammerFell()
        {
            // During the function check the hammer falls on an empty chamber: only live once the check has passed.
            if (!functionCheck.Passed || !functionCheck.HasRig) return;
            bool lockOpen = false;
            if (ammo != null && !ammo.TakeChamberedRound(out lockOpen)) return; // empty chamber: only the hammer's click
            Fire(lockOpen);
        }

        // lockOpen: the last round, the slide stays back.
        public void Fire(bool lockOpen = false)
        {
            ShotsFired++;
            Vector3 muzzle = functionCheck.MuzzlePosition;
            Vector3 dir = Spread(functionCheck.MuzzleDirection);

            bool hit = TryHit(muzzle, dir, out RaycastHit h);
            Vector3 end = hit ? h.point : muzzle + dir * range;
            RangeTarget target = hit ? h.collider.GetComponentInParent<RangeTarget>() : null;
            if (effects != null) effects.Shot(muzzle, end, hit, hit ? h.normal : -dir, hit ? h.collider : null, target != null ? target.transform : null);

            if (gunshot != null)
            {
                audioSource.transform.position = muzzle;
                audioSource.pitch = Random.Range(0.96f, 1.04f);
                audioSource.PlayOneShot(gunshot, volume);
            }

            functionCheck.CycleSlide(lockOpen);
            sinceShot = 0f;
            if (frameGrab != null && frameGrab.isSelected)
            {
                for (int i = 0; i < frameGrab.interactorsSelecting.Count; i++)
                    VRAssemblyGrab.Pulse(frameGrab.interactorsSelecting[i], shotHaptic, shotHapticTime);
            }

            if (hit)
            {
                if (target != null) target.RegisterHit(h.collider, h.point);
                Rigidbody body = h.rigidbody;
                if (body != null && !body.isKinematic) body.AddForceAtPosition(dir * impactForce, h.point, ForceMode.Impulse);
                Hit?.Invoke(h);
            }
            Fired?.Invoke();
        }

        void Update()
        {
            sinceShot += Time.deltaTime;
            float k;
            if (sinceShot < kickRiseTime) k = sinceShot / kickRiseTime;
            else k = Mathf.Exp(-(sinceShot - kickRiseTime) / kickRecoverTime);
            if (k < 0.002f) k = 0f;
            SetKick(k);
        }

        // k = 0..1 of the full kick.
        private void SetKick(float k)
        {
            if (frameGrab == null || functionCheck == null) return;
            if (k <= 0f || !frameGrab.isSelected)
            {
                frameGrab.kickAngle = 0f;
                frameGrab.kickOffsetLocal = Vector3.zero;
                return;
            }
            Vector3 m = functionCheck.MuzzleLocal, up = functionCheck.UpLocal;
            frameGrab.kickAxisLocal = Vector3.Cross(m, up); // turning about this lifts the muzzle
            frameGrab.kickAngle = kickAngle * k;
            float scale = Mathf.Max(0.0001f, frameGrab.transform.lossyScale.x);
            frameGrab.kickOffsetLocal = -m * (kickBack * k / scale);
        }

        private Vector3 Spread(Vector3 dir)
        {
            if (spreadDegrees <= 0f) return dir;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(side, dir);
            return (dir + side * r.x + up * r.y).normalized;
        }

        // Nearest solid collider along the shot that is not the pistol or the player.
        private bool TryHit(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, range, ~0, QueryTriggerInteraction.Ignore);
            Transform gun = functionCheck.frame != null ? functionCheck.frame.transform : null;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (gun != null && c.transform.IsChildOf(gun)) continue;
                if (c is CharacterController || c.GetComponentInParent<XROrigin>() != null) continue;
                if (!found || hits[i].distance < best.distance)
                {
                    best = hits[i];
                    found = true;
                }
            }
            return found;
        }
    }
}
