using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Whisper;
using Whisper.Utils;
using ROVR;

public class VRControlPanel : MonoBehaviour
{
    [Header("Whisper References")]
    public WhisperManager whisperManager;
    public MicrophoneRecord microphoneRecord;

    [Header("Voice Commands")]
    [Tooltip("On: every sentence goes to the LLM pipeline (Ollama must be running), which moves the player. " +
             "Off: only the fixed phrases \"move forward/backward/left/right\" work.")]
    public bool useLLM = true;

    [Header("Movement References & Settings")]
    public Transform playerBody;
    public Transform vrCameraTransform;
    public float moveSpeed = 2f;     // fixed-phrase mode only; the LLM pipeline has its own speed
    public float moveDuration = 1f;  // fixed-phrase mode only

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
    public Text outputText;    // Displays feedback: green = understood, yellow = needs you, red = didn't work

    private bool _isMicMuted = true; // Default is muted/off until pressed
    private Text micButtonText;

    private WhisperStream _stream;
    private CharacterController _controller;
    private Vector3 _moveDirection = Vector3.zero;
    private float _moveTimer = 0f;
    private string _lastCommand = "";
    private float _commandCooldown = 0f;

    // LLM pipeline (Assets/ROVR), on Player Body
    private SemanticIntentResolver _resolver;
    private VoiceCommandRouter _router;

