// Assets/GUI/StudyUI.cs
// Builds the study's headset UI at run time (the opening ID menu, StudyMenu, and the objective banner,
// ObjectiveBanner) in the same look as the panel and tutorial card (PanelBuilder): dark glass, rounded
// buttons, drawn over the world (UIOnTop), following the user lazily (LazyFollow). Built from code so
// main.unity needs no hand edits. The rounded shapes are taken from the scene's tutorial card and panel,
// because Unity's built-in UI sprites can only be loaded in the Editor; without them the shapes are square.
using UnityEngine;
using UnityEngine.UI;

public static class StudyUI
{
    public static readonly Color Glass = new Color(0.07f, 0.08f, 0.10f, 0.93f);
    public static readonly Color ButtonFill = new Color(0.20f, 0.23f, 0.28f, 0.96f);
    public static readonly Color Accent = new Color(0.18f, 0.44f, 0.89f, 0.98f);
    public static readonly Color SoftText = new Color(0.78f, 0.81f, 0.86f, 1f);
    public static readonly Color Done = new Color(0.12f, 0.45f, 0.25f, 0.95f);

    private static Sprite _rounded;
    private static Font _font;

    // Takes the rounded sprite from the panel's tutorial card or buttons, if there are any.
    public static void UseLookOf(VRControlPanel panel)
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_rounded != null || panel == null) return;
        var sources = new Component[] { panel.tutorialCard, panel.plainButton, panel };
        foreach (var source in sources)
        {
            if (source == null) continue;
            foreach (var image in source.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && image.type == Image.Type.Sliced) { _rounded = image.sprite; return; }
        }
    }

    // A world-space canvas that follows the user lazily. Clickable ones get the XR controller's raycaster
    // (through reflection, so this compiles without the XR Interaction Toolkit's assembly reference).
    public static GameObject NewCanvas(string name, Vector2 size, float scale, float distance, float height, int sortingOrder, bool clickable)
    {
        var go = new GameObject(name, typeof(RectTransform));
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0) go.layer = ui;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = sortingOrder;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        if (clickable)
        {
            go.AddComponent<GraphicRaycaster>();
            var xrRaycaster = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (xrRaycaster != null) go.AddComponent(xrRaycaster);
        }
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one * scale;
        var follow = go.AddComponent<LazyFollow>();
        follow.distance = distance;
        follow.heightOffset = height;
        follow.recenterDegrees = 25f;
        return go;
    }

    // Call once all the children exist: UIOnTop sets their material when it starts.
    public static void DrawOnTop(GameObject root)
    {
        if (root.GetComponent<UIOnTop>() == null) root.AddComponent<UIOnTop>();
    }

    public static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    public static Image Box(string name, Transform parent, Color color, Vector2 pos, Vector2 size, float corner = 0.45f)
    {
        var image = Child(name, parent).AddComponent<Image>();
        image.sprite = _rounded;
        image.type = _rounded != null ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = corner;
        image.color = color;
        image.raycastTarget = false;
        image.rectTransform.anchoredPosition = pos;
        image.rectTransform.sizeDelta = size;
        return image;
    }

    public static Image Background(Transform parent, Color color)
    {
        var image = Box("Background", parent, color, Vector2.zero, Vector2.zero, 0.25f);
        var r = image.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        return image;
    }

    public static Text Label(string name, Transform parent, string text, int size, Color color, Vector2 pos, Vector2 box)
    {
        var t = Child(name, parent).AddComponent<Text>();
        t.font = _font != null ? _font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        t.rectTransform.anchoredPosition = pos;
        t.rectTransform.sizeDelta = box;
        return t;
    }

    public static Button Button(string name, Transform parent, string text, Vector2 pos, Vector2 size, Color fill, int fontSize = 30)
    {
        var image = Box(name, parent, fill, pos, size);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
        button.colors = colors;
        Label("Label", image.transform, text, fontSize, Color.white, Vector2.zero, size);
        return button;
    }

    public static void Show(GameObject root, bool shown)
    {
        if (root == null) return;
        var canvas = root.GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = shown;
        foreach (var raycaster in root.GetComponents<UnityEngine.EventSystems.BaseRaycaster>()) raycaster.enabled = shown;
        var follow = root.GetComponent<LazyFollow>();
        if (shown && follow != null) follow.SnapNow();
    }
}
