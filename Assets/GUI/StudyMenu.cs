// Assets/GUI/StudyMenu.cs
// The headset's opening menu: the participant types their ID (p007 = 0, 0, 7) on a number pad with the
// controller's pointer, checks it, and confirms. StudySession then looks up their condition and world
// sequence (StudyAssignments), or shows why it can't (an ID that isn't on the list, or a pilot ID the
// researcher still has to set up). Built at run time (StudyUI); in the Editor, the number keys, Backspace
// and Enter type into it too.
using System;
using UnityEngine;
using UnityEngine.UI;

public class StudyMenu : MonoBehaviour
{
    public event Action<string> OnIdConfirmed; // "p007"

    public bool Shown => _canvas != null && _canvas.enabled;
    public string Typed => _digits;

    private const int MaxDigits = 3;

    private Canvas _canvas;
    private GameObject _keypadView, _confirmView, _messageView;
    private Text _display, _confirmText, _messageTitle, _messageText;
    private Button _ok, _messageBack;
    private string _digits = "";

    public static StudyMenu Build(VRControlPanel panel)
    {
        StudyUI.UseLookOf(panel);
        var root = StudyUI.NewCanvas("StudyMenu", new Vector2(560f, 700f), 0.0015f, 1.1f, 0f, 30, true);
        var menu = root.AddComponent<StudyMenu>();
        menu._canvas = root.GetComponent<Canvas>();
        StudyUI.Background(root.transform, StudyUI.Glass);
        menu.BuildKeypad(root.transform);
        menu.BuildConfirm(root.transform);
        menu.BuildMessage(root.transform);
        StudyUI.DrawOnTop(root);
        menu.Hide();
        return menu;
    }

    private void BuildKeypad(Transform root)
    {
        _keypadView = StudyUI.Child("Keypad", root);
        var t = _keypadView.transform;
        StudyUI.Label("Title", t, "Welcome to ROVR", 34, Color.white, new Vector2(0f, 300f), new Vector2(520f, 50f)).fontStyle = FontStyle.Bold;
        StudyUI.Label("Prompt", t, "Type your participant ID", 24, StudyUI.SoftText, new Vector2(0f, 255f), new Vector2(520f, 40f));
        StudyUI.Box("DisplayBack", t, new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 185f), new Vector2(300f, 80f));
        _display = StudyUI.Label("Display", t, "", 46, Color.white, new Vector2(0f, 185f), new Vector2(300f, 80f));

