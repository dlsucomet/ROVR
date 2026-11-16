using UnityEngine;
using UnityEngine.UI;
using Whisper;
using Whisper.Utils;
using ROVR;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class VRControlPanel : MonoBehaviour
{
    [Header("Whisper References")]
    public WhisperManager whisperManager;
    public MicrophoneRecord microphoneRecord;

    [Header("Speech")]
    [Tooltip("Seconds of silence that end a sentence. Shorter pauses stay in the same sentence. " +
             "Halt words (stop, ops) act before this, while the sentence is still being spoken.")]
    public float deadAirSec = 1.5f;
    [Tooltip("Seconds of silence after which an open-ended move already said in full (\"move forward\", \"move back\") " +
             "runs, without waiting out deadAirSec. Turns and counted moves always wait. 0 turns this off.")]
    public float earlyEndSec = 0.6f;
    [Tooltip("Log every transcription to the Console: the sentence so far while speaking, and the finished sentence.")]
    public bool logSpeech;
    [Tooltip("Log each command sent to the LLM (what was heard and the objects in view) and its reply.")]
    public bool logLLM;

    [Header("Transcript")]
    [Tooltip("Save a transcript of each Play session (buttons, teleports, what the mic heard, panel replies) " +
             "to Transcripts/ in the project folder.")]
    public bool saveTranscript = true;
    [Tooltip("Also save the sentence-so-far captions while someone is still speaking (many lines).")]
    public bool transcriptPartials;

    [Header("Bridge (PC)")]
    [Tooltip("The ROVR bridge on the PC (Bridge/rovr_bridge.py). LLM commands go through it to Ollama, and " +
             "transcript lines are shown live in its terminal. On the headset over the USB cable this stays " +
             "127.0.0.1 (the build hook links the port); over Wi-Fi the headset finds the PC by itself (Find Bridge On Wifi). " +
             "Empty = talk to Ollama directly.")]
    public string bridgeUrl = "http://127.0.0.1:8765";
    [Tooltip("On the headset: when the bridge doesn't answer at Bridge Url (no cable), find it on the Wi-Fi network. Start it " +
             "with Bridge/run_bridge_wifi.bat; the headset must be on the same network. Not in the Editor, where the bridge is on this PC.")]
    public bool findBridgeOnWifi = true;
    [Tooltip("Turn speech into text on the PC, through the bridge (about 0.3 s a sentence), instead of on the headset " +
             "(14-17 s on a Quest). Whenever the bridge or its Whisper isn't there, the headset (or the Editor) does it itself.")]
    public bool transcribeOnPC = true;

    [Header("Voice Commands")]
    [Tooltip("On: every sentence goes to the LLM pipeline (Ollama must be running), which moves the player. " +
             "Off: only the fixed phrases \"move forward/backward/left/right\" work.")]
    public bool useLLM = true;

    [Header("Movement References & Settings")]
    public Transform playerBody;
    public Transform vrCameraTransform;
    public float moveSpeed = 2f;     // fixed-phrase mode only; the LLM pipeline has its own speed
    public float moveDuration = 1f;  // fixed-phrase mode only

    [Header("Study")]
    [Tooltip("ROVR = voice only; Manual = controller sticks only (voice off, as Manual participants think aloud). " +
             "The session control page on the PC sets this; without it, this value is used.")]
    public StudyCondition condition = StudyCondition.ROVR;

    [Header("Steps")]
    [Tooltip("The slight up-and-down of walking on step commands (\"take 3 steps\"): Off, Subtle (1.5 cm) or Strong (3 cm). " +
             "Other moves never bob.")]
    public StepBob.Strength stepBob = StepBob.Strength.Subtle;
    [Tooltip("Metres in one step, for forward, back and sidesteps (\"take two steps\" = twice this when nothing's in the way). " +
             "It still adapts a little to each person during a session.")]
    [Range(0.5f, 1.5f)] public float stepMeters = 1f;

    [Header("Comfort")]
    [Tooltip("m/s² when movement speeds up (voice and sticks). Lower = gentler starts.")]
    public float acceleration = 4f;
    [Tooltip("m/s² when movement slows down at the end of a move or when a new command takes over.")]
    public float deceleration = 5f;
    [Tooltip("m/s² when a spoken \"stop\" (or \"wait\", \"freeze\") slows the player down: gentler, as a sudden stop is " +
             "nauseating. Walls and the cancel button (B) still stop at once.")]
    public float stopDeceleration = 3f;
    [Tooltip("Turn in instant jumps instead of smooth eased turns (voice turns and the right stick).")]
    public bool snapTurning;
    [Tooltip("Also darken the edges of the view while walking forward or back (voice and sticks), not only while turning.")]
    public bool movementVignette = true;

    [Header("Teleport Destinations")]
    public Transform practiceDestination; // the practice area (PLACEHOLDER), where a session starts
    public Transform plainDestination;
    public Transform mazeDestination;
    public Transform houseDestination;

    [Header("UI Elements")]
    public Button plainButton;
    public Button mazeButton;
    public Button houseButton;
    public Button micMuteButton;
    [Tooltip("The mic icon on the mute button: the mic, or the mic with a red slash when muted (like Discord).")]
    public Image micIcon;
    public Sprite micOnSprite;
    public Sprite micOffSprite;
    public Button tutorialButton;      // "?": shows the tutorial card again
    public TutorialCard tutorialCard;  // shown at the start, before practice
    public Text subtitlesText; // Displays whatever speech is registered
    public Text outputText;    // Displays feedback: green = understood, yellow = needs you, red = didn't work

    private bool _isMicMuted = true; // Default is muted/off until pressed
    private Text micButtonText;

    // Speech: microphone audio -> sentences (SpeechSegmenter) -> text (Whisper on the PC, or here), one transcription at a time
    private SpeechSegmenter _segmenter;
    private readonly System.Collections.Generic.Queue<float[]> _pendingSentences = new System.Collections.Generic.Queue<float[]>();
    private float[] _pendingPartial; // the newest sentence-so-far waiting for Whisper
    private float[] _pendingPause;   // the sentence so far at an earlyEndSec pause, waiting for Whisper
    private int _pauseSentence, _pauseVoiced; // which sentence that pause was in, and its voiced frames then
    private PCSpeechToText _pcSpeech; // null when transcribeOnPC is off
    private string _world;            // the world the player was last teleported to ("Plain"), for Whisper's prompt
    private BridgeLink _bridge;       // where the bridge is (cable or Wi-Fi); null when Bridge Url is empty
    private BodyUnderHead _bodyUnderHead;
    private StudySession _session; // the session run from the PC's control page
    private StepBob _stepBob;  // in the LLM pipeline
    private ManualLocomotion _manual;      // the Manual condition's sticks; off in ROVR
    private ControllerButton _aButton;     // A: short press = mute / unmute, long press = show / hide the panel
    private ControllerButton _bButton;     // B: cancel the command (ROVR)
    private bool _cutByMute;               // the sentence being transcribed was ended by muting
    private PlayerGravity _gravity;
    private bool _panelShown = true; // hidden at start (Start); A held shows it
    private RawImage _panelDiagram;  // the controls diagram, just under the panel (shown and hidden with it)
    private bool _transcribing;
    private bool _speechStarted; // a sentence is being spoken (for the SPEECH transcript lines)
    private const float SlowWhisperSec = 3f; // "so far" transcriptions slower than this are logged
    private int _speechGeneration; // bumped by Cancel so speech from before it is dropped (muting keeps it)

    private CharacterController _controller;
    private Vector3 _moveDirection = Vector3.zero;
    private float _moveTimer = 0f;
    private string _lastCommand = "";
    private float _commandCooldown = 0f;

    // LLM pipeline (Assets/ROVR), on Player Body
    private SemanticIntentResolver _resolver;
    private NavigationController _navigation;
    private FOVMetadataGrounding _grounding;
    private OllamaClient _ollama;
    private VoiceCommandRouter _router;

    private SessionTranscript _transcript;

    private void Start()
    {
        if (playerBody == null) playerBody = transform;
        _controller = playerBody.GetComponent<CharacterController>();
        if (vrCameraTransform == null && Camera.main != null) vrCameraTransform = Camera.main.transform;

        if (saveTranscript)
        {
            _transcript = SessionTranscript.Begin();
            if (_transcript != null && !string.IsNullOrEmpty(bridgeUrl)) _transcript.SendTo(bridgeUrl.TrimEnd('/') + "/log");
            Record("SESSION", "Started. Scene: " + gameObject.scene.name + ". Voice commands: " + (useLLM ? "LLM" : "fixed phrases") + ". Mic: muted");
        }

        // Where the bridge is, over the cable or on Wi-Fi; everything that talks to it follows (OnBridgeChanged).
        if (!string.IsNullOrEmpty(bridgeUrl))
        {
            _bridge = GetComponent<BridgeLink>();
            if (_bridge == null) _bridge = gameObject.AddComponent<BridgeLink>();
            _bridge.OnChanged += OnBridgeChanged;
            _bridge.Begin(bridgeUrl, findBridgeOnWifi && !Application.isEditor);
        }

        // In Start, not Awake: the pipeline takes the head camera from Camera.main, which the headset
        // camera may not have registered yet during Awake.
        if (useLLM) SetUpLLMPipeline();

        // The minimaps of the Maze and the House (lower left of the view when the world loads, grabbable),
        // for both conditions.
        if (FindAnyObjectByType<WorldMinimap>() == null) gameObject.AddComponent<WorldMinimap>();

        // Hands drawn where the user's hands are (holding the controllers, or tracked hands without them).
        if (FindAnyObjectByType<VirtualHands>() == null) gameObject.AddComponent<VirtualHands>();
        // A brighter pointer ray, with a dot where it lands.
        if (FindAnyObjectByType<PointerLook>() == null) gameObject.AddComponent<PointerLook>();

        _gravity = playerBody.GetComponent<PlayerGravity>();
        if (_gravity == null) _gravity = playerBody.gameObject.AddComponent<PlayerGravity>();

        // For the study's logs: jumps, the minimap, what the pointer rests on.
        _gravity.OnJumped += HandleJumped;
        WorldMinimap.Logged += HandleMinimap;
        PointerLook.PointedAt += HandlePointedAt;

        // The Manual condition's stick movement, on the same body; only on in Manual (SetCondition).
        _manual = playerBody.GetComponent<ManualLocomotion>();
        if (_manual == null) _manual = playerBody.gameObject.AddComponent<ManualLocomotion>();
        _manual.OnLog += line => Record("MANUAL", line);
        _manual.OnInput += (kind, direction, push) => _session?.Stick(kind, direction, push); // the study's world log

        // A on the right controller (F1 in the Editor): a short press mutes / unmutes, a long press shows / hides the panel.
        _aButton = GetComponent<ControllerButton>();
        if (_aButton == null) _aButton = gameObject.AddComponent<ControllerButton>();
        _aButton.OnShortPress += OnAShortPress;
        _aButton.OnLongPress += TogglePanel;

        // B on the right controller (F2 in the Editor): cancel the command. Separate from muting, which only
        // stops listening and still carries out what was already said.
        _bButton = gameObject.AddComponent<ControllerButton>();
        _bButton.button = ControllerButton.Which.Secondary;
        _bButton.OnShortPress += OnBPress;
        _bButton.OnLongPress += OnBPress;

        TurnOffRigLocomotion();

        // Keeps the body (and its collisions) under the user's head in the headset, and hides its capsule.
        _bodyUnderHead = playerBody.GetComponent<BodyUnderHead>();
        if (_bodyUnderHead == null) _bodyUnderHead = playerBody.gameObject.AddComponent<BodyUnderHead>();

        // Force initial spawn rotation to plain destination facing 0 degrees Y
        TeleportTo(plainDestination, 0f, "Plain", "at start");

        // The world buttons, for trying the worlds out. During a study session the worlds follow the
        // participant's sequence instead (the control page's Next), so the buttons do nothing then.
        if (plainButton != null) plainButton.onClick.AddListener(() => WorldButton("Plain"));
        if (mazeButton != null) mazeButton.onClick.AddListener(() => WorldButton("Maze"));
        if (houseButton != null) houseButton.onClick.AddListener(() => WorldButton("House"));

        if (tutorialButton != null) tutorialButton.onClick.AddListener(() => { Record("BUTTON", "Tutorial"); ShowTutorial(); });
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
        ShowMic();

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

            if (transcribeOnPC && !string.IsNullOrEmpty(bridgeUrl))
            {
                _pcSpeech = GetComponent<PCSpeechToText>();
                if (_pcSpeech == null) _pcSpeech = gameObject.AddComponent<PCSpeechToText>();
                _pcSpeech.OnReadyChanged += (ready, whyNot) => Record("STT", ready
                    ? "Speech to text: on the PC, through the bridge"
                    : "Speech to text: " + LocalWhisper + ", because " + whyNot + ". The PC is asked again every " + PCSpeechToText.RetrySec + " s");
                _pcSpeech.Connect(BridgeNow);
                _pcSpeech.Prompt = WorldPrompt(StartOf(_world)); // the world it started in
            }
        }

        // Ensure mic starts disabled based on requirements
        if (microphoneRecord != null)
        {
            microphoneRecord.enabled = false;
        }

        // On the headset (Android) the microphone needs the user's permission, asked at run time.
        // Without it the mic records silence, so nothing is ever transcribed. Ask once at start.
        RequestMicPermission();

        // The controls diagram, as a second card just under the panel, so holding A also shows the controls.
        // A child of the panel's canvas: shown, hidden and moved with it. SetCondition picks the image.
        var panelRect = (RectTransform)transform;
        float diagramWidth = Mathf.Min(640f, panelRect.rect.width);
        _panelDiagram = ControlsDiagram.Attach(transform,
            new Vector2(0f, -panelRect.rect.height / 2f - 14f - diagramWidth / ControlsDiagram.Aspect / 2f), diagramWidth);

        SetCondition(condition);
        ClearUI();

        // The panel starts hidden (the caption strip shows what's heard). On the headset the session then
        // waits for the control page (a waiting card); otherwise the tutorial card comes first.
        SetPanelShown(false, false);
        _session = GetComponent<StudySession>();
        if (_session == null) _session = gameObject.AddComponent<StudySession>();
        _session.Begin(this);
        if (!_session.WaitsForSetup) ShowTutorial();
    }

    private void Update()
    {
        if (_ollama != null) _ollama.LogExchanges = logLLM;
        if (_stepBob != null) _stepBob.level = stepBob;
        ApplyComfort();
        HandleMicPermissionAnswer();

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

    // world and reason are for the transcript only ("Maze", "button").
    private void TeleportTo(Transform destination, float targetYRotation, string world, string reason)
    {
        if (destination != null)
        {
            Teleport(destination.position, targetYRotation, world, reason, destination.name);
        }
        else
        {
            Debug.LogWarning("[Teleport] Destination transform is not assigned!");
            Record("TELEPORT", $"{world} ({reason}): failed, no destination assigned");
        }
    }

    private void Teleport(Vector3 position, float targetYRotation, string world, string reason, string where)
    {
        // Stop any movement first, so it doesn't carry on in the new world.
        _moveTimer = 0f;
        if (_resolver != null) _resolver.HaltNow(true);

        // A live CharacterController can snap back to its old position, so pause it while moving.
        _controller.enabled = false;
        playerBody.position = position;
        playerBody.rotation = Quaternion.Euler(0f, targetYRotation, 0f);
        _controller.enabled = true;
        if (_gravity != null) _gravity.Settle();
        // The new world's walkable area, for "go to the table" round furniture and "get me out of this room".
        RoutePlanner.Prepare(position, _controller.radius);
        // Whisper on the PC is told this world's object names, so it hears them right ("shelf", not "shell").
        _world = world;
        if (_pcSpeech != null) _pcSpeech.Prompt = WorldPrompt(StartOf(world));

        // The head (not just the body) lands on the destination, facing targetYRotation, wherever the
        // user stands in their play area. (InputTracking.Recenter did this before OpenXR.)
        if (_bodyUnderHead != null) _bodyUnderHead.FaceFrom(targetYRotation);

        Debug.Log($"<color=blue><b>[Teleport]</b> Moved player to {where} facing {targetYRotation:0}° Y</color>");
        Record("TELEPORT", $"{world} ({reason}): to {where} at ({position.x:0.##}, {position.y:0.##}, {position.z:0.##}), facing {targetYRotation:0}°");
    }

    private void WorldButton(string world)
    {
        if (_session != null && _session.InStudy)
        {
            Record("BUTTON", world + " (ignored: in a study session the worlds follow the participant's sequence)");
            return;
        }
        Record("BUTTON", world);
        GoToWorld(world, "button");
    }

    // The object names in the world that start belongs to (its root: Plain, Maze, House, Practice), as a
    // short list for Whisper's prompt: "Tree, pond, rock, plant." Only names, no commands: with commands in
    // the prompt Whisper was seen to write them into room noise. Empty when there are none.
    public const int PromptNames = 40; // Whisper's prompt holds about 220 tokens: 40 names fit easily
    public static string WorldPrompt(Transform start)
    {
        if (start == null) return string.Empty;
        Transform root = start.root;
        var names = new System.Collections.Generic.List<string>();
        foreach (var label in SceneLabel.All)
        {
            if (label == null || string.IsNullOrEmpty(label.label) || label.transform.root != root) continue;
            string name = label.label.Trim().ToLowerInvariant();
            name = (" " + name + " ").Replace(" tv ", " TV ").Trim();
            if (name.Length > 0 && !names.Contains(name)) names.Add(name);
            if (names.Count >= PromptNames) break;
        }
        if (names.Count == 0) return string.Empty;
        string list = string.Join(", ", names);
        return char.ToUpperInvariant(list[0]) + list.Substring(1) + ".";
    }

    // ---------- For the study session (StudySession) ----------

    // A world's start point (for the world log's positions), or null.
    public Transform StartOf(string world)
    {
        switch ((world ?? "").ToLowerInvariant())
        {
            case "practice": return practiceDestination;
            case "plain": return plainDestination;
            case "maze": return mazeDestination;
            case "house": return houseDestination;
            default: return null;
        }
    }

    // Between worlds (the questionnaires) and at the end: nothing moves the player. Movement stops, the
    // sticks are off and the mic is muted; SetCondition turns the sticks back on for the next world.
    public void PauseForBreak()
    {
        if (_resolver != null) _resolver.HaltNow(true);
        _moveTimer = 0f;
        if (_manual != null) _manual.enabled = false;
        DropSpeech(); // nothing said before the break is carried out after it
        if (!_isMicMuted) ToggleMicMute();
    }

    // Practice, Plain, Maze or House: to that world's start. False if there's no such world here.
    public bool GoToWorld(string world, string reason)
    {
        switch ((world ?? "").ToLowerInvariant())
        {
            case "practice": TeleportTo(practiceDestination, 0f, "Practice", reason); return practiceDestination != null;
            case "plain": TeleportTo(plainDestination, 0f, "Plain", reason); return plainDestination != null;
            case "maze": TeleportTo(mazeDestination, 90f, "Maze", reason); return mazeDestination != null;
            case "house": TeleportTo(houseDestination, 0f, "House", reason); return houseDestination != null;
            default: Record("TELEPORT", "Unknown world '" + world + "'"); return false;
        }
    }

    // Back to a spot (a restarted task begins where it began).
    public void PlaceAt(Vector3 position, float yaw, string world, string reason)
    {
        Teleport(position, yaw, world, reason, "the task's start");
    }

    // A new participant: nothing carries over from the last (their "a bit", step length, speed, the
    // command to go back from), and the mic starts muted again.
    public void ResetForParticipant()
    {
        if (_resolver != null) _resolver.ResetSession();
        DropSpeech();
        if (!_isMicMuted) ToggleMicMute();
    }

    // One transcript line, for the session.
    public void Log(string kind, string details) => Record(kind, details);

    private void ToggleMicMute()
    {
        if (microphoneRecord == null || whisperManager == null) return;
        if (_isMicMuted && condition == StudyCondition.Manual) return; // no voice commands in Manual

        // Unmuting without microphone permission would only record silence: ask again instead.
        if (_isMicMuted && !HasMicPermission())
        {
            Record("BUTTON", "Unmute (no microphone permission yet, asking)");
            SetOutputFeedback("Please allow microphone access, then press Unmute again.", Color.yellow);
            RequestMicPermission();
            return;
        }

        _isMicMuted = !_isMicMuted;
        Record("BUTTON", _isMicMuted ? "Mute (mic off)" : "Unmute (mic on)");
        // Muting only stops listening. What was already said is still carried out: a sentence in progress
        // ends here (as if the speaker had paused) and goes on to Whisper, and anything already with
        // Whisper or the LLM finishes. Only Cancel (B) throws speech away.
        _pendingPartial = null;
        _pendingPause = null;

        if (_isMicMuted)
        {
            // Muted state
            bool working = _transcribing || _pendingSentences.Count > 0 || (_resolver != null && _resolver.IsThinking);
            if (_segmenter != null && _segmenter.InSentence)
            {
                _cutByMute = true;
                if (_segmenter.Flush()) Record("MIC", "Muted mid-sentence: what was said so far is still transcribed and carried out");
                else _cutByMute = false;
            }
            else _segmenter?.Reset(); // nothing being said: drop the few silent frames kept before speech
            if (working) Record("MIC", "Muted while a command was still being worked on: it carries on");
            _speechStarted = false;
            if (microphoneRecord.IsRecording) microphoneRecord.StopRecord();
            microphoneRecord.enabled = false;

            if (micButtonText != null) micButtonText.text = "Unmute";
            ShowMic();
            Debug.Log("<color=yellow><b>[Mic Muted]</b> Speech recognition paused.</color>");
            if (working || _cutByMute) SetOutputFeedback(StillWorking, Color.white);
            else ClearUI();
        }
        else
        {
            // Unmuted state (Activates speech-to-text loop)
            if (_resolver != null) _router = NewRouter();
            microphoneRecord.enabled = true;
            microphoneRecord.StartRecord();

            if (micButtonText != null) micButtonText.text = "Mute";
            ShowMic();
            string devices = Microphone.devices.Length == 0 ? "none found" : string.Join(", ", Microphone.devices);
            string stt = _pcSpeech != null && _pcSpeech.Ready ? "on the PC" : LocalWhisper;
            Debug.Log("<color=green><b>[Mic Unmuted]</b> Listening continuously...</color> Microphones: " + devices
                      + ". Recording: " + microphoneRecord.IsRecording + ". Whisper model loaded: " + whisperManager.IsLoaded + ". Speech to text: " + stt);
            Record("MIC", "Microphones: " + devices + ". Recording: " + microphoneRecord.IsRecording + ". Whisper model loaded: " + whisperManager.IsLoaded + ". Speech to text: " + stt);
        }
    }

    // ---------- The bridge on the PC (cable or Wi-Fi) ----------

    // The bridge in use: the one BridgeLink found, else Bridge Url (the cable, or this PC in the Editor).
    private string BridgeNow => _bridge != null && _bridge.Url != null ? _bridge.Url : bridgeUrl.TrimEnd('/');

    // The bridge was found, moved (cable <-> Wi-Fi) or lost: point the LLM, the speech and the transcript at it.
    private void OnBridgeChanged(string url)
    {
        if (url == null)
        {
            Record("BRIDGE", "Lost the PC. Looking for it again");
            if (!Application.isEditor) SetOutputFeedback("Lost the connection to the PC. Looking for it...", Color.yellow);
            return;
        }

        string how = _bridge.OverWifi ? "over Wi-Fi" : Application.isEditor ? "on this PC" : "over the cable";
        _transcript?.SendTo(url + "/log"); // first, so the bridge also gets the line below
        Debug.Log($"[Bridge] Connected to the PC ({_bridge.PcName}) {how}: {url}");
        Record("BRIDGE", $"Connected to the PC ({_bridge.PcName}) {how}: {url}");
        if (_ollama != null) _ollama.UseEndpoint(url + "/api/chat");
        if (_pcSpeech != null) _pcSpeech.Connect(url);
        if (!Application.isEditor) SetOutputFeedback("Connected to the PC " + how, Color.white);
    }

    // ---------- Microphone permission (Android / Quest) ----------

    private static bool HasMicPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
        return true;
#endif
    }

    private void RequestMicPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Record("MIC", "Microphone permission: granted");
            return;
        }

        // The answer can arrive on an Android thread, so it is only noted here and handled in Update.
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => _micPermissionAnswer = 1;
        callbacks.PermissionDenied += _ => _micPermissionAnswer = 2;
        Permission.RequestUserPermission(Permission.Microphone, callbacks);
