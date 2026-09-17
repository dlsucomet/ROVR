// Assets/Scripts/ROVR/OllamaClient.cs
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
        [SerializeField] string endpoint = "http://localhost:11434/api/chat";
        [SerializeField] string model = "gemma3n:e4b";
        [SerializeField] int timeoutSeconds = 30;

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

        public void RequestCommand(string systemPrompt, string userPrompt, Action<NavigationCommand> onSuccess, Action<string> onError)
        {
            StartCoroutine(RequestCoroutine(systemPrompt, userPrompt, onSuccess, onError));
        }

        IEnumerator RequestCoroutine(string systemPrompt, string userPrompt, Action<NavigationCommand> onSuccess, Action<string> onError)
        {
            string body = BuildRequestJson(systemPrompt, userPrompt);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);

            var request = new UnityWebRequest(endpoint, "POST");
            try
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = timeoutSeconds;

                yield return request.SendWebRequest();

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
