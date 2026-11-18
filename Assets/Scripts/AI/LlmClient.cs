using System.Collections.Generic;
using System.Text;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Request builders and response parsers for each provider, kept free of Unity so they are easy to test.
    /// Every provider is asked for JSON that matches the same schema, so the rest of the app never cares
    /// which brain an agent uses.
    /// </summary>
    public static class LlmClient
    {
        public struct Request
        {
            public string url;
            public string body;
            public Dictionary<string, string> headers;
        }

        public static Request Build(AIServiceManager.Backend b, string system, string user, string schema, int maxTokens, string effort)
        {
            var headers = new Dictionary<string, string>();
            string q(string s) => MiniJson.Quote(s);
            switch (b.id)
            {
                case "anthropic":
                {
                    string baseUrl = string.IsNullOrEmpty(b.baseUrl) ? "https://api.anthropic.com" : b.baseUrl;
                    headers["x-api-key"] = b.apiKey ?? "";
                    headers["anthropic-version"] = "2023-06-01";
                    headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
                    string output = schema != null
                        ? $"{{\"effort\":{q(effort)},\"format\":{{\"type\":\"json_schema\",\"schema\":{schema}}}}}"
                        : $"{{\"effort\":{q(effort)}}}";
                    string body = "{" + $"\"model\":{q(b.model)},\"max_tokens\":{maxTokens},\"system\":{q(system)}," +
                                  $"\"messages\":[{{\"role\":\"user\",\"content\":{q(user)}}}],\"output_config\":{output},\"fallbacks\":\"default\"" + "}";
                    return new Request { url = baseUrl + "/v1/messages", body = body, headers = headers };
                }
                case "gemini":
                {
                    string baseUrl = string.IsNullOrEmpty(b.baseUrl) ? "https://generativelanguage.googleapis.com" : b.baseUrl;
                    headers["x-goog-api-key"] = b.apiKey ?? "";
                    // Gemini's response schema doesn't accept additionalProperties.
                    string gen = $"\"maxOutputTokens\":{maxTokens}" +
                                 (schema != null ? $",\"responseMimeType\":\"application/json\",\"responseSchema\":{schema.Replace(",\"additionalProperties\":false", "")}" : "");
                    string body = "{" + $"\"systemInstruction\":{{\"parts\":[{{\"text\":{q(system)}}}]}}," +
                                  $"\"contents\":[{{\"role\":\"user\",\"parts\":[{{\"text\":{q(user)}}}]}}],\"generationConfig\":{{{gen}}}" + "}";
                    return new Request { url = $"{baseUrl}/v1beta/models/{b.model}:generateContent", body = body, headers = headers };
                }
                default:   // openai + custom (OpenAI-compatible)
                {
                    bool openai = b.id == "openai";
                    string baseUrl = string.IsNullOrEmpty(b.baseUrl) ? "https://api.openai.com/v1" : b.baseUrl;
                    if (!string.IsNullOrEmpty(b.apiKey)) headers["Authorization"] = "Bearer " + b.apiKey;
                    string sys = system;
                    string format = "";
                    if (schema != null)
                    {
                        if (openai) format = $",\"response_format\":{{\"type\":\"json_schema\",\"json_schema\":{{\"name\":\"reply\",\"strict\":true,\"schema\":{schema}}}}}";
                        else
                        {
                            // Compatible servers vary; JSON mode + the schema in the prompt works almost everywhere.
                            format = ",\"response_format\":{\"type\":\"json_object\"}";
                            sys += "\nReply with JSON only, matching this JSON schema: " + schema;
                        }
                    }
                    string tokens = openai ? $"\"max_completion_tokens\":{maxTokens}" : $"\"max_tokens\":{maxTokens}";
                    string body = "{" + $"\"model\":{q(b.model)},{tokens}," +
                                  $"\"messages\":[{{\"role\":\"system\",\"content\":{q(sys)}}},{{\"role\":\"user\",\"content\":{q(user)}}}]{format}" + "}";
                    return new Request { url = baseUrl + "/chat/completions", body = body, headers = headers };
                }
            }
        }

        /// <summary>Pulls the model's text out of a provider response. problem != null on refusals / empty answers.</summary>
        public static string ParseText(string id, string json, out string problem)
        {
            problem = null;
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) { problem = "Could not read the response."; return null; }
            var sb = new StringBuilder();
            switch (id)
            {
                case "anthropic":
                    if (root.TryGetValue("stop_reason", out var stop) && stop as string == "refusal") { problem = "The model declined this task."; return null; }
                    if (root.TryGetValue("content", out var content) && content is List<object> blocks)
                        foreach (var o in blocks)
                            if (o is Dictionary<string, object> blk && blk.TryGetValue("type", out var t) && t as string == "text")
                                sb.Append(blk["text"] as string);
                    break;
                case "gemini":
                    if (root.TryGetValue("candidates", out var cands) && cands is List<object> cl && cl.Count > 0 &&
                        cl[0] is Dictionary<string, object> c0)
                    {
                        if (c0.TryGetValue("finishReason", out var fr) && (fr as string == "SAFETY" || fr as string == "PROHIBITED_CONTENT"))
                        { problem = "The model declined this task."; return null; }
                        if (c0.TryGetValue("content", out var cc) && cc is Dictionary<string, object> cd &&
                            cd.TryGetValue("parts", out var parts) && parts is List<object> pl)
                            foreach (var part in pl)
                                if (part is Dictionary<string, object> pd && pd.TryGetValue("text", out var tx)) sb.Append(tx as string);
                    }
                    break;
                default:
                    if (root.TryGetValue("choices", out var ch) && ch is List<object> chl && chl.Count > 0 &&
                        chl[0] is Dictionary<string, object> first && first.TryGetValue("message", out var m) && m is Dictionary<string, object> msg)
                    {
                        if (msg.TryGetValue("refusal", out var rf) && rf is string r && r.Length > 0) { problem = "The model declined this task."; return null; }
                        if (msg.TryGetValue("content", out var mc)) sb.Append(mc as string);
                    }
                    break;
            }
            string text = sb.ToString().Trim();
            if (text.Length == 0) problem = "Empty response.";
            return text;
        }

        /// <summary>Error message from a provider's error body, if any.</summary>
        public static string ErrorMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            if (MiniJson.Parse(json) is Dictionary<string, object> root && root.TryGetValue("error", out var e))
            {
                if (e is Dictionary<string, object> ed && ed.TryGetValue("message", out var msg)) return TaskLibrary.Trim(msg as string ?? "", 140);
                if (e is string es) return TaskLibrary.Trim(es, 140);
            }
            return "";
        }

        /// <summary>Strips ```json fences and leading prose so a JSON object can be parsed.</summary>
        public static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int a = text.IndexOf('{'), b = text.LastIndexOf('}');
            return a >= 0 && b > a ? text.Substring(a, b - a + 1) : text;
        }
    }
}
