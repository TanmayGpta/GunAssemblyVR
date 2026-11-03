using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // Keeps a controller ray almost invisible while it points at nothing useful, and brings it up to full strength
    // when it is over a UI panel/button or an interactable. Sits next to the XRInteractorLineVisual on a ray
    // interactor and drives both its valid and invalid gradients with one faded colour, so the visual's own
    // valid/invalid switch never causes a pop. Works on any XRRayInteractor (controller or, later, hand rays).
    [RequireComponent(typeof(XRRayInteractor), typeof(XRInteractorLineVisual))]
    public class VRRayHoverDimmer : MonoBehaviour
    {
        [Tooltip("Ray colour when it can interact with something.")]
        public Color activeColor = Color.white;
        [Range(0f, 1f), Tooltip("Alpha at the ray's origin while pointing at nothing clickable.")]
        public float idleAlpha = 0.08f;
        [Range(0f, 1f), Tooltip("Alpha at the ray's origin while over a panel, button or interactable.")]
        public float activeAlpha = 1f;
        [Tooltip("Seconds to fade between idle and active.")]
        public float fadeTime = 0.12f;

        XRRayInteractor m_Ray;
        XRInteractorLineVisual m_Line;
        Gradient m_Gradient;
        readonly GradientColorKey[] m_ColorKeys = new GradientColorKey[2];
        readonly GradientAlphaKey[] m_AlphaKeys = new GradientAlphaKey[2];
        float m_Alpha;

        void Awake()
        {
            m_Ray = GetComponent<XRRayInteractor>();
            m_Line = GetComponent<XRInteractorLineVisual>();
            m_Gradient = new Gradient();
            m_Alpha = idleAlpha;
            ApplyGradient();
            // One shared instance: the line visual hands it to the LineRenderer every frame, so updating its keys is enough.
            m_Line.validColorGradient = m_Gradient;
            m_Line.invalidColorGradient = m_Gradient;
        }

        void LateUpdate()
        {
            float target = IsOverSomethingUseful() ? activeAlpha : idleAlpha;
            m_Alpha = fadeTime > 0f
                ? Mathf.MoveTowards(m_Alpha, target, Mathf.Abs(activeAlpha - idleAlpha) * Time.deltaTime / fadeTime)
                : target;
            ApplyGradient();
        }

        bool IsOverSomethingUseful()
        {
            if (m_Ray.hasSelection || m_Ray.interactablesHovered.Count > 0)
                return true;
            return m_Ray.enableUIInteraction && m_Ray.TryGetCurrentUIRaycastResult(out _);
        }

        void ApplyGradient()
        {
            m_ColorKeys[0] = new GradientColorKey(activeColor, 0f);
            m_ColorKeys[1] = new GradientColorKey(activeColor, 1f);
            m_AlphaKeys[0] = new GradientAlphaKey(m_Alpha, 0f);
            m_AlphaKeys[1] = new GradientAlphaKey(m_Alpha * 0.25f, 1f); // tapers towards the tip
            m_Gradient.SetKeys(m_ColorKeys, m_AlphaKeys);
        }
    }
}
