# ROVR — LLM Navigation Pipeline

The Semantic Intent Resolution layer from Thesis Chapter 6: turns a natural-language
navigation command into deterministic Unity movement via a locally-hosted LLM, without any
cloud API dependency (Section 6.3.1).

This first pass wires up the whole loop **except real speech input** — see
[`ROVRDebugConsole.cs`](ROVRDebugConsole.cs) for why, and what to swap in later.

## Files

| File | Role |
|---|---|
| `NavigationCommand.cs` | The four-vector kinematic schema (Action / Direction / Magnitude / Condition) and the JSON schema handed to Ollama for structured output. |
| `OllamaClient.cs` | HTTP client for a local Ollama server; requests schema-constrained JSON. |
| `FOVMetadataGrounding.cs` | Raycast fan from the HMD forward vector; builds the POV-scoped, Closed-World object list injected into every prompt. |
| `InterruptModule.cs` | Keyword-based halt/self-correction detector that bypasses the LLM entirely. |
| `NavigationController.cs` | `CharacterController`-driven executor: continuous / magnitude / conditional moves, discrete snap-turns, sequential chaining. |
| `SemanticIntentResolver.cs` | Orchestrates the above: utterance → interrupt check → metadata scan → LLM → execute or clarify. |
| `ROVRDebugConsole.cs` | OnGUI text-input harness standing in for voice, so the pipeline is testable without a microphone. |

## Setup

1. Install [Ollama](https://ollama.com).
2. Pull the model the thesis specifies (Gemma 3n E4B — the paper's "Gemma 4 E4B" refers to this; it's the model with native audio-input support):
   ```bash
   ollama pull gemma3n:e4b
   ```
3. Start the server (usually already running as a background service after install; if not):
   ```bash
   ollama serve
   ```
4. In Unity, add all seven scripts under `Assets/Scripts/ROVR/` (adjust the path comment at the top of each file if you use a different location).
5. On a GameObject with a `CharacterController` (your avatar/rig), add:
   - `NavigationController`
   - `FOVMetadataGrounding` (point `pov` at the HMD camera)
   - `OllamaClient`
   - `SemanticIntentResolver` (wire its `ollama`, `grounding`, `controller` fields)
   - `ROVRDebugConsole` (wire its `resolver` field)
6. Tag walls/doors/furniture/goal objects the way the world generators already do (`Wall`, `Door`, `Chair`, `Furniture`, `Goal`) — these are exactly the tags that show up in the metadata list the LLM sees.
7. Enter Play mode, type a command into the on-screen text field (e.g. `move forward 5 meters`, `turn left`, `move forward until you hit a wall`, `stop`), and press Send.

## Known gaps / next steps

- **No real voice input yet.** `ROVRDebugConsole` is a deliberate stand-in — next step is Unity `Microphone` capture feeding a speech-to-text stage (or native audio input to Ollama's multimodal endpoint, once that's stable) ahead of `SemanticIntentResolver.SubmitUtterance`.
- **No controller-based (joystick) locomotion yet** for the between-subjects control condition (Section 7.1) — this pass only covers the ROVR/voice arm.
- **No Task Completion Time logging** (Section 7.1.3) hooked up yet.
- **Interrupt Module is keyword-based on a complete string**, not a true continuous-stream interrupt (Section 5.5.2 assumes the halt can land mid-utterance). This is the correct behavior once real streaming ASR is in place — see the comment in `InterruptModule.cs`.
- Gravity/ground-snapping isn't simulated — vertical motion only happens on explicit up/down commands, which matches the flat, single-story study environments but won't hold if that changes.
