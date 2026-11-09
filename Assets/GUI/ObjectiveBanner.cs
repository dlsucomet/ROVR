// Assets/GUI/ObjectiveBanner.cs
// The current objective, in a strip near the top of the view while the participant is in a world
// ("Objective 2 of 4: the bathroom"). Objectives come back to back: when one is reached the strip flashes
// green and already shows the next one, so there's no pause between them. Built at run time (StudyUI).
using UnityEngine;
using UnityEngine.UI;

public class ObjectiveBanner : MonoBehaviour
{
    public bool Shown => _canvas != null && _canvas.enabled;
    public string Text => _text != null ? _text.text : "";

    private const float FlashSeconds = 0.8f;

    private Canvas _canvas;
    private Image _background;
    private Text _text;
    private float _flashUntil = -1f;

    public static ObjectiveBanner Build(VRControlPanel panel)
    {
        StudyUI.UseLookOf(panel);
        var root = StudyUI.NewCanvas("ObjectiveBanner", new Vector2(820f, 96f), 0.0012f, 1.3f, 0.3f, 6, false);
        var banner = root.AddComponent<ObjectiveBanner>();
        banner._canvas = root.GetComponent<Canvas>();
        banner._background = StudyUI.Background(root.transform, StudyUI.Glass);
        banner._text = StudyUI.Label("Objective", root.transform, "", 30, Color.white, Vector2.zero, new Vector2(780f, 90f));
        banner._text.resizeTextForBestFit = true;
        banner._text.resizeTextMinSize = 16;
        banner._text.resizeTextMaxSize = 30;
        StudyUI.DrawOnTop(root);
        banner.Hide();
        return banner;
    }

    // flash: the previous objective was just reached.
    public void Show(int number, int count, string instruction, bool flash)
    {
        _text.text = "Objective " + number + " of " + count + ":  " + instruction;
        if (flash) _flashUntil = Time.realtimeSinceStartup + FlashSeconds;
        if (!Shown) StudyUI.Show(gameObject, true);
    }

    public void Hide()
    {
        _flashUntil = -1f;
        StudyUI.Show(gameObject, false);
    }

    private void Update()
    {
        if (_background == null) return;
        bool flashing = Time.realtimeSinceStartup < _flashUntil;
        _background.color = flashing ? StudyUI.Done : StudyUI.Glass;
    }
}
