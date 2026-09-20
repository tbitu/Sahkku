using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

namespace Sahkku.Bench
{
    /// <summary>
    /// The headless entry point of the benchmark tool:
    ///
    /// <code>
    /// dotnet run --project Tools/SahkkuBench -- --games 100 --p1 heuristic --p2 random
    /// </code>
    ///
    /// It wires one agent per seat, runs the games through <see cref="MatchRunner"/> — which hands every
    /// action to <see cref="RulesEngine"/> and checks the board after every one — and prints the run as a
    /// table or as JSON. Nothing here decides a rule, and nothing here needs Unity.
    /// </summary>
    public static class Program
    {
        const string RulesetRelativePath = "Sahkku/Assets/Resources/SahkkuRules.json";
        const string RulesetEnvironmentVariable = "SAHKKU_RULESET";

        public static async Task<int> Main(string[] args)
        {
            Options options;
            string error;
            if (!Options.TryParse(args, out options, out error))
            {
                Console.Error.WriteLine("error: " + error);
                Console.Error.WriteLine();
                Console.Error.WriteLine(Options.Usage);
                return 1;
            }

            if (options.Help)
            {
                Console.WriteLine(Options.Usage);
                return 0;
            }

            try
            {
                return await RunAsync(options).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("error: the run was cancelled.");
                return 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("error: " + exception.GetType().Name + ": " + exception.Message);
                return 1;
            }
        }

