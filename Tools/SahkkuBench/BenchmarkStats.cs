using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sahkku.Rules;

namespace Sahkku.Bench
{
    /// <summary>
    /// Aggregates the games of one run into the numbers a benchmark is read for: who won how often, how
    /// long a game ran, how much happened in it, and whether anything went wrong.
    ///
    /// It is filled one game at a time (<see cref="Add"/>), so a long run never has to keep its results —
    /// which matters because a <see cref="MatchResult"/> pins the final board of its game.
    /// </summary>
    public sealed class BenchmarkSummary
    {
        public int TotalGames;
        public int P1Wins;
        public int P2Wins;

        /// <summary>Games that ended as a draw: no winner, either by the turn limit or because both queens went.</summary>
        public int Draws;

        /// <summary>Share of all games player one won, draws included in the denominator.</summary>
        public double P1WinRate;

        public double P2WinRate;

        public double AverageHalfMoves;
        public int MinHalfMoves;
        public int MaxHalfMoves;

        public int TotalCaptures;

        /// <summary>Sum of the per-game wall-clock times, agent decisions included.</summary>
        public TimeSpan TotalDuration;

        /// <summary>Games in which an agent proposed an illegal move or a state invariant broke.</summary>
        public int RuleViolations;

        // ------------------------------------------------------------------ extra counters

        public int TotalHalfMoves;
        public int TotalDiceRolls;
        public int TotalRerollDecisions;

        /// <summary>How often an LLM agent gave up on its endpoint and played the heuristic choice.</summary>
        public int TotalLlmFallbacks;

        /// <summary>Games that did not complete cleanly, whether by rule violation or by an agent/timeout error.</summary>
        public int Failures;

        /// <summary>One line per failed game, in the order the games ran.</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>Folds one finished game into the totals.</summary>
        public void Add(MatchResult result)
        {
            if (result == null) return;

            TotalGames++;

            if (result.Winner == PieceOwner.P1) P1Wins++;
            else if (result.Winner == PieceOwner.P2) P2Wins++;
            else Draws++;

            if (result.RuleViolationOccurred) RuleViolations++;
            if (result.Failed)
            {
                Failures++;
                Errors.Add("game " + result.MatchIndex + ": " + result.ErrorMessage);
            }

            TotalHalfMoves += result.TotalHalfMoves;
            TotalDiceRolls += result.TotalDiceRolls;
            TotalRerollDecisions += result.TotalRerollDecisions;
            TotalLlmFallbacks += result.LlmFallbacks;
            TotalCaptures += result.TotalCaptures;
            TotalDuration += result.Duration;

            if (TotalGames == 1 || result.TotalHalfMoves < MinHalfMoves) MinHalfMoves = result.TotalHalfMoves;
            if (TotalGames == 1 || result.TotalHalfMoves > MaxHalfMoves) MaxHalfMoves = result.TotalHalfMoves;

            P1WinRate = (double)P1Wins / TotalGames;
            P2WinRate = (double)P2Wins / TotalGames;
            AverageHalfMoves = (double)TotalHalfMoves / TotalGames;
        }

