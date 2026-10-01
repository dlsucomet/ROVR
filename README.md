# ROVR

LLM-driven voice navigation in VR. A thesis prototype (De La Salle University) comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | The LLM pipeline that turns a command into avatar movement |

## Status

There are two separate movement systems. They are **not connected yet**.

| | What it is | Where | In `Assets/main.unity`? |
|---|---|---|---|
| **Scene voice control** | Speech-to-text ([whisper.unity](https://github.com/Macoron/whisper.unity)) plus a VR control panel: Plain / Maze / House teleport buttons, mic mute, subtitles, feedback text. It understands only four fixed phrases: `move forward`, `move backward`, `move left`, `move right`. No LLM, no Ollama. | `Assets/STT/`, `Assets/GUI/` | **Yes** |
| **LLM navigation pipeline** | Turns free-form commands ("go to the door") into movement through a local model, tested at about 0.6 s per command. Typed input only. | `Unity Navigation/` | **No.** The scripts are not in the Unity project |

Done: the three worlds (solid collision), already generated and saved in `Assets/main.unity` with a hedge maze; the scene voice control; the LLM pipeline (standalone).

Not done: connecting the LLM pipeline to the scene's voice input, the joystick comparison arm, task timing and logging, questionnaires. Ollama can't take audio for this model, so speech has to go through Whisper first.

## Requirements

- **Unity 6** (the project is on 6000.6.2f1), 3D project, built-in render pipeline (the maze shader is not URP/HDRP)
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB), **only for the LLM navigation pipeline** (section 2). The scene voice control does not need it. Tested on Windows 11 with an RTX 5070.

## Setup

**1. Worlds**

If you cloned this repo, the worlds are already in `Assets/main.unity`: open it and press Play. Nothing generates on Play, because the generators only run from the **Tools > ROVR** menu.

To set the worlds up in a different project:

1. Create `Assets/Editor/` and copy the six `.cs` files from `Unity Environments/` into it.
2. Copy the maze look: `Assets/Shaders/ROVRWorldSpaceTiled.shader` and the two textures in `Assets/Resources/Props/Maze/`. Without them the maze falls back to flat colours.
3. Open a scene and run **Tools > ROVR > Generate All Three Worlds**. If the scene has a `VRControlPanel`, this also points its Plain / Maze / House teleport destinations at each world's new `Start`.
4. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`

Regenerating deletes and rebuilds each world, so anything you hand-placed inside `House`, `Plain` or `Maze` is lost. Save the scene afterwards.

**2. LLM navigation (optional, not part of `main.unity`)**

This sets up the pipeline in `Unity Navigation/` on its own player. The commands below are typed into its on-screen box. They do not work with the voice panel in `main.unity`, which only understands `move forward/backward/left/right`.

1. Pull the model:
   ```bash
   ollama pull gemma3n:e4b
   ```
2. Create `Assets/Scripts/ROVR/` (not an `Editor` folder) and copy the `.cs` files from `Unity Navigation/` into it.
3. Make a player: a GameObject with a `CharacterController` and a child camera. Add `FOVMetadataGrounding`, `NavigationController`, `OllamaClient`, `SemanticIntentResolver` and `ROVRDebugConsole` to it, and set each `pov` field to the camera.
4. Put the player on a world's `Start` object, enter Play mode, type a command in the on-screen box and press Send.

**Sample commands (LLM navigation only)**

| Type this | What should happen |
|---|---|
| `move forward 5 meters` | Moves 5 m forward |
| `move forward until you hit a wall` | Stops 0.5 m short of the wall |
| `move forward a bit`, then `a bit more` | Moves 0.5 m, then slightly further (the "bit" adapts to you) |
| `go to the door` | Moves toward the door and stops 0.5 m short. Asks which one if several are in different directions |
| `turn around until you see the tree` | Turns in 90° steps until the tree is in view (Plain) |
| `wait, I mean left` (while moving) | Stops instantly, then moves left |
| `stop` | Stops immediately |

If it can't do what you asked, it says why in the on-screen box.

## Troubleshooting

- **No `Tools > ROVR` menu:** the environment scripts must be in a folder named exactly `Editor`.
- **"Ollama request failed":** check that Ollama is running and `ollama list` shows the model.
- **Walks through walls:** the player must move via `CharacterController.Move`, not by setting its position.
