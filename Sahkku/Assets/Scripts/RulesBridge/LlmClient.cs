using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sahkku.Rules;

namespace Sahkku.Rules.Bridge
{
    /// <summary>
    /// The network seam under <see cref="LlmPlayerAgent"/>: one POST of an OpenAI-compatible
    /// chat-completions body, resolved with the response text. Tests script it, which is what keeps the
    /// headless suite offline; every failure surfaces as an exception, because deciding what to do about
    /// a failure is the agent's job (see the fallback matrix in <see cref="LlmPlayerAgent"/>), not the
    /// transport's.
    /// </summary>
    public interface ILlmTransport
    {
        /// <summary>Posts <paramref name="requestJson"/> to <paramref name="endpointUrl"/> and returns the response body.</summary>
        Task<string> PostChatCompletionAsync(string endpointUrl, string requestJson, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The shipped transport: plain <see cref="HttpClient"/> against any OpenAI-compatible endpoint —
    /// LM Studio (<c>http://localhost:1234/v1/chat/completions</c>), Ollama, or a hosted API. It never
    /// runs on a frame-critical path by itself; the match loop awaits it.
    /// </summary>
    public sealed class HttpClientLlmTransport : ILlmTransport, IDisposable
    {
        readonly HttpClient httpClient;
        readonly bool ownsHttpClient;

        /// <summary>
        /// Deadline applied to every request. The default is deliberately short: a bot that cannot answer
        /// quickly is worth less than the deterministic fallback the agent uses instead.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; }

        /// <param name="client">An externally owned client, or null to create (and later dispose) one.</param>
        /// <param name="timeout">Per-request deadline; defaults to 10 seconds.</param>
        public HttpClientLlmTransport(HttpClient client = null, TimeSpan? timeout = null)
        {
            ownsHttpClient = client == null;
            httpClient = client ?? new HttpClient();
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(10);
        }

        public async Task<string> PostChatCompletionAsync(string endpointUrl, string requestJson, CancellationToken cancellationToken)
        {
            if (endpointUrl == null) throw new ArgumentNullException("endpointUrl");

            // The deadline is a linked token rather than HttpClient.Timeout: a caller-supplied client may
            // already have sent a request (setting Timeout on it then throws), and a shared client must
            // not be reconfigured by a transport that does not own it. A deadline that fires cancels the
            // *linked* token only, so the agent can tell "the model was too slow" (a failure it recovers
            // from) apart from "the match was cancelled" (an exception it rethrows).
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (RequestTimeout > TimeSpan.Zero) deadline.CancelAfter(RequestTimeout);

                using (var content = new StringContent(requestJson ?? "{}", Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await httpClient.PostAsync(endpointUrl, content, deadline.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Disposes the underlying client, but only when this transport created it.</summary>
        public void Dispose()
        {
            if (ownsHttpClient) httpClient.Dispose();
        }
    }

    /// <summary>
    /// Everything an LLM agent needs to reach its endpoint. Plain data with defaults aimed at a local
    /// LM Studio server, so it can be owned by the menu (<c>GameSettings</c>) or by a headless harness.
    /// </summary>
    public sealed class LlmConfig
    {
        public string EndpointUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
        public string ModelName { get; set; } = "pairflow-player";
        public float Temperature { get; set; } = 0.2f;
        public int MaxTokens { get; set; } = 256;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Builds the OpenAI-compatible chat-completions request body. Hand-rolled rather than serialized
    /// through reflection, because the whole bridge layer has to keep compiling for IL2CPP/WebGL.
    /// </summary>
    public static class LlmChatRequest
    {
        public static string Build(LlmConfig config, string systemPrompt, string userPrompt)
        {
            LlmConfig effective = config ?? new LlmConfig();
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"model\":").Append(Quote(effective.ModelName ?? string.Empty)).Append(',');
            sb.Append("\"temperature\":").Append(effective.Temperature.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"max_tokens\":").Append(effective.MaxTokens.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"stream\":false,");
            sb.Append("\"messages\":[")
              .Append("{\"role\":\"system\",\"content\":").Append(Quote(systemPrompt ?? string.Empty)).Append("},")
              .Append("{\"role\":\"user\",\"content\":").Append(Quote(userPrompt ?? string.Empty)).Append("}").Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Quotes and escapes a string for embedding in JSON (a prompt must not be able to break the body).</summary>
        public static string Quote(string value)
        {
            string text = value ?? string.Empty;
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Reads a chat-completion response down to the decision object the agent's prompt asked for:
    /// <c>{"move_index": N, "reasoning": "..."}</c> or <c>{"reroll": true, "reasoning": "..."}</c>.
    ///
    /// Every method here is total: a chatty, truncated or hostile answer yields <c>false</c>/<c>null</c>
    /// and never throws, which is what lets the agent fall back to the heuristic instead of failing the
    /// match. "Unreadable" therefore covers every way the engine's reader can refuse a candidate, not just
    /// the exception it documents — see <see cref="TryParseObject"/>. Accepted shapes, in order of
    /// preference:
    /// <list type="number">
    /// <item>the OpenAI envelope (<c>choices[0].message.content</c>, a string or a text-parts array),</item>
    /// <item>its content wrapped in a Markdown fence (<c>```json ... ```</c>),</item>
    /// <item>a plain body that is the decision object itself,</item>
    /// <item>any of the above with conversational prose around the JSON object.</item>
    /// </list>
    /// </summary>
    public static class LlmResponseParser
    {
        public const string MoveIndexKey = "move_index";
        public const string MoveIndexCamelKey = "moveIndex";
        public const string RerollKey = "reroll";
        public const string ReasoningKey = "reasoning";

        /// <summary>
        /// True when the model named a move index inside <c>[0, legalMoveCount)</c>. An index that is
        /// present but unreadable, negative or out of range is a rejection, not a clue: the caller falls
        /// back to the heuristic rather than guessing what the model meant.
        /// </summary>
        public static bool TryParseMoveIndex(string responseBody, int legalMoveCount, out int moveIndex)
        {
            moveIndex = 0;
            if (legalMoveCount <= 0) return false;

            JsonValue decision = FindDecision(responseBody, MoveIndexKey, MoveIndexCamelKey);
            if (decision == null) return false;

            int index;
            if (!TryReadInt(decision, out index)) return false;
            if (index < 0 || index >= legalMoveCount) return false;

            moveIndex = index;
            return true;
        }

        /// <summary>True when the model answered the re-roll question with a usable boolean.</summary>
        public static bool TryParseReroll(string responseBody, out bool reroll)
        {
            reroll = false;
            JsonValue decision = FindDecision(responseBody, RerollKey);
            if (decision == null) return false;

            bool value;
            if (!TryReadBool(decision, out value)) return false;

            reroll = value;
            return true;
        }

        /// <summary>The model's own explanation, when it sent one. Null when it did not.</summary>
        public static string ReadReasoning(string responseBody)
        {
            JsonValue reasoning = FindDecision(responseBody, ReasoningKey);
            return reasoning == null ? null : reasoning.AsStringOrNull();
        }

        /// <summary>
        /// The text the decision lives in: the first message content of a chat-completion envelope, or
        /// the body itself when it is not an envelope (or is an unreadable one).
        /// </summary>
        public static string DecisionText(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody)) return null;

            // A body that is not a readable chat-completion envelope is its own decision text: the object the
            // agent is after may still be in there (or nowhere at all), and the extraction below decides that.
            JsonValue root = TryParseObject(responseBody);
            if (root == null) return responseBody;

            JsonValue choices = root.Get("choices");
            if (choices == null || choices.Kind != JsonValueKind.Array) return responseBody;

            IReadOnlyList<JsonValue> items = choices.Items();
            if (items.Count == 0 || items[0].Kind != JsonValueKind.Object) return responseBody;

            JsonValue message = items[0].Get("message");
            if (message == null || message.Kind != JsonValueKind.Object) return responseBody;

            string text = ContentText(message.Get("content"));
            return string.IsNullOrEmpty(text) ? responseBody : text;
        }

        /// <summary>
        /// The balanced <c>{ ... }</c> objects in <paramref name="text"/>, outermost first and including
        /// nested ones, with braces inside strings ignored. Scanning rather than parsing means prose,
        /// fences and truncated tails around the object do not have to be understood.
        /// </summary>
        public static IEnumerable<string> ObjectCandidates(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            for (int start = 0; start < text.Length; ++start)
            {
                if (text[start] != '{') continue;

                int depth = 0;
                bool inString = false;
                bool escaped = false;
                for (int i = start; i < text.Length; ++i)
                {
                    char c = text[i];
                    if (inString)
                    {
                        if (escaped) { escaped = false; continue; }
                        if (c == '\\') { escaped = true; continue; }
                        if (c == '"') inString = false;
                        continue;
                    }

                    if (c == '"') { inString = true; continue; }
                    if (c == '{') { depth++; continue; }
                    if (c != '}') continue;

                    depth--;
                    if (depth != 0) continue;

                    yield return text.Substring(start, i - start + 1);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ internals

        /// <summary>
        /// The first candidate object that carries one of <paramref name="keys"/>. The first object that
        /// mentions the key wins even when its value is unusable, so a later object can never be mistaken
        /// for the answer the model actually gave.
        /// </summary>
        static JsonValue FindDecision(string responseBody, params string[] keys)
        {
            string text = DecisionText(responseBody);
            foreach (string candidate in ObjectCandidates(text))
            {
                JsonValue parsed = TryParseObject(candidate);
                if (parsed == null) continue;

                foreach (string key in keys)
                {
                    JsonValue value = parsed.Get(key);
                    if (value != null) return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Reads a candidate object, folding *every* way the engine's reader can refuse it into one
        /// <c>null</c>.
        ///
        /// <see cref="RuleSetException"/> is what the reader documents for malformed JSON, but it is not all
        /// it throws: a malformed <c>\u</c> escape — a model writing <c>C:\users</c> inside its reasoning,
        /// say — leaves its string reader as a <see cref="FormatException"/>. To a parser that is promised
        /// to be total the distinction is meaningless: either way the text is not the JSON the prompt asked
        /// for, so a later candidate still gets its turn and an answer nobody can read is simply not one.
        /// </summary>
        static JsonValue TryParseObject(string candidate)
        {
            JsonValue parsed;
            try
            {
                parsed = JsonParser.Parse(candidate);
            }
            catch (RuleSetException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }

            return parsed != null && parsed.Kind == JsonValueKind.Object ? parsed : null;
        }

        /// <summary>Reads a JSON integer, tolerating the quoted form some small models emit.</summary>
        static bool TryReadInt(JsonValue value, out int result)
        {
            result = 0;
            if (value.Kind == JsonValueKind.Number)
            {
                double number = value.AsDouble();
                if (number != Math.Truncate(number)) return false;          // "1.5" is not a move index
                if (number < int.MinValue || number > int.MaxValue) return false;
                result = (int)number;
                return true;
            }

            if (value.Kind == JsonValueKind.String)
                return int.TryParse(value.AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

            return false;
        }

        /// <summary>Reads a JSON boolean, tolerating the quoted form some small models emit.</summary>
        static bool TryReadBool(JsonValue value, out bool result)
        {
            result = false;
            if (value.Kind == JsonValueKind.Bool)
            {
                result = value.AsBool();
                return true;
            }

            if (value.Kind == JsonValueKind.String)
                return bool.TryParse(value.AsString(), out result);

            return false;
        }

        /// <summary>The content of a message: a plain string, or the concatenated text parts of a multimodal one.</summary>
        static string ContentText(JsonValue content)
        {
            if (content == null) return null;
            if (content.Kind == JsonValueKind.String) return content.AsString();
            if (content.Kind != JsonValueKind.Array) return null;

            var sb = new StringBuilder();
            foreach (JsonValue part in content.Items())
            {
                if (part == null || part.Kind != JsonValueKind.Object) continue;
                JsonValue text = part.Get("text");
                if (text != null && text.Kind == JsonValueKind.String) sb.Append(text.AsString());
            }
            return sb.ToString();
        }
    }
}
