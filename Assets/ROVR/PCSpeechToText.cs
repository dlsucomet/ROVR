// Assets/ROVR/PCSpeechToText.cs
// Speech to text on the PC: sends the audio to the ROVR bridge (Bridge/rovr_bridge.py, POST /transcribe),
// where whisper.cpp transcribes it with the same model the headset has (ggml-base.bin). Whisper on the
// Quest's CPU took 14-17 s for a sentence; on the PC it takes about 0.3 s, plus a few ms over the cable.
//
// Ready says whether to use it. The bridge is asked (GET /transcribe) at start and every RetrySec while
// it isn't ready, in the background, so a missing bridge never delays speech: until it answers, and
// for RetrySec after any failure, the caller transcribes on the headset (or in the Editor) instead.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ROVR
{
    public class PCSpeechToText : MonoBehaviour
    {
        public const float RetrySec = 5f;
        const int TimeoutSec = 10; // a 15 s sentence takes well under 1 s on the PC

        string _url;
        UnityWebRequest _probe;
        float _probeAt;

        // True while the bridge says it can transcribe and the last transcription worked.
        public bool Ready { get; private set; }

        // Why it isn't ready (for the transcript), or null.
        public string WhyNot { get; private set; }

        // Ready changed: (ready, why not).
        public event Action<bool, string> OnReadyChanged;

        // "http://127.0.0.1:8765", the bridge.
        public void Connect(string bridgeUrl)
        {
            _url = bridgeUrl.TrimEnd('/') + "/transcribe";
            StopProbe();
            _probeAt = 0f; // ask the new address right away
        }

        void StopProbe()
        {
            _probe?.Abort();
            _probe?.Dispose();
            _probe = null;
        }

        void Update()
        {
            if (Ready || _url == null) return;
            if (_probe == null)
            {
                if (Time.realtimeSinceStartup < _probeAt) return;
                _probe = UnityWebRequest.Get(_url);
                _probe.timeout = 5;
                _probe.SendWebRequest();
                return;
            }
            if (!_probe.isDone) return;

            if (_probe.result == UnityWebRequest.Result.Success) SetReady(true, null);
            else
            {
                // 503 = the bridge runs, but without its Whisper server: it says why.
                string reason = _probe.responseCode == 503 && !string.IsNullOrEmpty(_probe.downloadHandler.text)
                    ? "the bridge has no speech to text: " + _probe.downloadHandler.text
                    : _probe.responseCode == 404 ? "the bridge is an older one, without speech to text"
                    : "the bridge isn't reachable (" + _probe.error + ")";
                SetReady(false, reason);
                _probeAt = Time.realtimeSinceStartup + RetrySec;
            }
            _probe.Dispose();
            _probe = null;
        }

        void SetReady(bool ready, string whyNot)
        {
            bool changed = ready != Ready || whyNot != WhyNot;
            Ready = ready;
            WhyNot = whyNot;
            if (changed) OnReadyChanged?.Invoke(ready, whyNot);
        }

        [Serializable]
        class Reply
        {
            public string text;
        }

        // Mono audio -> its text ("" if nothing was said), or null if the PC couldn't do it (then WhyNot
        // says why, and Ready stays false until the bridge answers again). isSentence: a finished sentence,
        // which the bridge prints, rather than the sentence so far.
        public async Task<string> TranscribeAsync(float[] audio, int sampleRate, bool isSentence)
        {
            if (_url == null) return null;
            using (var request = new UnityWebRequest(_url + (isSentence ? "?kind=sentence" : "?kind=partial"), "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Wav(audio, sampleRate));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "audio/wav");
                request.timeout = TimeoutSec;

                var done = new TaskCompletionSource<bool>();
                request.SendWebRequest().completed += _ => done.TrySetResult(true);
                await done.Task;

                string reply = request.downloadHandler.text;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string text = JsonUtility.FromJson<Reply>(reply)?.text;
                        if (text != null) return text;
                    }
                    catch (ArgumentException) { }
                    Fail("the bridge's answer wasn't understood: " + Short(reply));
                }
                else Fail("the bridge couldn't transcribe it (" + request.error + (string.IsNullOrEmpty(reply) ? "" : ": " + Short(reply)) + ")");
                return null;
            }
        }

        void Fail(string why)
        {
            SetReady(false, why);
            _probeAt = Time.realtimeSinceStartup + RetrySec;
        }

        static string Short(string text)
        {
            text = (text ?? "").Trim();
            return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
        }

        // 16-bit PCM WAV, one channel. whisper.cpp resamples whatever rate it gets to 16 kHz (the
        // microphone already records at 16 kHz).
        public static byte[] Wav(float[] samples, int sampleRate)
        {
            int dataBytes = samples.Length * 2;
            using (var stream = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(stream)) // little-endian, as WAV wants
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);                // fmt chunk size
                w.Write((short)1);          // PCM
                w.Write((short)1);          // mono
                w.Write(sampleRate);
                w.Write(sampleRate * 2);    // bytes per second
                w.Write((short)2);          // bytes per sample
                w.Write((short)16);         // bits per sample
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                foreach (float s in samples)
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
                w.Flush();
                return stream.ToArray();
            }
        }

        void OnDestroy()
        {
            StopProbe();
        }
    }
}
