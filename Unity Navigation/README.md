# ROVR — LLM Navigation Pipeline

The Semantic Intent Resolution layer from Thesis Chapter 6: turns a natural-language
navigation command into deterministic Unity movement via a locally-hosted LLM, without any
cloud API dependency (Section 6.3.1).

This pass wires up the whole loop **except real speech input** — see
[`ROVRDebugConsole.cs`](ROVRDebugConsole.cs) for why, and what to swap in later.

## How a command flows

```
utterance
  -> InterruptModule        "stop" / "wait, I mean left": halts NOW, bypassing the LLM
  -> FOVMetadataGrounding   raycast matrix over the whole view -> what the user can see
  -> Ollama (Gemma 3n)      + previous command + pending question -> schema-constrained JSON
  -> engine checks          closed-world validation, "a bit" -> metres, clarify or execute
  -> NavigationController   CharacterController movement, reports how it ended
```

## Files

| File | Role |
|---|---|
| `NavigationCommand.cs` | Command schema (Action / Direction / Amount+Magnitude / Condition), the JSON schema sent to Ollama, and outcomes. Unusable model output becomes a clarification, never a silent no-op. |
| `OllamaClient.cs` | HTTP client for a local Ollama server. Keeps the model loaded, warms it up at start, times each request (`LastLatencySeconds`). |
| `FOVMetadataGrounding.cs` | Raycast matrix (21×13 over 100°×80°). Sees through door triggers, is blocked by solids, keeps each object separate, and only reports tags in `groundedTags`. |
| `InterruptModule.cs` | Halt detection. Only a *leading* halt word counts; only an explicit correction after it is kept. |
| `MovementHabits.cs` | How far "a bit" is: starts at 0.5 m, adapts to the user, resets per participant. |
| `NavigationController.cs` | Movement: fixed speed, smooth stop at a target, instant 90° snap-turns about the head, "turn until you see X", blocked detection. |
| `SemanticIntentResolver.cs` | Orchestrates all of it and enforces the rules below in code. |
| `ROVRDebugConsole.cs` | OnGUI text box standing in for voice input. |

## Setup

1. Install [Ollama](https://ollama.com).
2. Pull the model the thesis specifies (Gemma 3n E4B — the paper's "Gemma 4 E4B" refers to this):
   ```bash
   ollama pull gemma3n:e4b
   ```
3. Start the server (usually already running as a background service after install; if not):
   ```bash
   ollama serve
   ```
4. In Unity, add all the scripts under `Assets/Scripts/ROVR/`.
5. On a GameObject with a `CharacterController` (your avatar/rig), add:
   - `FOVMetadataGrounding` (point `pov` at the HMD camera)
   - `NavigationController` (same `pov`; it finds `FOVMetadataGrounding` on the same object)
   - `OllamaClient`
   - `SemanticIntentResolver` (it finds the other three on the same object)
   - `ROVRDebugConsole` (wire its `resolver` field)
6. Generate the worlds (`Unity Environments/`). The tags the LLM can see are set by `groundedTags` on `FOVMetadataGrounding` — by default **Wall, Door, Chair, Tree**. Furniture and Goal are deliberately left out (Thesis 7.1.2: the House's metadata is doors, walls and chairs; the maze has no semantic objects). Add a tag to that list to expose it.
7. Enter Play mode and type commands, e.g. `move forward 5 meters`, `move forward until you reach the door`, `a bit more`, `turn around until you see the tree`, `stop`.

Call `SemanticIntentResolver.ResetSession()` between participants: it clears the previous command and resets "a bit" to 0.5 m.

## Behaviour worth knowing

- **Closed world, enforced by the engine.** A move may only target something in the current view. If the model returns a target that isn't (or isn't a known object type), the engine replaces it with a clarification question. The one exception is `turn ... until you see X` — that is how X gets found.
- **One command of memory.** The previous command, how it ended (completed / halted / blocked), and any question still waiting for an answer go back to the model, so "a bit more" and "the other way" work. Object knowledge never comes from that memory, only from the current view. *This relaxes the strict statelessness in Thesis 5.5.4 by exactly one command — the thesis text should say so.*
- **"A bit".** The model only labels a move `small`; the engine converts it. Repeating a nudge in the same direction soon after grows it (×1.15), reversing right after shrinks it (×0.85), bounded to 0.2–2 m.
- **Halts always win.** A halt cancels any request still waiting on the LLM, so a late reply can't restart movement. A newer utterance also supersedes an older one that hasn't been answered yet. "Wait, I mean left" halts immediately, then runs "left"; "stop moving" or "wait a bit" never restart anything.
- **Turns** are whole 90° snaps (`turn around` = two), instant, and pivot about the head so the view doesn't shift.
- **Stopping at a target** is measured from the avatar's body surface (stop gap 0.5 m, eased over the last metre), so it works for any rig radius. A step that can't make progress ends as *Blocked* and aborts the rest of the chain, with a message to the user.
- **Nothing fails silently.** Unusable model output, unknown or unseen objects, blocked moves, and turns that find nothing all produce a message.
- The step offset is forced to 0 so the avatar can't hop onto props and hover (the worlds are flat and there is no gravity).

## Tested against the real model

Verified end to end with `gemma3n:e4b` through the real `OllamaClient`, driving the avatar in the real worlds: stopping 0.5 m short of a wall and of a chair, asking when several chairs are in view or none are, "a bit" / "a bit more" / "back a bit" adapting, "turn around until you see the tree", and "wait, I mean left" halting instantly then going left. On an RTX 5070 a command takes about **0.6 s** round trip.

**Use `127.0.0.1`, not `localhost`, for the Ollama endpoint.** On Windows `localhost` tries IPv6 first and adds about 2 s to every request; the client's default and an automatic rewrite of `localhost` already handle this.

## Known gaps / next steps

- **No real voice input yet.** `ROVRDebugConsole` is a deliberate stand-in — next is Unity `Microphone` capture feeding speech-to-text (or native audio to Ollama's multimodal endpoint, if Ollama supports audio for Gemma 3n — verify before relying on it) ahead of `SubmitUtterance`.
- **Interrupt Module works on a complete string**, not a live stream (Section 5.5.2 assumes a halt can land mid-utterance). Swap in a streaming check once real ASR exists.
- **One conversational gap:** after "which chair?", a reply like "the left one" can't aim at that chair (turns are 90° snaps), and the model sometimes turns left instead of asking the user to face it. The engine's message tells users to face the object and repeat the command, which works.
- **Prompt-driven behaviour is model-dependent.** It was tuned and tested against `gemma3n:e4b` (41 of 42 scripted commands correct; the one miss is the "the left one" case above). Re-run those checks if you change the model or the prompt.
- **No controller (joystick) arm yet** for the between-subjects control condition (Section 7.1) — it must move through `CharacterController.Move` with the same step offset, and use the same snap-turn and speed settings.
- **No Task Completion Time logging** (Section 7.1.3) hooked up yet. `OllamaClient.LastLatencySeconds` covers LLM latency only.
- Ground-following isn't simulated (no gravity, no stairs) — vertical motion only happens on explicit up/down commands.
