# ROVR — LLM Navigation Pipeline

The Semantic Intent Resolution layer from Thesis Chapter 6: turns a natural-language
navigation command into deterministic Unity movement via a locally-hosted LLM, without any
cloud API dependency (Section 6.3.1).

For the whole-project setup (worlds and navigation together), see the [repository README](../README.md); the worlds themselves are documented in [`Unity Environments/`](../Unity%20Environments/README.md).

The scripts live in [`Assets/ROVR/`](../Assets/ROVR) and are part of `Assets/main.unity`; this folder only holds this README. Voice comes from Vert's control panel ([`Assets/GUI/VRControlPanel.cs`](../Assets/GUI/VRControlPanel.cs)): whisper.unity transcribes English speech, and every sentence goes through this pipeline.

## How a command flows

```
microphone -> SpeechSegmenter -> Whisper (English) -> VRControlPanel -> VoiceCommandRouter
            sentence so far (every 0.5 s, and at each short pause): live caption; a leading
                halt word ("stop", "ops") halts right away
            finished sentence (1.5 s of silence): cleaned, de-duplicated, shown as subtitles, then:
utterance
  -> InterruptModule        "stop" / "wait, I mean left": halts NOW, bypassing the LLM
  -> FOVMetadataGrounding   rays through exactly what's on screen -> every visible object, by name
  -> Ollama (Gemma 3n)      + previous command + pending question -> schema-constrained JSON
  -> engine checks          the user's word -> an object in view (couch -> sofa), closed-world
                            validation, "a bit" -> metres, clarify or execute
  -> NavigationController   CharacterController movement, reports how it ended
```

## Files

| File | Role |
|---|---|
| `NavigationCommand.cs` | Command schema (Action / Direction / Amount+Magnitude / Condition), the JSON schema sent to Ollama, and outcomes. Unusable model output becomes a clarification, never a silent no-op. |
| `OllamaClient.cs` | HTTP client for a local Ollama server. Keeps the model loaded, warms it up at start, times each request (`LastLatencySeconds`). |
| `FOVMetadataGrounding.cs` | What the user can see: a 40×24 grid of rays through the camera's actual view, out to its far clip plane, plus a line-of-sight check on every labelled object so small or far ones aren't missed. Sees through door triggers, is blocked by solids, gives each object a session-long id ("tree-3"), and merges walls into one nearest entry. |
| `SceneLabel.cs` | Names an object for the scan ("sofa", "rock", "tree"). The world generators add these. |
| `Highlighter.cs` | Makes the options glow while the system asks "which one?"; the one being looked at glows brighter. The glow is `Assets/Resources/ROVR/Highlight.shader`. |
| `ObjectVocabulary.cs` | Maps the word the user said onto scan names: couch → sofa, refrigerator → fridge, chair → chair / armchair / stool, table → any kind of table. Add words here. |
| `InterruptModule.cs` | Halt detection, by fixed keywords (never the LLM). Only a *leading* halt word counts; only an explicit correction after it is kept. |
| `MovementHabits.cs` | How far "a bit" is: starts at 0.5 m, adapts to the user, resets per participant. |
| `NavigationController.cs` | Movement: fixed speed, smooth stop at a target, instant 90° snap-turns about the head, "turn until you see X", blocked detection. |
| `SemanticIntentResolver.cs` | Orchestrates all of it and enforces the rules below in code. |
| `SpeechSegmenter.cs` | Splits microphone audio into sentences. A sentence ends only after 1.5 s of silence, so pausing mid-sentence doesn't cut it off. Hands out the sentence so far every 0.5 s and at each short pause, for early halts. Tells voice from silence against the room's noise floor. |
| `VoiceCommandRouter.cs` | Routes speech-to-text output: halts early on a leading halt word in the sentence in progress, drops a sentence reported twice within 1 s, and submits the rest. |
| `TranscriptFilter.cs` | Strips Whisper's sound annotations ("[BLANK_AUDIO]", "(wind blowing)") and stock hallucinations ("Thank you."). |
| `ROVRDebugConsole.cs` | Optional on-screen box in the Game view for typing commands instead of speaking. |
| `../GUI/VRControlPanel.cs` | Vert's panel. Adds the pipeline to `PlayerBody` at Play, turns microphone audio into text (SpeechSegmenter + Whisper), and shows the reply (see below). |

## Halt words

Matched as fixed keywords at the start of what was said, so they work without the LLM and before the sentence ends:

- **English:** stop, halt, wait, hold on, hang on, cancel, freeze, whoa, no wait
- **Filipino:** ops, oops, and how Whisper tends to hear a short "ops": op, oop, off

Add more in `HaltLeads` in `InterruptModule.cs`. A halt followed by a correction ("wait, I mean left") stops first, then sends the correction to the LLM.

## The control panel

`VRControlPanel` keeps all of Vert's behaviour (world buttons, mute button, subtitles) and adds:

