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

    [Header("Speech")]
    [Tooltip("Seconds of silence that end a sentence. Shorter pauses stay in the same sentence. " +
             "Halt words (stop, ops) act before this, while the sentence is still being spoken.")]
    public float deadAirSec = 1.5f;
    [Tooltip("Log every transcription to the Console: the sentence so far while speaking, and the finished sentence.")]
    public bool logSpeech;
    [Tooltip("Log each command sent to the LLM (what was heard and the objects in view) and its reply.")]
    public bool logLLM;

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
    public Button slowButton;   // walking speed (experimenter); also "faster" / "slower" by voice
    public Button normalButton;
    public Button fastButton;
    public Text subtitlesText; // Displays whatever speech is registered
    public Text outputText;    // Displays feedback: green = understood, yellow = needs you, red = didn't work

    private bool _isMicMuted = true; // Default is muted/off until pressed
    private Text micButtonText;

    // Speech: microphone audio -> sentences (SpeechSegmenter) -> text (Whisper), one transcription at a time
    private SpeechSegmenter _segmenter;
    private float[] _pendingPartial, _pendingSentence; // newest audio waiting for Whisper
    private bool _transcribing;
    private int _speechGeneration; // bumped on mute/unmute so results from before are dropped

    private CharacterController _controller;
    private Vector3 _moveDirection = Vector3.zero;
    private float _moveTimer = 0f;
    private string _lastCommand = "";
    private float _commandCooldown = 0f;

    // LLM pipeline (Assets/ROVR), on Player Body
    private SemanticIntentResolver _resolver;
    private FOVMetadataGrounding _grounding;
    private OllamaClient _ollama;
    private VoiceCommandRouter _router;

    private void Start()
    {
        if (playerBody == null) playerBody = transform;
        _controller = playerBody.GetComponent<CharacterController>();
        if (vrCameraTransform == null && Camera.main != null) vrCameraTransform = Camera.main.transform;

        // In Start, not Awake: the pipeline takes the head camera from Camera.main, which the headset
        // camera may not have registered yet during Awake.
        if (useLLM) SetUpLLMPipeline();

        // The Maze minimap (corner of the view, only in the Maze), for both voice and fixed-phrase modes.
        if (FindAnyObjectByType<MazeMinimap>() == null) gameObject.AddComponent<MazeMinimap>();

        // Force initial spawn rotation to plain destination facing 0 degrees Y
        TeleportTo(plainDestination, 0f);

        // Hook up button listeners with specific Y rotation constraints
        if (plainButton != null) plainButton.onClick.AddListener(() => TeleportTo(plainDestination, 0f));
        if (mazeButton != null) mazeButton.onClick.AddListener(() => TeleportTo(mazeDestination, 90f));
        if (houseButton != null) houseButton.onClick.AddListener(() => TeleportTo(houseDestination, 0f));

        if (slowButton != null) slowButton.onClick.AddListener(() => SetSpeed(SpeedLevel.Slow));
        if (normalButton != null) normalButton.onClick.AddListener(() => SetSpeed(SpeedLevel.Normal));
        if (fastButton != null) fastButton.onClick.AddListener(() => SetSpeed(SpeedLevel.Fast));
        ShowSpeed(SpeedLevel.Normal, false);

        // Questions can be longer than a line ("Which plant? Look at it..."): wrap, and shrink to fit the box.
        if (outputText != null)
        {
            outputText.horizontalOverflow = HorizontalWrapMode.Wrap;
            outputText.resizeTextForBestFit = true;
            outputText.resizeTextMinSize = Mathf.Max(8, outputText.fontSize / 2);
            outputText.resizeTextMaxSize = outputText.fontSize;
        }

        if (micMuteButton != null)
        {
            micButtonText = micMuteButton.GetComponentInChildren<Text>();
            if (micButtonText != null) micButtonText.text = "Unmute"; // Default text requirement
            micMuteButton.onClick.AddListener(ToggleMicMute);
        }

        // Initialize speech input (the Whisper model loads by itself in the background)
        if (whisperManager != null && microphoneRecord != null)
        {
            // Keep recording until muted, instead of stopping after the 60 s buffer or a silence.
            microphoneRecord.loop = true;
            microphoneRecord.vadStop = false;
            microphoneRecord.echo = false;
            // SpeechSegmenter decides where sentences end, from small pieces of audio for close timing.
            microphoneRecord.useVad = false;
            microphoneRecord.chunksLengthSec = 0.1f;
            microphoneRecord.OnChunkReady += OnMicChunk;
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
        if (_ollama != null) _ollama.LogExchanges = logLLM;

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
        if (microphoneRecord == null || whisperManager == null) return;

        _isMicMuted = !_isMicMuted;
        // A sentence cut off by muting must not carry over, and late transcriptions are dropped.
        _speechGeneration++;
        _pendingPartial = _pendingSentence = null;
        _segmenter?.Reset();

        if (_isMicMuted)
        {
            // Muted state
            if (microphoneRecord.IsRecording) microphoneRecord.StopRecord();
            microphoneRecord.enabled = false;

            if (micButtonText != null) micButtonText.text = "Unmute";
            Debug.Log("<color=yellow><b>[Mic Muted]</b> Speech recognition paused.</color>");
            ClearUI();
        }
        else
        {
            // Unmuted state (Activates speech-to-text loop)
            if (_resolver != null) _router = NewRouter();
            microphoneRecord.enabled = true;
            microphoneRecord.StartRecord();

            if (micButtonText != null) micButtonText.text = "Mute";
            Debug.Log("<color=green><b>[Mic Unmuted]</b> Listening continuously...</color>");
        }
    }

    // ---------- Speech input ----------

    private void OnMicChunk(AudioChunk chunk)
    {
        if (_isMicMuted || chunk.Data == null) return;
        FeedAudio(ToMono(chunk.Data, chunk.Channels), chunk.Frequency);
    }

    // Mono microphone audio. Public so a recording can be played in for testing without a microphone.
    public void FeedAudio(float[] samples, int frequency)
    {
        if (_segmenter == null || _segmenter.SampleRate != frequency)
        {
            _segmenter = new SpeechSegmenter(frequency);
            _segmenter.OnPartial += audio => QueueTranscription(audio, false);
            _segmenter.OnSentence += audio => QueueTranscription(audio, true);
        }
        _segmenter.DeadAirSec = deadAirSec;
        _segmenter.AddSamples(samples);
    }

    // Only the newest partial is worth transcribing; a finished sentence always is, and goes first.
    private void QueueTranscription(float[] audio, bool isSentence)
    {
        if (isSentence) { _pendingSentence = audio; _pendingPartial = null; }
        else if (_pendingSentence == null) _pendingPartial = audio;

        if (!_transcribing) TranscribeQueued();
    }

    private async void TranscribeQueued()
    {
        _transcribing = true;
        while (_pendingSentence != null || _pendingPartial != null)
        {
            bool isSentence = _pendingSentence != null;
            float[] audio = isSentence ? _pendingSentence : _pendingPartial;
            if (isSentence) _pendingSentence = null; else _pendingPartial = null;
            int generation = _speechGeneration;

            WhisperResult result = await whisperManager.GetTextAsync(PadForWhisper(audio, _segmenter.SampleRate), _segmenter.SampleRate, 1);
            if (this == null) return; // destroyed while Whisper was working
            if (result == null || generation != _speechGeneration || _isMicMuted) continue;

            if (logSpeech) Debug.Log("[Speech] " + (isSentence ? "sentence" : "so far") + " (" + (audio.Length / (float)_segmenter.SampleRate).ToString("F1") + " s): \"" + result.Result + "\"");
            if (isSentence) OnSentenceHeard(result.Result);
            else OnPartialHeard(result.Result);
        }
        _transcribing = false;
    }

    // Whisper returns nothing for audio under 1 s, and a lone "stop" or "ops" is shorter than that,
    // so short audio gets silence added after it.
    private static float[] PadForWhisper(float[] audio, int sampleRate)
    {
        int minimum = (int)(sampleRate * 1.5f);
        if (audio.Length >= minimum) return audio;
        var padded = new float[minimum];
        System.Array.Copy(audio, padded, audio.Length);
        return padded;
    }

    private static float[] ToMono(float[] data, int channels)
    {
        if (channels <= 1) return data;
        var mono = new float[data.Length / channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += data[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    // The sentence so far, while it's still being spoken: shown as a live caption, and checked for a
    // leading halt word ("stop", "wait", "ops") so the player stops before the sentence is finished.
    private void OnPartialHeard(string text)
    {
        string soFar = TranscriptFilter.Clean(text);
        if (soFar != null && subtitlesText != null) subtitlesText.text = soFar.ToLower() + " ...";

        if (useLLM && _router != null) _router.OnPartial(text);
    }

    // A finished sentence (it ended with deadAirSec of silence).
    private void OnSentenceHeard(string text)
    {
        // Drops Whisper's notes for non-speech ("[BLANK_AUDIO]") and the stock phrases it invents from noise ("Thank you.").
        string spokenText = TranscriptFilter.Clean(text);
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
            _router.OnFinal(text); // also ends the sentence for the early halt check, even if it was noise
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
        _grounding = GetOrAdd<FOVMetadataGrounding>();
        GetOrAdd<NavigationController>();
        _ollama = GetOrAdd<OllamaClient>();
        _resolver = GetOrAdd<SemanticIntentResolver>();
        GetOrAdd<Highlighter>(); // makes the options glow when the system asks "which one?"
        GetOrAdd<ComfortVignette>(); // darkens the edges of the view while turning

        _resolver.OnCommandResolved += HandleCommandResolved;
        _resolver.OnClarificationNeeded += HandleQuestion;
        _resolver.OnStatus += HandleNotice;
        _resolver.OnError += HandleError;
        _resolver.OnSpeedChanged += level => ShowSpeed(level, true);

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
        return new VoiceCommandRouter(HaltFromVoice, SubmitToLLM, () => Time.time, 1.0f, change => _resolver.ChangeSpeed(change));
    }

    // ---------- Speed ----------

    private static readonly float[] FixedPhraseSpeeds = { 1f, 2f, 3f }; // m/s for Slow / Normal / Fast without the LLM

    private void SetSpeed(SpeedLevel level)
    {
        if (_resolver != null) _resolver.SetSpeed(level); // calls back ShowSpeed
        else ShowSpeed(level, true);
    }

    // Highlights the speed in use, and says so.
    private void ShowSpeed(SpeedLevel level, bool announce)
    {
        moveSpeed = FixedPhraseSpeeds[(int)level];
        var buttons = new[] { slowButton, normalButton, fastButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            var image = buttons[i].GetComponent<Image>();
            if (image != null) image.color = i == (int)level ? new Color(0.55f, 0.9f, 0.55f) : Color.white;
        }
        if (announce) SetOutputFeedback("Speed: " + level.ToString().ToLower(), true);
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
    private string Describe(NavigationCommand command)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var step in command.steps)
        {
            string direction = step.direction == DirectionType.None ? "" : " " + step.direction.ToString().ToLower();
            // The target can be one particular object ("tree-3"); say what it is ("tree").
            string target = step.HasCondition ? _grounding.NameFor(step.condition).ToLower() : "";
            switch (step.action)
            {
                case ActionType.Move:
                    string how = step.HasCondition ? (step.until == UntilType.See ? " until I see the " : " to the ") + target
                        : step.amount == AmountType.Small ? " a bit"
                        : step.HasMagnitude ? " " + step.magnitude.ToString("0.#") + " m"
                        : "";
                    string pace = step.speed == SpeedLevel.Slow ? ", slowly" : step.speed == SpeedLevel.Fast ? ", fast" : "";
                    parts.Add("moving" + direction + how + pace);
                    break;
                case ActionType.Turn:
                    string until = step.HasCondition ? " to face the " + target
                        : step.amount == AmountType.Small ? " a little"
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
        if (microphoneRecord != null) microphoneRecord.OnChunkReady -= OnMicChunk;
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
