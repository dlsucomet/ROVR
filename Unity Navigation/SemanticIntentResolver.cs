// Assets/Scripts/ROVR/SemanticIntentResolver.cs
// Top-level orchestrator for the pipeline in Section 6.2.1: utterance -> (Interrupt check) ->
// POV metadata scan -> LLM -> NavigationCommand -> controller execution or clarification.
//
// Every call is evaluated independently against the current field of view (6.2.4: "the system
// operates statelessly -- evaluating each command independently against the current POV -- we
// eliminate the risk of contextual hallucinations"). No conversation history is retained or sent.
using UnityEngine;
using System;

namespace ROVR
{
    public class SemanticIntentResolver : MonoBehaviour
    {
        [SerializeField] OllamaClient ollama;
        [SerializeField] FOVMetadataGrounding grounding;
        [SerializeField] NavigationController controller;

        public event Action<string> OnClarificationNeeded;
        public event Action<string> OnError;
        public event Action<NavigationCommand> OnCommandResolved;

        const string SystemPrompt =
            "You are the navigation intent parser for ROVR, a voice-driven VR locomotion system. " +
            "You do not control the camera and know nothing about the environment beyond the " +
            "'Visible objects' list given with each command -- that list is the ONLY thing currently " +
            "within the user's field of view (Closed-World Assumption). Evaluate this command entirely " +
            "on its own; you have no memory of any prior command (stateless). " +
            "Respond with the required JSON only, no prose. " +
            "Each step has: action (move | turn | stop), direction (forward | back | left | right | up | down | none), " +
            "magnitude (a number: meters for move, degrees for turn; use 0 if no explicit amount was given), " +
            "and condition (the exact tag string from the visible-objects list to move toward/until, or an empty " +
            "string if the step isn't conditioned on reaching an object). " +
            "If the command references an object that is NOT in the visible-objects list, do not guess or invent " +
            "movement -- return an empty steps array and set 'clarification' to a short question asking the user " +
            "to describe where the target is relative to what they can currently see. " +
            "Never resolve a command against an object outside the visible-objects list.";

        public void SubmitUtterance(string utterance)
        {
            if (string.IsNullOrWhiteSpace(utterance)) return;

            // 5.5.2 / 6.3.3: halt phrases bypass the LLM entirely and take effect immediately.
            if (InterruptModule.TryDetectHalt(utterance))
            {
                controller.Halt();
                return;
            }

            var visible = grounding.ScanFieldOfView();
            string metadataJson = grounding.ToJson(visible);
            string userPrompt = $"Visible objects (POV-bounded): {metadataJson}\nCommand: \"{utterance}\"";

            ollama.RequestCommand(SystemPrompt, userPrompt, OnResolved, err => OnError?.Invoke(err));
        }

        void OnResolved(NavigationCommand command)
        {
            OnCommandResolved?.Invoke(command);

            if (command.IsClarification)
            {
                // 5.5.6: conversational fallback rather than a silent failure or mesh-clipping attempt.
                OnClarificationNeeded?.Invoke(command.clarificationPrompt);
                return;
            }

            controller.Execute(command);
        }
    }
}