- **Use LLM** (on by default): finished sentences go to the pipeline. Off: only Vert's fixed phrases (`move forward/backward/left/right`) work, as before.
- **The microphone starts muted.** Press Unmute to start listening.
- **Dead Air Sec** (1.5 by default): the silence that ends a sentence. Pauses shorter than this stay in the same sentence. Halt words don't wait for it.
- **Live captions:** while you speak, the subtitles show the sentence so far, ending in "...".
- **Log Speech:** prints everything Whisper hears to the Console, including the sentence so far. Useful for checking a microphone.
- **Log LLM:** prints each command sent to the LLM (what was heard, and the objects in view) and the model's reply.
- **Output colours:** green = understood and moving ("Moving forward 5 m", "Stopped"), yellow = needs you (a question, or "Something is in the way."), red = didn't work (Ollama not running, a timeout).
- **Switching worlds** stops the current move first.

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
4. Open `Assets/main.unity` and press Play. Nothing to add by hand: `VRControlPanel` adds `FOVMetadataGrounding`, `NavigationController`, `OllamaClient` and `SemanticIntentResolver` to `PlayerBody`, where they find each other and the headset camera (`Camera.main`).
   To change their settings (e.g. the Ollama model or `maxObjects`), add them to `PlayerBody` yourself in that order; the panel uses the ones already there.
5. Press **Unmute** on the panel and speak, e.g. `move forward 5 meters`, `move forward until you reach the door`, `a bit more`, `turn around until you see the tree`, `stop`.

**What the LLM is told about** is everything on screen that has a name: every object with a `SceneLabel` (furniture by its everyday name, plants, statues, rocks, flowers, ponds, every tree, the Maze exit), plus objects tagged Wall, Door, Chair or Tree. Floors, ceilings, grass tufts and lily pads have no name and are never listed. To hide or rename something, edit or remove its `SceneLabel`. The 30 nearest objects are sent (`maxObjects`). *This goes beyond Thesis 7.1.2, which limits the metadata to doors, walls and chairs (House), walls (Maze) and the tree (Plain); the thesis text needs to match.*

**Typing instead of speaking:** add all four components above plus `ROVRDebugConsole` to `PlayerBody`, and set the console's `resolver` field.

Call `SemanticIntentResolver.ResetSession()` between participants: it clears the previous command and resets "a bit" to 0.5 m.

## Behaviour worth knowing

