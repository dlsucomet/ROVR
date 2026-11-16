# ROVR

LLM-driven voice navigation in VR. A thesis prototype comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | Docs for the LLM pipeline that turns a spoken command into avatar movement (scripts in `Assets/ROVR/`) |

## Status

Voice navigation runs through the LLM in `Assets/main.unity`:

- **Speech:** Whisper transcribes English speech. A sentence ends after **1.5 s of silence**, so pausing mid-sentence is fine; an open-ended move said in full ("move forward", "move back", "keep going"; not "go back", which is the undo) runs after a **0.6 s** pause instead (`earlyEndSec` on the panel; turns and counted moves always wait the full 1.5 s, so "turn right, 30 degrees" never turns 90 first). On the headset, speech is transcribed on the PC through the bridge: with whisper.cpp's CUDA build and `ggml-small.en.bin` on an NVIDIA GPU, under 0.1 s a sentence (on the CPU with the base model about 0.4 s; the Quest itself takes about 15 s). Each sentence is sent with the current world's object names, which Whisper gets as a prompt, so it hears "shelf", not "shell"; a sentence is transcribed without them first, so noise never turns into those words. In the Editor, [whisper.unity](https://github.com/Macoron/whisper.unity) can also do it.
- **Panel:** a dark glass panel with the subtitles and reply, the mic icon (a mic, slashed when muted), the Plain / Maze / House buttons and a **?** button that shows the tutorial card again. The tutorial card, and a second card just under the panel, show the controls diagram for the condition (`Study/diagrams/`, copied to `Assets/Resources/ROVR/Controls/`; `Study/diagrams/make_diagrams.py` makes them). It starts hidden and follows your gaze lazily. While it's hidden, a caption strip low in the view still shows what was heard and the reply, with the mic icon. **The mic starts muted.**
- **A button (right controller):** a short press mutes or unmutes the mic, and closes the tutorial card. Holding it for 0.6 s shows or hides the panel. In the Editor, **F1** is the A button. **Muting only stops listening:** a sentence you were saying when you muted is finished there and still carried out, and a command already being worked on carries on (the LLM is told the sentence may be cut off, so it asks rather than guesses). The caption strip shows the mic (MIC ON / MIC OFF) apart from the command's status (Working on it, Moving forward, Done, Stopped, Cancelled).
- **B button (right controller, ROVR):** cancels the command: movement stops at once, and anything said but not yet acted on is dropped. In the Editor, **F2**.
- **Two conditions:** **ROVR** (voice only) and **Manual** ("traditional", `trad` in the logs: controllers only. The left stick moves, faster the further it's pushed, up to 2 m/s; the right stick turns smoothly, with the edges of the view darkened; voice is off). In a study session the participant's ID sets it (step 4 below); otherwise the panel's **Condition** field in the Inspector.
- **Study sessions:** the headset opens on an ID menu. The participant's ID (p001–p030) gives their condition and one of six orders of the three worlds, from a fixed balanced table (`Assets/GUI/StudyAssignments.cs`, copy in `Study/assignment-table.csv`). In each world the objectives appear one after another at the top of the view; the clock starts at the participant's first action there. Each world run is saved as its own time-stamped CSV, for example `p007-rovr-house.csv`.
- **Minimaps** (`Assets/GUI/WorldMinimap.cs`): the Maze and the House each have one. It appears low on the left of the view when the world loads and travels and turns with you; grab it with the grip button (or pinch it with a tracked hand) to move it. It shows the walls, you (an arrow the way you face), the current objective (yellow), and in the House the doorways (green), the room names and the room you're in.
- **Hands** (`Assets/GUI/VirtualHands.cs`): a simple gloved hand round each controller, or, with hand tracking (controllers put down), each tracked hand's joints. Hidden at once when tracking is lost. Hand tracking needs the XR Hands package (in the manifest) and **Tools > ROVR > Set Up Hand Tracking**.
- **Movement (both conditions):** gravity keeps you on the floor (`Assets/ROVR/PlayerGravity.cs`); "up"/"jump" jumps. Starts and stops ease (acceleration 4 m/s², deceleration 5 m/s²); every spoken stop ("stop", "wait", "freeze") eases down more gently (**Stop Deceleration**, 3 m/s²: about two thirds of a second), never a dead stop; only walls and **B** stop at once. The edges of the view darken while turning and while walking forward or back (not for sidesteps). Comfort options on the panel's component: **Snap Turning**, **Movement Vignette** (on), **Step Meters** (1 m by default, the same for forward, back and sidesteps), **Acceleration** and **Deceleration**.
- **Pointer:** the controller rays are wider, cyan over something, with a dot where they land and the name of the object under it.
- **Objectives** (`Assets/GUI/StudyTasks.cs`): the Plain has five landmarks round the field, the current one marked by a beacon: a column of see-through yellow light from the ground up, with no solid part. The Maze (8 x 6 cells of 5 m, a 19-cell route with three short dead ends, and a red flag, a blue lamp and a white statue as landmarks) has a glowing trail along its correct route (`Assets/GUI/MazeTrail.cs`, worked out from the walls when Play starts) with three yellow checkpoint markers on it (shorter beacons, just over the hedges: "Go to the yellow marker"), then the exit under a tall yellow beacon. The House has two per room, unmarked: going to furniture, going into the two bathrooms and turning towards the shower and the toilet. The full list is in `Study/protocol/01-run-sheet.md`.
- **Commands:** the primary moves are read straight from the words, without the LLM, so they act at once (`Assets/ROVR/QuickCommands.cs`): "move forward", "take two steps forward", "step left" (a sidestep, you don't turn), "turn right 90 degrees", "turn around"; a bare "left" or "right" turns, and so does a bare angle ("10 degrees to the left"). Forward is the way you face when the command starts. Everything else goes to the local LLM (about 0.5 s). Each sentence is its own instruction: a target, distance or angle you didn't say again isn't taken from the last command. A new command takes over from a running one at once (it eases out, then the new one runs; the old one isn't resumed). "Go to the table" heads straight there, stops 0.5 m short and ends facing it; if something stops it on the way, it goes round ("Going round the armchair", `Assets/ROVR/RoutePlanner.cs`), and only stops ("Something is in the way") when there's no way round. "Go through the door" walks through the doorway to just past it, facing on. In the House, "get me out of this room" heads through the nearest doorway.
- **Halt words** (`stop`, `wait`, and Filipino `ops`) and **`go back`** are fixed phrases. Halt words stop the player before the sentence is finished, without the LLM, and end the sentence there: what's said next is a new command ("stop... turn left" turns left). "Stop moving" or "wait a bit" just stop.
- **Expressions and side talk aren't commands:** a sentence that's only "okay", "alright", "yeah", "cool", "got it", "uh" and the like is ignored, and so is talk with no command in it ("I guess I'm ready", "was that 15 degrees?", "see you later": `Assets/ROVR/SideTalk.cs`), unless ROVR has just asked something. Requests phrased as questions still work ("could you walk to the door?"). Ignored sentences are logged.
- **Memory:** ROVR keeps the last 3 exchanges (what was said, what it did or asked, how it ended), each for 60 s, and gives them to the LLM. A target, distance or angle is reused only when the user points back to it ("again", "the same", "do that twice", "back to the rock"); "turn around more" turns a little further the same way.
- **Fallback:** untick **Use LLM** on the panel's component in the Inspector for the four fixed phrases only (`move forward/backward/left/right`).
- **Head bob** for step commands: subtle, set with **Step Bob** in the Inspector (subtle / strong / off).

| Part | Where |
|---|---|
| Control panel (voice in, replies out) | `Assets/GUI/VRControlPanel.cs` |
| LLM pipeline | `Assets/ROVR/` |
| World generators | `Assets/Editor/` (copies in `Unity Environments/`) |

**Done:**
- the three worlds (solid collision, hedge maze, doors in the House);
- voice + LLM navigation (tested headless with the real model, and on a Quest 3S with its microphone);
- the controller (Manual) condition;
- study sessions: the ID menu, the six world sequences, objectives back to back, a CSV per world run, and the control page on the PC. Compiled and the logic tested outside Unity; the flow check in Unity (**Tools > ROVR > Check Study Flow**) still has to be run, and it hasn't been tried on the headset.
- the changes from Miguel's test session (quick commands, sidesteps, interrupting, easing, gravity, cancel vs mute, minimaps, hands, pointer, the simpler maze, "get me out of this room"). Checked headless in Unity by **Tools > ROVR > Check Movement** (`Assets/Editor/MovementCheck.cs`) and **Check Study Flow**, both passing; not yet tried on the headset, and comfort still needs testing with people.

**Not done:**
- the practice area is a **placeholder** for now (see step 4), and the objectives haven't been piloted yet;
- the questionnaires as online forms (drafts in `Study/instruments/`).

## Requirements

- **Unity 6** (the project is on 6000.6.2f1), 3D project, built-in render pipeline (the maze shader is not URP/HDRP)
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB), for voice navigation. Without it, only the fixed phrases work (untick **Use LLM**). Tested on Windows 11 with an RTX 5070.
- The Whisper model `Assets/StreamingAssets/ggml-base.bin` is stored with [Git LFS](https://git-lfs.com): run `git lfs pull` if it is a 1 KB text file.

## Setup

**1. Worlds**

If you cloned this repo, the worlds are already in `Assets/main.unity`: open it and press Play. Nothing generates on Play, because the generators only run from the **Tools > ROVR** menu.

To set the worlds up in a different project:

1. Create `Assets/Editor/` and copy the seven `.cs` files from `Unity Environments/` into it.
2. Copy the maze look: `Assets/Shaders/ROVRWorldSpaceTiled.shader` and the two textures in `Assets/Resources/Props/Maze/`. Without them the maze falls back to flat colours.
3. Open a scene and run **Tools > ROVR > Generate All Worlds**. If the scene has a `VRControlPanel`, this also points its Plain / Maze / House / Practice teleport destinations at each world's new `Start`.
4. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`

Regenerating deletes and rebuilds each world, so anything you hand-placed inside `House`, `Plain` or `Maze` is lost. Save the scene afterwards.

**2. Voice navigation**

1. Pull the model (once):
   ```bash
   ollama pull gemma3n:e4b
   ```
2. Make sure Ollama is running and give it a moment: the first load after it starts can take about 30 s.
3. Open `Assets/main.unity` and press Play. The panel adds the LLM pipeline to `PlayerBody` by itself. The tutorial card comes up first: press **F1** to close it.
4. Press **F1** again to unmute, and speak. The caption strip shows what it heard and what it is doing (green = moving, yellow = a question or a problem, red = an error). Hold **F1** to show the panel.

**Sample commands**

| Say this | What should happen |
|---|---|
| `move forward 5 meters` | Moves 5 m forward |
| `move forward until you hit a wall` | Stops 0.5 m short of the wall |
| `move forward a bit`, then `a bit more` | Moves 0.5 m, then slightly further (the "bit" adapts to you) |
| `take 3 steps forward`, `step back` | Walks 3 steps (a step starts at 0.7 m), or 1 step back, at about 2 steps a second; the view dips slightly with each step |
| `one more step` (right after steps) | One more step, and steps get 15% longer; `step back` right after makes them 15% shorter |
| `take two steps to the left` | Turns left, then takes 2 steps |
| `take 4 steps toward the tree`, `walk in steps to the door`, `take steps until you see the door` | Steps toward an object (stopping early at it), all the way to it, or until it's in view |
| `go to the door` | Moves toward the door and stops 0.5 m short. Asks which one if several are in different directions |
| `walk to the couch` (House) | Everything on screen has a name, and everyday words work: goes to the sofa |
| `go to the plant` (House start) | Several plants: asks which, and they glow. Look at one and say `that one`, or say `the left one` / `the one next to the stool` |
| `go to the left plant`, `go to that tree` | Picks one straight away (`that` = the one you're looking at) |
| `move forward 10 meters, then turn right` | Steps run in order |
| `face to the right when you reach the tree` | Goes to the tree, then turns right |
| `move forward and turn left when you see the door` | Walks until the door comes into view, then turns |
| `turn right three times` | Repeats the turn |
| `turn left a little`, `turn 45 degrees to the right` | Smooth turns of any angle, with the edges of the view darkened while turning |
| `face the door` | Turns to face it |
| `faster`, `slow down`, `normal speed` | Changes walking speed at once (1 / 2 / 3 m/s), even mid-move |
| `slowly walk to the tree` | Slow for this command only |
| `turn around until you see the tree` | Turns in 90° steps until the tree is in view (Plain) |
| `wait, I mean left` (while moving) | Stops instantly, then moves left |
| `go back`, `take me back`, `undo that` | Turns around, walks the last command back, then turns to face the way you were facing. After a chain (`move forward 3 meters, then turn right`) it undoes the whole chain in reverse; after a turn alone, it just turns back. Saying it again undoes the undo. `go back 2 meters` and `step back` are ordinary moves |
| `stop`, or `ops` | Stops immediately |

If it can't do what you asked, it says why on the panel.

**3. On the headset (Meta Quest)**

The headset runs the app. The PC runs Ollama and the speech to text. They talk over the USB cable through the ROVR bridge (`Bridge/rovr_bridge.py`).

1. Speech to text on the PC (once), none of it in git. On a PC with an NVIDIA GPU, run `Bridge/setup_whisper_gpu.bat`: it downloads both files below and warms Whisper up, so the GPU code (compiled by the driver the first time on RTX 50-series cards) is ready before a participant's first sentence; run it again after a driver update. By hand: from the [whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases), unzip into `Bridge/whisper/` the CUDA build `whisper-bin-win-cuda-12.4.0-x64.zip` (about 650 MB; it runs on RTX 50-series cards too) for an NVIDIA GPU, or `whisper-bin-x64.zip` (about 20 MB) for the CPU. Put `ggml-small.en.bin` (about 470 MB, from [ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp)) in `Bridge/whisper/` too; without it the bridge uses the headset's base model.
2. Plug in the headset and run `Bridge/run_bridge.bat`, or use **Tools > ROVR > Start Bridge**. It links the headset over the cable. It should say `Speech to text: whisper.cpp on this PC, model ggml-small.en.bin, on the GPU (...)`, and the control page says the same; on the CPU it says so in yellow. Leave the window open.
3. Build and run on the headset. In the bridge window each sentence shows as `STT ... [0.4 s on this PC]`, then the command and the model's reply.

Without step 1 the bridge still works, but the headset transcribes by itself, at about 15 s a sentence. The panel's **Transcribe On PC** setting turns this off.

**Without the cable (Wi-Fi)**

1. Put the headset on the same network as the PC. Use your own Wi-Fi or a phone hotspot, not a shared network: anyone on the network could reach the bridge.
2. Run `Bridge/run_bridge_wifi.bat`, or use **Tools > ROVR > Start Bridge (Wi-Fi)**. It should say `Wi-Fi: on`.
3. Start ROVR on the headset. It finds the PC by itself. The panel says `Connected to the PC over Wi-Fi`, and the bridge window shows `WI-FI found by <the headset's address>`.

The cable still works with the Wi-Fi bridge, and the app moves between the two on its own. Installing a new build still needs the cable. On battery a Quest 3S lasts about 2 hours.

**4. Running a study session (the ID menu and the control page)**

The participant types their ID in the headset; the researchers follow the session on the PC. The full procedure, with what to say, is in [`Study/protocol/01-run-sheet.md`](Study/protocol/01-run-sheet.md).

1. Start the bridge (step 3). It prints `Session control: http://127.0.0.1:8765/control`. Open that page in a browser on the PC. Only this PC can open it, not other devices on the network.
2. Start ROVR on the headset. It shows the **ID menu**, and the page shows the headset as connected.
3. The participant types their ID on the number pad with the controller's pointer (p007 = 0, 0, 7, then OK and Yes). Their ID gives their condition and the order of the three worlds:

   | Sequence | Order |
   |---|---|
   | seq1 | plains → maze → house |
   | seq2 | plains → house → maze |
   | seq3 | maze → plains → house |
   | seq4 | maze → house → plains |
   | seq5 | house → maze → plains |
   | seq6 | house → plains → maze |

   IDs p001–p030 are in a fixed table: 15 per condition, each sequence 5 times, balanced in blocks of 12 (`Assets/GUI/StudyAssignments.cs`; `Study/assignment-table.csv` is the same table). Pilot IDs **p901–p904** aren't: the page then shows a pilot box to pick the condition and sequence, so one pilot participant can try both conditions on two days. Any other ID gets "ID not found". The headset moves them to the practice area, in their condition, with its tutorial card. Nothing carries over from the previous participant.
4. Press **Next** (or **F5** on the page) to load the first world in their sequence. Its objectives appear at the top of the participant's view, one after another: when one is reached (a landmark, a checkpoint, a piece of furniture, a room, or turning towards something) the next appears straight away. **The world's clock starts at the participant's first action there**: their first stick push (Manual), or when they start speaking (ROVR; pressing A to unmute doesn't count). Nothing before that is logged. **Skip objective** moves on; **Restart world** starts the world again as a new run. There are no time limits.
5. After the last objective the world's CSV is saved and sent to the PC, and the headset asks the participant to take it off for the questionnaires (movement and the mic are off meanwhile). Press **Next** when they're back for the next world. After the third world the headset says **All done**. **Reset** goes back to the ID menu for the next participant.
6. **The world logs:** one CSV per world run, named `<id>-<rovr|trad>-<plains|maze|house>.csv`, with `-run2`, `-run3` for repeats, so nothing is overwritten. They're saved on the headset (`Android/data/com.hxil.ROVR/files/StudyData/`, or `StudyData/` in the project in the Editor) and in `Bridge/logs/study/` on the PC. The headset sends each one when its world ends and keeps trying until the bridge confirms it, including after a restart of the app. Columns:

   `participant_id, condition, sequence_id, sequence, world, world_position, run, t_s, clock_time, event, objective, input, system, result, push, x_m, z_m, heading_deg, body_heading_deg`

   `t_s` is seconds since the first action. Events: `objective_start` / `objective_done` / `objective_skipped`; `position` (once a second); `world_end` (`completed`, or `incomplete:` and why). ROVR: `speech_start`, `heard` (input = the words), `command` (system = what it will do), `question`, `notice`, `error`, `halt`, `speed`, `move_end` (result = completed / halted / blocked / not found), `mic`, `ignored` (input = side talk, not sent to the LLM). Both: `jump`, `minimap` (input = shown / grabbed / let go; system = the world, hand or controller, or where it was put), `pointer` (input = the object a controller's ray came to rest on). Manual: `move` (input = forward, forward-right, right, back-right, back, back-left, left, forward-left or stopped; push = how far the stick is pushed, 0–1) and `turn` (left / right / stopped). `x_m` / `z_m` are metres from the world's start point; `heading_deg` is where the head faces, 0 for north, 90 for east; `body_heading_deg` is where the body faces (it turns with ROVR's and the stick's turns, not when the head looks round), so turns can be told from looking.

The same commands can be typed in the bridge window: `id p007`, `assign rovr 3` or `assign trad 3` (pilot IDs), `next`, `skip`, `restart`, `reset`.

In the Editor the scene doesn't show the ID menu: it plays at once, in the condition set in the Inspector, and the world buttons work. Set a participant with `id p007` on the page or in the bridge window; **F5 / F6 / F7** are Next, Skip and Restart. Tick **Wait In Editor** on the panel's `StudySession` component to start with the ID menu, as on the headset (number keys and Enter type into it).

**Checking the session flow:** **Tools > ROVR > Check Study Flow** plays `main.unity` and walks a traditional participant through all three worlds (reaching every objective, with a restart and a skipped objective), a pilot, an unknown ID and a ROVR world, checking every CSV. Test files go to `Temp/StudyFlowCheck/`, never to the real data folders.

> **Practice:** in ROVR, a checklist at the top of the view lists what to try in the practice area (turn left, turn right 45 degrees, two steps forward, step left, go to the tree, stop while moving, faster, two at once) and ticks each off as it's done (`Assets/GUI/PracticeChecklist.cs`; each tick is in the bridge log). It gates nothing: press Next when they're ready. The practice area itself (**Tools > ROVR > Generate Practice Area**) is still a placeholder, marked `PLACEHOLDER`. The objectives are set in `Assets/GUI/StudyTasks.cs` (`Plain()`, `Maze()`, `House()`).

## Troubleshooting

- **No `Tools > ROVR` menu:** the environment scripts must be in a folder named exactly `Editor`.
- **"Can't reach the language model":** check that Ollama is running and `ollama list` shows the model.
- **"The language model is taking too long":** Ollama was still loading the model. Say it again after a few seconds.
- **Nothing happens when you speak:** press Unmute first; the mic starts muted.
- **"Can't find the PC" on the headset:** start the bridge with Wi-Fi (`Bridge/run_bridge_wifi.bat`), and check that the headset and the PC are on the same network. Windows Firewall must allow Python; it asks the first time.
- **On the headset, about 15 s pass before anything happens:** it is transcribing by itself. Check that the bridge window says `Speech to text: whisper.cpp on this PC`, and that the headset is linked (run `Bridge/run_bridge.bat` again after replugging it).
- **Walks through walls:** the player must move via `CharacterController.Move`, not by setting its position.
