# ROVR

LLM-driven voice navigation in VR. A thesis prototype comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | Docs for the LLM pipeline that turns a spoken command into avatar movement (scripts in `Assets/ROVR/`) |

## Status

Voice navigation runs through the LLM in `Assets/main.unity`:

- **Speech:** Whisper transcribes English speech. A sentence ends after **1.5 s of silence**, so pausing mid-sentence is fine. On the headset, speech is transcribed on the PC through the bridge (about 0.4 s a sentence; the Quest itself takes about 15 s). In the Editor, [whisper.unity](https://github.com/Macoron/whisper.unity) can also do it.
- **Panel:** a dark glass panel with the subtitles and reply, the mic icon (a mic, slashed when muted), the Plain / Maze / House buttons and a **?** button that shows the tutorial card again. It starts hidden and follows your gaze lazily. While it's hidden, a caption strip low in the view still shows what was heard and the reply, with the mic icon. **The mic starts muted.**
- **A button (right controller):** a short press mutes or unmutes the mic, and closes the tutorial card. Holding it for 0.6 s shows or hides the panel. In the Editor, **F1** is the A button.
- **Two conditions:** **ROVR** (voice only) and **Manual** ("traditional", `trad` in the logs: controllers only. The left stick moves, faster the further it's pushed, up to 2 m/s; the right stick turns smoothly, with the edges of the view darkened; voice is off). In a study session the participant's ID sets it (step 4 below); otherwise the panel's **Condition** field in the Inspector.
- **Study sessions:** the headset opens on an ID menu. The participant's ID (p001–p030) gives their condition and one of six orders of the three worlds, from a fixed balanced table (`Assets/GUI/StudyAssignments.cs`, copy in `Study/assignment-table.csv`). In each world the objectives appear one after another at the top of the view; the clock starts at the participant's first action there. Each world run is saved as its own time-stamped CSV, for example `p007-rovr-house.csv`.
- **Maze minimap:** in the corner of the view while in the Maze: the whole maze, you, and the exit.
- **Commands:** each sentence goes to the local LLM, which moves the player. A command takes about 0.5 s.
- **Halt words** (`stop`, `wait`, and Filipino `ops`) and **`go back`** are fixed phrases. Halt words stop the player before the sentence is finished, without the LLM.
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

**Not done:**
- the real objectives and practice area: both are **placeholders** for now (see step 4);
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

1. Speech to text on the PC (once): download `whisper-bin-x64.zip` (about 20 MB) from the [whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases) and unzip it into `Bridge/whisper/`. It isn't in git.
2. Plug in the headset and run `Bridge/run_bridge.bat`, or use **Tools > ROVR > Start Bridge**. It links the headset over the cable. It should say `Speech to text: whisper.cpp on this PC`. Leave the window open.
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
4. Press **Next** (or **F5** on the page) to load the first world in their sequence. Its objectives appear at the top of the participant's view, one after another: when one is reached (a yellow pad, a room, the maze exit) the next appears straight away. **The world's clock starts at the participant's first action there**: their first stick push (Manual), or when they start speaking (ROVR; pressing A to unmute doesn't count). Nothing before that is logged. **Skip objective** moves on; **Restart world** starts the world again as a new run. There are no time limits.
5. After the last objective the world's CSV is saved and sent to the PC, and the headset asks the participant to take it off for the questionnaires (movement and the mic are off meanwhile). Press **Next** when they're back for the next world. After the third world the headset says **All done**. **Reset** goes back to the ID menu for the next participant.
6. **The world logs:** one CSV per world run, named `<id>-<rovr|trad>-<plains|maze|house>.csv`, with `-run2`, `-run3` for repeats, so nothing is overwritten. They're saved on the headset (`Android/data/com.hxil.ROVR/files/StudyData/`, or `StudyData/` in the project in the Editor) and in `Bridge/logs/study/` on the PC. The headset sends each one when its world ends and keeps trying until the bridge confirms it, including after a restart of the app. Columns:

   `participant_id, condition, sequence_id, sequence, world, world_position, run, t_s, clock_time, event, objective, input, system, result, push, x_m, z_m, heading_deg`

   `t_s` is seconds since the first action. Events: `objective_start` / `objective_done` / `objective_skipped`; `position` (once a second); `world_end` (`completed`, or `incomplete:` and why). ROVR: `speech_start`, `heard` (input = the words), `command` (system = what it will do), `question`, `notice`, `error`, `halt`, `speed`, `move_end` (result = completed / halted / blocked / not found), `mic`. Manual: `move` (input = forward, forward-right, right, back-right, back, back-left, left, forward-left or stopped; push = how far the stick is pushed, 0–1) and `turn` (left / right / stopped). `x_m` / `z_m` are metres from the world's start point; `heading_deg` is 0 for north, 90 for east.

The same commands can be typed in the bridge window: `id p007`, `assign rovr 3` or `assign trad 3` (pilot IDs), `next`, `skip`, `restart`, `reset`.

In the Editor the scene doesn't show the ID menu: it plays at once, in the condition set in the Inspector, and the world buttons work. Set a participant with `id p007` on the page or in the bridge window; **F5 / F6 / F7** are Next, Skip and Restart. Tick **Wait In Editor** on the panel's `StudySession` component to start with the ID menu, as on the headset (number keys and Enter type into it).

**Checking the session flow:** **Tools > ROVR > Check Study Flow** plays `main.unity` and walks a traditional participant through all three worlds (with a restart and skipped objectives), a pilot, an unknown ID and a ROVR world, checking every CSV. Test files go to `Temp/StudyFlowCheck/`, never to the real data folders.

> **Placeholders:** the objectives (`Assets/GUI/StudyTasks.cs`) and the practice area (**Tools > ROVR > Generate Practice Area**) are placeholders, marked `PLACEHOLDER`. Replace them with the real study objectives before user testing.

## Troubleshooting

- **No `Tools > ROVR` menu:** the environment scripts must be in a folder named exactly `Editor`.
- **"Can't reach the language model":** check that Ollama is running and `ollama list` shows the model.
- **"The language model is taking too long":** Ollama was still loading the model. Say it again after a few seconds.
- **Nothing happens when you speak:** press Unmute first; the mic starts muted.
- **"Can't find the PC" on the headset:** start the bridge with Wi-Fi (`Bridge/run_bridge_wifi.bat`), and check that the headset and the PC are on the same network. Windows Firewall must allow Python; it asks the first time.
- **On the headset, about 15 s pass before anything happens:** it is transcribing by itself. Check that the bridge window says `Speech to text: whisper.cpp on this PC`, and that the headset is linked (run `Bridge/run_bridge.bat` again after replugging it).
- **Walks through walls:** the player must move via `CharacterController.Move`, not by setting its position.
