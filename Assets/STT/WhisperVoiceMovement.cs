using UnityEngine;
using UnityEngine.InputSystem;
using Whisper;
using Whisper.Utils;

public class WhisperVoiceMovement : MonoBehaviour
{
    [Header("Whisper References")]
    public WhisperManager whisperManager;
    public MicrophoneRecord microphoneRecord;

    [Header("Movement References & Settings")]
    public Transform bodyTransform;
    public Transform vrCameraTransform;
    public float moveSpeed = 2f;
    public float moveDuration = 1f;

    private WhisperStream _stream;
    private bool _isListening;
    private Vector3 _moveDirection = Vector3.zero;
    private float _moveTimer = 0f;

    private string _lastCommand = "";
    private float _commandCooldown = 0f;

    private async void Start()
    {
        if (whisperManager == null || microphoneRecord == null)
        {
            Debug.LogError("[WhisperVoiceMovement] Assign WhisperManager and MicrophoneRecord in the Inspector!");
            return;
        }

        if (bodyTransform == null) bodyTransform = transform;
        if (vrCameraTransform == null && Camera.main != null) vrCameraTransform = Camera.main.transform;

        _stream = await whisperManager.CreateStream(microphoneRecord);

        _stream.OnResultUpdated += OnResultUpdated;
        _stream.OnSegmentFinished += OnSegmentFinished;

        Debug.Log("<color=cyan><b>[Whisper Ready]</b> Press SPACEBAR to start continuous listening.</color>");
    }

    private void Update()
    {
        // Toggle listening strictly via Spacebar using the new Input System
        if (_stream != null && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (_isListening)
            {
                _isListening = false;
                _stream.StopStream();
                if (microphoneRecord.IsRecording) microphoneRecord.StopRecord();
                Debug.Log("<color=yellow><b>[Stopped]</b> Stopped listening by user input.</color>");
            }
            else
            {
                _isListening = true;
                microphoneRecord.StartRecord();
                _stream.StartStream();
                Debug.Log("<color=green><b>[Listening Continuously...]</b> Speak anytime. Press SPACEBAR to stop.</color>");
            }
        }

        // Handle Command Cooldown Timer
        if (_commandCooldown > 0f)
        {
            _commandCooldown -= Time.deltaTime;
            if (_commandCooldown <= 0f)
            {
                _lastCommand = ""; // Clear memory so same command can be said again later
            }
        }

        // Handle Active Movement Over Time (Grounded on XZ plane)
        if (_moveTimer > 0f)
        {
            bodyTransform.Translate(_moveDirection * moveSpeed * Time.deltaTime, Space.World);
            _moveTimer -= Time.deltaTime;
        }
    }

    private void OnResultUpdated(string partialResult)
    {
        // Optional live preview
    }

    private void OnSegmentFinished(WhisperResult result)
    {
        if (result != null && !string.IsNullOrWhiteSpace(result.Result))
        {
            string spokenText = result.Result.Trim().ToLower();
            Debug.Log($"<color=green><b>[Sentence Completed]:</b> {spokenText}</color>");

            ParseAndExecuteCommand(spokenText);
        }

        // Ensure microphone stays recording if session is active
        if (_isListening && microphoneRecord != null && !microphoneRecord.IsRecording)
        {
            microphoneRecord.StartRecord();
        }
    }

    private void ParseAndExecuteCommand(string command)
    {
        // Ignore duplicate overlapping triggers within a 1.5 second window
        if (command == _lastCommand && _commandCooldown > 0f) return;

        _lastCommand = command;
        _commandCooldown = 1.5f; // Lock out identical triggers for 1.5 seconds

        bool useViewDirection = command.Contains("looking") || command.Contains("where i look") || command.Contains("view");
        
        Vector3 forwardRef = useViewDirection ? GetFlatDirection(vrCameraTransform.forward) : bodyTransform.forward;
        Vector3 rightRef = useViewDirection ? GetFlatDirection(vrCameraTransform.right) : bodyTransform.right;

        if (command.Contains("forward") || command.Contains("move forward"))
        {
            _moveDirection = forwardRef;
            _moveTimer = moveDuration;
            Debug.Log("<color=magenta>Command: Moving Forward</color>");
        }
        else if (command.Contains("backward") || command.Contains("back"))
        {
            _moveDirection = -forwardRef;
            _moveTimer = moveDuration;
            Debug.Log("<color=magenta>Command: Moving Backward</color>");
        }
        else if (command.Contains("left"))
        {
            _moveDirection = -rightRef;
            _moveTimer = moveDuration;
            Debug.Log("<color=magenta>Command: Moving Left</color>");
        }
        else if (command.Contains("right"))
        {
            _moveDirection = rightRef;
            _moveTimer = moveDuration;
            Debug.Log("<color=magenta>Command: Moving Right</color>");
        }
    }

    private Vector3 GetFlatDirection(Vector3 sourceDir)
    {
        sourceDir.y = 0;
        return sourceDir.normalized;
    }

    private void OnDisable()
    {
        if (_stream != null)
        {
            _stream.OnResultUpdated -= OnResultUpdated;
            _stream.OnSegmentFinished -= OnSegmentFinished;

            if (_isListening)
            {
                _stream.StopStream();
                if (microphoneRecord != null && microphoneRecord.IsRecording)
                {
                    microphoneRecord.StopRecord();
                }
                _isListening = false;
            }
        }
    }
}