        // 1 2 3 / 4 5 6 / 7 8 9 / Delete 0 OK
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Delete", "0", "OK" };
        var size = new Vector2(140f, 92f);
        for (int i = 0; i < keys.Length; i++)
        {
            int row = i / 3, col = i % 3;
            var pos = new Vector2((col - 1) * 160f, 75f - row * 108f);
            string key = keys[i];
            bool ok = key == "OK";
            var button = StudyUI.Button("Key " + key, t, key, pos, size, ok ? StudyUI.Accent : StudyUI.ButtonFill, key.Length > 1 ? 26 : 38);
            button.onClick.AddListener(() => Press(key));
            if (ok) _ok = button;
        }
    }

    private void BuildConfirm(Transform root)
    {
        _confirmView = StudyUI.Child("Confirm", root);
        var t = _confirmView.transform;
        StudyUI.Label("Title", t, "Is this right?", 34, Color.white, new Vector2(0f, 220f), new Vector2(520f, 50f)).fontStyle = FontStyle.Bold;
        _confirmText = StudyUI.Label("Id", t, "", 64, Color.white, new Vector2(0f, 90f), new Vector2(480f, 100f));
        StudyUI.Label("Hint", t, "Check it with the researcher if you're not sure.", 22, StudyUI.SoftText, new Vector2(0f, 10f), new Vector2(480f, 40f));
        StudyUI.Button("Yes", t, "Yes, start", new Vector2(0f, -110f), new Vector2(360f, 96f), StudyUI.Accent).onClick.AddListener(Confirm);
        StudyUI.Button("No", t, "No, change it", new Vector2(0f, -230f), new Vector2(360f, 96f), StudyUI.ButtonFill).onClick.AddListener(ShowKeypad);
    }

    private void BuildMessage(Transform root)
    {
        _messageView = StudyUI.Child("Message", root);
        var t = _messageView.transform;
        _messageTitle = StudyUI.Label("Title", t, "", 34, Color.white, new Vector2(0f, 200f), new Vector2(520f, 60f));
        _messageTitle.fontStyle = FontStyle.Bold;
        _messageText = StudyUI.Label("Text", t, "", 26, StudyUI.SoftText, new Vector2(0f, 20f), new Vector2(480f, 260f));
        _messageBack = StudyUI.Button("Back", t, "Back", new Vector2(0f, -230f), new Vector2(300f, 96f), StudyUI.ButtonFill);
        _messageBack.onClick.AddListener(ShowKeypad);
    }

    // The number pad, empty.
    public void Show()
    {
        _digits = "";
        ShowKeypad();
        StudyUI.Show(gameObject, true);
    }

    public void Hide() => StudyUI.Show(gameObject, false);

    // A message instead of the pad ("Thank you, the researcher will finish setting up"). With back, the
    // participant can return to the pad to type the ID again.
    public void ShowMessage(string title, string text, bool back)
    {
        _messageTitle.text = title;
        _messageText.text = text;
        _messageBack.gameObject.SetActive(back);
        View(_messageView);
        StudyUI.Show(gameObject, true);
    }

    // For the Editor's keyboard and for tests: the same as pressing that key in the headset.
    public void Press(string key)
    {
        if (!_keypadView.activeSelf) return;
        if (key == "Delete") { if (_digits.Length > 0) _digits = _digits.Substring(0, _digits.Length - 1); }
        else if (key == "OK") { if (_digits.Length > 0) ShowConfirm(); return; }
        else if (key.Length == 1 && char.IsDigit(key[0]) && _digits.Length < MaxDigits) _digits += key;
        Refresh();
    }

    // For tests: the confirm screen's Yes.
    public void Confirm()
    {
        if (!_confirmView.activeSelf) return;
        string id = StudyAssignments.NormalizeId(_digits);
        if (id != null) OnIdConfirmed?.Invoke(id);
    }

    private void ShowKeypad()
    {
        View(_keypadView);
        Refresh();
    }

    private void ShowConfirm()
    {
        string id = StudyAssignments.NormalizeId(_digits);
        if (id == null) return;
        _confirmText.text = id;
        View(_confirmView);
    }

    private void View(GameObject view)
    {
        _keypadView.SetActive(view == _keypadView);
        _confirmView.SetActive(view == _confirmView);
        _messageView.SetActive(view == _messageView);
    }

    // "p0 7 _" style: the digits typed so far, then blanks.
    private void Refresh()
    {
        if (_display != null) _display.text = "p" + _digits + new string('_', MaxDigits - _digits.Length);
        if (_ok != null) _ok.interactable = _digits.Length > 0;
    }

#if UNITY_EDITOR
    private static readonly UnityEngine.InputSystem.Key[] DigitKeys =
    {
        UnityEngine.InputSystem.Key.Digit0, UnityEngine.InputSystem.Key.Digit1, UnityEngine.InputSystem.Key.Digit2,
        UnityEngine.InputSystem.Key.Digit3, UnityEngine.InputSystem.Key.Digit4, UnityEngine.InputSystem.Key.Digit5,
        UnityEngine.InputSystem.Key.Digit6, UnityEngine.InputSystem.Key.Digit7, UnityEngine.InputSystem.Key.Digit8,
        UnityEngine.InputSystem.Key.Digit9,
    };
    private static readonly UnityEngine.InputSystem.Key[] NumpadKeys =
    {
        UnityEngine.InputSystem.Key.Numpad0, UnityEngine.InputSystem.Key.Numpad1, UnityEngine.InputSystem.Key.Numpad2,
        UnityEngine.InputSystem.Key.Numpad3, UnityEngine.InputSystem.Key.Numpad4, UnityEngine.InputSystem.Key.Numpad5,
        UnityEngine.InputSystem.Key.Numpad6, UnityEngine.InputSystem.Key.Numpad7, UnityEngine.InputSystem.Key.Numpad8,
        UnityEngine.InputSystem.Key.Numpad9,
    };
#endif

    private void Update()
    {
#if UNITY_EDITOR
        if (!Shown) return;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;
        for (int d = 0; d <= 9; d++)
            if (keyboard[DigitKeys[d]].wasPressedThisFrame || keyboard[NumpadKeys[d]].wasPressedThisFrame) Press(d.ToString());
        if (keyboard.backspaceKey.wasPressedThisFrame) Press("Delete");
        if (keyboard.enterKey.wasPressedThisFrame)
        {
            if (_keypadView.activeSelf) Press("OK");
            else if (_confirmView.activeSelf) Confirm();
        }
#endif
    }
}
