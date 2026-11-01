// Assets/GUI/UIOnTop.cs
// Draws a world-space UI over the world, so standing close to a wall or a hedge never hides the panel,
// the caption strip or the tutorial card (as MazeMinimap does for the map). The setting is made at run
// time: the UI shader doesn't save it in a material file.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class UIOnTop : MonoBehaviour
{
    private void Awake()
    {
        var onTop = new Material(Graphic.defaultGraphicMaterial) { name = "UI on top" };
        onTop.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        onTop.renderQueue = (int)RenderQueue.Overlay; // after see-through things too (glass, fridge handles)
        foreach (var graphic in GetComponentsInChildren<Graphic>(true)) graphic.material = onTop;
    }
}
