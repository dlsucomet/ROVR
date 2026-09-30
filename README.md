# ROVR

Voice navigation in VR. A thesis prototype (De La Salle University) comparing joystick movement against spoken commands in three environments: a **plain**, a **maze** and a **house**.

This repo is a complete Unity project. Open the repo folder itself in Unity Hub.

| Path | What it is |
|---|---|
| `Assets/main.unity` | The scene: the three worlds, the XR rig and the floating voice panel |
| `Assets/GUI/VRControlPanel.cs` | Speech-to-text (Whisper), voice commands, teleport buttons and movement |
| `Assets/Editor/` | Editor tools that generate the worlds and check collisions |
| [`Unity Environments/`](Unity%20Environments/README.md) | Copies of the world generators, with their docs |
| [`Unity Navigation/`](Unity%20Navigation/README.md) | The LLM pipeline that turns free-form commands into movement (not wired into the scene yet) |

## Status

Done: the three worlds with Kenney furniture (collision checked), an XR rig with controller pointers, and continuous speech-to-text with a local Whisper model, tested in the Unity editor with a microphone.

Not done: a build on the Quest 2 hasn't been tested yet. Voice commands are four fixed phrases; the LLM pipeline in `Unity Navigation/` isn't connected to voice. Also missing: the joystick comparison arm, task timing and logging, questionnaires.

## Requirements

- **Unity 6000.6.2f1**, installed through Unity Hub, signed in with a (free Personal) license. To build for the Quest, also install **Android Build Support** with OpenJDK and the Android SDK & NDK.
- **Git** and **[Git LFS](https://git-lfs.com)**. The Whisper model is stored with LFS; without it you get a tiny placeholder file and speech recognition fails.
- Internet on the first open: Unity downloads the Whisper package from GitHub.
- Optional: a **Meta Quest 2** in developer mode.

## Setup

**1. Get the code**

Run this once per computer, before cloning:
```bash
git lfs install
```
Then clone the branch:
```bash
git clone --branch gui-stt-house-meshes https://github.com/dlsucomet/ROVR.git
```
Check the Whisper model downloaded. It should be about 141 MB, not 134 bytes:
```bash
ls -lh ROVR/Assets/StreamingAssets/ggml-base.bin
```
If it's tiny, run `git lfs pull` inside the `ROVR` folder.

**2. Open it in Unity**

1. Unity Hub → **Add** → select the `ROVR` folder → open with 6000.6.2f1. The first open takes several minutes.
2. Open `Assets/main.unity` from the Project window.

The worlds are already saved in the scene. Don't run **Tools > ROVR > Generate All Three Worlds** unless you mean to rebuild them (see below).

**3. Test in the editor (no headset needed)**

1. Press **Play**.
2. Click **Unmute** (the mic button on the left of the panel) to start listening, and allow microphone access if your computer asks. It reads **Mute** while listening.
3. Speak a command (below). The panel shows what it heard: green if it understood, red "I don't know that." if not.
4. The **Plain**, **Maze** and **House** buttons teleport you to that world.

**4. Build to the Quest 2 (untested)**

1. Put the Quest in developer mode, connect it by USB and allow USB debugging in the headset.
2. **File > Build Profiles > Android > Switch Platform.** XR is already set up (OpenXR with Meta Quest support and Touch controllers).
3. Pick the Quest under **Run Device**, then **Build And Run**.
4. In the headset, allow the microphone permission on first launch. Point a controller at **Unmute** and pull the trigger.

## Voice commands

Movement is relative to where you're looking. Each command moves you about 2 m.

| Say this | What happens |
|---|---|
| `move forward` | Moves forward |
| `move backward` | Moves backward |
| `move left` | Moves left |
| `move right` | Moves right |

Listening stays on until you click **Mute**. Silence is skipped, so you can pause between commands.

## Rebuilding the worlds

**Tools > ROVR > Generate All Three Worlds** rebuilds House, Plain and Maze in the open scene from the generator scripts. It also deletes the `Spawn` objects the teleport buttons use. After regenerating:

1. Add an empty `Spawn` object where players should appear: one under `Plain`, one under `Maze/Start` and one under `House`.
2. Select `MainCanvas` and assign them to the three destination fields on `VRControlPanel`.
3. Run **Tools > ROVR > Check Collisions**. It should report `All worlds solid.`
4. Save the scene and commit it together with any new `.meta` files.

## Troubleshooting

- **"Failed to load Whisper model" or "bad magic" in the Console:** the model is an LFS placeholder. Run `git lfs pull`.
- **Mic doesn't hear you:** check your system's microphone permission for Unity (macOS: System Settings > Privacy & Security > Microphone; Windows: Settings > Privacy > Microphone). On the Quest, allow the permission when asked.
- **OpenXR "runtime unavailable" errors in the editor:** normal when no headset is connected.
- **Teleport buttons do nothing:** the spawn points are missing, usually after regenerating the worlds. See "Rebuilding the worlds".
- **Git says untracked files "would be overwritten" when switching to this branch:** they're `.meta` files Unity generated in your copy. Delete the files it lists and switch again.
- **Walks through walls:** movement code must go through `CharacterController.Move`, not set the position directly.