#endif
    }

    private volatile int _micPermissionAnswer; // 0 = none yet, 1 = granted, 2 = denied

    private void HandleMicPermissionAnswer()
    {
        int answer = _micPermissionAnswer;
        if (answer == 0) return;
        _micPermissionAnswer = 0;

        if (answer == 1)
        {
            Debug.Log("[Mic] Permission granted");
            Record("MIC", "Microphone permission: granted by the user");
        }
        else
        {
            Debug.LogWarning("[Mic] Permission denied");
            Record("MIC", "Microphone permission: denied");
            SetOutputFeedback("Microphone access was denied. Allow it in the headset's settings to use voice.", Color.red);
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
        if (condition == StudyCondition.Manual) return; // Manual participants think aloud: that's not for the system
        if (_segmenter == null || _segmenter.SampleRate != frequency)
        {
            _segmenter = new SpeechSegmenter(frequency);
            _segmenter.OnPartial += audio =>
            {
                if (!_speechStarted) NoteSpeechStarted();
                QueueTranscription(audio, false);
            };
            _segmenter.OnPause += audio =>
            {
                if (!useLLM) return;
                _pendingPause = audio;
                _pauseSentence = _segmenter.SentenceNumber;
                _pauseVoiced = _segmenter.VoicedFrames;
                if (!_transcribing) TranscribeQueued();
            };
            _segmenter.OnSentence += audio =>
            {
                _speechStarted = false;
                _pendingPause = null; // the whole sentence is here: it decides
                Record("SPEECH", $"Sentence ended: {Seconds(audio):F1} s, queued for Whisper" + (_transcribing ? " (Whisper is busy, it waits)" : ""));
                QueueTranscription(audio, true);
            };
        }
        _segmenter.DeadAirSec = deadAirSec;
        _segmenter.EarlyEndSec = earlyEndSec;
        _segmenter.AddSamples(samples);
    }

    // The participant started speaking: in the study, their first time in a world starts its clock.
    private void NoteSpeechStarted()
    {
        _speechStarted = true;
        Record("SPEECH", "Started" + (_transcribing ? " (Whisper is still busy with earlier audio)" : ""));
        _session?.Note("speech_start", "", "", "", -1f, true);
    }

    // Every finished sentence is transcribed, in the order spoken. Of the sentence-so-far pieces only the
    // newest is worth it, and only while no finished sentence is waiting.
    private void QueueTranscription(float[] audio, bool isSentence)
    {
        if (isSentence)
        {
            _pendingSentences.Enqueue(audio);
            _pendingPartial = null;
        }
        else if (_pendingSentences.Count == 0) _pendingPartial = audio;

        if (!_transcribing) TranscribeQueued();
    }

    // Where Whisper runs when it isn't on the PC (for the transcript).
    private static string LocalWhisper => Application.isEditor ? "in Unity on this PC" : "on the headset";

    private async void TranscribeQueued()
    {
        _transcribing = true;
        while (_pendingSentences.Count > 0 || _pendingPause != null || _pendingPartial != null)
        {
            bool isSentence = _pendingSentences.Count > 0;
            bool isPause = !isSentence && _pendingPause != null;
            float[] audio = isSentence ? _pendingSentences.Dequeue() : isPause ? _pendingPause : _pendingPartial;
            int pauseSentence = _pauseSentence, pauseVoiced = _pauseVoiced;
            if (isPause) { _pendingPause = null; _pendingPartial = null; } // the pause piece covers the partial too
            else if (!isSentence) _pendingPartial = null;
            int generation = _speechGeneration;
            int sampleRate = _segmenter.SampleRate;
            float[] padded = PadForWhisper(audio, sampleRate);

            string piece = isSentence ? "sentence" : isPause ? "pause piece" : "so-far piece";
            float whisperStarted = Time.realtimeSinceStartup;
            string text = null;
            string where = "on the PC";

            // The headset's own Whisper takes about 15 s. Worth it for a finished sentence the PC can't do,
            // never for the sentence so far (live caption, early halt): it would hold up everything behind it.
            if (Application.isMobilePlatform && !(_pcSpeech != null && _pcSpeech.Ready))
            {
                if (!isSentence) continue;
                // Connected, but the PC's speech to text isn't ready this moment (e.g. just moved between
                // cable and Wi-Fi): give it a moment before falling back.
                float waitUntil = Time.realtimeSinceStartup + 2f;
                while (_pcSpeech != null && !_pcSpeech.Ready && _bridge != null && _bridge.Url != null && Time.realtimeSinceStartup < waitUntil)
                    await System.Threading.Tasks.Task.Delay(50);
                if (this == null) return;
            }

            // On the PC when it can (fast); otherwise, or if it fails, here.
            if (_pcSpeech != null && _pcSpeech.Ready)
            {
                text = await _pcSpeech.TranscribeAsync(padded, sampleRate, isSentence);
                if (this == null) return; // destroyed while waiting for the PC
                if (text == null)
                    Record("WHISPER", $"The PC couldn't transcribe a {piece}, so it's done {LocalWhisper} ({_pcSpeech.WhyNot})");
            }
            if (text == null)
            {
                where = LocalWhisper;
                WhisperResult result;
                try
                {
                    result = await whisperManager.GetTextAsync(padded, sampleRate, 1);
                }
                catch (System.Exception e)
                {
                    if (this == null) return;
                    Debug.LogWarning("[Speech] Whisper failed: " + e);
                    Record("ERROR", $"Whisper failed on a {piece}: {e.Message}");
                    continue;
                }
                if (this == null) return; // destroyed while Whisper was working
                if (result == null)
                {
                    Record("WHISPER", $"Returned nothing for a {Seconds(audio):F1} s {piece} (after {Time.realtimeSinceStartup - whisperStarted:F1} s)");
                    continue;
                }
                text = result.Result;
            }
            float whisperSeconds = Time.realtimeSinceStartup - whisperStarted;

            // Say why a transcription isn't used, and flag slow ones, so the transcript shows what Whisper did.
            if (generation != _speechGeneration)
            {
                Record("DROPPED", $"{piece} \"{(text ?? "").Trim()}\": finished {whisperSeconds:F1} s after it started, but the command was cancelled in between");
                continue;
            }
            if (!isSentence && _isMicMuted) continue; // a live caption for speech that has ended: nothing to show
            if (!isSentence && whisperSeconds > SlowWhisperSec)
                Record("WHISPER", $"Slow: a {Seconds(audio):F1} s so-far piece took {whisperSeconds:F1} s {where} (\"{(text ?? "").Trim()}\")");

            if (logSpeech) Debug.Log("[Speech] " + (isSentence ? "sentence" : "so far") + " (" + Seconds(audio).ToString("F1") + " s, " + where + "): \"" + text + "\"");
            if (isSentence)
            {
                // The last sentence before muting may be cut off: the LLM is told, so it asks rather than guesses.
                if (_cutByMute && _pendingSentences.Count == 0)
                {
                    _cutByMute = false;
                    if (_resolver != null) _resolver.NextIsCutOff = true;
                }
                OnSentenceHeard(text, Seconds(audio), whisperSeconds, where);
            }
            else if (isPause) OnPauseHeard(text, pauseSentence, pauseVoiced, Seconds(audio), whisperSeconds, where);
            else OnPartialHeard(text);
        }
        _transcribing = false;
    }

    // The sentence so far at an earlyEndSec pause. If the speaker hasn't carried on and it's a whole
    // open-ended move ("move forward", "move back"), it runs now and the sentence ends here, as if the full
    // deadAirSec had passed; anything else ("turn left", "move forward two steps", or more words coming)
    // waits for the sentence to end as usual.
    private void OnPauseHeard(string text, int sentenceNumber, int voicedFrames, float speechSeconds, float whisperSeconds, string where)
    {
        OnPartialHeard(text); // the live caption and the halt check, as for any piece of the sentence
        if (_segmenter == null || !_segmenter.InSentence || _segmenter.SentenceNumber != sentenceNumber
            || _segmenter.VoicedFrames != voicedFrames)
            return; // they carried on speaking, or the sentence already ended: the whole sentence decides
        string clean = TranscriptFilter.Clean(text);
        // "Go back" reads as a move back, but it's the undo (GoBackWords): it waits for the whole sentence.
        if (clean == null || GoBackWords.IsGoBack(clean) || !QuickCommands.IsOpenEndedMove(QuickCommands.TryParse(clean))) return;

        _segmenter.EndNow();
        _speechStarted = false;
        _router?.EndSentence();
        Record("SPEECH", $"Sentence ended early: an open-ended move after a {earlyEndSec:F1} s pause ({speechSeconds:F1} s of speech)");
        OnSentenceHeard(text, speechSeconds, whisperSeconds, where);
    }

    private float Seconds(float[] audio)
    {
        return _segmenter != null && _segmenter.SampleRate > 0 ? audio.Length / (float)_segmenter.SampleRate : 0f;
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
        if (transcriptPartials && soFar != null) Record("SO FAR", soFar.ToLower() + " ...");

        if (useLLM && _router != null) _router.OnPartial(text);
    }

    // A finished sentence (it ended with deadAirSec of silence).
    // speechSeconds / whisperSeconds / where: how long the sentence was, how long Whisper took and where it ran (for the transcript).
    private void OnSentenceHeard(string text, float speechSeconds = -1f, float whisperSeconds = -1f, string where = null)
    {
        // Drops Whisper's notes for non-speech ("[BLANK_AUDIO]") and the stock phrases it invents from noise ("Thank you.").
        string spokenText = TranscriptFilter.Clean(text);
        string timing = whisperSeconds >= 0f ? $"  [{speechSeconds:F1} s of speech, transcribed in {whisperSeconds:F2} s{(where != null ? " " + where : "")}]" : "";
        Record("HEARD", (spokenText != null
            ? "\"" + spokenText.ToLower() + "\"  (Whisper: \"" + (text ?? "").Trim() + "\")"
            : "ignored as noise  (Whisper: \"" + (text ?? "").Trim() + "\")") + timing);
        _session?.Note("heard", spokenText != null ? spokenText.ToLower() : (text ?? "").Trim(), "", spokenText != null ? "" : "ignored as noise");
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

        // What was finished off by muting turned out to be nothing (silence, "uh"): don't leave "still working" up.
        if (spokenText == null && outputText != null && outputText.text == StillWorking) ClearUI();

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
        if (!string.IsNullOrEmpty(bridgeUrl)) _ollama.UseEndpoint(BridgeNow + "/api/chat");
        _ollama.OnLatency += seconds => Record("LLM", "Answered in " + seconds.ToString("F2") + " s");
        _resolver = GetOrAdd<SemanticIntentResolver>();
        GetOrAdd<Highlighter>(); // makes the options glow when the system asks "which one?"
        _vignette = GetOrAdd<ComfortVignette>(); // darkens the edges of the view while turning
        _stepBob = GetOrAdd<StepBob>(); // the slight up-and-down of walking, for step commands

        _resolver.OnCommandResolved += HandleCommandResolved;
        _resolver.OnClarificationNeeded += HandleQuestion;
        _resolver.OnStatus += HandleNotice;
        _resolver.OnError += HandleError;
        _resolver.OnSpeedChanged += level => ShowSpeed(level, true);
        _resolver.OnIgnored += HandleIgnored;
        // How each command's movement ended, for the study's world log.
        _navigation = GetOrAdd<NavigationController>();
        _navigation.OnCommandFinished += HandleCommandFinished;
        _navigation.OnDetour += HandleDetour;

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

    // The speed in use ("faster" / "slower" by voice), and says so.
    private void ShowSpeed(SpeedLevel level, bool announce)
    {
        moveSpeed = FixedPhraseSpeeds[(int)level];
        if (!announce) return;
        SetOutputFeedback("Speed: " + level.ToString().ToLower(), true);
        _session?.Note("speed", "", level.ToString().ToLower(), "");
    }

    // ---------- Study condition and the A button ----------

    // ROVR: voice on (the mic still starts muted), sticks off. Manual: sticks on, mic muted and staying muted.
    public void SetCondition(StudyCondition next)
    {
        condition = next;
        if (_resolver != null) _resolver.HaltNow(true);
        if (_manual != null) _manual.enabled = next == StudyCondition.Manual;
        if (next == StudyCondition.Manual) { DropSpeech(); if (!_isMicMuted) ToggleMicMute(); }
        if (micMuteButton != null) micMuteButton.interactable = next == StudyCondition.ROVR;
        ControlsDiagram.Show(_panelDiagram, next);
        Record("SESSION", "Condition: " + (next == StudyCondition.ROVR ? "ROVR (voice)" : "Manual (controller)"));
    }

    // The XR rig (XRI Starter Assets) comes with its own stick controls. Its Locomotion object is off in
    // the scene, but on the right controller pushing the stick forward still brought up the teleport arc and
    // took the UI pointer away from that hand, while nothing teleported. "Smooth motion" turns the rig's
    // teleport and turn inputs off for good; the move input it enables drives nothing, as its provider is
    // in the switched-off Locomotion object. Only ManualLocomotion (Manual) moves with the sticks.
    private void TurnOffRigLocomotion()
    {
        var managers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var manager in managers) manager.smoothMotionEnabled = true;
        if (managers.Length > 0) Record("SESSION", "Rig stick controls (teleport, turn) off on " + managers.Length + " controller(s)");
    }

    private void OnAShortPress()
    {
        if (CardShown)
        {
            if (!tutorialCard.Closable) return; // the waiting card stays until the session is set up
            Record("BUTTON", "A: closed the tutorial");
            tutorialCard.Hide();
            return;
        }
        if (condition != StudyCondition.ROVR) return; // in Manual, A only shows / hides the panel (long press)
        Record("BUTTON", "A: " + (_isMicMuted ? "unmute" : "mute"));
        ToggleMicMute();
        _session?.Note("mic", _isMicMuted ? "off" : "on", "", "");
    }

    public bool PanelShown => _panelShown;
    public bool MicMuted => _isMicMuted;
    public bool CardShown => tutorialCard != null && tutorialCard.Shown;

    private void TogglePanel() => SetPanelShown(!_panelShown, true);

    // Shows or hides the whole panel. The canvas stays alive (this script lives on it), it just isn't
    // drawn or clickable. Shown, it comes up straight in front of the user, then follows lazily.
    private void SetPanelShown(bool shown, bool record)
    {
        _panelShown = shown;
        var canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = shown;
        foreach (var raycaster in GetComponents<UnityEngine.EventSystems.BaseRaycaster>()) raycaster.enabled = shown;
        var follow = GetComponent<LazyFollow>();
        if (shown && follow != null) follow.SnapNow();
        if (record) Record("BUTTON", "A held: panel " + (shown ? "shown" : "hidden"));
    }

    public void ShowTutorial()
    {
        if (tutorialCard == null) return;
        tutorialCard.Show(condition);
        Record("PANEL", "Tutorial card: " + condition);
    }

    // The mic icon: the mic, or the mic with a red slash when muted.
    private void ShowMic()
    {
        if (micIcon == null) return;
        var sprite = _isMicMuted ? micOffSprite : micOnSprite;
        if (sprite != null) micIcon.sprite = sprite;
    }

    // ---------- Cancel (B) ----------

    private const string Working = "Working on it...";
    private const string StillWorking = "Mic off. Still working on what you said...";

    // B: the command is cancelled. Movement stops at once, speech not yet acted on is thrown away (the
    // sentence being spoken, anything with Whisper or the LLM), and any question is dropped. The mic stays
    // as it is: muting is a separate thing.
    private void OnBPress()
    {
        if (CardShown || condition != StudyCondition.ROVR) return;
        Record("BUTTON", "B: cancel the command");
        CancelCommand();
    }

    public void CancelCommand()
    {
        DropSpeech();
        if (_resolver != null) _resolver.CancelCommand();
        _moveTimer = 0f;
        SetOutputFeedback("Cancelled", Color.white);
        _session?.Note("cancel", "", "cancel button", "cancelled");
    }

    // Throws away speech not yet acted on: what's being said, and what's waiting for or with Whisper.
    private void DropSpeech()
    {
        _speechGeneration++;
        _pendingSentences.Clear();
        _pendingPartial = null;
        _pendingPause = null;
        _segmenter?.Reset();
        _speechStarted = false;
        _cutByMute = false;
        if (_resolver != null) _resolver.NextIsCutOff = false;
    }

    // The Comfort settings, on whatever moves the body (they can be changed during Play).
    private void ApplyComfort()
    {
        if (_navigation != null)
        {
            _navigation.acceleration = acceleration;
            _navigation.deceleration = deceleration;
            _navigation.stopDeceleration = stopDeceleration;
            _navigation.snapTurning = snapTurning;
        }
        if (_manual != null)
        {
            _manual.acceleration = acceleration * 1.5f; // the stick asks for speed directly: a little snappier
            _manual.deceleration = deceleration * 1.6f;
            _manual.snapTurning = snapTurning;
        }
        if (_vignette != null) _vignette.duringMovement = movementVignette;
        if (_resolver != null && !Mathf.Approximately(_resolver.StepMeters, stepMeters)) _resolver.StepMeters = stepMeters;
    }

    private ComfortVignette _vignette;

    // A halt word, heard while the sentence was still being spoken: stop, and end the sentence right there.
    // The rest of what was being said, and anything still waiting for Whisper, is dropped (the halt wins), so
    // what the user says next is a new sentence of its own: a follow-up command, not part of the "stop".
    private void HaltFromVoice()
    {
        _speechGeneration++;
        _pendingSentences.Clear();
        _pendingPartial = null;
        _pendingPause = null;
        _segmenter?.Reset();
        _speechStarted = false;
        _cutByMute = false;
        _router?.EndSentence();
        Record("HALT", "Halt word heard while speaking: stopping, and the sentence ends here");
        _session?.Note("halt", "", "halt word while speaking", "stopped");
        _resolver.HaltNow();
        SetOutputFeedback("Stopped", true);
    }

    private void SubmitToLLM(string sentence)
    {
        var interrupt = InterruptModule.Evaluate(sentence);
        if (interrupt.isHalt && string.IsNullOrEmpty(interrupt.remainder))
            SetOutputFeedback("Stopped", true);
        else
            SetOutputFeedback(Working, Color.white); // heard, being worked out

        _resolver.SubmitUtterance(sentence);
    }

    private void HandleCommandResolved(NavigationCommand command)
    {
        if (command.IsClarification) return;
        string description = Describe(command);
        // Understood and started; a command still running is interrupted by it (and not resumed).
        bool interrupting = _navigation != null && _navigation.IsBusy;
        if (interrupting) Record("COMMAND", "Interrupts the running command");
        SetOutputFeedback((interrupting ? "Switching: " : "") + description, true);
        _session?.Note("command", "", description, "");

        // Steps are the user's own (learned) length: the transcript says how far that was.
        foreach (var step in command.steps)
            if (step.action == ActionType.Move && step.InSteps)
                Record("STEPS", step.HasMagnitude
                    ? $"{step.magnitude:0.#} x {step.stepLength:F2} m = {step.magnitude * step.stepLength:F2} m"
                    : $"steps of {step.stepLength:F2} m, no count");
    }

    private void HandleQuestion(string question)
    {
        SetOutputFeedback(question, Color.yellow);
        _session?.Note("question", "", question, "");
    }

    private void HandleNotice(string notice)
    {
        SetOutputFeedback(notice, Color.yellow);
        _session?.Note("notice", "", notice, "");
    }

    private void HandleJumped()
    {
        Record("JUMP", "Jumped");
        _session?.Note("jump", "", "", "");
    }

    // ("shown", "House"), ("grabbed", "hand"), ("let go", where).
    private void HandleMinimap(string what, string details)
    {
        Record("MINIMAP", what + ": " + details);
        _session?.Note("minimap", what, details, "");
    }

    private void HandlePointedAt(string name)
    {
        Record("POINTER", "On the " + name);
        _session?.Note("pointer", name, "", "");
    }

    // On the way to an object, something was in the way and ROVR goes round it.
    private void HandleDetour(string what)
    {
        string line = what == "something" ? "Going round what's in the way" : "Going round the " + what;
        SetOutputFeedback(line, Color.white);
        _session?.Note("notice", "", line, "");
    }

    // Side talk (SideTalk): logged, not acted on, and the panel's line stays as it was.
    private void HandleIgnored(string sentence)
    {
        Record("IGNORED", $"\"{sentence}\": not a command, so not sent to the LLM");
        _session?.Note("ignored", sentence.ToLowerInvariant(), "not a command", "ignored");
    }

    // How a command's movement ended: completed, halted, blocked or not found.
    private void HandleCommandFinished(NavigationCommand command, CommandOutcome outcome)
    {
        string result = outcome == CommandOutcome.NotFound ? "not found" : outcome.ToString().ToLowerInvariant();
        _session?.Note("move_end", "", command != null ? Describe(command) : "", result);
        // Completed: say so, unless something newer is already on the line (a question, the next command).
        if (outcome != CommandOutcome.Completed || command == null || outputText == null) return;
        string description = Describe(command);
        if (outputText.text.EndsWith(description))
            SetOutputFeedback("Done: " + char.ToLower(description[0]) + description.Substring(1), true);
    }

    private void HandleError(string error)
    {
        Debug.LogWarning("[LLM] " + error);
        Record("ERROR", "LLM: " + error);
        _session?.Note("error", "", error, "");
        // A model that was just started can take over 30 s to answer the first time, so a timeout isn't "not running".
        string message = error.Contains("Request timeout") ? "The language model is taking too long (still loading?). Please say that again."
            : error.StartsWith("Ollama request failed") && !Application.isEditor && _bridge != null && _bridge.Url == null
                ? "Can't find the PC. Is the bridge running, and is the headset on the same Wi-Fi?"
            : error.StartsWith("Ollama request failed") ? "Can't reach the language model. Is Ollama running?"
            : "Something went wrong. Please say that again.";
        SetOutputFeedback(message, false);
    }

    // "Moving forward 5 m", "Moving left a bit", "Moving forward to the door, then turning right",
    // "Taking 3 steps forward", "Taking 3 steps toward the tree", "Walking in steps to the door"
    private string Describe(NavigationCommand command)
    {
        if (!string.IsNullOrEmpty(command.summary)) return command.summary; // made by the engine ("Going back")
        var parts = new System.Collections.Generic.List<string>();
        foreach (var step in command.steps)
        {
            string direction = step.direction == DirectionType.None ? "" : " " + step.direction.ToString().ToLower();
            // The target can be one particular object ("tree-3"); say what it is ("tree").
            string target = step.HasCondition ? _grounding.NameFor(step.condition).ToLower() : "";
            string pace = step.speed == SpeedLevel.Slow ? ", slowly" : step.speed == SpeedLevel.Fast ? ", fast" : "";
            switch (step.action)
            {
                case ActionType.Move when step.InSteps:
                    string to = !step.HasCondition ? "" : step.until == UntilType.See ? " until I see the " + target
                        : (step.HasMagnitude ? " toward the " : " to the ") + target;
                    string count = step.magnitude.ToString("0.#") + (step.magnitude == 1f ? " step" : " steps");
                    parts.Add(step.HasMagnitude
                        ? "taking " + count + (step.HasCondition ? "" : direction) + to + pace
                        : "walking" + (step.HasCondition ? "" : direction) + " in steps" + to + pace);
                    break;
                case ActionType.Move:
                    string how = step.HasCondition ? (step.until == UntilType.See ? " until I see the " : " to the ") + target
                        : step.amount == AmountType.Small ? " a bit"
                        : step.HasMagnitude ? " " + step.magnitude.ToString("0.#") + " m"
                        : "";
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
                case ActionType.GoTo:
                    parts.Add("going there");
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
        if (message != Working) Record("PANEL", message); // just "waiting on the LLM"
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

    // One transcript line. Does nothing when saveTranscript is off.
    private void Record(string kind, string details)
    {
        _transcript?.Log(kind, details);
    }

    private void OnDestroy()
    {
        _transcript?.End();
        _transcript = null;
        if (_bridge != null) _bridge.OnChanged -= OnBridgeChanged;
        if (_aButton != null) { _aButton.OnShortPress -= OnAShortPress; _aButton.OnLongPress -= TogglePanel; }
        if (_bButton != null) { _bButton.OnShortPress -= OnBPress; _bButton.OnLongPress -= OnBPress; }

        if (_resolver != null)
        {
            _resolver.OnCommandResolved -= HandleCommandResolved;
            _resolver.OnClarificationNeeded -= HandleQuestion;
            _resolver.OnStatus -= HandleNotice;
            _resolver.OnError -= HandleError;
            _resolver.OnIgnored -= HandleIgnored;
        }
        if (_navigation != null) { _navigation.OnCommandFinished -= HandleCommandFinished; _navigation.OnDetour -= HandleDetour; }
        if (_gravity != null) _gravity.OnJumped -= HandleJumped;
        WorldMinimap.Logged -= HandleMinimap;
        PointerLook.PointedAt -= HandlePointedAt;
    }
}
