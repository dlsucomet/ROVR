// Assets/GUI/CaptionStrip.cs
// While the panel is hidden, a small strip low in the view still shows what was heard and the reply or
// question ("Which plant?"), with the mic icon, so a participant always knows whether they were understood.
// It copies the panel's texts and mic state every frame. ROVR only: in Manual there's nothing to show.
using UnityEngine;
using UnityEngine.UI;

public class CaptionStrip : MonoBehaviour
{
    public VRControlPanel panel;
    public Canvas canvas;
    public Text heard;
    public Text reply;
    public Image micIcon;

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
    }
}
