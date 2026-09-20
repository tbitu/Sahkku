using System;

namespace Sahkku.Bench
{
    /// <summary>
    /// Counts how often an LLM agent gave up on its endpoint and played the heuristic choice, by watching
    /// the lines the agent logs.
    ///
    /// <see cref="Sahkku.Rules.Bridge.LlmPlayerAgent"/> logs two kinds of line, and it is the shape of the
    /// line that tells them apart:
    ///
    /// <list type="bullet">
    /// <item>a decision it took from the model's own answer, which begins with <c>chose …</c> and may carry
    /// the model's free-form reasoning after it, and</item>
    /// <item>a fallback, which begins with one of the sentences below.</item>
    /// </list>
    ///
    /// A line is therefore matched structurally, on the message body the agent logged (everything after its
    /// <c>&lt;name&gt;: </c> prefix): the body has to <em>begin</em> with a fallback sentence — and, for the
    /// two that name the failure they absorbed, end with it as well. Searching the line for a phrase
    /// anywhere in it, as this class used to, let a model that merely quoted the agent's own wording in its
    /// reasoning inflate the number: that text is a decision line of an entirely different kind.
    /// </summary>
    public sealed class LlmFallbackCounter
    {
        /// <summary>The agent's separator between its name and the line it logged.</summary>
        const string NameSeparator = ": ";

        /// <summary>
        /// The four fallback lines whose wording is fixed — no endpoint configured, or an answer the agent
        /// could not use. Each is the whole body the agent logs.
        /// </summary>
        static readonly string[] FallbackSentences =
        {
            "no endpoint is configured; deciding heuristically.",
            "no endpoint is configured; playing the heuristic choice.",
            "the endpoint did not answer with a usable reroll decision; deciding heuristically.",
            "the endpoint did not answer with a usable move index; playing the heuristic choice."
        };

        /// <summary>
        /// The two fallback lines that name the failure they absorbed, and so carry the transport's own
        /// text — <c>the reroll request failed (&lt;reason&gt;); deciding heuristically.</c> and its move
        /// counterpart. Only the opening and the closing sentence are fixed, which is why these are matched
        /// on both ends at once.
        /// </summary>
        static readonly string[] FailedRequestOpenings =
        {
            "the reroll request failed (",
            "the move request failed ("
        };

        /// <summary>The two closing sentences, in the same order as <see cref="FailedRequestOpenings"/>.</summary>
        static readonly string[] FailedRequestEndings =
        {
            "); deciding heuristically.",
            "); playing the heuristic choice."
        };

        /// <summary>How many fallbacks have been observed since the last <see cref="Reset"/>.</summary>
        public int Count;

        /// <summary>Forgets the fallbacks of the previous game, so each game is reported on its own.</summary>
        public void Reset() { Count = 0; }

        /// <summary>
        /// True when <paramref name="message"/> is a fallback line, which it then counts. Every other kind
        /// of line returns false and counts nothing — including a decision whose model-written reasoning
        /// quotes a fallback sentence.
        /// </summary>
        public bool Observe(string message)
        {
            if (!IsFallback(message)) return false;
            Count++;
            return true;
        }

        static bool IsFallback(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;

            // Every line the agent logs is prefixed with its name, and no agent name contains ": ", so the
            // first separator is the one that opens the body. A line without it is not the agent's own log,
            // and there is nothing to count.
            int separator = message.IndexOf(NameSeparator, StringComparison.Ordinal);
            if (separator < 0) return false;
            string body = message.Substring(separator + NameSeparator.Length);

            foreach (string sentence in FallbackSentences)
            {
                if (string.Equals(body, sentence, StringComparison.Ordinal)) return true;
            }

            for (int i = 0; i < FailedRequestOpenings.Length; ++i)
            {
                if (body.StartsWith(FailedRequestOpenings[i], StringComparison.Ordinal) &&
                    body.EndsWith(FailedRequestEndings[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
