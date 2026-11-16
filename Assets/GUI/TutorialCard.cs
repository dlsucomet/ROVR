// Assets/GUI/TutorialCard.cs
// The card shown before practice that explains the controls for the participant's condition, and again
// from the panel's Tutorial (?) button.
//
// It shows the controls diagram for the condition (ControlsDiagram) where PanelBuilder put the empty
// diagram frame, and under it the main moves in words: for ROVR a few plain examples, how to stop, how to
// change speed and that two things can be said at once. Messages
// (ShowMessage) show their text in the frame instead.
//
// Closed with A or its Close button. It also shows the session's waiting card ("Waiting to start",
// ShowMessage), which the participant can't close: it stays until the session is set up.
using System;
using UnityEngine;
using UnityEngine.UI;

public class TutorialCard : MonoBehaviour
{
    public Canvas canvas;
    public Text title;
    public Text diagramLabel;
    public Button closeButton;
    public Text hint; // "Press A to continue"
    public LazyFollow follow;

    private const float DiagramWidth = 560f; // canvas units; the card is 640 x 460
    private RawImage _diagram;
    private Text _moves; // the main moves in words, under the diagram

    // Plain words (a pilot asked for a less technical briefing): what to say, how to stop, and the two things
    // pilots didn't know they could do (change speed, say two things at once).
    private const string VoiceMoves =
        "Tell ROVR where to go, the way you'd tell a friend: \"go to the tree\", \"turn left\", \"take two steps forward\".\n" +
        "Say \"stop\" to stop, \"faster\" or \"slower\" for speed, or two things at once: \"turn left and move forward\". B cancels.";
    private const string StickMoves =
        "Left stick: walk forward and back, and sidestep left and right. Right stick: turn.\n" +
        "Forward is the way you're facing.";

    public bool Shown => canvas != null && canvas.enabled;
    public bool Closable { get; private set; } = true;
    public event Action OnClosed;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
    }

    public void Show(StudyCondition condition)
    {
        if (canvas == null) return;
        bool voice = condition == StudyCondition.ROVR;
        if (title != null) title.text = voice ? "Tutorial: ROVR (voice)" : "Tutorial: Manual (controller)";
        if (_diagram == null) _diagram = ControlsDiagram.Attach(transform, new Vector2(0f, 30f), DiagramWidth);
        ControlsDiagram.Show(_diagram, condition);
        bool hasDiagram = _diagram.enabled;
        if (_moves == null) _moves = MakeMoves();
        if (_moves != null)
        {
            _moves.gameObject.SetActive(hasDiagram);
            _moves.text = voice ? VoiceMoves : StickMoves;
        }
        SetFrame(!hasDiagram);
        if (!hasDiagram && diagramLabel != null)
            diagramLabel.text = voice ? "Speak to move. Tap A: mic on / off." : "Left stick: walk. Right stick: turn.";
        SetClosable(true);
        Open();
    }

    // Two lines under the diagram, in the title's font, made at run time so the card in the scene needn't
    // be rebuilt.
    private Text MakeMoves()
    {
        if (title == null) return null;
        var go = new GameObject("Moves", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(600f, 56f);
        rt.anchoredPosition = new Vector2(0f, -150f);
        var t = go.GetComponent<Text>();
        t.font = title.font;
        t.fontSize = 15;
        t.lineSpacing = 1.1f;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = new Color(0.86f, 0.89f, 0.94f);
        t.raycastTarget = false;
        if (title.material != null) t.material = title.material; // drawn on top, like the rest of the card
        return t;
    }

    // The empty frame and its text, shown for messages (and if the diagram is missing).
    private void SetFrame(bool shown)
    {
        if (diagramLabel != null) diagramLabel.gameObject.SetActive(shown);
        var frame = transform.Find("DiagramFrame");
        if (frame != null) frame.gameObject.SetActive(shown);
    }

    // A message in place of the tutorial ("Waiting to start"). One that isn't closable has no Close
    // button or hint, and stays until something else is shown.
    public void ShowMessage(string heading, string message, bool closable)
    {
        if (canvas == null) return;
        if (title != null) title.text = heading;
        if (_diagram != null) _diagram.enabled = false;
        if (_moves != null) _moves.gameObject.SetActive(false);
        SetFrame(true);
        if (diagramLabel != null) diagramLabel.text = message;
        SetClosable(closable);
        Open();
    }

    private void SetClosable(bool closable)
    {
        Closable = closable;
        if (closeButton != null) closeButton.gameObject.SetActive(closable);
        if (hint != null) hint.gameObject.SetActive(closable);
    }

    private void Open()
    {
        canvas.enabled = true;
        foreach (var raycaster in GetComponents<UnityEngine.EventSystems.BaseRaycaster>()) raycaster.enabled = true;
        if (follow != null) follow.SnapNow();
    }

    // Closes it even when the participant can't (the session's "World complete" card, when the researcher
    // moves on).
    public void Dismiss()
    {
        Closable = true;
        Hide();
    }

    public void Hide()
    {
        if (canvas == null || !canvas.enabled || !Closable) return;
        canvas.enabled = false;
        foreach (var raycaster in GetComponents<UnityEngine.EventSystems.BaseRaycaster>()) raycaster.enabled = false;
        OnClosed?.Invoke();
    }
}
