# ROVR

LLM-driven voice navigation in VR. A thesis prototype (De La Salle University) comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | The LLM pipeline that turns a command into avatar movement |

## Status

Done:
- The three worlds (solid collision), already generated and saved in `Assets/main.unity`. The maze is a hedge maze.
- The language-to-movement pipeline in `Unity Navigation/`, tested with a real model at about 0.6 s per command.
- Speech-to-text with [whisper.unity](https://github.com/Macoron/whisper.unity) (`Assets/STT/`), and a VR control panel (`Assets/GUI/`) with Plain / Maze / House teleport buttons, a mic mute button, subtitles and feedback text. Movement in the scene is by voice only; the panel understands `move forward`, `move backward`, `move left` and `move right`.

Not done: wiring the LLM pipeline from `Unity Navigation/` into `Assets/main.unity` (the scripts are not in the Unity project yet, so the panel uses its own fixed commands), the joystick comparison arm, task timing and logging, questionnaires. Ollama can't take audio for this model, so speech goes through Whisper first.

## Requirements

- **Unity 6** (the project is on 6000.6.2f1), 3D project, built-in render pipeline (the maze shader is not URP/HDRP)
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB). Tested on Windows 11 with an RTX 5070.

## Setup

**1. Worlds**

If you cloned this repo, the worlds are already in `Assets/main.unity`: open it and press Play. Nothing generates on Play, because the generators only run from the **Tools > ROVR** menu.

To set the worlds up in a different project:

1. Create `Assets/Editor/` and copy the six `.cs` files from `Unity Environments/` into it.
2. Copy the maze look: `Assets/Shaders/ROVRWorldSpaceTiled.shader` and the two textures in `Assets/Resources/Props/Maze/`. Without them the maze falls back to flat colours.
3. Open a scene and run **Tools > ROVR > Generate All Three Worlds**. If the scene has a `VRControlPanel`, this also points its Plain / Maze / House teleport destinations at each world's new `Start`.
4. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`

Regenerating deletes and rebuilds each world, so anything you hand-placed inside `House`, `Plain` or `Maze` is lost. Save the scene afterwards.

**2. Navigation**

1. Pull the model:
   ```bash
   ollama pull gemma3n:e4b
   ```
2. Create `Assets/Scripts/ROVR/` (not an `Editor` folder) and copy the `.cs` files from `Unity Navigation/` into it.
3. Make a player: a GameObject with a `CharacterController` and a child camera. Add `FOVMetadataGrounding`, `NavigationController`, `OllamaClient`, `SemanticIntentResolver` and `ROVRDebugConsole` to it, and set each `pov` field to the camera.
4. Put the player on a world's `Start` object, enter Play mode, type a command in the on-screen box and press Send.

**Sample commands**

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
