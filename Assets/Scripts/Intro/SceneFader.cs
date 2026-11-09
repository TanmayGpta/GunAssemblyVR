using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRArmory
{
    // Fade to / from black in front of the active camera (a screen overlay does not render in a headset, so the fade is
    // a small black quad just in front of the eyes). FadeIn on start; FadeOutAndLoad hides the scene switch.
    public class SceneFader : MonoBehaviour
    {
        [Tooltip("Unlit transparent material (Sprites/Default); tinted black at runtime.")]
        public Material fadeMaterial;
        public float fadeInTime = 1.2f;
        public float fadeOutTime = 0.8f;

        private Renderer quad;
        private Material instance;
        private float alpha = 1f;

        public bool IsFading { get; private set; }

        void Start()
        {
            Camera cam = Camera.main;
            if (cam == null || fadeMaterial == null) return;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Scene_Fade";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(cam.nearClipPlane * 1.5f, 0.06f));
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 2f;
            quad = go.GetComponent<Renderer>();
            quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.receiveShadows = false;
            instance = new Material(fadeMaterial) { renderQueue = 5000 }; // drawn after everything else
            quad.sharedMaterial = instance;
            Apply(1f);
            StartCoroutine(Fade(1f, 0f, fadeInTime));
        }

        public void FadeOutAndLoad(string sceneName)
        {
            StopAllCoroutines();
            StartCoroutine(FadeOutThenLoad(sceneName));
        }

        IEnumerator FadeOutThenLoad(string sceneName)
        {
            IsFading = true;
            yield return Fade(alpha, 1f, fadeOutTime);
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            while (load != null && !load.isDone) yield return null;
        }

        IEnumerator Fade(float from, float to, float time)
        {
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                Apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time)));
                yield return null;
            }
            Apply(to);
        }

        void Apply(float a)
        {
            alpha = a;
            if (quad == null) return;
            quad.enabled = a > 0.001f;
            instance.color = new Color(0f, 0f, 0f, a);
        }

        void OnDestroy()
        {
            if (instance != null) Destroy(instance);
        }
    }
}
