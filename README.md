# ROVR

LLM-driven voice navigation in VR. A thesis prototype (De La Salle University) comparing joystick movement against spoken commands, interpreted by a local LLM, in three environments: a **plain**, a **maze** and a **house**.

| Folder | What it is |
|---|---|
| [`Unity Environments/`](Unity%20Environments/README.md) | Editor tools that generate the three worlds |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | The LLM pipeline that turns a command into avatar movement |

## Status

Done: the three worlds (solid collision) and the language-to-movement pipeline, tested with a real model at about 0.6 s per command.

Not done: voice input (a typed text box stands in), the joystick comparison arm, VR rig and scenes, task timing and logging, questionnaires. Note that Ollama can't take audio for this model, so voice will need a separate speech-to-text step.

## Requirements

- **Unity 6** (tested on 6000.4.10f1), 3D project
- **[Ollama](https://ollama.com)** and the model `gemma3n:e4b` (about 7.5 GB). Tested on Windows 11 with an RTX 5070.

## Setup

**1. Worlds**

1. Create `Assets/Editor/` in your Unity project and copy the six `.cs` files from `Unity Environments/` into it.
2. Open a scene and run **Tools > ROVR > Generate All Three Worlds**.
3. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`

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