- **Closed world, enforced by the engine.** A move may only target something in the current view. If the model returns a target that isn't (or isn't a known object type), the engine replaces it with a clarification question. The one exception is `turn ... until you see X` — that is how X gets found.
- **Which one.** When several matching objects are in view in the direction asked, the system asks ("Which plant? Look at the one you mean and say "that one", or tell me the one on your left, the one ahead or the one on your right.") and the options glow. The user answers by **looking at one and saying "that one"**, or in words: "the left one", "the closer one", "the one next to the stool". Those answers are read by the engine, not the LLM (instant, and the model was seen to misread them). The choice can also be part of the command: "go to the left plant", "go to that tree" (while looking at it), "the plant by the pond". The pick is remembered for a minute, so turning to face it and repeating the command doesn't ask again, and "it" means that object. A pick that's off to the side still asks the user to turn to it first, and only it glows.
- **Stacked commands.** A command can be several steps, done in order: sequences ("move forward 10 meters, then turn right", "back 3 meters then left 2 meters"), on arrival ("face to the right when you reach the tree" = go to the tree, then turn), on sight ("move forward and turn left when you see the door": walks until the door comes into view, then turns; if it's already in view, that means at the door) and repeats ("turn right three times"). Up to 10 steps. Only the first step is checked against the current view: a later step's target ("turn around and go to the tree") is found when that step starts, and if it isn't there, the chain stops with a message. A blocked step ends the chain. The engine also corrects three things the model was seen to get wrong in chains: repeats ("three times"), "then turn around" (180°), and "left 2 meters" (a move, not a turn).
- **Heading for one object.** Once a command targets a particular object, the walk heads straight for it, even if it's a little to one side; the view doesn't turn. There's no path-finding: something in between ends the move with "Something is in the way." "Until you hit a wall" still goes straight ahead.
- **The engine, not the LLM, decides which object is meant.** The LLM writes the object in the user's own word ("couch"); the engine maps it onto what's in view (`ObjectVocabulary`). Given the full list of visible objects, `gemma3n:e4b` was seen to swap one kind for another ("tree" became a listed plant), so the words the user actually said always win, and a "go to the X" that the model left without a target gets X.
- **One command of memory.** The previous command, how it ended (completed / halted / blocked), and any question still waiting for an answer go back to the model, so "a bit more" and "the other way" work. Object knowledge never comes from that memory, only from the current view. *This relaxes the strict statelessness in Thesis 5.5.4 by exactly one command — the thesis text should say so.*
- **"A bit".** The model only labels a move `small`; the engine converts it. Repeating a nudge in the same direction soon after grows it (×1.15), reversing right after shrinks it (×0.85), bounded to 0.2–2 m.
- **Halts always win.** A halt cancels any request still waiting on the LLM, so a late reply can't restart movement. A newer utterance also supersedes an older one that hasn't been answered yet. "Wait, I mean left" halts immediately, then runs "left"; "stop moving" or "wait a bit" never restart anything.
- **Turns** are whole 90° snaps (`turn around` = two), instant, and pivot about the head so the view doesn't shift.
- **Stopping at a target** is measured from the avatar's body surface (stop gap 0.5 m, eased over the last metre), so it works for any rig radius. A step that can't make progress ends as *Blocked* and aborts the rest of the chain, with a message to the user.
- **Nothing fails silently.** Unusable model output, unknown or unseen objects, blocked moves, and turns that find nothing all produce a message.
- The step offset is forced to 0 so the avatar can't hop onto props and hover (the worlds are flat and there is no gravity).

## Tested against the real model

**Voice through the control panel:** tested in headless Unity with the real panel, the real pipeline, the real model and the three generated worlds; only whisper.unity's output was simulated (no microphone on the test machine). All 46 checks passed, including:

- muted at start, and nothing heard while muted;
- "move forward five meters" moves exactly 5 m, and "go to the tree" stops 0.5 m short of it;
- a partial "Ops", "Op stop", "Off stop", "Oops", "Stop", "Wait" or "Hold on" stops the player in the same frame, before the sentence is finished, without asking the LLM;
- "Wait, I mean left" stops, then moves left;
- "Thank you.", [BLANK_AUDIO] and a sentence reported twice are ignored;
- "go to the tree" in the House asks instead of guessing;
- the world buttons stop the current move;
- with Ollama stopped, the panel says so in red;
- with Use LLM off, Vert's fixed phrases still work.

LLM round trip: **median 0.46 s, max 0.58 s** over 17 commands on an RTX 5070.

**Typed:** verified end to end with `gemma3n:e4b` through the real `OllamaClient`, driving the avatar in the real worlds: stopping 0.5 m short of a wall and of a chair, asking when several chairs are in view or none are, "a bit" / "a bit more" / "back a bit" adapting, "turn around until you see the tree", and "wait, I mean left" halting instantly then going left.

**Speech model:** the scene uses whisper.unity's `ggml-base.bin` with language `en`. On 96 recorded test commands it got 94 exactly right (the English-only `ggml-base.en.bin` got 93), at about 0.55 s each, so it was kept.

**Start Ollama before the session.** Right after Ollama starts, loading the model took 24 to 73 s, and the first full-size commands were very slow (one timed out at 30 s). At Play, `OllamaClient` now warms up with one throwaway command in the real format, so that cost is paid before the first spoken command. Once loaded, the model stays loaded for 30 minutes after the last command.

**Use `127.0.0.1`, not `localhost`, for the Ollama endpoint.** On Windows `localhost` tries IPv6 first and adds about 2 s to every request; the client's default and an automatic rewrite of `localhost` already handle this.

## Known gaps / next steps

- **Voice hasn't been tried with a real headset microphone.** Tested by playing recorded speech into the panel: a 0.8 s pause stayed in one sentence, and "ops" / "stop" stopped the player within 0.15 s of the word ending. A real room and microphone may need **Dead Air Sec** tuned; turn on **Log Speech** to see what Whisper hears.
- **Whisper ignores audio under 1 s,** and a lone "stop" is shorter than that, so the panel pads short audio with silence before transcribing it.
- **Ollama can't take audio** for `gemma3n:e4b` ("model does not support multimodal requests"), so speech is transcribed separately, by whisper.unity, before the LLM sees it. This differs from Thesis 6.3.1.
- **If the LLM answers slower than you speak**, a newer sentence replaces an older unanswered one, so a very fast string of repeats can collapse into fewer moves.
- **House:** a potted plant ("Hall Plant 4") stands straight ahead of the House's Start, against the north wall. Plants are `Furniture`, which the LLM can't see, so "move forward until you hit a wall" from Start ends with "Something is in the way." at the plant.
- **Prompt-driven behaviour is model-dependent.** It was tuned and tested against `gemma3n:e4b`: 24 of 24 scripted commands gave the right final result once the engine's checks were applied (median 0.57 s). On its own, the model often named the wrong object or invented a choice, which is why the engine double-checks both against the user's words. Re-run those checks if you change the model or the prompt.
- **No controller (joystick) arm yet** for the between-subjects control condition (Section 7.1) — it must move through `CharacterController.Move` with the same step offset, and use the same snap-turn and speed settings.
- **No Task Completion Time logging** (Section 7.1.3) hooked up yet. `OllamaClient.LastLatencySeconds` covers LLM latency only.
- Ground-following isn't simulated (no gravity, no stairs) — vertical motion only happens on explicit up/down commands.
