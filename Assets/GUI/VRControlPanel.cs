using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Whisper;
using Whisper.Utils;

public class VRControlPanel : MonoBehaviour
{
    [Header("Whisper References")]
    public WhisperManager whisperManager;
    public MicrophoneRecord microphoneRecord;

    [Header("Movement References & Settings")]
    public Transform playerBody;
    public Transform vrCameraTransform;
    public float moveSpeed = 2f;
    public float moveDuration = 1f;

    [Header("Teleport Destinations")]
    public Transform plainDestination;
    public Transform mazeDestination;
    public Transform houseDestination;

    [Header("UI Elements")]
    public Button plainButton;
    public Button mazeButton;
    public Button houseButton;
    public Button micMuteButton;
    public Text subtitlesText; // Displays whatever speech is registered
    public Text outputText;    // Displays validation feedback (Green/Red)

    private bool _isMicMuted = true; // Default is muted/off until pressed
    private Text micButtonText;

    private WhisperStream _stream;
    private CharacterController _controller;
    private Vector3 _moveDirection = Vector3.zero;
    private float _moveTimer = 0f;
    private string _lastCommand = "";
    private float _commandCooldown = 0f;

    private async void Start()
    {
        if (playerBody == null) playerBody = transform;
        _controller = playerBody.GetComponent<CharacterController>();
        if (vrCameraTransform == null && Camera.main != null) vrCameraTransform = Camera.main.transform;

        // Force initial spawn rotation to plain destination facing 0 degrees Y
        TeleportTo(plainDestination, 0f);

        // Hook up button listeners with specific Y rotation constraints
        if (plainButton != null) plainButton.onClick.AddListener(() => TeleportTo(plainDestination, 0f));
        if (mazeButton != null) mazeButton.onClick.AddListener(() => TeleportTo(mazeDestination, 90f));
        if (houseButton != null) houseButton.onClick.AddListener(() => TeleportTo(houseDestination, 0f));

        if (micMuteButton != null)
        {
            micButtonText = micMuteButton.GetComponentInChildren<Text>();
            if (micButtonText != null) micButtonText.text = "Unmute"; // Default text requirement
            micMuteButton.onClick.AddListener(ToggleMicMute);
        }

        // Initialize Whisper Stream
        if (whisperManager != null && microphoneRecord != null)
        {
            // The stream shuts down for good whenever the mic stops, so the mic must run until muted.
            microphoneRecord.loop = true;
            microphoneRecord.vadStop = false;
            microphoneRecord.echo = false;
            // Only transcribe detected speech (Whisper invents text for silence), one segment per utterance.
            whisperManager.useVad = true;
            // A continuous stream would otherwise feed every past transcription back in as the prompt.
            whisperManager.updatePrompt = false;

            _stream = await whisperManager.CreateStream(microphoneRecord);
            _stream.OnSegmentFinished += OnSegmentFinished;
        }

        // Ensure mic starts disabled based on requirements
        if (microphoneRecord != null)
        {
            microphoneRecord.enabled = false;
        }

        ClearUI();
    }

    private void Update()
    {
        // Handle Command Cooldown Timer
        if (_commandCooldown > 0f)
        {
            _commandCooldown -= Time.deltaTime;
            if (_commandCooldown <= 0f)
            {
                _lastCommand = "";
            }
        }

        // Handle Active Movement Over Time (XZ plane)
        if (_moveTimer > 0f)
        {
            _controller.Move(_moveDirection * moveSpeed * Time.deltaTime);
            _moveTimer -= Time.deltaTime;
        }
    }

    private void TeleportTo(Transform destination, float targetYRotation)
    {
        if (destination != null)
        {
            // A live CharacterController can snap back to its old position, so pause it while moving.
            _controller.enabled = false;
            playerBody.position = destination.position;
            playerBody.rotation = Quaternion.Euler(0f, targetYRotation, 0f);
            _controller.enabled = true;

            // Recenter XR tracking origin so headset layout matches world orientation cleanly
            InputTracking.Recenter();

            Debug.Log($"<color=blue><b>[Teleport]</b> Moved player to {destination.name} facing {targetYRotation}° Y</color>");
        }
        else
        {
            Debug.LogWarning("[Teleport] Destination transform is not assigned!");
        }
    }

