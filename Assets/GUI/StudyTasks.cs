// Assets/GUI/StudyTasks.cs
// The tasks of a study session, per world, in order. Each task ends by itself when the participant
// reaches its goal: a spot on the floor (shown as a glowing pad while the task is on), or for the Maze its
// exit. There are no time limits; the operator can skip or restart a task from the control page.
//
// PLACEHOLDERS: apart from the Maze exit, these tasks are stand-ins so the session flow can be tried out.
// Write the real tasks (Study/protocol/01-run-sheet.md) before user testing: either edit Placeholders()
// below, or add this component to the panel in the scene and fill in its list in the Inspector (a
// non-empty list there is used instead).
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class StudyTask
{
    public string world;          // Practice, Plain, Maze or House
    [TextArea] public string instruction;
    public Vector3 goal;          // world position; for "the Maze exit" it's taken from the Maze's Goal
    public float radius = 1.5f;  // reached within this many metres (level distance from the head)
    public bool mazeExit;        // the goal is the Maze's exit pad
}

public class StudyTasks : MonoBehaviour
{
    [Tooltip("Leave empty to use the built-in PLACEHOLDER tasks.")]
    public List<StudyTask> tasks = new List<StudyTask>();

    private GameObject _pad;

    public List<StudyTask> For(string world)
    {
        return tasks.FindAll(t => string.Equals(t.world, world, StringComparison.OrdinalIgnoreCase));
    }

    // Fills in the PLACEHOLDER tasks when the list is empty, around each world's start point.
    public void UsePlaceholdersIfEmpty(VRControlPanel panel)
    {
        if (tasks.Count > 0) return;
        tasks.AddRange(Placeholders(panel));
    }

    private static IEnumerable<StudyTask> Placeholders(VRControlPanel panel)
    {
        if (panel.plainDestination != null)
        {
            Vector3 s = panel.plainDestination.position; // the Plain's start faces north (+z)
            yield return Task("Plain", "PLACEHOLDER task 1: reach the marker ahead", s + new Vector3(0f, 0f, 10f));
            yield return Task("Plain", "PLACEHOLDER task 2: reach the marker on the left", s + new Vector3(-8f, 0f, 14f));
            yield return Task("Plain", "PLACEHOLDER task 3: reach the marker on the right", s + new Vector3(8f, 0f, 14f));
            yield return Task("Plain", "PLACEHOLDER task 4: go back to the start", s);
        }
        if (panel.mazeDestination != null)
            yield return new StudyTask { world = "Maze", instruction = "Find the exit, marked by the pad", mazeExit = true, radius = 2f };
        if (panel.houseDestination != null)
        {
            Vector3 h = panel.houseDestination.parent != null ? panel.houseDestination.parent.position : Vector3.zero; // the House's corner
            yield return Task("House", "PLACEHOLDER room 1: the living room", h + new Vector3(4.5f, 0f, 5f));
            yield return Task("House", "PLACEHOLDER room 2: the bathroom (powder room)", h + new Vector3(17f, 0f, 17.5f));
            yield return Task("House", "PLACEHOLDER room 3: the kitchen", h + new Vector3(19.5f, 0f, 8f));
            yield return Task("House", "PLACEHOLDER room 4: the bedroom", h + new Vector3(5.5f, 0f, 15f));
        }
    }

    private static StudyTask Task(string world, string instruction, Vector3 goal)
    {
        return new StudyTask { world = world, instruction = instruction, goal = goal, radius = 1.5f };
    }

    // Where a task's goal is, resolving "the Maze exit".
    public Vector3 GoalOf(StudyTask task)
    {
        if (task.mazeExit)
        {
            var exit = GameObject.FindGameObjectWithTag("Goal");
            if (exit != null) return exit.transform.position;
        }
        return task.goal;
    }

    // A glowing pad on the floor at the goal of the task that's on (none for the Maze, which has its own).
    public void ShowPad(StudyTask task)
    {
        HidePad();
        if (task == null || task.mazeExit) return;
        _pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _pad.name = "Task Goal (placeholder)";
        Destroy(_pad.GetComponent<Collider>()); // never in the way
        _pad.transform.position = GoalOf(task) + Vector3.up * 0.02f;
        _pad.transform.localScale = new Vector3(task.radius * 2f, 0.02f, task.radius * 2f);
        var renderer = _pad.GetComponent<Renderer>();
        renderer.material.color = new Color(1f, 0.85f, 0.2f); // the primitive's own material: nothing a build could strip
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public void HidePad()
    {
        if (_pad != null) Destroy(_pad);
        _pad = null;
    }
}
