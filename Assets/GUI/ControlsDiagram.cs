// Assets/GUI/ControlsDiagram.cs
// The controls diagram for the participant's condition: which Quest 3 controller buttons do what.
// Shown on the tutorial card (TutorialCard) and under the panel while A is held to show it (VRControlPanel).
//
// The images are made by Study/diagrams/make_diagrams.py (rovr-diagram.png, controller-diagram.png) and
// copied to Assets/Resources/ROVR/Controls/ as ControlsROVR.png and ControlsManual.png. They are 2:1, with
// their own dark glass background and rounded corners, in the panel's look.
using UnityEngine;
using UnityEngine.UI;

public static class ControlsDiagram
{
    public const float Aspect = 2f; // width / height of the images

    public static Texture2D For(StudyCondition condition)
    {
        var name = condition == StudyCondition.ROVR ? "ControlsROVR" : "ControlsManual";
        var texture = Resources.Load<Texture2D>("ROVR/Controls/" + name);
        if (texture == null) Debug.LogWarning("[ControlsDiagram] Missing Resources/ROVR/Controls/" + name + ".png");
        return texture;
    }

    // A RawImage for the diagram under parent, centred at position (canvas units), width wide. It doesn't
    // block the controller's pointer. Made once and reused (found again by name).
    public static RawImage Attach(Transform parent, Vector2 position, float width)
    {
        var existing = parent.Find("ControlsDiagram");
        var go = existing != null ? existing.gameObject : new GameObject("ControlsDiagram", typeof(RectTransform), typeof(RawImage));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, width / Aspect);
        var image = go.GetComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    // Puts the condition's diagram on image; hides it when the image is missing.
    public static void Show(RawImage image, StudyCondition condition)
    {
        if (image == null) return;
        image.texture = For(condition);
        image.enabled = image.texture != null;
    }
}