        static async Task<int> RunAsync(Options options)
        {
            string rulesetPath = LocateRuleset(options.RulesetPath);
            var engine = new RulesEngine(RuleSetJson.FromJson(File.ReadAllText(rulesetPath)));

            int seed = options.UseRandomSeed ? Environment.TickCount : options.Seed;

            // The seed is resolved once, here, so every game of the run draws its own stream from it and yet
            // the whole run is reproducible from --seed.
            var config = new MatchConfig
            {
                MaxHalfMoves = options.MaxHalfMoves,
                Seed = seed,
                UseRandomSeed = false
            };

            var fallbacks = new LlmFallbackCounter();
            var held = new List<IDisposable>();

            Action<string> llmLog = delegate(string message)
            {
                fallbacks.Observe(message);
                if (options.Verbose) Console.WriteLine("    " + message);
            };

            IPlayerAgent p1 = CreateAgent("--p1", options.P1, PieceOwner.P1, engine, seed, options, llmLog, held);
            IPlayerAgent p2 = CreateAgent("--p2", options.P2, PieceOwner.P2, engine, seed, options, llmLog, held);
            var runner = new MatchRunner(engine, p1, p2);

            var summary = new BenchmarkSummary();
            try
            {
                if (options.Verbose)
                    Console.WriteLine("Sáhkku benchmark: " + p1.Name + " (P1) vs " + p2.Name + " (P2), " + options.Games +
                                      " game(s), seed " + seed + ", ruleset " + rulesetPath);

                for (int game = 0; game < options.Games; ++game)
                {
                    fallbacks.Reset();
                    MatchResult result = await runner
                        .RunMatchAsync(game, config, CancellationToken.None)
                        .ConfigureAwait(false);

                    result.LlmFallbacks = fallbacks.Count;
                    summary.Add(result);

                    if (options.Verbose) Console.WriteLine(Describe(result, game, options.Games));
                }
            }
            finally
            {
                foreach (IDisposable disposable in held) disposable.Dispose();
            }

            Console.WriteLine(options.Json
                ? summary.ToJsonString(p1.Name, p2.Name)
                : summary.ToPrettyString(p1.Name, p2.Name));

            return summary.Failures > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ agents

        /// <summary>
        /// Builds the agent for one seat from its CLI name. A random bot gets its own deterministic stream
        /// (offset by the seat), and an LLM agent gets the endpoint configuration plus the run's log sink,
        /// which is what lets the summary report how often the model was not the one deciding.
        /// </summary>
        static IPlayerAgent CreateAgent(
            string flag,
            string kind,
            PieceOwner owner,
            RulesEngine engine,
            int seed,
            Options options,
            Action<string> log,
            List<IDisposable> held)
        {
            switch (kind)
            {
                case "heuristic":
                    return new HeuristicPlayerAgent(owner, "Heuristic", engine);

                case "random":
                    return new RandomPlayerAgent(owner, "Random", new SeededRandomSource(unchecked(seed * 31 + (int)owner)));

                case "llm":
                    var config = new LlmConfig { EndpointUrl = NormalizeEndpoint(options.Endpoint) };
                    if (!string.IsNullOrEmpty(options.Model)) config.ModelName = options.Model;

                    var transport = new HttpClientLlmTransport(null, config.RequestTimeout);
                    held.Add(transport);
                    return new LlmPlayerAgent(owner, "LLM", transport, config, engine, log);

                default:
                    throw new ArgumentException("Unknown agent type '" + kind + "' for " + flag + ".");
            }
        }

        /// <summary>
        /// Accepts an endpoint or a base URL, because both are natural to type: the shipped default (and the
        /// one in the help) is the base <c>http://localhost:1234/v1</c>, while the client posts to the
        /// chat-completions path under it.
        /// </summary>
        static string NormalizeEndpoint(string endpoint)
        {
            string value = string.IsNullOrEmpty(endpoint) ? "http://localhost:1234/v1" : endpoint.Trim();
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return value;
            return value.TrimEnd('/') + "/chat/completions";
        }

        // ------------------------------------------------------------------ reporting

        static string Describe(MatchResult result, int index, int total)
        {
            string outcome;
            if (result.Failed) outcome = "failed: " + result.ErrorMessage;
            else if (result.Winner == PieceOwner.None) outcome = result.HitTurnLimit ? "draw (turn limit)" : "draw";
            else outcome = result.Winner + " wins by " + result.WinReason;

            return "game " + (index + 1) + "/" + total + ": " + outcome +
                   " in " + result.TotalHalfMoves + " half-moves, " +
                   result.TotalCaptures + " captures, " +
                   result.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s";
        }

        // ------------------------------------------------------------------ ruleset lookup

        /// <summary>
        /// Finds the ruleset the way the rest of the tooling does: an explicit path, then
        /// <c>SAHKKU_RULESET</c>, then an upward search from the binary and the working directory, so the
        /// tool runs from anywhere inside the repository.
        /// </summary>
        static string LocateRuleset(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            {
                if (!File.Exists(explicitPath))
                    throw new FileNotFoundException("The ruleset '" + explicitPath + "' does not exist.");
                return explicitPath;
            }

            string fromEnvironment = Environment.GetEnvironmentVariable(RulesetEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                if (!File.Exists(fromEnvironment))
                    throw new FileNotFoundException(RulesetEnvironmentVariable + " points at '" + fromEnvironment + "', which does not exist.");
                return fromEnvironment;
            }

            string found = SearchUpwards(AppContext.BaseDirectory) ?? SearchUpwards(Environment.CurrentDirectory);
            if (found != null) return found;

            throw new FileNotFoundException(
                "Could not find '" + RulesetRelativePath + "'. Run the tool from inside the repository, or pass --ruleset <path>.");
        }

        static string SearchUpwards(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;

            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, RulesetRelativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            return null;
        }

        // ------------------------------------------------------------------ arguments

        /// <summary>The parsed command line. Parsing stops at the first problem and reports it, so a typo can never mean a silent default.</summary>
        sealed class Options
        {
            static readonly string[] AgentKinds = { "heuristic", "random", "llm" };

            public int Games = 10;
            public string P1 = "heuristic";
            public string P2 = "heuristic";
            public string Endpoint = "http://localhost:1234/v1";
            public string Model = "";
            public int Seed;
            public bool UseRandomSeed = true;
            public int MaxHalfMoves = 10000;
            public string RulesetPath;
            public bool Json;
            public bool Verbose;
            public bool Help;

            public const string Usage =
@"Sáhkku headless match runner, evaluation harness and benchmark.

Plays automated matches between any combination of agents and reports the outcome. Every action goes
through the rules engine, which validates each move and is checked after every change: the harness
cannot drive the game into an illegal position, and a rule violation ends the run with exit code 1.

Usage:
  dotnet run --project Tools/SahkkuBench -- [options]

Options:
  --games <N>        Games to play (default: 10).
  --p1 <type>        Agent for player one: heuristic | random | llm (default: heuristic).
  --p2 <type>        Agent for player two: heuristic | random | llm (default: heuristic).
  --endpoint <url>   OpenAI-compatible endpoint for LLM agents (default: http://localhost:1234/v1).
  --model <name>     Model name sent to the endpoint (default: pairflow-player, as in the game).
  --seed <int>       Base seed for a reproducible run (default: the clock).
  --max-turns <N>    Half-moves before a game is declared a draw (default: 10000).
  --ruleset <path>   Path to SahkkuRules.json (default: SAHKKU_RULESET, else searched upwards).
  --json             Print the summary as JSON instead of as a table.
  --verbose          Print one line per game and the agents' decisions as they happen.
  --help, -h         Print this help.

Examples:
  dotnet run --project Tools/SahkkuBench -- --games 100 --p1 heuristic --p2 random
  dotnet run --project Tools/SahkkuBench -- --games 10 --p1 llm --p2 heuristic --endpoint http://localhost:1234/v1

Exit codes:
  0  every game completed: no rule violations and no failed games.
  1  bad arguments, a missing ruleset, a rule violation, or a game that did not complete.";

            public static bool TryParse(string[] args, out Options options, out string error)
            {
                options = new Options();
                error = null;

                for (int i = 0; i < args.Length; ++i)
                {
                    string value;
                    switch (args[i])
                    {
                        case "--help":
                        case "-h":
                            options.Help = true;
                            return true;

                        case "--json":
                            options.Json = true;
                            break;

                        case "--verbose":
                            options.Verbose = true;
                            break;

                        case "--games":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out options.Games) || options.Games < 1)
                            {
                                error = "--games needs a positive integer, got '" + value + "'.";
                                return false;
                            }
                            break;

                        case "--max-turns":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out options.MaxHalfMoves) || options.MaxHalfMoves < 1)
                            {
                                error = "--max-turns needs a positive integer, got '" + value + "'.";
                                return false;
                            }
                            break;

                        case "--seed":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out options.Seed))
                            {
                                error = "--seed needs an integer, got '" + value + "'.";
                                return false;
                            }
                            options.UseRandomSeed = false;
                            break;