        /// <summary>The human-readable report: one aligned label/value column per statistic.</summary>
        public string ToPrettyString(string p1Name, string p2Name)
        {
            string p1 = Name(p1Name);
            string p2 = Name(p2Name);

            var rows = new List<string[]>
            {
                Row("games", TotalGames.ToString(CultureInfo.InvariantCulture)),
                Row("P1 wins (" + p1 + ")", P1Wins + "  " + Percent(P1WinRate)),
                Row("P2 wins (" + p2 + ")", P2Wins + "  " + Percent(P2WinRate)),
                Row("draws", Draws.ToString(CultureInfo.InvariantCulture)),
                Row("half-moves avg/min/max", Num(AverageHalfMoves) + " / " + MinHalfMoves + " / " + MaxHalfMoves),
                Row("dice rolls", TotalDiceRolls.ToString(CultureInfo.InvariantCulture)),
                Row("re-roll decisions", TotalRerollDecisions.ToString(CultureInfo.InvariantCulture)),
                Row("captures", TotalCaptures.ToString(CultureInfo.InvariantCulture)),
                Row("rule violations", RuleViolations.ToString(CultureInfo.InvariantCulture)),
                Row("failures", Failures.ToString(CultureInfo.InvariantCulture)),
                Row("LLM fallbacks", TotalLlmFallbacks.ToString(CultureInfo.InvariantCulture)),
                Row("duration", Num(TotalDuration.TotalSeconds) + "s")
            };

            int width = 0;
            foreach (string[] row in rows) width = Math.Max(width, row[0].Length);

            string header = "Sáhkku benchmark: " + p1 + " (P1) vs " + p2 + " (P2)";
            var sb = new StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine(new string('-', header.Length));
            foreach (string[] row in rows)
            {
                sb.Append(row[0].PadRight(width)).Append("  ").AppendLine(row[1]);
            }

            // A failed game is the one thing a reader must not have to go looking for, so the first few
            // reasons are printed with the table rather than left to the JSON.
            const int MaxReportedErrors = 5;
            for (int i = 0; i < Errors.Count && i < MaxReportedErrors; ++i) sb.AppendLine("  ! " + Errors[i]);
            if (Errors.Count > MaxReportedErrors) sb.AppendLine("  ! ... and " + (Errors.Count - MaxReportedErrors) + " more");

            return sb.ToString();
        }

        /// <summary>The machine-readable report, for CI: the same numbers, with the two agent names and the errors.</summary>
        public string ToJsonString(string p1Name, string p2Name)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"p1\":").Append(Quote(Name(p1Name))).Append(',');
            sb.Append("\"p2\":").Append(Quote(Name(p2Name))).Append(',');
            sb.Append("\"totalGames\":").Append(TotalGames).Append(',');
            sb.Append("\"p1Wins\":").Append(P1Wins).Append(',');
            sb.Append("\"p2Wins\":").Append(P2Wins).Append(',');
            sb.Append("\"draws\":").Append(Draws).Append(',');
            sb.Append("\"p1WinRate\":").Append(Num(P1WinRate)).Append(',');
            sb.Append("\"p2WinRate\":").Append(Num(P2WinRate)).Append(',');
            sb.Append("\"averageHalfMoves\":").Append(Num(AverageHalfMoves)).Append(',');
            sb.Append("\"minHalfMoves\":").Append(MinHalfMoves).Append(',');
            sb.Append("\"maxHalfMoves\":").Append(MaxHalfMoves).Append(',');
            sb.Append("\"totalHalfMoves\":").Append(TotalHalfMoves).Append(',');
            sb.Append("\"totalCaptures\":").Append(TotalCaptures).Append(',');
            sb.Append("\"totalDiceRolls\":").Append(TotalDiceRolls).Append(',');
            sb.Append("\"totalRerollDecisions\":").Append(TotalRerollDecisions).Append(',');
            sb.Append("\"totalLlmFallbacks\":").Append(TotalLlmFallbacks).Append(',');
            sb.Append("\"ruleViolations\":").Append(RuleViolations).Append(',');
            sb.Append("\"failures\":").Append(Failures).Append(',');
            sb.Append("\"totalDurationSeconds\":").Append(Num(TotalDuration.TotalSeconds)).Append(',');
            sb.Append("\"errors\":[");
            for (int i = 0; i < Errors.Count; ++i)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(Errors[i]));
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ formatting

        static string[] Row(string label, string value)
        {
            return new[] { label, value };
        }

        static string Name(string name)
        {
            return string.IsNullOrEmpty(name) ? "agent" : name;
        }

        /// <summary>Four decimals, trailing zeros dropped: 0.7 rather than 0.7000, so both readers agree.</summary>
        static string Num(double value)
        {
            double rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        static string Percent(double rate)
        {
            return Num(rate * 100.0) + "%";
        }

        static string Quote(string value)
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
}
