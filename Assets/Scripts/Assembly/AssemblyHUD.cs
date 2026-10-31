using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VRArmory
{
    // Binds to the existing Tactical_HUD_Canvas children by name (they are rebuilt by the editor helper,
    // so no serialized references) and renders the assembly state.
    public class AssemblyHUD : MonoBehaviour
    {
        private Text header, subtitle, checklist, directive, footer;

        void Awake()
        {
            header = FindText("Header_Title");
            subtitle = FindText("Subtitle");
            checklist = FindText("Checklist");
            directive = FindText("Directive_Status");
            footer = FindText("Start_Button/Button_Text");
        }

        private Text FindText(string path)
        {
            Transform t = transform.Find(path);
            return t != null ? t.GetComponent<Text>() : null;
        }

        public void SetHeader(string title, string sub)
        {
            if (header != null) header.text = title;
            if (subtitle != null) subtitle.text = sub;
        }

        public void SetChecklist(List<AssemblyStep> steps, int current)
        {
            if (checklist == null) return;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                string mark = steps[i].completed ? "[ X ]" : i == current ? "[ > ]" : "[   ]";
                string state = steps[i].completed ? "[COMPLETE]" : i == current ? "[ACTIVE]" : "[LOCKED]";
                sb.Append($"{mark}  STEP {i + 1:00} : {steps[i].title,-34} {state}");
                if (i < steps.Count - 1) sb.Append('\n');
            }
            checklist.text = sb.ToString();
        }

        public void SetDirective(string text, Color color)
        {
            if (directive == null) return;
            // Assigning the same text still rebuilds the text mesh, so skip unchanged updates.
            if (directive.text == text && directive.color == color) return;
            directive.text = text;
            directive.color = color;
        }

        public string DirectiveText => directive != null ? directive.text : null;

        public void SetFooter(string text)
        {
            if (footer != null) footer.text = text;
        }
    }
}
