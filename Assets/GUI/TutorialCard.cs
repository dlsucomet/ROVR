// Assets/GUI/TutorialCard.cs
// The card shown before practice that explains the controls for the participant's condition, and again
// from the panel's Tutorial (?) button.
//
// PLACEHOLDER: the diagrams aren't made yet. Each card shows its title and a labelled empty frame where the
// diagram goes. Replace them before user testing.
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
        if (diagramLabel != null)
            diagramLabel.text = "PLACEHOLDER\n" + (voice ? "Voice controls diagram" : "Controller diagram") + "\n(to be made before user testing)";
        SetClosable(true);
        Open();
    }

    // A message in place of the tutorial ("Waiting to start"). One that isn't closable has no Close
    // button or hint, and stays until something else is shown.
    public void ShowMessage(string heading, string message, bool closable)
    {
        if (canvas == null) return;
        if (title != null) title.text = heading;
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

    public void Hide()
    {
        if (canvas == null || !canvas.enabled || !Closable) return;
        canvas.enabled = false;
        foreach (var raycaster in GetComponents<UnityEngine.EventSystems.BaseRaycaster>()) raycaster.enabled = false;
        OnClosed?.Invoke();
    }
}
