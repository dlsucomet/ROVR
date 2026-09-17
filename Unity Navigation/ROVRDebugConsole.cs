// Assets/Scripts/ROVR/ROVRDebugConsole.cs
// Text-input stand-in for the voice channel. Lets the whole intent -> command -> movement
// pipeline be exercised and debugged in Play mode before real microphone capture and speech
// recognition are wired up. Not meant to ship in the study build -- swap this component out for
// the real audio input path once that's ready; SemanticIntentResolver.SubmitUtterance is the
// same entry point either way, so nothing downstream needs to change.
using UnityEngine;

namespace ROVR
{
    public class ROVRDebugConsole : MonoBehaviour
    {
        [SerializeField] SemanticIntentResolver resolver;

        string input = "";
        string lastStatus = "Type a command and press Send.";

        void OnEnable()
        {
            resolver.OnClarificationNeeded += msg => lastStatus = $"[clarify] {msg}";
            resolver.OnError += msg => lastStatus = $"[error] {msg}";
            resolver.OnCommandResolved += cmd =>
            {
                if (!cmd.IsClarification)
                    lastStatus = $"[ok] executing {cmd.steps.Length} step(s)";
            };
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, 130), GUI.skin.box);
            GUILayout.Label("ROVR text-command stub (stand-in for voice input)");

            GUI.SetNextControlName("ROVRInput");
            input = GUILayout.TextField(input);

            bool enterPressed = Event.current.isKey
                && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "ROVRInput";

            if (GUILayout.Button("Send") || enterPressed)
            {
                resolver.SubmitUtterance(input);
                input = "";
            }

            GUILayout.Label(lastStatus);
            GUILayout.EndArea();
        }
    }
}
