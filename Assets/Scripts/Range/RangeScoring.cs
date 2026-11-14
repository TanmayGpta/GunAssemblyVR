using UnityEngine;
using UnityEngine.UI;

namespace VRArmory
{
    // Scores live-fire hits on the range dummies: RangeTarget.BaseScore (head 10, chest rings 10 .. 4, rest of the torso 3)
    // times the dummy's distance multiplier (x1 / x1.5 / x2). Each scoring hit shows a floating "+N" at the hole that
    // rises and fades, sized so it reads from the firing line. Keeps the running total for the fire training.
    public class RangeScoring : MonoBehaviour
    {
        [Tooltip("Every weapon's live fire (only the one in use fires).")]
        public PistolFiring[] firings = new PistolFiring[0];
        public float popupTime = 1.3f;
        [Tooltip("Popup height as a share of its distance from the eye (about 3 degrees).")]
        public float popupAngularSize = 0.05f;

        public int TotalScore { get; private set; }
        public int ScoringHits { get; private set; }
        public int BestShot { get; private set; }
        public event System.Action<int> Scored; // points of the hit

        private const int PoolSize = 8;
        private readonly Popup[] pool = new Popup[PoolSize];
        private int next;

        private class Popup
        {
            public Transform root;
            public CanvasGroup group;
            public Text text;
            public Vector3 start;
            public float age = -1f;
        }

        void OnEnable()
        {
            foreach (PistolFiring f in firings)
                if (f != null) f.Hit += OnHit;
        }

        void OnDisable()
        {
            foreach (PistolFiring f in firings)
                if (f != null) f.Hit -= OnHit;
        }

        public void ResetScore()
        {
            TotalScore = 0;
            ScoringHits = 0;
            BestShot = 0;
        }

        private void OnHit(RaycastHit h)
        {
            RangeTarget target = h.collider.GetComponentInParent<RangeTarget>();
            if (target == null) return;
            int baseScore = target.BaseScore(h.collider, h.point);
            if (baseScore <= 0) return;
            float multiplier = target.DistanceMultiplier;
            int points = Mathf.RoundToInt(baseScore * multiplier);
            TotalScore += points;
            ScoringHits++;
            BestShot = Mathf.Max(BestShot, points);
            Show(h.point + h.normal * 0.03f, points, baseScore, multiplier, h.collider.name == "HitZone_Head");
            Scored?.Invoke(points);
        }

        void Update()
        {
            Camera cam = Camera.main;
            foreach (Popup p in pool)
            {
                if (p == null || p.age < 0f) continue;
                p.age += Time.deltaTime;
                float k = p.age / popupTime;
                if (k >= 1f || cam == null)
                {
                    p.age = -1f;
                    p.root.gameObject.SetActive(false);
                    continue;
                }
                float dist = Vector3.Distance(cam.transform.position, p.start);
                float height = dist * popupAngularSize;
                p.root.position = p.start + Vector3.up * (height * (0.6f + 1.2f * k));
                p.root.rotation = Quaternion.LookRotation(p.root.position - cam.transform.position, Vector3.up);
                p.root.localScale = Vector3.one * (height / 100f) * (k < 0.12f ? Mathf.Lerp(0.6f, 1f, k / 0.12f) : 1f);
                p.group.alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            }
        }

        private void Show(Vector3 at, int points, int baseScore, float multiplier, bool head)
        {
            Popup p = pool[next] ?? (pool[next] = Build());
            next = (next + 1) % PoolSize;
            string color = baseScore >= 10 ? "#FFD34D" : baseScore >= 7 ? "#5CE68A" : "#FFFFFF";
            string detail = head ? "HEAD" : baseScore == 10 ? "BULLSEYE" : $"RING {baseScore}";
            p.text.text = $"<size=72><b><color={color}>+{points}</color></b></size>\n<size=28>{detail}  x{multiplier:0.#}</size>";
            p.start = at;
            p.age = 0f;
            p.group.alpha = 1f;
            p.root.gameObject.SetActive(true);
        }

        private Popup Build()
        {
            var go = new GameObject("Score_Popup", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(300f, 100f);
            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            text.transform.SetParent(go.transform, false);
            var r = (RectTransform)text.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = true;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = text.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);

            go.SetActive(false);
            return new Popup { root = go.transform, group = group, text = text };
        }
    }
}