                        case "--p1":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (!IsAgentKind(value))
                            {
                                error = "--p1 needs one of " + string.Join(" | ", AgentKinds) + ", got '" + value + "'.";
                                return false;
                            }
                            options.P1 = value;
                            break;

                        case "--p2":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (!IsAgentKind(value))
                            {
                                error = "--p2 needs one of " + string.Join(" | ", AgentKinds) + ", got '" + value + "'.";
                                return false;
                            }
                            options.P2 = value;
                            break;

                        case "--endpoint":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (string.IsNullOrEmpty(value))
                            {
                                error = "--endpoint needs a URL.";
                                return false;
                            }
                            options.Endpoint = value;
                            break;

                        case "--model":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            options.Model = value;
                            break;

                        case "--ruleset":
                            if (!TryReadValue(args, ref i, out value, out error)) return false;
                            if (!File.Exists(value))
                            {
                                error = "the ruleset '" + value + "' does not exist.";
                                return false;
                            }
                            options.RulesetPath = value;
                            break;

                        default:
                            error = "unknown option '" + args[i] + "'.";
                            return false;
                    }
                }

                return true;
            }

            static bool IsAgentKind(string value)
            {
                foreach (string kind in AgentKinds)
                {
                    if (kind == value) return true;
                }
                return false;
            }

            static bool TryReadValue(string[] args, ref int index, out string value, out string error)
            {
                value = null;
                error = null;

                if (index + 1 >= args.Length)
                {
                    error = args[index] + " needs a value.";
                    return false;
                }

                value = args[++index];
                return true;
            }
        }
    }
}
