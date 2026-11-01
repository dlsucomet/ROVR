// Assets/Editor/PanelBuilder.cs
// Menu: Tools > ROVR > Rebuild Panel UI
// Builds the control panel, the caption strip and the tutorial card into the open scene (main.unity):
// dark glass with rounded corners, a mic icon that gets a red slash when muted (like Discord), Plain /
// Maze / House, and a "?" for the tutorial. After building, everything can be adjusted by hand in the
// scene: layout, colours, sizes. Running this again rebuilds the three from scratch, losing such changes.
//
//   MainCanvas (the panel, with VRControlPanel): heard text, reply, and a row of buttons. Hidden at start;
//     A held shows it, straight in front of the user, then it follows lazily (LazyFollow).
//   CaptionStrip: a small strip low in the view while the panel is hidden (heard, reply, mic icon).
//   TutorialCard: before practice, and from "?". PLACEHOLDER diagrams for now.
//
// The mic icons are drawn here into Assets/GUI/Sprites (MicOn.png, MicOff.png). The rounded shapes use
// Unity's built-in UI sprites.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PanelBuilder
{
    const string SpriteFolder = "Assets/GUI/Sprites";

    static readonly Color Glass = new Color(0.07f, 0.08f, 0.10f, 0.93f);
    static readonly Color ButtonFill = new Color(0.20f, 0.23f, 0.28f, 0.96f);
    static readonly Color Faint = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color SoftText = new Color(0.78f, 0.81f, 0.86f, 1f);

    static Sprite rounded, circle;
    static Font font;

    [MenuItem("Tools/ROVR/Rebuild Panel UI")]
    static void Rebuild()
    {
        if (!EditorUtility.DisplayDialog("Rebuild Panel UI",
                "This rebuilds the control panel, the caption strip and the tutorial card from scratch. Changes made to them by hand are lost.",
                "Rebuild", "Cancel")) return;
        Build();
    }

    // For batch mode: Unity -batchmode -projectPath . -executeMethod PanelBuilder.BuildAndSave
    public static void BuildAndSave()
    {
        EditorSceneManager.OpenScene("Assets/main.unity");
        Build();
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    static void Build()
    {
        rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var (micOn, micOff) = MicSprites();

        var panel = Object.FindAnyObjectByType<VRControlPanel>();
        if (panel == null) { Debug.LogError("[PanelBuilder] No VRControlPanel in the open scene."); return; }
        var canvasRoot = panel.gameObject;
        var eventCamera = canvasRoot.GetComponent<Canvas>().worldCamera;

        BuildPanel(panel, micOn, micOff);
        var card = BuildCard(eventCamera);
        panel.tutorialCard = card;
        var strip = BuildStrip(panel);

        EditorUtility.SetDirty(panel);
        EditorUtility.SetDirty(card);
        EditorUtility.SetDirty(strip);
        EditorSceneManager.MarkSceneDirty(canvasRoot.scene);
        Debug.Log("[PanelBuilder] Built the panel, the caption strip and the tutorial card.");
    }

    // ------------------------------------------------------------------ the panel

    static void BuildPanel(VRControlPanel panel, Sprite micOn, Sprite micOff)
    {
        var go = panel.gameObject;
        for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);

        // Vert's VFFloatingMenu followed every head movement; LazyFollow waits until you look away.
        foreach (var old in go.GetComponents<MonoBehaviour>())
            if (old != null && old.GetType().Name == "VFFloatingMenu") Object.DestroyImmediate(old);
        var follow = GetOrAdd<LazyFollow>(go);
        follow.distance = 1.2f; follow.heightOffset = -0.05f; follow.recenterDegrees = 25f;
        GetOrAdd<UIOnTop>(go);

        var canvas = go.GetComponent<Canvas>();
        canvas.sortingOrder = 10;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(680f, 300f);
        rect.localScale = Vector3.one * 0.0015f;
        var scaler = go.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.dynamicPixelsPerUnit = 4f;

        Fill(Panel("Background", go.transform, Glass, 0.25f));

        panel.subtitlesText = Label("Heard", go.transform, "", 28, Color.white, new Vector2(0f, 85f), new Vector2(620f, 56f));
        panel.outputText = Label("Reply", go.transform, "", 24, new Color(0.55f, 0.9f, 0.55f), new Vector2(0f, 25f), new Vector2(620f, 60f));
        Box("Divider", go.transform, rounded, Faint, new Vector2(0f, -22f), new Vector2(600f, 2f));

        const float row = -88f;
        panel.micMuteButton = RoundButton("MicButton", go.transform, new Vector2(-255f, row), out Image micIcon);
        micIcon.sprite = micOff;
        panel.micIcon = micIcon;
        panel.micOnSprite = micOn;
        panel.micOffSprite = micOff;
        panel.plainButton = PillButton("PlainButton", go.transform, "Plain", new Vector2(-115f, row));
        panel.mazeButton = PillButton("MazeButton", go.transform, "Maze", new Vector2(15f, row));
        panel.houseButton = PillButton("HouseButton", go.transform, "House", new Vector2(145f, row));
        panel.tutorialButton = RoundButton("TutorialButton", go.transform, new Vector2(255f, row), out Image unused);
        Object.DestroyImmediate(unused.gameObject);
        Label("Mark", panel.tutorialButton.transform, "?", 36, Color.white, Vector2.zero, new Vector2(72f, 72f)).fontStyle = FontStyle.Bold;
    }

    // ------------------------------------------------------------------ the caption strip

    static CaptionStrip BuildStrip(VRControlPanel panel)
    {
        var go = FreshRoot("CaptionStrip");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(780f, 120f);
        rect.localScale = Vector3.one * 0.0011f;
        var follow = go.AddComponent<LazyFollow>();
        follow.distance = 1.0f; follow.heightOffset = -0.33f; follow.recenterDegrees = 18f;

        Fill(Panel("Background", go.transform, Glass, 0.3f));
        var micBack = Box("Mic", go.transform, circle, ButtonFill, new Vector2(-335f, 0f), new Vector2(72f, 72f));
        var micIcon = Box("Icon", micBack.transform, panel.micOffSprite, Color.white, Vector2.zero, new Vector2(46f, 46f));
        var heard = Label("Heard", go.transform, "", 25, Color.white, new Vector2(35f, 24f), new Vector2(660f, 46f));
        var reply = Label("Reply", go.transform, "", 22, new Color(0.55f, 0.9f, 0.55f), new Vector2(35f, -24f), new Vector2(660f, 46f));
        reply.resizeTextForBestFit = true; reply.resizeTextMinSize = 12; reply.resizeTextMaxSize = 22;

        var strip = go.AddComponent<CaptionStrip>();
        strip.panel = panel; strip.canvas = canvas; strip.heard = heard; strip.reply = reply; strip.micIcon = micIcon;
        go.AddComponent<UIOnTop>();
        return strip;
    }

    // ------------------------------------------------------------------ the tutorial card

    static TutorialCard BuildCard(Camera eventCamera)
    {
        var go = FreshRoot("TutorialCard");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eventCamera;
        canvas.sortingOrder = 20;
        canvas.enabled = false;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        go.AddComponent<GraphicRaycaster>();
        var xrRaycaster = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRaycaster != null) go.AddComponent(xrRaycaster);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(640f, 460f);
        rect.localScale = Vector3.one * 0.0015f;
        var follow = go.AddComponent<LazyFollow>();
        follow.distance = 1.2f; follow.heightOffset = 0f; follow.recenterDegrees = 25f;

        Fill(Panel("Background", go.transform, Glass, 0.25f));
        var title = Label("Title", go.transform, "Tutorial", 32, Color.white, new Vector2(0f, 190f), new Vector2(580f, 50f));
        title.fontStyle = FontStyle.Bold;
        Box("DiagramFrame", go.transform, rounded, Faint, new Vector2(0f, 15f), new Vector2(560f, 270f)).pixelsPerUnitMultiplier = 0.4f;
        var label = Label("DiagramLabel", go.transform, "PLACEHOLDER", 22, SoftText, new Vector2(0f, 15f), new Vector2(520f, 240f));
        var hint = Label("Hint", go.transform, "Press A to continue", 20, SoftText, new Vector2(-90f, -180f), new Vector2(360f, 40f));
        var close = PillButton("CloseButton", go.transform, "Close", new Vector2(200f, -180f));

        var card = go.AddComponent<TutorialCard>();
        card.canvas = canvas; card.title = title; card.diagramLabel = label; card.closeButton = close; card.hint = hint; card.follow = follow;
        go.AddComponent<UIOnTop>();
        return card;
    }

    // ------------------------------------------------------------------ pieces

    static GameObject FreshRoot(string name)
    {
        var old = GameObject.Find(name);
        if (old != null) Object.DestroyImmediate(old);
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        return go;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    static Image Panel(string name, Transform parent, Color color, float cornerScale)
    {
        var image = Child(name, parent).AddComponent<Image>();
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = cornerScale; // smaller = rounder corners
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Image Box(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
    {
        var image = Child(name, parent).AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite == rounded ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = sprite != rounded;
        image.color = color;
        image.raycastTarget = false;
        image.rectTransform.anchoredPosition = pos;
        image.rectTransform.sizeDelta = size;
        return image;
    }

    static void Fill(Graphic g)
    {
        var r = g.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    static Text Label(string name, Transform parent, string text, int size, Color color, Vector2 pos, Vector2 box)
    {
        var t = Child(name, parent).AddComponent<Text>();
        t.font = font;
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

    static Button PillButton(string name, Transform parent, string text, Vector2 pos)
    {
        var image = Box(name, parent, rounded, ButtonFill, pos, new Vector2(120f, 56f));
        image.pixelsPerUnitMultiplier = 0.45f;
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        Tint(button);
        Label("Label", image.transform, text, 22, Color.white, Vector2.zero, new Vector2(120f, 56f));
        return button;
    }

    static Button RoundButton(string name, Transform parent, Vector2 pos, out Image icon)
    {
        var image = Box(name, parent, circle, ButtonFill, pos, new Vector2(72f, 72f));
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        Tint(button);
        icon = Box("Icon", image.transform, null, Color.white, Vector2.zero, new Vector2(46f, 46f));
        return button;
    }

    static void Tint(Button button)
    {
        var colors = button.colors;
        colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = colors;
    }

    // ------------------------------------------------------------------ the mic icons

    // A white microphone, and the same with a red slash (with a dark gap either side of it, like Discord's).
    static (Sprite on, Sprite off) MicSprites()
    {
        Directory.CreateDirectory(SpriteFolder);
        string onPath = SpriteFolder + "/MicOn.png", offPath = SpriteFolder + "/MicOff.png";
        File.WriteAllBytes(onPath, DrawMic(false).EncodeToPNG());
        File.WriteAllBytes(offPath, DrawMic(true).EncodeToPNG());
        AssetDatabase.Refresh();
        return (ImportSprite(onPath), ImportSprite(offPath));
    }

    static Sprite ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Texture2D DrawMic(bool slashed)
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var red = new Color(0.93f, 0.26f, 0.26f, 1f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // Microphone: the head (a capsule), the holder (a U round its bottom), the stem and the base.
                float head = Segment(p, new Vector2(64f, 66f), new Vector2(64f, 96f)) - 17f;
                float ring = Mathf.Abs(Vector2.Distance(p, new Vector2(64f, 70f)) - 31f) - 4f;
                if (p.y > 70f) ring = Mathf.Max(ring, p.y - 70f); // only the lower half
                float stem = Segment(p, new Vector2(64f, 39f), new Vector2(64f, 26f)) - 4f;
                float foot = Segment(p, new Vector2(46f, 24f), new Vector2(82f, 24f)) - 4f;
                float mic = Mathf.Min(Mathf.Min(head, ring), Mathf.Min(stem, foot));
                float a = Mathf.Clamp01(0.5f - mic);
                Color c = new Color(1f, 1f, 1f, a);

                if (slashed)
                {
                    float slash = Segment(p, new Vector2(28f, 104f), new Vector2(100f, 22f)) - 5.5f;
                    float gap = slash - 6f; // clear a little either side of the slash
                    c.a *= Mathf.Clamp01(0.5f + gap);
                    float s = Mathf.Clamp01(0.5f - slash);
                    c = Color.Lerp(c, red, s);
                    c.a = Mathf.Max(c.a, s);
                }
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
        return Vector2.Distance(p, a + ab * t);
    }
}
