using System;
using System.IO;
using System.Text;
using Sahkku.Rules;

namespace Sahkku.Rules.Bridge
{
    /// <summary>
    /// The shared LLM endpoint configuration: one small JSON file (<c>llm-config.json</c>) holding the
    /// OpenAI-compatible endpoint and the model name, read by the Unity game and the headless benchmark
    /// alike so the menu and the evaluation harness agree on where the NPC is served from.
    ///
    /// It is deliberately free of any <c>UnityEngine</c> reference, because the same source compiles into
    /// Unity, the headless test runner and the benchmark tool. The one Unity-specific location a player is
    /// guaranteed to be able to write to is injected by the caller through
    /// <see cref="WritableFallbackDirectory"/>.
    ///
    /// Discovery order, from strongest to weakest:
    /// <list type="number">
    /// <item>an explicit path,</item>
    /// <item>the <c>SAHKKU_LLM_CONFIG</c> environment variable,</item>
    /// <item>a settings file already saved in the writable fallback directory (Unity's persistent data
    /// path), so an edit to a read-only install is read back rather than shadowed by the shipped file,</item>
    /// <item>a search upwards from the binary and then the working directory for an existing file — which
    /// in a player is the folder next to the executable (<c>Application.dataPath + "/.."</c>) and in the
    /// editor is the project root,</item>
    /// <item>otherwise a new file in that same build/project root, with the write retried in the writable
    /// fallback directory whenever that root turns out to be read-only.</item>
    /// </list>
    ///
    /// Every read is total: a missing, empty, unreadable or hand-broken file yields the defaults rather than
    /// an exception, because no configuration file is worth failing a game over. Writes normalize the URL
    /// first, so an endpoint typed as a base URL is stored ready to post to.
    /// </summary>
    public static class LlmConfigFile
    {
        /// <summary>The file the game and the tools share.</summary>
        public const string FileName = "llm-config.json";

        /// <summary>Overrides the discovery order with a path of its own.</summary>
        public const string EnvironmentVariable = "SAHKKU_LLM_CONFIG";

        /// <summary>LM Studio's local server, with the chat-completions path spelled out.</summary>
        public const string DefaultEndpoint = "http://localhost:1234/v1/chat/completions";

        /// <summary>The model name the endpoint is asked to serve; LM Studio ignores it when one model is loaded.</summary>
        public const string DefaultModel = "pairflow-player";

        /// <summary>The JSON keys <see cref="Serialize"/> writes and <see cref="Parse"/> reads.</summary>
        public const string EndpointKey = "endpoint";
        public const string ModelKey = "model";

        /// <summary>
        /// The one directory a Unity player can always write to (<c>Application.persistentDataPath</c>): a
        /// settings file saved there is read back in preference to a shipped default, and a write to a
        /// read-only build root is retried there. Left null by headless callers.
        /// </summary>
        public static string WritableFallbackDirectory { get; set; }

        // ------------------------------------------------------------------ reading

        /// <summary>
        /// The endpoint and model to use, resolved through <see cref="LocateConfigFile(string)"/>. Returns
        /// <see cref="DefaultEndpoint"/> and <see cref="DefaultModel"/> for a file that is missing, empty,
        /// unreadable or not the object this class writes; the endpoint is normalized so a base URL is
        /// returned ready to post to.
        /// </summary>
        public static (string endpoint, string model) Load(string explicitPath = null)
        {
            string path = LocateConfigFile(explicitPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return (DefaultEndpoint, DefaultModel);

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                return (DefaultEndpoint, DefaultModel);
            }
            catch (UnauthorizedAccessException)
            {
                return (DefaultEndpoint, DefaultModel);
            }

            return Parse(text);
        }

        /// <summary>
        /// Reads the two settings out of <paramref name="text"/>. Total like <see cref="Load"/>: text that is
        /// not a JSON object, or a missing/empty/wrongly-typed value, falls back to that setting's default.
        /// </summary>
        public static (string endpoint, string model) Parse(string text)
        {
            JsonValue root = TryParseObject(text);
            if (root == null) return (DefaultEndpoint, DefaultModel);

            return (NormalizeEndpoint(ReadString(root, EndpointKey)), NormalizeModel(ReadString(root, ModelKey)));
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Writes <paramref name="endpoint"/> and <paramref name="model"/> as the shared JSON file, creating
        /// the directory if needed. Both are normalized first. When the resolved location cannot be written
        /// (a read-only install) the write is retried in <see cref="WritableFallbackDirectory"/>; if that is
        /// unset or also fails, the error surfaces to the caller.
        /// </summary>
        public static void Save(string endpoint, string model, string explicitPath = null)
        {
            string path = LocateConfigFile(explicitPath);
            string json = Serialize(endpoint, model);

            try
            {
                WriteAllText(path, json);
            }
            catch (Exception exception) when (IsRecoverableWriteFailure(exception) && HasFallback(path))
            {
                WriteAllText(Path.Combine(WritableFallbackDirectory, FileName), json);
            }
        }

        /// <summary>The exact text <see cref="Save"/> writes: a two-key JSON object with the values normalized.</summary>
        public static string Serialize(string endpoint, string model)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"endpoint\": ").Append(LlmChatRequest.Quote(NormalizeEndpoint(endpoint))).Append(",\n");
            builder.Append("  \"model\": ").Append(LlmChatRequest.Quote(NormalizeModel(model))).Append('\n');
            builder.Append("}\n");
            return builder.ToString();
        }

