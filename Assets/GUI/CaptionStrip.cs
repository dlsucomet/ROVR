// Assets/GUI/CaptionStrip.cs
// While the panel is hidden, a small strip low in the view still shows what was heard and the reply or
// question ("Which plant?"), with the mic icon, so a participant always knows whether they were understood.
// It copies the panel's texts and mic state every frame. ROVR only: in Manual there's nothing to show.
//
// Two separate things, kept apart: the microphone (the icon, and "MIC ON" / "MIC OFF" under it) and the
// command (the reply line: "Working on it...", "Moving forward", "Done: ...", "Stopped", "Cancelled").
// Muting changes only the first; a command already said carries on.
using UnityEngine;
using UnityEngine.UI;

public class CaptionStrip : MonoBehaviour
{
    public VRControlPanel panel;
    public Canvas canvas;
    public Text heard;
    public Text reply;
    public Image micIcon;

    private Text _micLabel;

    private void LateUpdate()
    {
        if (panel == null || canvas == null) return;
        bool show = !panel.PanelShown && !panel.CardShown && panel.condition == StudyCondition.ROVR;
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show) return;

        if (heard != null && panel.subtitlesText != null) heard.text = panel.subtitlesText.text;
        if (reply != null && panel.outputText != null)
        {
            reply.text = panel.outputText.text;
            reply.color = panel.outputText.color;
        }
        if (micIcon != null && panel.micIcon != null) micIcon.sprite = panel.micIcon.sprite;
        if (_micLabel == null) _micLabel = MakeMicLabel();
        if (_micLabel != null)
        {
            _micLabel.text = panel.MicMuted ? "MIC OFF" : "MIC ON";
            _micLabel.color = panel.MicMuted ? new Color(1f, 0.55f, 0.5f) : new Color(0.55f, 1f, 0.6f);
        }
    }

    // A small label under the mic icon, made at run time so the strip in the scene needn't be rebuilt.
    private Text MakeMicLabel()
    {
        if (micIcon == null || heard == null) return null;
        var go = new GameObject("Mic Label", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(micIcon.transform, false);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -2f);
        rt.sizeDelta = new Vector2(120f, 24f);
        var t = go.GetComponent<Text>();
        t.font = heard.font;
        t.fontSize = Mathf.Max(10, heard.fontSize * 2 / 3);
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.UpperCenter;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (heard.material != null) t.material = heard.material; // drawn on top, like the rest of the strip
        return t;
    }
}
