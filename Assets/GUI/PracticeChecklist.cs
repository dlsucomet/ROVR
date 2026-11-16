// Assets/GUI/PracticeChecklist.cs
// The practice area's list of things to try before the worlds start (ROVR only), near the top of the view,
// each ticked off as it's done: the commands people needed in the pilots, including the two they didn't
// know they could use (changing speed, two commands at once). It isn't timed and gates nothing: the
// researcher presses Next whenever the participant is ready. Each tick goes to the bridge log
// ("Practice: turn right 45 degrees, done"). Built at run time by StudySession, like the objective banner.
using System.Collections.Generic;
using ROVR;
using UnityEngine;
using UnityEngine.UI;

public class PracticeChecklist : MonoBehaviour
{
    public bool Shown => _canvas != null && _canvas.enabled;
    public int Done => _done.Count;
    public int Count => Items.Length;

    // What to say, as it's shown.
    public static readonly string[] Items =
    {
        "\"Turn left\"", "\"Turn right 45 degrees\"", "\"Take two steps forward\"", "\"Step left\"",
        "\"Go to the tree\"", "\"Stop\" while moving", "\"Faster\"", "Two at once: \"turn left and move forward\""
    };

    private Canvas _canvas;
    private Text _title, _left, _right;
    private readonly HashSet<int> _done = new HashSet<int>();
    private System.Action<string> _say;

    public static PracticeChecklist Build(VRControlPanel panel, System.Action<string> say)
    {
        StudyUI.UseLookOf(panel);
        var root = StudyUI.NewCanvas("PracticeChecklist", new Vector2(820f, 250f), 0.0012f, 1.3f, 0.3f, 6, false);
        var list = root.AddComponent<PracticeChecklist>();
        list._say = say;
        list._canvas = root.GetComponent<Canvas>();
        StudyUI.Background(root.transform, StudyUI.Glass);
        list._title = StudyUI.Label("Title", root.transform, "", 28, Color.white, new Vector2(0f, 92f), new Vector2(780f, 44f));
        list._left = StudyUI.Label("Left", root.transform, "", 24, StudyUI.SoftText, new Vector2(-200f, -22f), new Vector2(380f, 180f));
        list._right = StudyUI.Label("Right", root.transform, "", 24, StudyUI.SoftText, new Vector2(200f, -22f), new Vector2(380f, 180f));
        list._left.alignment = list._right.alignment = TextAnchor.UpperLeft;
        StudyUI.DrawOnTop(root);
        list.Hide();
        return list;
    }

    public void Show()
    {
        _done.Clear();
        Redraw();
        if (!Shown) StudyUI.Show(gameObject, true);
    }

    public void Hide()
    {
        StudyUI.Show(gameObject, false);
    }

    // A command ROVR is about to run.
    public void Observe(NavigationCommand command)
    {
        if (!Shown || command == null || command.steps == null || command.IsClarification) return;
        if (command.steps.Length >= 2) Tick(7);
        foreach (var s in command.steps)
        {
            if (s.action == ActionType.Turn && s.direction == DirectionType.Left && s.amount == AmountType.None && !s.HasCondition) Tick(0);
            if (s.action == ActionType.Turn && s.direction == DirectionType.Right && s.amount == AmountType.Exact && Mathf.Abs(s.magnitude - 45f) < 1f) Tick(1);
            if (s.action == ActionType.Move && s.direction == DirectionType.Forward && s.InSteps && Mathf.Approximately(s.magnitude, 2f)) Tick(2);
            if (s.action == ActionType.Move && s.direction == DirectionType.Left && s.InSteps) Tick(3);
            if (s.action == ActionType.Move && s.HasCondition && s.condition.ToLowerInvariant().StartsWith("tree")) Tick(4);
        }
    }

    // "Stop" said while the body was moving.
    public void ObserveHalt(bool wasMoving)
    {
        if (Shown && wasMoving) Tick(5);
    }

    public void ObserveSpeed(SpeedLevel level)
    {
        if (Shown && level == SpeedLevel.Fast) Tick(6);
    }

    private void Tick(int item)
    {
        if (!_done.Add(item)) return;
        _say?.Invoke("Practice: " + Items[item].Replace("\"", "") + ", done (" + _done.Count + " of " + Items.Length + ")");
        Redraw();
    }

    private void Redraw()
    {
        if (_title == null) return;
        _title.text = _done.Count == Items.Length ? "Practice: all done. Tell the researcher when you're ready."
                                                  : "Practice: try each of these (" + _done.Count + " of " + Items.Length + " done)";
        _left.text = Lines(0, 4);
        _right.text = Lines(4, Items.Length);
    }

    private string Lines(int from, int to)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < to; i++)
            sb.Append(_done.Contains(i) ? "<color=#5ad17a>[x]  " : "[  ]  ").Append(Items[i]).Append(_done.Contains(i) ? "</color>" : "").Append('\n');
        return sb.ToString();
    }
}
