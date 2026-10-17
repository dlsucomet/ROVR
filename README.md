# ROVR

LLM-driven voice navigation in VR. A thesis prototype comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | Docs for the LLM pipeline that turns a spoken command into avatar movement (scripts in `Assets/ROVR/`) |

## Status

Voice navigation runs through the LLM in `Assets/main.unity`:

- **Speech:** Whisper transcribes English speech. A sentence ends after **1.5 s of silence**, so pausing mid-sentence is fine. On the headset, speech is transcribed on the PC through the bridge (about 0.4 s a sentence; the Quest itself takes about 15 s). In the Editor, [whisper.unity](https://github.com/Macoron/whisper.unity) can also do it.
- **Panel:** Vert's VR control panel shows the subtitles. It also has the Plain / Maze / House buttons, a mic mute button and Slow / Normal / Fast speed buttons. **The mic starts muted.**
- **Maze minimap:** in the corner of the view while in the Maze: the whole maze, you, and the exit.
- **Commands:** each sentence goes to the local LLM, which moves the player. A command takes about 0.5 s.
- **Halt words** (`stop`, `wait`, and Filipino `ops`) are fixed keywords. They stop the player before the sentence is finished, without the LLM.
- **Fallback:** untick **Use LLM** on the panel for the four fixed phrases only (`move forward/backward/left/right`).

| Part | Where |
|---|---|
| Control panel (voice in, replies out) | `Assets/GUI/VRControlPanel.cs` |
| LLM pipeline | `Assets/ROVR/` |
| World generators | `Assets/Editor/` (copies in `Unity Environments/`) |

**Done:**
- the three worlds (solid collision, hedge maze);
- voice + LLM navigation (tested headless with the real model, and on a Quest 3S with its microphone).

**Not done:**
- the joystick comparison arm;
- task timing and logging;
- questionnaires.

## Requirements

- **Unity 6** (the project is on 6000.6.2f1), 3D project, built-in render pipeline (the maze shader is not URP/HDRP)
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB), for voice navigation. Without it, only the fixed phrases work (untick **Use LLM**). Tested on Windows 11 with an RTX 5070.
- The Whisper model `Assets/StreamingAssets/ggml-base.bin` is stored with [Git LFS](https://git-lfs.com): run `git lfs pull` if it is a 1 KB text file.

## Setup

**1. Worlds**

If you cloned this repo, the worlds are already in `Assets/main.unity`: open it and press Play. Nothing generates on Play, because the generators only run from the **Tools > ROVR** menu.

To set the worlds up in a different project:

1. Create `Assets/Editor/` and copy the six `.cs` files from `Unity Environments/` into it.
2. Copy the maze look: `Assets/Shaders/ROVRWorldSpaceTiled.shader` and the two textures in `Assets/Resources/Props/Maze/`. Without them the maze falls back to flat colours.
3. Open a scene and run **Tools > ROVR > Generate All Three Worlds**. If the scene has a `VRControlPanel`, this also points its Plain / Maze / House teleport destinations at each world's new `Start`.
4. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`

Regenerating deletes and rebuilds each world, so anything you hand-placed inside `House`, `Plain` or `Maze` is lost. Save the scene afterwards.

**2. Voice navigation**

1. Pull the model (once):
   ```bash
   ollama pull gemma3n:e4b
   ```
2. Make sure Ollama is running and give it a moment: the first load after it starts can take about 30 s.
3. Open `Assets/main.unity` and press Play. The panel adds the LLM pipeline to `PlayerBody` by itself.
4. Press **Unmute** on the panel and speak. The panel shows what it heard and what it is doing (green = moving, yellow = a question or a problem, red = an error).

**Sample commands**

| Say this | What should happen |
|---|---|
| `move forward 5 meters` | Moves 5 m forward |
| `move forward until you hit a wall` | Stops 0.5 m short of the wall |
| `move forward a bit`, then `a bit more` | Moves 0.5 m, then slightly further (the "bit" adapts to you) |
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
| `stop`, or `ops` | Stops immediately |

If it can't do what you asked, it says why on the panel.

**3. On the headset (Meta Quest)**

The headset runs the app. The PC runs Ollama and the speech to text. They talk over the USB cable through the ROVR bridge (`Bridge/rovr_bridge.py`).

1. Speech to text on the PC (once): download `whisper-bin-x64.zip` (about 20 MB) from the [whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases) and unzip it into `Bridge/whisper/`. It isn't in git.
2. Plug in the headset and run `Bridge/run_bridge.bat`, or use **Tools > ROVR > Start Bridge**. It links the headset over the cable. It should say `Speech to text: whisper.cpp on this PC`. Leave the window open.
3. Build and run on the headset. In the bridge window each sentence shows as `STT ... [0.4 s on this PC]`, then the command and the model's reply.

Without step 1 the bridge still works, but the headset transcribes by itself, at about 15 s a sentence. The panel's **Transcribe On PC** setting turns this off.

## Troubleshooting

- **No `Tools > ROVR` menu:** the environment scripts must be in a folder named exactly `Editor`.
- **"Can't reach the language model":** check that Ollama is running and `ollama list` shows the model.
- **"The language model is taking too long":** Ollama was still loading the model. Say it again after a few seconds.
- **Nothing happens when you speak:** press Unmute first; the mic starts muted.
- **On the headset, about 15 s pass before anything happens:** it is transcribing by itself. Check that the bridge window says `Speech to text: whisper.cpp on this PC`, and that the headset is linked (run `Bridge/run_bridge.bat` again after replugging it).
- **Walks through walls:** the player must move via `CharacterController.Move`, not by setting its position.
