// Assets/ROVR/OllamaClient.cs
// Talks to a locally-hosted Ollama server (Section 6.3.1: "the ROVR architecture completely
// rejects cloud-based API dependencies in favor of a localized Large Language Model"). Pull the
// model once with `ollama pull gemma3n:e4b`, run `ollama serve`, and this hits it over loopback.
//
// Uses Ollama's structured-output support (`format` as a JSON schema, not just "json") so the
// model's response is constrained to NavigationCommandSchema.Json rather than merely encouraged
// toward it — this is what Section 6.3.1 means by "restrict the model's response to a strict
// JSON format."
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ROVR
{
    public class OllamaClient : MonoBehaviour
    {
        // 127.0.0.1, not "localhost": on Windows "localhost" tries IPv6 first and adds ~2 s to every request.
        [SerializeField] string endpoint = "http://127.0.0.1:11434/api/chat";
        [SerializeField] string model = "gemma3n:e4b";
        [SerializeField] int timeoutSeconds = 30;
        const string KeepAlive = "30m"; // keep the model resident between commands

        // Scenes saved with the old default still say "localhost"; fix that on the fly.
        string Endpoint => useDirect ? DirectEndpoint : endpoint.Replace("//localhost", "//127.0.0.1");

        // When the endpoint is the ROVR bridge (Bridge/rovr_bridge.py) and nothing answers there, the
        // client falls back to Ollama on this PC and stays with it for the session. That keeps the
        // Unity Editor working without the bridge. On the headset the fallback also fails, as it should.
        const string DirectEndpoint = "http://127.0.0.1:11434/api/chat";
        bool useDirect;

        // Sends requests to this URL instead of the Inspector's endpoint (the panel uses it for the bridge).
        public void UseEndpoint(string url)
        {
            endpoint = url;
            useDirect = false;
        }

        // Only "nothing is listening" falls back. A timeout means the server is there but slow
        // (the model still loading), so waiting again elsewhere would only double the wait.
        bool ShouldFallBack(UnityWebRequest request)
        {
            return !useDirect && Endpoint != DirectEndpoint
                && request.result == UnityWebRequest.Result.ConnectionError
                && (request.error == null || request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) < 0);
        }

        void SwitchToDirect()
        {
            Debug.LogWarning("[ROVR] The bridge at " + endpoint + " isn't running, so this session talks to Ollama directly. "
                             + "Start it with Tools > ROVR > Start Bridge to see the commands live.");
            useDirect = true;
        }

        static UnityWebRequest NewPost(string url, byte[] body, int timeout)
        {
            var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeout;
            return request;
        }

        [Serializable]
        class OllamaMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        class OllamaChatResponse
        {
            public OllamaMessage message;
        }

        // Seconds the last successful request took, for the thesis's latency testing.
        public float LastLatencySeconds { get; private set; }

        // Prints each command's prompt (what the user said and the objects in view) and the model's reply.
        public bool LogExchanges { get; set; }
        public event Action<float> OnLatency;

        public virtual void RequestCommand(string systemPrompt, string userPrompt, Action<NavigationCommand> onSuccess, Action<string> onError)
        {
            StartCoroutine(RequestCoroutine(systemPrompt, userPrompt, onSuccess, onError));
        }

        // Loads the model and runs one throwaway command in exactly the shape of a real one (same system
        // prompt and answer format), so the first spoken command isn't the one that pays for it. Right
        // after Ollama starts, that first full-size request was seen to take over 30 s; later ones ~0.5 s.
        // Without a system prompt it only loads the model (an empty message list does that in Ollama).
        public void WarmUp(string systemPrompt = null)
        {
            StartCoroutine(WarmUpCoroutine(systemPrompt));
        }

        IEnumerator WarmUpCoroutine(string systemPrompt)
        {
            string body = systemPrompt != null
                ? BuildRequestJson(systemPrompt, "Visible objects: []\nPrevious command: none\nCommand: \"stop\"")
                : "{\"model\":\"" + JsonEscape(model) + "\",\"messages\":[],\"keep_alive\":\"" + KeepAlive + "\"}";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
            var request = NewPost(Endpoint, bodyRaw, 120);
            try
            {
                yield return request.SendWebRequest();

                if (ShouldFallBack(request))
                {
                    request.Dispose();
                    request = NewPost(DirectEndpoint, bodyRaw, 120);
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) SwitchToDirect();
                }

                if (request.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning("[ROVR] Ollama warm-up failed: " + request.error);
            }
            finally
            {
                request.Dispose();
            }
        }

        IEnumerator RequestCoroutine(string systemPrompt, string userPrompt, Action<NavigationCommand> onSuccess, Action<string> onError)
        {
            string body = BuildRequestJson(systemPrompt, userPrompt);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);

            float sentAt = Time.realtimeSinceStartup;
            var request = NewPost(Endpoint, bodyRaw, timeoutSeconds);
            try
            {
                yield return request.SendWebRequest();

                if (ShouldFallBack(request))
                {
                    request.Dispose();
                    request = NewPost(DirectEndpoint, bodyRaw, timeoutSeconds);
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) SwitchToDirect();
                }
                LastLatencySeconds = Time.realtimeSinceStartup - sentAt;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Ollama request failed ({request.result}): {request.error}. " +
                                     "Is `ollama serve` running and has `ollama pull gemma3n:e4b` completed?");
                    yield break;
                }

                OllamaChatResponse chatResponse;
                try
                {
                    chatResponse = JsonUtility.FromJson<OllamaChatResponse>(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse Ollama response envelope: {e.Message}\nRaw: {request.downloadHandler.text}");
                    yield break;
                }

                if (chatResponse?.message == null || string.IsNullOrEmpty(chatResponse.message.content))
                {
                    onError?.Invoke($"Ollama response had no message content.\nRaw: {request.downloadHandler.text}");
                    yield break;
                }

                RawNavigationCommand raw;
                try
                {
                    raw = JsonUtility.FromJson<RawNavigationCommand>(chatResponse.message.content);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Model did not return valid navigation-command JSON: {e.Message}\nContent: {chatResponse.message.content}");
                    yield break;
                }

                if (LogExchanges)
                    Debug.Log("[LLM] " + LastLatencySeconds.ToString("F2") + " s\n" + userPrompt + "\n-> " + chatResponse.message.content);
                OnLatency?.Invoke(LastLatencySeconds);
                onSuccess?.Invoke(NavigationCommand.FromRaw(raw));
            }
            finally
            {
                request.Dispose();
            }
        }

        string BuildRequestJson(string systemPrompt, string userPrompt)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"model\":\"").Append(JsonEscape(model)).Append("\",");
            sb.Append("\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":\"").Append(JsonEscape(systemPrompt)).Append("\"},");
            sb.Append("{\"role\":\"user\",\"content\":\"").Append(JsonEscape(userPrompt)).Append("\"}");
            sb.Append("],");
            sb.Append("\"stream\":false,");
            sb.Append("\"format\":").Append(NavigationCommandSchema.Json).Append(',');
            sb.Append("\"keep_alive\":\"").Append(KeepAlive).Append("\",");
            sb.Append("\"options\":{\"temperature\":0}");
            sb.Append('}');
            return sb.ToString();
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
