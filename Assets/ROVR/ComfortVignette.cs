// Assets/ROVR/ComfortVignette.cs
// Darkens the edges of the view while the avatar turns (a comfort vignette): seeing the world rotate
// without feeling it is a common cause of motion sickness in VR, and narrowing the view while it
// happens reduces that. Fades in when NavigationController starts a turn and out when it ends.
// Drawn on a quad just in front of the head camera, sized to the camera's field of view each frame,
// so the ring sits at the edges of the screen in the headset and on the desktop alike.
using UnityEngine;

namespace ROVR
{
    public class ComfortVignette : MonoBehaviour
    {
        [SerializeField] NavigationController controller;
        [Tooltip("How dark the edges get while turning (0 = off).")]
        [Range(0f, 1f)] [SerializeField] float strength = 0.85f;
        [Tooltip("Seconds to fade in and out.")]
        [SerializeField] float fadeSeconds = 0.15f;
        [Tooltip("How much of the middle of the view stays clear (0 to 1).")]
        [Range(0f, 0.95f)] [SerializeField] float clearMiddle = 0.55f;

        const float Distance = 0.2f; // in front of the eyes; must be beyond the camera's near clip plane
        // The quad is this much bigger than the view and fully dark past the view's edge: in a headset
        // each eye looks from a little to one side, and an exact fit would leave a clear strip.
        const float Oversize = 1.5f;

        Camera cam;
        Transform quad;
        Material material;
        float current;
        static readonly int StrengthId = Shader.PropertyToID("_Strength");

        void Start()
        {
            if (controller == null) controller = GetComponent<NavigationController>();
            cam = Camera.main;
            var shader = Resources.Load<Shader>("ROVR/Vignette");
            if (cam == null || shader == null)
            {
                Debug.LogWarning("[ROVR] Comfort vignette needs a main camera and Assets/Resources/ROVR/Vignette.shader.");
                enabled = false;
                return;
            }

            material = new Material(shader) { mainTexture = MakeRing() };
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Comfort Vignette";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad = go.transform;
            quad.SetParent(cam.transform, false);
            quad.localPosition = new Vector3(0f, 0f, Mathf.Max(Distance, cam.nearClipPlane * 2f));
            quad.localRotation = Quaternion.identity;
            go.SetActive(false);
        }

        void LateUpdate()
        {
            float target = controller != null && controller.IsTurning ? strength : 0f;
            current = Mathf.MoveTowards(current, target, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            material.SetFloat(StrengthId, current);

            bool show = current > 0.001f;
            if (quad.gameObject.activeSelf != show) quad.gameObject.SetActive(show);
            if (!show) return;

            // Cover exactly the view, so the ring's edge is the screen's edge.
            float d = quad.localPosition.z;
            float height = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            quad.localScale = new Vector3(height * cam.aspect, height, 1f) * Oversize;
        }

        void OnDestroy()
        {
            if (quad != null) Destroy(quad.gameObject);
        }

        // Alpha ring: clear inside clearMiddle of the way to the edge, dark at the edge.
        Texture2D MakeRing()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clearMiddle / Oversize, 1.02f / Oversize, r));
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
