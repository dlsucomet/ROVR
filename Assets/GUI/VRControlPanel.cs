using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
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

    // Speech: microphone audio -> sentences (SpeechSegmenter) -> text (Whisper on the PC, or here), one transcription at a time
    private SpeechSegmenter _segmenter;
    private readonly System.Collections.Generic.Queue<float[]> _pendingSentences = new System.Collections.Generic.Queue<float[]>();
    private float[] _pendingPartial; // the newest sentence-so-far waiting for Whisper
    private PCSpeechToText _pcSpeech; // null when transcribeOnPC is off
    private BridgeLink _bridge;       // where the bridge is (cable or Wi-Fi); null when Bridge Url is empty
    private bool _transcribing;
    private bool _speechStarted; // a sentence is being spoken (for the SPEECH transcript lines)
    private const float SlowWhisperSec = 3f; // "so far" transcriptions slower than this are logged
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

        // The Maze minimap (corner of the view, only in the Maze), for both voice and fixed-phrase modes.
        if (FindAnyObjectByType<MazeMinimap>() == null) gameObject.AddComponent<MazeMinimap>();

        // Force initial spawn rotation to plain destination facing 0 degrees Y
        TeleportTo(plainDestination, 0f, "Plain", "at start");

        // Hook up button listeners with specific Y rotation constraints
        if (plainButton != null) plainButton.onClick.AddListener(() => { Record("BUTTON", "Plain"); TeleportTo(plainDestination, 0f, "Plain", "button"); });
        if (mazeButton != null) mazeButton.onClick.AddListener(() => { Record("BUTTON", "Maze"); TeleportTo(mazeDestination, 90f, "Maze", "button"); });
        if (houseButton != null) houseButton.onClick.AddListener(() => { Record("BUTTON", "House"); TeleportTo(houseDestination, 0f, "House", "button"); });

        if (slowButton != null) slowButton.onClick.AddListener(() => { Record("BUTTON", "Slow"); SetSpeed(SpeedLevel.Slow); });
        if (normalButton != null) normalButton.onClick.AddListener(() => { Record("BUTTON", "Normal"); SetSpeed(SpeedLevel.Normal); });
        if (fastButton != null) fastButton.onClick.AddListener(() => { Record("BUTTON", "Fast"); SetSpeed(SpeedLevel.Fast); });
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

            if (transcribeOnPC && !string.IsNullOrEmpty(bridgeUrl))
            {
                _pcSpeech = GetComponent<PCSpeechToText>();
                if (_pcSpeech == null) _pcSpeech = gameObject.AddComponent<PCSpeechToText>();
                _pcSpeech.OnReadyChanged += (ready, whyNot) => Record("STT", ready
                    ? "Speech to text: on the PC, through the bridge"
                    : "Speech to text: " + LocalWhisper + ", because " + whyNot + ". The PC is asked again every " + PCSpeechToText.RetrySec + " s");
                _pcSpeech.Connect(BridgeNow);
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

        ClearUI();
    }

    private void Update()
    {
        if (_ollama != null) _ollama.LogExchanges = logLLM;
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
            Vector3 p = destination.position;
            Record("TELEPORT", $"{world} ({reason}): to {destination.name} at ({p.x:0.##}, {p.y:0.##}, {p.z:0.##}), facing {targetYRotation}°");
        }
        else
        {
            Debug.LogWarning("[Teleport] Destination transform is not assigned!");
            Record("TELEPORT", $"{world} ({reason}): failed, no destination assigned");
        }
    }

    private void ToggleMicMute()
    {
        if (microphoneRecord == null || whisperManager == null) return;

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
        // A sentence cut off by muting must not carry over, and late transcriptions are dropped.
        _speechGeneration++;
        _pendingSentences.Clear();
        _pendingPartial = null;
        _segmenter?.Reset();

        if (_isMicMuted)
        {
            // Muted state
            if (_transcribing) Record("MIC", "Muted while Whisper was still working: its result will be dropped");
            _speechStarted = false;
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
        if (_segmenter == null || _segmenter.SampleRate != frequency)
        {
            _segmenter = new SpeechSegmenter(frequency);
            _segmenter.OnPartial += audio =>
            {
                if (!_speechStarted)
                {
                    _speechStarted = true;
                    Record("SPEECH", "Started" + (_transcribing ? " (Whisper is still busy with earlier audio)" : ""));
                }
                QueueTranscription(audio, false);
            };
            _segmenter.OnSentence += audio =>
            {
                _speechStarted = false;
                Record("SPEECH", $"Sentence ended: {Seconds(audio):F1} s, queued for Whisper" + (_transcribing ? " (Whisper is busy, it waits)" : ""));
                QueueTranscription(audio, true);
            };
        }
        _segmenter.DeadAirSec = deadAirSec;
        _segmenter.AddSamples(samples);
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
        while (_pendingSentences.Count > 0 || _pendingPartial != null)
        {
            bool isSentence = _pendingSentences.Count > 0;
            float[] audio = isSentence ? _pendingSentences.Dequeue() : _pendingPartial;
            if (!isSentence) _pendingPartial = null;
            int generation = _speechGeneration;
            int sampleRate = _segmenter.SampleRate;
            float[] padded = PadForWhisper(audio, sampleRate);

            string piece = isSentence ? "sentence" : "so-far piece";
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
            if (generation != _speechGeneration || _isMicMuted)
            {
                Record("DROPPED", $"{piece} \"{(text ?? "").Trim()}\": finished {whisperSeconds:F1} s after it started, but the mic was muted in between");
                continue;
            }
            if (!isSentence && whisperSeconds > SlowWhisperSec)
                Record("WHISPER", $"Slow: a {Seconds(audio):F1} s so-far piece took {whisperSeconds:F1} s {where} (\"{(text ?? "").Trim()}\")");

            if (logSpeech) Debug.Log("[Speech] " + (isSentence ? "sentence" : "so far") + " (" + Seconds(audio).ToString("F1") + " s, " + where + "): \"" + text + "\"");
            if (isSentence) OnSentenceHeard(text, Seconds(audio), whisperSeconds, where);
            else OnPartialHeard(text);
        }
        _transcribing = false;
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
        if (!string.IsNullOrEmpty(bridgeUrl)) _ollama.UseEndpoint(BridgeNow + "/api/chat");
        _ollama.OnLatency += seconds => Record("LLM", "Answered in " + seconds.ToString("F2") + " s");
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
        Record("HALT", "Halt word heard while speaking");
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
        Record("ERROR", "LLM: " + error);
        // A model that was just started can take over 30 s to answer the first time, so a timeout isn't "not running".
        string message = error.Contains("Request timeout") ? "The language model is taking too long (still loading?). Please say that again."
            : error.StartsWith("Ollama request failed") && !Application.isEditor && _bridge != null && _bridge.Url == null
                ? "Can't find the PC. Is the bridge running, and is the headset on the same Wi-Fi?"
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
        if (message != "...") Record("PANEL", message); // "..." is just "waiting on the LLM"
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

        if (_resolver != null)
        {
            _resolver.OnCommandResolved -= HandleCommandResolved;
            _resolver.OnClarificationNeeded -= HandleQuestion;
            _resolver.OnStatus -= HandleNotice;
            _resolver.OnError -= HandleError;
        }
    }
}
