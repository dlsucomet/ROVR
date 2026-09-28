using UnityEngine;

public class VFFloatingMenu : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform vrCamera; // Drag your Main Camera here
    public float distanceFromCamera = 1.5f; // Distance in meters in front of you
    public float followSpeed = 5f; // How smoothly it catches up to your view

    private void Start()
    {
        if (vrCamera == null && Camera.main != null)
        {
            vrCamera = Camera.main.transform;
        }

        // Instantly snap to position on start
        if (vrCamera != null)
        {
            Vector3 targetPos = vrCamera.position + vrCamera.forward * distanceFromCamera;
            transform.position = targetPos;
            transform.LookAt(transform.position + vrCamera.forward);
        }
    }

    private void Update()
    {
        if (vrCamera == null) return;

        // Calculate target position in front of the camera (flattened to look natural horizontally)
        Vector3 targetPosition = vrCamera.position + vrCamera.forward * distanceFromCamera;

        // Smoothly glide towards the target position
        transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);

        // Make the canvas rotate to always face the camera smoothly
        Quaternion targetRotation = Quaternion.LookRotation(transform.position - vrCamera.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, followSpeed * Time.deltaTime);
    }
}