    private void ToggleMicMute()
    {
        if (microphoneRecord == null || _stream == null) return;

        _isMicMuted = !_isMicMuted;

        if (_isMicMuted)
        {
            // Muted state
            _stream.StopStream();
            if (microphoneRecord.IsRecording) microphoneRecord.StopRecord();
            microphoneRecord.enabled = false;

            if (micButtonText != null) micButtonText.text = "Unmute";
            Debug.Log("<color=yellow><b>[Mic Muted]</b> Speech recognition paused.</color>");
            ClearUI();
        }
        else
        {
            // Unmuted state (Activates speech-to-text loop)
            microphoneRecord.enabled = true;
            microphoneRecord.StartRecord();
            _stream.StartStream();

            if (micButtonText != null) micButtonText.text = "Mute";
            Debug.Log("<color=green><b>[Mic Unmuted]</b> Listening continuously...</color>");
        }
    }

    private void OnSegmentFinished(WhisperResult result)
    {
        if (_isMicMuted) return;

        if (result != null && !string.IsNullOrWhiteSpace(result.Result))
        {
            string spokenText = result.Result.Trim().ToLower();
            
            // Place registered text into Subtitles UI
            if (subtitlesText != null)
            {
                subtitlesText.text = $"\"{spokenText}\"";
            }

            Debug.Log($"<color=green><b>[Subtitles Updated]:</b> {spokenText}</color>");
            ParseAndExecuteCommand(spokenText);
        }
    }

    private void ParseAndExecuteCommand(string command)
    {
        if (command == _lastCommand && _commandCooldown > 0f) return;

        _lastCommand = command;
        _commandCooldown = 1.5f;

        bool isValidCommand = false;
        Vector3 forwardRef = GetFlatDirection(vrCameraTransform.forward);
        Vector3 rightRef = GetFlatDirection(vrCameraTransform.right);

        // Strict hardcoded commands validation
        if (command.Contains("move forward"))
        {
            _moveDirection = forwardRef;
            _moveTimer = moveDuration;
            SetOutputFeedback("Moving forward", true);
            isValidCommand = true;
        }
        else if (command.Contains("move backward"))
        {
            _moveDirection = -forwardRef;
            _moveTimer = moveDuration;
            SetOutputFeedback("Moving backward", true);
            isValidCommand = true;
        }
        else if (command.Contains("move left"))
        {
            _moveDirection = -rightRef;
            _moveTimer = moveDuration;
            SetOutputFeedback("Moving left", true);
            isValidCommand = true;
        }
        else if (command.Contains("move right"))
        {
            _moveDirection = rightRef;
            _moveTimer = moveDuration;
            SetOutputFeedback("Moving right", true);
            isValidCommand = true;
        }

        if (!isValidCommand)
        {
            SetOutputFeedback("I don't know that.", false);
        }
    }

    private void SetOutputFeedback(string message, bool isSuccess)
    {
        if (outputText != null)
        {
            outputText.text = message;
            // Green for recognized/valid, Red for unknown commands
            outputText.color = isSuccess ? Color.green : Color.red;
        }
    }

    private Vector3 GetFlatDirection(Vector3 sourceDir)
    {
        sourceDir.y = 0;
        return sourceDir.normalized;
    }

    private void ClearUI()
    {
        if (subtitlesText != null) subtitlesText.text = "";
        if (outputText != null) outputText.text = "";
    }

    private void OnDisable()
    {
        if (_stream != null)
        {
            _stream.OnSegmentFinished -= OnSegmentFinished;
            _stream.StopStream();
        }
        if (microphoneRecord != null && microphoneRecord.IsRecording)
        {
            microphoneRecord.StopRecord();
        }
    }
}