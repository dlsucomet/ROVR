# ROVR — Environments

Unity Editor tools that build the three test worlds from Thesis §3.5 and §7.1.2: a barrier-free **plain**, a **maze** with no semantic objects, and a furnished **house**. Everything is generated from code, so every participant gets identical worlds. 1 unit = 1 metre.

For the whole-project setup, see the [repository README](../README.md).

## Install

1. In your Unity project, create `Assets/Editor/`. The name matters: these scripts use `UnityEditor` and only compile from a folder called `Editor`.
2. Copy all six `.cs` files from this folder into it:

   | File | Role |
   |---|---|
   | `WorldKit.cs` | Shared helpers (walls with doorways, tags, colours). **Required by the generators.** |
   | `HouseGenerator.cs` | House world |
   | `MazeGenerator.cs` | Maze world |
   | `PlainGenerator.cs` | Plain world |
   | `WorldBuilder.cs` | Builds all three into one scene |
   | `CollisionCheck.cs` | Verifies the worlds are solid |

3. For the hedge maze, also copy `Assets/Shaders/ROVRWorldSpaceTiled.shader` and the two textures in `Assets/Resources/Props/Maze/`. Without them the maze uses flat colours (a warning appears in the Console).
   For the Plain scenery and the House plants and statues, copy `Assets/Resources/Props/Nature/` (Kenney Nature Kit models and license). Without it the Plain falls back to the old primitive tree and the House skips its decor, each with a warning in the Console.
4. Wait for Unity to compile. A **Tools > ROVR** menu appears.

## Menu

| Item | Does |
|---|---|
| Generate All Three Worlds | House at (0,0,0), Plain at (200,0,0), Maze at (0,0,200) |
| Generate House / Plain / Maze | One world at the origin |
| Check Collisions | Tests that nothing can be walked through (see below) |

Re-running a generator **replaces** that world's existing object (`House`, `Plain` or `Maze`) rather than adding a second copy, and Ctrl+Z undoes it. Anything you placed by hand inside that object is deleted with it. Save the scene afterwards.

**Generate Maze / Plain / House on their own build at the origin.** In `Assets/main.unity` the worlds sit at the offsets above, so use **Generate All Three Worlds**, or move the world back to its offset afterwards.

Nothing generates when you press Play. The saved scene is a snapshot, so after changing a generator you must regenerate and save before the change appears.

Every world has an empty `Start` object; place the player there.

**Teleport buttons.** Each generator ends by finding the scene's `VRControlPanel` (if there is one) and setting its `plainDestination`, `mazeDestination` or `houseDestination` to the new world's `Start`. This is needed because regenerating deletes the old world and would otherwise empty the reference. The panel takes its position from `Start` and sets the facing itself (Plain 0°, Maze 90°, House 0°).

## The worlds

Floor plans are in [`rovr-house-floorplan.svg`](rovr-house-floorplan.svg), [`rovr-maze-plan.svg`](rovr-maze-plan.svg) and [`rovr-combined-scene-layout.svg`](rovr-combined-scene-layout.svg).

### House: 24 × 20 m, single storey
- A 6 m-wide hall runs down the middle from the entrance (south wall).
- West side: **living room** and a **bedroom** with an **ensuite bathroom**.
- East side: **kitchen** with a **dining area**, and a **powder room**.
- 47 furnishings define the rooms: sofa, two armchairs, coffee table and TV in the living room; stove, fridge, sink, island with stools and a six-chair dining table in the kitchen; bed, desk and wardrobe in the bedroom; toilets, vanities and a shower; two stools in the hall.
- **Decor** from the Nature Kit: 10 potted plants (4 in the hall, 2 in the living room, 2 in the bedroom, 2 in the dining area), a small plant on a nightstand, a cactus on the living-room side table, and two stone statues (a head on the hall console table, a column on the China cabinet). Items on furniture sit on that prop's measured top.
- **Warm look:** cream walls and ceiling, an oak floor, and dark baseboards along every wall (not over doorways).
- Doorways are 2 m wide with a lintel above. Each has a `Door` marker that doesn't block movement.
- Point lights in every room, and a `Roof` object. **Disable `Roof` to look down into the rooms** in the Scene view.
- `Start` is just inside the entrance, facing north.

A two-storey version (stairs and an upstairs master suite) is in git commit `f547f89`. It was removed because the avatar has no gravity or ground-following, so it couldn't walk up the stairs.

### Maze: 48 × 36 m
- 12 × 9 cells of 4 m, fixed, so every participant walks the same maze. The correct route is 52 cells (about 210 m) with 30 dead ends.
- **Entrance** on the west side (bottom row) and **exit** on the east side (top row). The blue start pad is just outside the entrance, and `Start` faces into the maze.
- A green **Goal** pad sits just outside the exit. Its trigger collider (tag `Goal`) is where task completion should be detected.
- Walls only, no furniture. The floor extends 8 m beyond the maze on every side.
- **To edit the layout:** the maze is the ASCII plan in `MazeGenerator.cs` (`Layout`). `+---+` are horizontal walls, `|` vertical walls, and a gap in the outer wall is an opening. Regenerate after editing. The SVG plan isn't regenerated automatically, so update it by hand if you change the layout.

