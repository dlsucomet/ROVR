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

3. Wait for Unity to compile. A **Tools > ROVR** menu appears.

## Menu

| Item | Does |
|---|---|
| Generate All Three Worlds | House at (0,0,0), Plain at (200,0,0), Maze at (0,0,200) |
| Generate House / Plain / Maze | One world at the origin |
| Check Collisions | Tests that nothing can be walked through (see below) |

Re-running a generator **replaces** that world's existing object (`House`, `Plain` or `Maze`) rather than adding a second copy, and Ctrl+Z undoes it. Save the scene afterwards.

Every world has an empty `Start` object; place the player there.

## The worlds

Floor plans are in [`rovr-house-floorplan.svg`](rovr-house-floorplan.svg), [`rovr-maze-plan.svg`](rovr-maze-plan.svg) and [`rovr-combined-scene-layout.svg`](rovr-combined-scene-layout.svg).

### House: 24 × 20 m, single storey
- A 6 m-wide hall runs down the middle from the entrance (south wall).
- West side: **living room** and a **bedroom** with an **ensuite bathroom**.
- East side: **kitchen** with a **dining area**, and a **powder room**.
- 42 furnishings define the rooms: sofa, coffee table and TV in the living room; stove, fridge, sink, island with stools and a six-chair dining table in the kitchen; bed, desk and wardrobe in the bedroom; toilets, vanities and a shower.
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
- A flat, barrier-free field with one **tree** as the only point of reference, 18 m directly ahead of `Start`.
- The tree is tagged `Tree`, so the LLM can name it. To make it a purely visual landmark, set `TreeTag = null` in `PlainGenerator.cs`.
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

The worlds are grey blockouts with flat colours. Materials are created once, as assets, in `Assets/Resources/Props/Generated/` so they survive saving the scene. To use real assets, put them in `Assets/Resources/Props/`; anything missing falls back to the blockout.

| Put this in `Assets/Resources/Props/` | To replace |
|---|---|
| `WallMaterial.mat` | Every wall (all three worlds) |
| `FloorMaterial.mat` | Every floor |
| `<Model>.prefab` | The furnishing with that model name |

House model names: `Bed`, `Bookshelf`, `Cabinet`, `Chair`, `CoatRack`, `Counter`, `Desk`, `Dresser`, `Fridge`, `Hood`, `Lamp`, `Nightstand`, `Shelf`, `Shower`, `Sink`, `Sofa`, `Stool`, `Stove`, `TV`, `TVStand`, `Table`, `Toilet`, `Vanity`, `Wardrobe`. Prefabs keep their prefab link, and get a collider added automatically if they don't have one. To change a furnishing's position or size, edit its `new Prop(...)` line in `HouseGenerator.cs`.