    private async void Start()
    {
        if (playerBody == null) playerBody = transform;
        _controller = playerBody.GetComponent<CharacterController>();
        if (vrCameraTransform == null && Camera.main != null) vrCameraTransform = Camera.main.transform;

        // In Start, not Awake: the pipeline takes the head camera from Camera.main, which the headset
        // camera may not have registered yet during Awake.
        if (useLLM) SetUpLLMPipeline();

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
            _stream.OnSegmentUpdated += OnSegmentUpdated;
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

        // Handle Active Movement Over Time (XZ plane), fixed-phrase mode
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
            // Stop any movement first, so it doesn't carry on in the new world.
            _moveTimer = 0f;
            if (_resolver != null) _resolver.HaltNow();

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
            // A sentence cut off by muting must not carry over into the next one.
            if (_resolver != null) _router = NewRouter();
            microphoneRecord.enabled = true;
            microphoneRecord.StartRecord();
            _stream.StartStream();

            if (micButtonText != null) micButtonText.text = "Mute";
            Debug.Log("<color=green><b>[Mic Unmuted]</b> Listening continuously...</color>");
        }
    }

    // The sentence in progress, re-transcribed every Whisper step (WhisperManager's stepSec, 0.5 s).
    // Only checked for a leading halt word ("stop", "wait", "ops"), so the player stops before the
    // sentence is finished.
    private void OnSegmentUpdated(WhisperResult result)
    {
        if (_isMicMuted || !useLLM || _router == null || result == null) return;
        _router.OnPartial(result.Result);
    }

    private void OnSegmentFinished(WhisperResult result)
    {
        if (_isMicMuted || result == null) return;

        // Drops Whisper's notes for non-speech ("[BLANK_AUDIO]") and the stock phrases it invents from noise ("Thank you.").
        string spokenText = TranscriptFilter.Clean(result.Result);
        if (spokenText != null)
        {
            spokenText = spokenText.ToLower();

            // Place registered text into Subtitles UI
            if (subtitlesText != null)
            {
                subtitlesText.text = $"\"{spokenText}\"";
            }

            Debug.Log($"<color=green><b>[Subtitles Updated]:</b> {spokenText}</color>");
        }

        if (useLLM)
        {
            if (_resolver == null) SetUpLLMPipeline(); // switched on during Play
            _router.OnFinal(result.Result); // also ends the sentence for the early halt check, even if it was noise
        }
        else if (spokenText != null)
        {
            ParseAndExecuteCommand(spokenText);
        }
    }

    // ---------- LLM pipeline ----------

    // The pipeline lives on Player Body, where its parts find each other and the head camera (Camera.main).
    // Parts already there are kept, so settings changed in their Inspector (e.g. the Ollama model) still apply.
    private void SetUpLLMPipeline()
    {
        if (_resolver != null) return;

        // Order matters: each part looks for the ones before it when it is added.
        GetOrAdd<FOVMetadataGrounding>();
        GetOrAdd<NavigationController>();
        GetOrAdd<OllamaClient>();
        _resolver = GetOrAdd<SemanticIntentResolver>();

        _resolver.OnCommandResolved += HandleCommandResolved;
        _resolver.OnClarificationNeeded += HandleQuestion;
        _resolver.OnStatus += HandleNotice;
        _resolver.OnError += HandleError;

        _router = NewRouter();
    }

    private T GetOrAdd<T>() where T : Component
    {
        var component = playerBody.GetComponent<T>();
        return component != null ? component : playerBody.gameObject.AddComponent<T>();
    }

    // Partial text can halt early; each finished sentence goes to the resolver. Halt words like "ops"
    // are matched by InterruptModule as fixed keywords, never by the LLM.
    private VoiceCommandRouter NewRouter()
    {
        return new VoiceCommandRouter(HaltFromVoice, SubmitToLLM, () => Time.time);
    }

    private void HaltFromVoice()
    {
        _resolver.HaltNow();
        SetOutputFeedback("Stopped", true);
    }

    private void SubmitToLLM(string sentence)
    {
        var interrupt = InterruptModule.Evaluate(sentence);
        if (interrupt.isHalt && string.IsNullOrEmpty(interrupt.remainder))
            SetOutputFeedback("Stopped", true);
        else
            SetOutputFeedback("...", Color.white); // waiting on the LLM

        _resolver.SubmitUtterance(sentence);
    }

    private void HandleCommandResolved(NavigationCommand command)
    {
        if (!command.IsClarification) SetOutputFeedback(Describe(command), true);
    }

    private void HandleQuestion(string question)
    {
        SetOutputFeedback(question, Color.yellow);
    }

    private void HandleNotice(string notice)
    {
        SetOutputFeedback(notice, Color.yellow);
    }

    private void HandleError(string error)
    {
        Debug.LogWarning("[LLM] " + error);
        // A model that was just started can take over 30 s to answer the first time, so a timeout isn't "not running".
        string message = error.Contains("Request timeout") ? "The language model is taking too long (still loading?). Please say that again."
            : error.StartsWith("Ollama request failed") ? "Can't reach the language model. Is Ollama running?"
            : "Something went wrong. Please say that again.";
        SetOutputFeedback(message, false);
    }

    // "Moving forward 5 m", "Moving left a bit", "Moving forward to the door, then turning right"
    private static string Describe(NavigationCommand command)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var step in command.steps)
        {
            string direction = step.direction == DirectionType.None ? "" : " " + step.direction.ToString().ToLower();
            string target = step.HasCondition ? step.condition.ToLower() : "";
            switch (step.action)
            {
                case ActionType.Move:
                    string how = step.HasCondition ? " to the " + target
                        : step.amount == AmountType.Small ? " a bit"
                        : step.HasMagnitude ? " " + step.magnitude.ToString("0.#") + " m"
                        : "";
                    parts.Add("moving" + direction + how);
                    break;
                case ActionType.Turn:
                    string until = step.HasCondition ? " until I see the " + target
                        : step.HasMagnitude ? " " + step.magnitude.ToString("0") + "°"
                        : "";
                    parts.Add("turning" + direction + until);
                    break;
                case ActionType.Stop:
                    parts.Add("stopping");
                    break;
            }
        }

        if (parts.Count == 0) return "OK";
        string text = string.Join(", then ", parts);
        return char.ToUpper(text[0]) + text.Substring(1);
    }

    // ---------- Fixed-phrase mode (useLLM off) ----------

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
        // Green for recognized/valid, Red for unknown commands
        SetOutputFeedback(message, isSuccess ? Color.green : Color.red);
    }

    private void SetOutputFeedback(string message, Color color)
    {
        if (outputText != null)
        {
            outputText.text = message;
            outputText.color = color;
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
            _stream.OnSegmentUpdated -= OnSegmentUpdated;
            _stream.OnSegmentFinished -= OnSegmentFinished;
            _stream.StopStream();
        }
        if (microphoneRecord != null && microphoneRecord.IsRecording)
        {
            microphoneRecord.StopRecord();
        }
    }

    private void OnDestroy()
    {
        if (_resolver != null)
        {
            _resolver.OnCommandResolved -= HandleCommandResolved;
            _resolver.OnClarificationNeeded -= HandleQuestion;
            _resolver.OnStatus -= HandleNotice;
            _resolver.OnError -= HandleError;
        }
    }
}
