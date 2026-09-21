using System;
using System.IO;
using NUnit.Framework;
using Sahkku.Rules.Bridge;

namespace Sahkku.Rules.Tests
{
    /// <summary>
    /// The shared LLM configuration file: the JSON the menu writes, the rules of the upward search that
    /// finds it, and the fallbacks that keep a missing or hand-broken file from reaching the game. Everything
    /// runs offline against a fresh temporary directory; nothing here needs Unity, a model or a server.
    /// </summary>
    [TestFixture]
    public class LlmConfigTests
    {
        const string CustomEndpoint = "http://192.168.0.5:8080/v1/chat/completions";
        const string CustomModel = "local-model";

        string sandbox;

        [SetUp]
        public void CreateSandbox()
        {
            sandbox = Path.Combine(Path.GetTempPath(), "sahkku-llm-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
        }

        [TearDown]
        public void ReleaseSandbox()
        {
            LlmConfigFile.WritableFallbackDirectory = null;
            if (sandbox != null && Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }

        static string ConfigPath(string directory)
        {
            return Path.Combine(directory, LlmConfigFile.FileName);
        }

        static string Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        static string WriteDefault(string directory)
        {
            return Write(ConfigPath(directory),
                "{\"endpoint\":\"" + CustomEndpoint + "\",\"model\":\"" + CustomModel + "\"}");
        }

        // ------------------------------------------------------------------ reading

        [Test]
        public void Load_MissingFile_ReturnsDefaultsWithoutThrowing()
        {
            var loaded = LlmConfigFile.Load(Path.Combine(sandbox, "not-there", LlmConfigFile.FileName));

            Assert.AreEqual(LlmConfigFile.DefaultEndpoint, loaded.endpoint);
            Assert.AreEqual(LlmConfigFile.DefaultModel, loaded.model);
        }

        [Test]
        public void Load_ReadsTheEndpointAndModel()
        {
            var loaded = LlmConfigFile.Load(WriteDefault(sandbox));

            Assert.AreEqual(CustomEndpoint, loaded.endpoint);
            Assert.AreEqual(CustomModel, loaded.model);
        }

        [Test]
        public void Load_BaseUrl_IsCompletedToTheChatCompletionsPath()
        {
            string path = Write(ConfigPath(sandbox), "{\"endpoint\":\"http://localhost:1234/v1\"}");

            var loaded = LlmConfigFile.Load(path);

            Assert.AreEqual("http://localhost:1234/v1/chat/completions", loaded.endpoint);
            Assert.AreEqual(LlmConfigFile.DefaultModel, loaded.model, "a file without a model keeps the default");
        }

        [Test]
        public void Load_MalformedText_ReturnsDefaultsWithoutThrowing()
        {
            string path = Write(ConfigPath(sandbox), "{ this is not json ");

            var loaded = LlmConfigFile.Load(path);

            Assert.AreEqual(LlmConfigFile.DefaultEndpoint, loaded.endpoint);
            Assert.AreEqual(LlmConfigFile.DefaultModel, loaded.model);
        }

        [Test]
        public void Load_EmptyObjectAndWrongTypes_ReturnDefaults()
        {
            string path = Write(ConfigPath(sandbox), "{\"endpoint\": 42, \"model\": [\"x\"], \"extra\": true}");

            var loaded = LlmConfigFile.Load(path);

            Assert.AreEqual(LlmConfigFile.DefaultEndpoint, loaded.endpoint);
            Assert.AreEqual(LlmConfigFile.DefaultModel, loaded.model);
        }

        [Test]
        public void Load_EmptyStrings_ReturnDefaults()
        {
            string path = Write(ConfigPath(sandbox), "{\"endpoint\":\"\",\"model\":\"   \"}");

            var loaded = LlmConfigFile.Load(path);

            Assert.AreEqual(LlmConfigFile.DefaultEndpoint, loaded.endpoint);
            Assert.AreEqual(LlmConfigFile.DefaultModel, loaded.model);
        }

        // ------------------------------------------------------------------ writing

        [Test]
        public void Save_WritesCleanJsonWithBothKeys()
        {
            string path = ConfigPath(sandbox);

            LlmConfigFile.Save(CustomEndpoint, CustomModel, path);
            string text = File.ReadAllText(path);

            StringAssert.Contains("\"endpoint\"", text);
            StringAssert.Contains("\"model\"", text);
            StringAssert.Contains(CustomEndpoint, text);
            StringAssert.Contains(CustomModel, text);
            string trimmed = text.Trim();
            Assert.AreEqual('{', trimmed[0], "the file is a JSON object");
            Assert.AreEqual('}', trimmed[trimmed.Length - 1], "the file is a JSON object");
        }

        [Test]
        public void Save_ThenLoad_RoundTripsTheValues()
        {
            string path = ConfigPath(sandbox);

            LlmConfigFile.Save(CustomEndpoint, CustomModel, path);
            var loaded = LlmConfigFile.Load(path);

            Assert.AreEqual(CustomEndpoint, loaded.endpoint);
            Assert.AreEqual(CustomModel, loaded.model);
        }

        [Test]
        public void Save_NormalizesTheValuesItStores()
        {
            string path = ConfigPath(sandbox);

            LlmConfigFile.Save("http://example.test:9999/v1", "  trimmed-model  ", path);
            var loaded = LlmConfigFile.Load(path);

            Assert.AreEqual("http://example.test:9999/v1/chat/completions", loaded.endpoint);
            Assert.AreEqual("trimmed-model", loaded.model);
        }

        [Test]
        public void Save_CreatesTheDirectory()
        {
            string path = Path.Combine(sandbox, "nested", "deeper", LlmConfigFile.FileName);

            LlmConfigFile.Save(CustomEndpoint, CustomModel, path);

            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void Save_WhenTheLocationCannotBeWritten_FallsBackToTheWritableDirectory()
        {
            // A path whose parent is a file, not a directory: creating it fails for every user, even root.
            string blocker = Path.Combine(sandbox, "blocker");
            File.WriteAllText(blocker, "not a directory");
            string unwritable = Path.Combine(blocker, LlmConfigFile.FileName);
            string fallback = Path.Combine(sandbox, "fallback");
            LlmConfigFile.WritableFallbackDirectory = fallback;

            LlmConfigFile.Save(CustomEndpoint, CustomModel, unwritable);

            string written = ConfigPath(fallback);
            Assert.IsTrue(File.Exists(written), "the write is retried in the persistent-data fallback");
            var loaded = LlmConfigFile.Load(written);
            Assert.AreEqual(CustomEndpoint, loaded.endpoint);
            Assert.AreEqual(CustomModel, loaded.model);
        }

        // ------------------------------------------------------------------ location

        [Test]
        public void LocateConfigFile_ExplicitPathWins()
        {
            string explicitPath = Path.Combine(sandbox, "explicit.json");
            WriteDefault(sandbox);

            Assert.AreEqual(Path.GetFullPath(explicitPath), LlmConfigFile.LocateConfigFile(explicitPath));
        }

        [Test]
        public void LocateConfigFile_EnvironmentVariableIsUsed()
        {
            string fromEnvironment = Path.Combine(sandbox, "from-environment.json");
            string previous = Environment.GetEnvironmentVariable(LlmConfigFile.EnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(LlmConfigFile.EnvironmentVariable, fromEnvironment);
                Assert.AreEqual(Path.GetFullPath(fromEnvironment), LlmConfigFile.LocateConfigFile());
            }
            finally
            {
                Environment.SetEnvironmentVariable(LlmConfigFile.EnvironmentVariable, previous);
            }
        }

        [Test]
        public void LocateConfigFile_SearchesUpwardsFromTheStartDirectory()
        {
            string found = WriteDefault(sandbox);
            string deep = Path.Combine(sandbox, "a", "b", "c");
            Directory.CreateDirectory(deep);

            Assert.AreEqual(Path.GetFullPath(found), LlmConfigFile.LocateConfigFile(null, deep));
        }

        [Test]
        public void LocateConfigFile_SavedFallbackWinsOverTheShippedFile()
        {
            // The user's own saved settings beat a file shipped next to the build, or an edit to a read-only
            // install would be read back as the shipped value on the next launch.
            string shipped = Write(Path.Combine(sandbox, "shipped", LlmConfigFile.FileName), "{\"model\":\"shipped\"}");
            string fallback = Path.Combine(sandbox, "fallback");
            Write(ConfigPath(fallback), "{\"model\":\"saved\"}");
            LlmConfigFile.WritableFallbackDirectory = fallback;

            string located = LlmConfigFile.LocateConfigFile(null, Path.GetDirectoryName(shipped));

            Assert.AreEqual(Path.GetFullPath(ConfigPath(fallback)), located);
            Assert.AreEqual("saved", LlmConfigFile.Load(located).model);
        }

        [Test]
        public void LocateConfigFile_WithNothingToFind_DefaultsToTheSearchRoot()
        {
            string fallback = Path.Combine(sandbox, "fallback");
            Directory.CreateDirectory(fallback);
            LlmConfigFile.WritableFallbackDirectory = fallback;
            string start = Path.Combine(sandbox, "start");
            Directory.CreateDirectory(start);

            Assert.AreEqual(Path.GetFullPath(ConfigPath(start)), LlmConfigFile.LocateConfigFile(null, start));
        }

        // ------------------------------------------------------------------ values

        [Test]
        public void NormalizeEndpoint_CompletesBaseUrlsAndKeepsFullOnes()
        {
            Assert.AreEqual("http://localhost:1234/v1/chat/completions", LlmConfigFile.NormalizeEndpoint("http://localhost:1234/v1"));
            Assert.AreEqual("http://localhost:1234/v1/chat/completions", LlmConfigFile.NormalizeEndpoint("http://localhost:1234/v1/"));
            Assert.AreEqual("http://host:1/custom/chat/completions", LlmConfigFile.NormalizeEndpoint("http://host:1/custom/chat/completions"));
            Assert.AreEqual(LlmConfigFile.DefaultEndpoint, LlmConfigFile.NormalizeEndpoint("   "));
            Assert.AreEqual(LlmConfigFile.DefaultEndpoint, LlmConfigFile.NormalizeEndpoint(null));
        }

        [Test]
        public void NormalizeModel_TrimsAndDefaults()
        {
            Assert.AreEqual("m", LlmConfigFile.NormalizeModel("  m  "));
            Assert.AreEqual(LlmConfigFile.DefaultModel, LlmConfigFile.NormalizeModel(""));
            Assert.AreEqual(LlmConfigFile.DefaultModel, LlmConfigFile.NormalizeModel(null));
        }
    }
}