        // ------------------------------------------------------------------ location

        /// <summary>
        /// Resolves the path of the shared config per the documented discovery order. The path need not exist
        /// yet: an explicit path, the environment variable and the last-resort build/project root are returned
        /// whether or not a file is there, so <see cref="Save"/> can create one.
        /// </summary>
        public static string LocateConfigFile(string explicitPath = null)
        {
            return Resolve(explicitPath, null);
        }

        /// <summary>
        /// The same resolution with a controlled upward-search root. Only the tests use it; everything in the
        /// game and the tools goes through <see cref="LocateConfigFile(string)"/>.
        /// </summary>
        public static string LocateConfigFile(string explicitPath, string searchStartDirectory)
        {
            return Resolve(explicitPath, searchStartDirectory);
        }

        static string Resolve(string explicitPath, string searchStartDirectory)
        {
            if (!string.IsNullOrEmpty(explicitPath)) return Path.GetFullPath(explicitPath);

            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment)) return Path.GetFullPath(fromEnvironment);

            // A settings file the user has already saved next to a read-only install has to win over a
            // shipped default, or every edit after the first would be read back as the shipped value.
            if (!string.IsNullOrEmpty(WritableFallbackDirectory))
            {
                string saved = Path.Combine(WritableFallbackDirectory, FileName);
                if (File.Exists(saved)) return saved;
            }

            if (searchStartDirectory != null) return SearchUpwards(searchStartDirectory) ?? Path.Combine(searchStartDirectory, FileName);

            string found = SearchUpwards(AppContext.BaseDirectory) ?? SearchUpwards(Environment.CurrentDirectory);
            if (found != null) return found;

            return Path.Combine(SearchRoot(), FileName);
        }

        /// <summary>
        /// The first <c>llm-config.json</c> between <paramref name="startDirectory"/> and the filesystem root,
        /// or null. Walks the directory itself first, then every parent.
        /// </summary>
        public static string SearchUpwards(string startDirectory)
        {
            if (string.IsNullOrEmpty(startDirectory)) return null;

            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, FileName);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            return null;
        }

        /// <summary>
        /// Where a config is created when discovery finds none: the folder next to the executable or the
        /// working directory, which is the build root in a player and the project root in the editor — the
        /// same place <c>Application.dataPath + "/.."</c> points at. Only a read-only location there sends
        /// the write to <see cref="WritableFallbackDirectory"/>, which <see cref="Save"/> handles by retry.
        /// </summary>
        static string SearchRoot()
        {
            if (!string.IsNullOrEmpty(Environment.CurrentDirectory)) return Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(AppContext.BaseDirectory)) return AppContext.BaseDirectory;
            return ".";
        }

        // ------------------------------------------------------------------ values

        /// <summary>
        /// Accepts either a base URL (<c>http://localhost:1234/v1</c>) or the full chat-completions URL, and
        /// returns the URL the client posts to. An empty value becomes <see cref="DefaultEndpoint"/>, so the
        /// UI can never leave the NPC without somewhere to send its requests.
        /// </summary>
        public static string NormalizeEndpoint(string endpoint)
        {
            string value = string.IsNullOrEmpty(endpoint) ? string.Empty : endpoint.Trim();
            if (value.Length == 0) value = DefaultEndpoint;
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return value;
            return value.TrimEnd('/') + "/chat/completions";
        }

        /// <summary>Trims a model name and falls back to <see cref="DefaultModel"/> when it is empty.</summary>
        public static string NormalizeModel(string model)
        {
            string value = model == null ? string.Empty : model.Trim();
            return value.Length == 0 ? DefaultModel : value;
        }

        // ------------------------------------------------------------------ internals

        /// <summary>The value of <paramref name="key"/> when it is a JSON string, else null; never throws.</summary>
        static string ReadString(JsonValue root, string key)
        {
            JsonValue value = root.Get(key);
            return value == null ? null : value.AsStringOrNull();
        }

        /// <summary>
        /// Reads the text as a JSON object, folding every way the engine's reader can refuse it into one
        /// <c>null</c> — a malformed escape throws a <see cref="FormatException"/> where malformed JSON
        /// throws a <see cref="RuleSetException"/>, and to a reader promised to be total that difference
        /// does not matter.
        /// </summary>
        static JsonValue TryParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            JsonValue parsed;
            try
            {
                parsed = JsonParser.Parse(text);
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

        static void WriteAllText(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
        }

        static bool HasFallback(string path)
        {
            if (string.IsNullOrEmpty(WritableFallbackDirectory)) return false;
            return !string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(WritableFallbackDirectory, FileName)), StringComparison.Ordinal);
        }

        /// <summary>The failures a read-only install produces; anything else (null path, bad argument) is a bug and is reraised.</summary>
        static bool IsRecoverableWriteFailure(Exception exception)
        {
            return exception is UnauthorizedAccessException
                || exception is IOException
                || exception is NotSupportedException;
        }
    }
}
