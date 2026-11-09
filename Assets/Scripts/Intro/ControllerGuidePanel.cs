using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VRArmory
{
    // The controller panel of the Intro scene: the two Quest Touch Plus controllers seen from above, and one card per
    // control along the bottom.
    //  - Highlight(control): that part glows (pulsing teal) on both controllers and its card lights up, so the soldier
    //    can say "this is the trigger" and the trainee sees where. The trigger is under the controller, hidden from
    //    above, so while it is taught both controllers turn smoothly side-on (and back for the next control).
    //  - Live: whatever the trainee presses on their real controllers glows green on the model, and the card of the
    //    control being taught turns green once they have tried it.
    // Each controller is one mesh with a sub-mesh per control (see TouchPlusControllerModels): sub-mesh index = Control.
    public class ControllerGuidePanel : MonoBehaviour
    {
        public enum Control { None, Grip, Trigger, Thumbstick, Primary, Secondary }

        [Serializable]
        public class Row
        {
            public Control control;
            public Image background;
            public Image chip;
            public Text chipText;
        }

        public MeshRenderer leftController;
        public MeshRenderer rightController;
        public Row[] rows = new Row[0];
        [Tooltip("Degrees both controllers turn (about the panel's vertical axis) while the trigger is taught.")]
        public float triggerTurn = 90f;
        public float turnSmoothTime = 0.35f;

        [Header("Look")]
        public Color highlightColor = new Color(0.3f, 0.92f, 0.86f);
        public Color pressedColor = new Color(0.36f, 0.9f, 0.52f);
        public Color rowColor = new Color(0.115f, 0.14f, 0.175f, 1f);
        public Color rowHighlightColor = new Color(0.12f, 0.3f, 0.3f, 1f);
        public Color chipColor = new Color(0.17f, 0.2f, 0.25f);
        public Color chipTextColor = new Color(0.3f, 0.92f, 0.86f);

        public Control Current { get; private set; }

        static readonly Control[] Controls = { Control.Grip, Control.Trigger, Control.Thumbstick, Control.Primary, Control.Secondary };
        const int Hands = 2; // 0 = left, 1 = right

        private MeshRenderer[] renderers;
        private InputAction[,] actions; // [hand, control]
        private bool[] tried;           // per card: pressed while it was being taught
        private MaterialPropertyBlock block;
        private Transform[] pivots;
        private Quaternion[] pivotBase;
        private float turn, turnVelocity;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            renderers = new[] { leftController, rightController };
            tried = new bool[rows.Length];
            // Each model sits in a pivot (its place and angle on the panel): the turn is applied on top of that angle.
            pivots = new Transform[Hands];
            pivotBase = new Quaternion[Hands];
            for (int h = 0; h < Hands; h++)
            {
                pivots[h] = renderers[h] != null ? renderers[h].transform.parent : null;
                if (pivots[h] != null) pivotBase[h] = pivots[h].localRotation;
            }
            Highlight(Control.None);
        }

        void OnEnable()
        {
            string[] device = { "{LeftHand}", "{RightHand}" };
            actions = new InputAction[Hands, Controls.Length + 1];
            for (int h = 0; h < Hands; h++)
            {
                actions[h, (int)Control.Grip] = Button($"<XRController>{device[h]}/gripPressed");
                actions[h, (int)Control.Trigger] = Button($"<XRController>{device[h]}/triggerPressed");
                actions[h, (int)Control.Primary] = Button($"<XRController>{device[h]}/primaryButton");
                actions[h, (int)Control.Secondary] = Button($"<XRController>{device[h]}/secondaryButton");
                var stick = new InputAction(type: InputActionType.Value, binding: $"<XRController>{device[h]}/thumbstick");
                stick.Enable();
                actions[h, (int)Control.Thumbstick] = stick;
            }
        }

        void OnDisable()
        {
            if (actions == null) return;
            foreach (InputAction a in actions) a?.Dispose();
            actions = null;
        }

        static InputAction Button(string binding)
        {
            var a = new InputAction(type: InputActionType.Button, binding: binding);
            a.Enable();
            return a;
        }

        bool IsPressed(int hand, Control c)
        {
            InputAction a = actions?[hand, (int)c];
            if (a == null) return false;
            if (c == Control.Thumbstick) return a.ReadValue<Vector2>().sqrMagnitude > 0.25f;
            return a.IsPressed();
        }

        // Which control the soldier is talking about (None: nothing glows).
        public void Highlight(Control control)
        {
            Current = control;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].background != null) rows[i].background.color = rows[i].control == control ? rowHighlightColor : rowColor;
                ApplyChip(i);
            }
        }

        void ApplyChip(int i)
        {
            Row r = rows[i];
            bool lit = tried[i] || r.control == Current;
            if (r.chip != null) r.chip.color = tried[i] ? pressedColor : (r.control == Current ? highlightColor : chipColor);
            if (r.chipText != null) r.chipText.color = lit ? new Color(0.05f, 0.065f, 0.085f) : chipTextColor;
        }

        void Update()
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 5f);
            for (int h = 0; h < Hands; h++)
            {
                MeshRenderer r = renderers[h];
                if (r == null) continue;
                foreach (Control c in Controls)
                {
                    if ((int)c >= r.sharedMaterials.Length) continue;
                    bool pressed = IsPressed(h, c);
                    if (pressed && c == Current) MarkTried(c);
                    if (pressed) Paint(r, c, Color.Lerp(Color.white, pressedColor, 0.7f), pressedColor * 1.3f);
                    else if (c == Current) Paint(r, c, Color.Lerp(Color.white, highlightColor, 0.6f), highlightColor * pulse);
                    else Paint(r, c, Color.white, Color.black);
                }
            }
            UpdateTurn();
        }

        // Side-on while the trigger is taught; mirrored, so both controllers show the trigger the same way.
        void UpdateTurn()
        {
            float target = Current == Control.Trigger ? triggerTurn : 0f;
            turn = Mathf.SmoothDamp(turn, target, ref turnVelocity, turnSmoothTime);
            for (int h = 0; h < Hands; h++)
            {
                if (pivots[h] == null) continue;
                float sign = h == 0 ? 1f : -1f;
                pivots[h].localRotation = Quaternion.AngleAxis(turn * sign, Vector3.up) * pivotBase[h];
            }
        }

        void MarkTried(Control c)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].control != c || tried[i]) continue;
                tried[i] = true;
                ApplyChip(i);
            }
        }

        void Paint(MeshRenderer r, Control c, Color color, Color emission)
        {
            block.Clear();
            block.SetColor(ColorId, color);
            block.SetColor(EmissionId, emission);
            r.SetPropertyBlock(block, (int)c);
        }
    }
}
