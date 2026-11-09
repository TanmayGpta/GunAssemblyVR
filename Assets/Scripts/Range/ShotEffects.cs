using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    // What a shot looks like (PistolFiring calls Shot):
    //  - muzzle flash: a short star-shaped glow at the muzzle plus a brief warm light;
    //  - tracer: a thin glowing line along the bullet's path that fades out in a fraction of a second;
    //  - impact: a bullet hole stuck to whatever was hit (it moves with moving targets) and a small dust puff.
    // Everything is pooled (the oldest hole is reused after maxHoles) so a long session never slows the Quest down.
    // All textures are generated here; baseMaterial only provides the shader (Sprites/Default: unlit, vertex colour + alpha),
    // as a saved material so the shader is included in the build.
    public class ShotEffects : MonoBehaviour
    {
        public Material baseMaterial;

        [Header("Muzzle flash")]
        public float flashSize = 0.09f;
        public float flashTime = 0.05f;
        public Color flashColor = new Color(1f, 0.82f, 0.5f, 1f);
        public float flashLightRange = 4f;
        public float flashLightIntensity = 2.5f;

        [Header("Tracer")]
        public float tracerTime = 0.08f;
        public float tracerWidth = 0.004f;
        public Color tracerColor = new Color(1f, 0.86f, 0.58f, 1f);

        [Header("Impact")]
        public int maxHoles = 60;
        [Tooltip("Bullet hole diameter in metres (9 mm round, with its torn edge).")]
        public float holeSize = 0.022f;
        [Tooltip("Range dummies: bigger holes with cracks around them, so they read from the firing line. Size at 0 m...")]
        public float targetHoleSize = 0.03f;
        [Tooltip("...plus this much per metre of distance from the shooter.")]
        public float targetHoleSizePerMeter = 0.003f;
        public Color dustColor = new Color(0.62f, 0.57f, 0.5f, 0.75f);
        public int dustParticles = 9;

        private Mesh quad;
        private Material flashMat, tracerMat, holeMat, targetHoleMat, dustMat;
        private Transform flash;
        private Light flashLight;
        private float flashTimer;
        private readonly List<LineRenderer> tracers = new List<LineRenderer>();
        private readonly List<float> tracerTimers = new List<float>();
        private readonly List<Transform> holes = new List<Transform>();
        private int nextHole;
        private ParticleSystem dust;
        private bool built;

        // onTarget: the shot hit a range dummy (cracked, bigger hole, scaled with distance).
        public void Shot(Vector3 muzzle, Vector3 end, bool hit, Vector3 normal, Collider hitCollider, bool onTarget = false)
        {
            if (!built) Build();
            ShowFlash(muzzle, (end - muzzle).normalized);
            ShowTracer(muzzle, end);
            if (hit)
            {
                float size = onTarget ? targetHoleSize + targetHoleSizePerMeter * Vector3.Distance(muzzle, end) : holeSize;
                PlaceHole(end, normal, hitCollider != null ? hitCollider.transform : null, onTarget ? targetHoleMat : holeMat, size);
                EmitDust(end, normal);
            }
        }

        void Update()
        {
            if (!built) return;

            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                float k = Mathf.Clamp01(flashTimer / flashTime);
                flashLight.intensity = flashLightIntensity * k;
                if (flashTimer <= 0f)
                {
                    flash.gameObject.SetActive(false);
                    flashLight.enabled = false;
                }
            }

            for (int i = 0; i < tracers.Count; i++)
            {
                if (tracerTimers[i] <= 0f) continue;
                tracerTimers[i] -= Time.deltaTime;
                float k = Mathf.Clamp01(tracerTimers[i] / tracerTime);
                // Faint at the muzzle, bright toward the far end; the whole line fades out.
                Color far = tracerColor;
                far.a = k;
                Color near = tracerColor;
                near.a = 0.25f * k;
                tracers[i].startColor = near;
                tracers[i].endColor = far;
                if (tracerTimers[i] <= 0f) tracers[i].enabled = false;
            }
        }

        // ---------- Pieces ----------

        private void ShowFlash(Vector3 muzzle, Vector3 dir)
        {
            Vector3 p = muzzle + dir * 0.025f;
            flash.position = p;
            Camera cam = Camera.main;
            Vector3 toCam = cam != null ? cam.transform.position - p : -dir;
            flash.rotation = Quaternion.LookRotation(-toCam) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flash.localScale = Vector3.one * flashSize * Random.Range(0.85f, 1.15f);
            flash.gameObject.SetActive(true);
            flashLight.transform.position = p;
            flashLight.enabled = true;
            flashLight.intensity = flashLightIntensity;
            flashTimer = flashTime;
        }

        private void ShowTracer(Vector3 from, Vector3 to)
        {
            int slot = 0;
            for (int i = 0; i < tracerTimers.Count; i++)
            {
                if (tracerTimers[i] < tracerTimers[slot]) slot = i; // a free one, or the most faded
            }
            LineRenderer lr = tracers[slot];
            lr.SetPosition(0, from + (to - from).normalized * 0.05f);
            lr.SetPosition(1, to);
            lr.enabled = true;
            tracerTimers[slot] = tracerTime;
        }

        private void PlaceHole(Vector3 point, Vector3 normal, Transform surface, Material mat, float size)
        {
            if (holes.Count < maxHoles) holes.Add(null);
            int i = nextHole;
            nextHole = (nextHole + 1) % maxHoles;
            Transform hole = holes[i];
            if (hole == null) // never made, or its surface was destroyed
            {
                hole = MakeQuad("Bullet_Hole", mat).transform;
                holes[i] = hole;
            }
            hole.GetComponent<MeshRenderer>().sharedMaterial = mat;
            hole.SetParent(null, false);
            // Off the surface: in front of thin overlays such as the dummies' scoring-ring sheet (2-3 mm out), no flicker.
            hole.position = point + normal * 0.005f;
            hole.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            hole.localScale = Vector3.one * size * Random.Range(0.9f, 1.1f);
            if (surface != null) hole.SetParent(surface, true); // follows a moving target
            hole.gameObject.SetActive(true);
        }

        private void EmitDust(Vector3 point, Vector3 normal)
        {
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < dustParticles; i++)
            {
                p.position = point + normal * 0.005f;
                p.velocity = (normal + Random.insideUnitSphere * 0.7f).normalized * Random.Range(0.3f, 1.2f);
                p.startColor = dustColor;
                p.startSize = Random.Range(0.012f, 0.035f);
                p.startLifetime = Random.Range(0.25f, 0.55f);
                dust.Emit(p, 1);
            }
        }

        // ---------- Setup ----------

        private void Build()
        {
            built = true;
            quad = BuildQuad();
            flashMat = Make(FlashTexture(), "Muzzle_Flash");
            tracerMat = Make(TracerTexture(), "Tracer");
            holeMat = Make(HoleTexture(), "Bullet_Hole");
            targetHoleMat = Make(CrackedHoleTexture(), "Bullet_Hole_Cracked");
            dustMat = Make(DotTexture(), "Impact_Dust");

            GameObject f = MakeQuad("Muzzle_Flash", flashMat);
            f.GetComponent<MeshRenderer>().sharedMaterial.color = flashColor;
            flash = f.transform;
            f.SetActive(false);

            flashLight = new GameObject("Muzzle_Flash_Light").AddComponent<Light>();
            flashLight.transform.SetParent(transform, false);
            flashLight.type = LightType.Point;
            flashLight.range = flashLightRange;
            flashLight.color = flashColor;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            for (int i = 0; i < 4; i++)
            {
                var lr = new GameObject("Tracer").AddComponent<LineRenderer>();
                lr.transform.SetParent(transform, false);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.widthMultiplier = tracerWidth;
                lr.textureMode = LineTextureMode.Stretch;
                lr.sharedMaterial = tracerMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                tracers.Add(lr);
                tracerTimers.Add(0f);
            }

            dust = BuildDust();
        }

        private ParticleSystem BuildDust()
        {
            var go = new GameObject("Impact_Dust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = 0.25f;
            main.maxParticles = 300;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = false;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 4f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = dustMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private GameObject MakeQuad(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private Material Make(Texture2D tex, string name)
        {
            var m = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Sprites/Default"));
            m.name = name;
            m.mainTexture = tex;
            m.color = Color.white;
            return m;
        }

        // A 1 x 1 quad in the XY plane, centred (seen from either side: the shader does not cull).
        private static Mesh BuildQuad()
        {
            var m = new Mesh { name = "Shot_Quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }

        // ---------- Generated textures ----------

        private static Texture2D NewTexture(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        // Bright core with a few uneven spikes, like a pistol's muzzle flash seen from the side.
        private static Texture2D FlashTexture()
        {
            const int n = 64;
            Texture2D t = NewTexture(n, n, "Muzzle_Flash");
            var px = new Color[n * n];
            float[] spike = new float[6];
            for (int i = 0; i < spike.Length; i++) spike[i] = Random.Range(0.55f, 1f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f) / (n * 0.5f);
                    float r = d.magnitude;
                    float ang = Mathf.Atan2(d.y, d.x) / (Mathf.PI * 2f) * spike.Length;
                    int s0 = (Mathf.FloorToInt(ang) % spike.Length + spike.Length) % spike.Length;
                    float frac = ang - Mathf.Floor(ang);
                    float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(frac * Mathf.PI)), 12f) * spike[s0];
                    float core = Mathf.Clamp01(1f - r / 0.35f);
                    float a = Mathf.Clamp01(core * core * 1.6f + ray * Mathf.Clamp01(1f - r) * 0.9f);
                    float white = Mathf.Clamp01(core * 1.5f);
                    px[y * n + x] = new Color(1f, Mathf.Lerp(0.85f, 1f, white), Mathf.Lerp(0.6f, 1f, white), a);
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // Soft across the line's width (v), solid along it.
        private static Texture2D TracerTexture()
        {
            const int h = 32;
            Texture2D t = NewTexture(4, h, "Tracer");
            var px = new Color[4 * h];
            for (int y = 0; y < h; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                float a = Mathf.Clamp01(1f - v);
                a = a * a * (3f - 2f * a);
                Color c = new Color(1f, 1f, Mathf.Lerp(0.85f, 1f, a), a);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // Black hole, dark torn ring, scorched grey halo fading out.
        private static Texture2D HoleTexture()
        {
            const int n = 64;
            Texture2D t = NewTexture(n, n, "Bullet_Hole");
            var px = new Color[n * n];
            float seed = Random.Range(0f, 100f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f) / (n * 0.5f);
                    float ang = Mathf.Atan2(d.y, d.x);
                    float rough = (Mathf.PerlinNoise(seed + Mathf.Cos(ang) * 2f, seed + Mathf.Sin(ang) * 2f) - 0.5f) * 0.18f;
                    float r = d.magnitude + rough;
                    Color c;
                    if (r < 0.36f) c = new Color(0.02f, 0.02f, 0.02f, 1f);
                    else if (r < 0.55f) c = new Color(0.1f, 0.09f, 0.08f, Mathf.Lerp(1f, 0.8f, (r - 0.36f) / 0.19f));
                    else c = new Color(0.16f, 0.15f, 0.14f, Mathf.Clamp01(1f - (r - 0.55f) / 0.45f) * 0.6f);
                    px[y * n + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // Black hole, pale chipped rim and dark cracks running out from it (fracture), for the dummies.
        private static Texture2D CrackedHoleTexture()
        {
            const int n = 128;
            Texture2D t = NewTexture(n, n, "Bullet_Hole_Cracked");
            var px = new Color[n * n];
            const int cracks = 8;
            var angle = new float[cracks * 2];
            var length = new float[cracks * 2];
            var start = new float[cracks * 2];
            for (int i = 0; i < cracks; i++)
            {
                angle[i] = (i + Random.Range(-0.3f, 0.3f)) / cracks * Mathf.PI * 2f;
                length[i] = Random.Range(0.6f, 0.97f);
                start[i] = 0.2f;
                // a shorter branch splitting off each main crack partway out
                angle[cracks + i] = angle[i] + Random.Range(0.25f, 0.45f) * (Random.value < 0.5f ? -1f : 1f);
                start[cracks + i] = Random.Range(0.35f, 0.5f);
                length[cracks + i] = start[cracks + i] + Random.Range(0.15f, 0.3f);
            }
            float seed = Random.Range(0f, 100f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f) / (n * 0.5f);
                    float th = Mathf.Atan2(d.y, d.x);
                    float rough = (Mathf.PerlinNoise(seed + Mathf.Cos(th) * 3f, seed + Mathf.Sin(th) * 3f) - 0.5f) * 0.12f;
                    float r = d.magnitude;
                    float rr = r + rough;

                    Color c = new Color(0f, 0f, 0f, 0f);
                    if (rr < 0.2f) c = new Color(0.02f, 0.02f, 0.02f, 1f);                       // the hole
                    else if (rr < 0.27f) c = new Color(0.12f, 0.1f, 0.09f, 1f);                  // torn dark edge
                    else if (rr < 0.36f) c = new Color(0.85f, 0.82f, 0.76f, Mathf.Lerp(0.85f, 0f, (rr - 0.27f) / 0.09f)); // chipped pale rim

                    for (int i = 0; i < cracks * 2; i++)
                    {
                        if (r < start[i] || r > length[i]) continue;
                        float wiggle = Mathf.Sin(r * 23f + i * 1.7f) * 0.06f + Mathf.Sin(r * 51f + i) * 0.025f;
                        float da = Mathf.DeltaAngle(th * Mathf.Rad2Deg, (angle[i] + wiggle) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        float dist = Mathf.Abs(da) * r;
                        float k = 1f - (r - start[i]) / (length[i] - start[i]);
                        float width = (i < cracks ? 0.022f : 0.014f) * (0.35f + 0.65f * k);
                        if (dist >= width) continue;
                        float a = (1f - dist / width) * Mathf.Clamp01(k * 3f);
                        if (a > c.a) c = new Color(0.06f, 0.05f, 0.05f, a);
                    }
                    px[y * n + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        private static Texture2D DotTexture()
        {
            const int n = 32;
            Texture2D t = NewTexture(n, n, "Dust");
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - r);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }
    }
}