### Plain: 100 × 100 m
- A flat, barrier-free grass-green field with one **tree** as the only tagged point of reference, 18 m directly ahead of `Start`. It is a Nature Kit tree, 8 m tall.
- The tree is tagged `Tree`, so the LLM can name it. To make it a purely visual landmark, set `TreeTag = null` in `PlainGenerator.cs`.
- **Scenery** fills the field so it doesn't read as an empty slab: 18 rocks, 79 grass tufts, 40 flowers, 6 background trees and 2 ponds. It is scattered with a fixed random seed (`ScenerySeed`), so every participant gets the identical field. The ground within 6 m of `Start`, the strip from `Start` to the tree and the area round the tree are kept clear, and the background trees stand at least 35 m from `Start`, so the tagged tree stays the obvious landmark.
- **Ponds:** two (5 m and 4 m radius): a solid disc of water ringed with stones and lily pads. They are obstacles to walk round, not water to wade through.
- The scenery is untagged and solid. The LLM can't see it, but it still blocks movement and view. To change the amounts, edit the `Scenery` table in `PlainGenerator.cs`; set a count to 0 to remove that kind, or edit `Ponds` for the ponds.
- There's no boundary at the edge of the floor. Walking off it leaves the avatar floating over nothing, which matches "no barriers" in the thesis.

## Tags

The generators create these tags automatically.

| Tag | On | Seen by the LLM by default |
|---|---|---|
| `Wall` | All walls and lintels | Yes |
| `Door` | Doorway markers (triggers) | Yes |
| `Chair` | Chairs, armchair, stools | Yes |
| `Tree` | The Plain's tree | Yes |
| `Furniture` | All other House furnishings | No |
| `Goal` | The Maze's goal trigger | No |

What the LLM can see is set by `groundedTags` on `FOVMetadataGrounding` (in `Unity Navigation/`), not by these scripts. Furniture and Goal are left out so each world's metadata matches the thesis (§7.1.2). Add a tag to that list to expose it.

## Collision

Every wall, prop, floor, roof and the tree is a solid collider, so a `CharacterController` can't pass through them. The only openings are the doorways, the maze entrance and exit, and the plain's open edge. The `Door` and `Goal` triggers and the flat start/goal pads are intentionally non-blocking.

**Tools > ROVR > Check Collisions** verifies this after you generate. It confirms every visible object has a solid collider, then drives a `CharacterController` around each world at random and reports any point where it passes through something. Re-run it after changing any generator.

Collision only holds if the player moves through `CharacterController.Move`. Setting `transform.position` directly bypasses it. `NavigationController` also forces the controller's step offset to 0 so the avatar can't hop onto furniture and hover there.

## Customising the look

Flat-colour materials (the Plain's grass and water, the House's cream, oak and trim) are created once, as assets, in `Assets/Resources/Props/Generated/` so they survive saving the scene. Changing a colour in `WorldKit.cs` does not update an existing asset: delete the asset, or use a new palette key.

**The Plain and the House decor use the [Kenney Nature Kit](https://kenney.nl/assets/nature-kit)** (CC0, credit optional). The models are in `Assets/Resources/Props/Nature/`, with the license. Models are sized by target height in metres, not by FBX scale. A missing model is skipped with a warning.

**The Maze is a hedge maze on a pale gravel floor.** Walls and floor use the `ROVR/WorldSpaceTiled` shader (`Assets/Shaders/`), which tiles a texture by real-world position. That keeps the texture the same size on every wall and stops it stretching along the long wall pieces. The two seamless textures (`MazeHedge.png`, `MazeGround.png`) are in `Assets/Resources/Props/Maze/`. To change how large the texture looks, edit "Metres per texture repeat" on `ROVR_MazeWallTiled` / `ROVR_MazeFloorTiled` in `Generated/`. The maze deliberately has no decoration or landmarks, so keep any replacement look uniform.

**The House uses the [Kenney Furniture Kit](https://kenney.nl/assets/furniture-kit)** (CC0, credit optional). Its 28 models are in `Assets/Resources/Props/` and `HouseGenerator.cs` places them, so the furnishings, walls, ceiling and floor share one low-poly, flat-colour style. The house is about 21,000 triangles of furniture in total.

| Put this in `Assets/Resources/Props/` | To replace |
|---|---|
| `MazeWall.mat` / `MazeFloor.mat` | The Maze's walls / floor only (checked first) |
| `WallMaterial.mat` | Every wall. Also overrides the House's built-in off-white walls and ceiling |
| `FloorMaterial.mat` | Every floor. Also overrides the House's built-in peach floor |
| `<name>.fbx` / `<name>.prefab` | The furnishing that uses that model name in `HouseGenerator.cs` |

Each `new Prop(...)` line in `HouseGenerator.cs` names its model (`"loungeSofa"`, `"bedDouble"`, ...), where it stands, which way it faces (`yaw`: 0 north, 90 east, 180 south, 270 west), and whether it sits against the wall behind it (`back`), repeats to fill a run (`row`) or needs shrinking (`scale`). A missing model falls back to the old coloured box, with a warning in the Console. All models share one scale (`KitScale`), which keeps the kit's real-world proportions. The prop's root carries the `Chair` / `Furniture` tag and one solid box collider.

Two constants at the top of `HouseGenerator.cs` are worth knowing about: `NativeFrontYaw` (set it to `0` if every piece of furniture faces backwards after generating) and `KitScale`.
