using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sahkku.Rules;

namespace Sahkku.Rules.Bridge
{
    /// <summary>
    /// The human player's half of the interaction: the presentation layer answers these two requests
    /// (a click on a piece plus a destination, a click on one of the re-roll buttons) and resolves the
    /// returned task. Keeping it an interface is what lets <see cref="HumanPlayerAgent"/> live outside
    /// Unity and be exercised by headless tests with a scripted implementation.
    /// </summary>
    public interface IHumanInteraction
    {
        /// <summary>Asks the human whether the sáhkku on offer should be thrown again. Never resolves on its own.</summary>
        Task<RerollDecision> RequestRerollAsync(GameState state, CancellationToken cancellationToken);

        /// <summary>Asks the human which of the <paramref name="legalMoves"/> to play.</summary>
        Task<Move> RequestMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Randomness for bot agents. Deliberately wider than <see cref="IRandomSource"/> (which only draws
    /// die faces) so a bot can draw the same two ranges the pre-engine CPU drew, while a test can pin
    /// them.
    /// </summary>
    public interface IBotRandomSource
    {
        /// <summary>Returns a value in <c>[minimumInclusive, maximumExclusive)</c>; must be below 0 when the range is empty.</summary>
        int NextInt(int minimumInclusive, int maximumExclusive);
    }

    /// <summary>A human playing at the board: every decision is delegated to <see cref="IHumanInteraction"/>.</summary>
    public sealed class HumanPlayerAgent : IPlayerAgent
    {
        readonly IHumanInteraction interaction;

        public HumanPlayerAgent(PieceOwner owner, string name, IHumanInteraction interaction)
        {
            Owner = owner;
            Name = name ?? "Human";
            this.interaction = interaction;
        }

        public PieceOwner Owner { get; private set; }
        public string Name { get; private set; }

        public Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
        {
            if (interaction == null)
                return Task.FromResult(RerollDecision.KeepDiceAndProceed);
            return interaction.RequestRerollAsync(state, cancellationToken);
        }

        public Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
        {
            if (interaction == null)
                return Task.FromResult(default(Move));
            return interaction.RequestMoveAsync(state, legalMoves, cancellationToken);
        }
    }

    /// <summary>
    /// The CPU the game shipped with: pick a random movable piece, then a random index in <c>[0, 4)</c>
    /// clamped to that piece's options. It always threw a sáhkku die again, which is why a turn could
    /// only continue via the re-roll button.
    /// </summary>
    public sealed class RandomPlayerAgent : IPlayerAgent
    {
        readonly IBotRandomSource random;

        public RandomPlayerAgent(PieceOwner owner, string name, IBotRandomSource random)
        {
            Owner = owner;
            Name = name ?? "Random";
            this.random = random;
        }

        public PieceOwner Owner { get; private set; }
        public string Name { get; private set; }

        public Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
        {
            return Task.FromResult(RerollDecision.RerollActiveDie);
        }

        public Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
        {
            if (legalMoves == null || legalMoves.Count == 0) return Task.FromResult(default(Move));
            if (random == null) return Task.FromResult(legalMoves[0]);

            var pieceIds = new List<int>();
            foreach (Move move in legalMoves)
            {
                if (!pieceIds.Contains(move.pieceId)) pieceIds.Add(move.pieceId);
            }

            int pieceId = pieceIds[Clamp(random.NextInt(0, pieceIds.Count), 0, pieceIds.Count - 1)];

            var targets = new List<int>();
            foreach (Move move in legalMoves)
            {
                if (move.pieceId == pieceId) targets.Add(move.targetPlaceIndex);
            }

            // The original draw clamped the raw [0, 4) roll instead of drawing inside the real range.
            int index = Clamp(random.NextInt(0, 4), 0, targets.Count - 1);
            return Task.FromResult(new Move(pieceId, targets[index]));
        }

        static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }
    }

    /// <summary>
    /// A deterministic opponent that scores every legal move — and every re-roll choice — with the
    /// weights below, and is also the controller's fallback whenever an agent fails to answer.
    /// </summary>
    public sealed class HeuristicPlayerAgent : IPlayerAgent
    {
        /// <summary>Landing on the enemy queen ends the game, so it dominates every other consideration.</summary>
        public const int CapturingQueenScore = 10000;

        /// <summary>Getting the queen out of immediate danger.</summary>
        public const int SavingQueenScore = 5000;

        public const int CapturingSoldierScore = 500;
        public const int RecruitingKingScore = 400;
        public const int ActivatingPieceScore = 300;

        /// <summary>Per track step advanced towards the opponent's territory.</summary>
        public const int AdvancePerStepScore = 10;

        readonly RulesEngine engine;

        public HeuristicPlayerAgent(PieceOwner owner, string name, RulesEngine engine)
        {
            Owner = owner;
            Name = name ?? "Heuristic";
            this.engine = engine;
        }

        public PieceOwner Owner { get; private set; }
        public string Name { get; private set; }

        public Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
        {
            return Task.FromResult(ChooseReroll(engine, state));
        }

        public Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
        {
            return Task.FromResult(ChooseMove(engine, state, legalMoves));
        }

        /// <summary>
        /// The engine-free re-roll policy: keep the sáhkku when it buys something concrete (a piece that
        /// can be activated with it, or a queen that has to be walked out of danger), otherwise throw it
        /// again hoping for a die that goes further.
        /// </summary>
        public static RerollDecision ChooseReroll(RulesEngine engine, GameState state)
        {
            if (engine == null || state == null) return RerollDecision.KeepDiceAndProceed;
            if (!engine.CanReroll(state)) return RerollDecision.KeepDiceAndProceed;

            PieceOwner owner = state.CurrentPlayer;
            if (CanActivateAPiece(engine, state, owner)) return RerollDecision.KeepDiceAndProceed;
            if (IsQueenThreatened(engine, state, owner)) return RerollDecision.KeepDiceAndProceed;
            return RerollDecision.RerollActiveDie;
        }

        /// <summary>The highest scoring of <paramref name="legalMoves"/>; ties go to the first one, so the choice is stable.</summary>
        public static Move ChooseMove(RulesEngine engine, GameState state, IReadOnlyList<Move> legalMoves)
        {
            if (legalMoves == null || legalMoves.Count == 0) return default(Move);
            if (engine == null || state == null) return legalMoves[0];

            Move best = legalMoves[0];
            int bestScore = int.MinValue;
            foreach (Move move in legalMoves)
            {
                int score = ScoreMove(engine, state, move);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        /// <summary>True when the sáhkku up for spending can move (i.e. activate) a piece that is still waiting.</summary>
        static bool CanActivateAPiece(RulesEngine engine, GameState state, PieceOwner owner)
        {
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner != owner || piece.isActive || !piece.canBeActivated) continue;
                    if (engine.GetAllowedPlaces(state, piece).Count > 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when any opponent piece could land on the queen with the next throw. The probe runs on a
        /// clone, because it has to try every die face on a board that is otherwise untouched.
        /// </summary>
        static bool IsQueenThreatened(RulesEngine engine, GameState state, PieceOwner owner)
        {
            Piece queen = FindPiece(state, owner, PieceType.Queen);
            if (queen == null) return false;

            PieceOwner opponent = owner == PieceOwner.P1 ? PieceOwner.P2 : PieceOwner.P1;
            RuleSet rules = engine.Rules;
            for (int face = 0; face < rules.dice.faces.Length; ++face)
            {
                if (rules.dice.faces[face].steps == 0) continue;

                GameState probe = state.Clone();
                if (probe.dice.Count == 0) return false;
                probe.currentActiveDie = 0;
                probe.dice[0] = rules.ResolveFace(rules.dice.faces[face].id);

                foreach (Place place in probe.places)
                {
                    foreach (Piece candidate in place.pieces)
                    {
                        if (candidate.owner != opponent) continue;
                        if (engine.GetAllowedPlaces(probe, candidate).Contains(queen.placeIndex)) return true;
                    }
                }
            }
            return false;
        }

        static int ScoreMove(RulesEngine engine, GameState state, Move move)
        {
            Piece piece = engine.FindPiece(state, move.pieceId);
            if (piece == null) return int.MinValue;

            PieceOwner owner = piece.owner;
            bool queenWasThreatened = IsQueenThreatened(engine, state, owner);
            bool wasInactive = !piece.isActive;
            int sourceArc = piece.arc;

            GameState clone = state.Clone();
            List<RuleEvent> events;
            if (!engine.TryApplyMove(clone, move, out events)) return int.MinValue;

            int score = 0;
            foreach (RuleEvent ruleEvent in events)
            {
                switch (ruleEvent.kind)
                {
                    case RuleEventKind.QueenCaptured: score += CapturingQueenScore; break;
                    case RuleEventKind.SoldierCaptured: score += CapturingSoldierScore; break;
                    case RuleEventKind.KingRecruited: score += RecruitingKingScore; break;
                }
            }

            if (wasInactive) score += ActivatingPieceScore;

            Piece moved = engine.FindPiece(clone, move.pieceId);
            int advanced = moved == null ? 0 : ForwardSteps(engine.TrackLength, sourceArc, moved.arc);
            score += advanced * AdvancePerStepScore;

            if (queenWasThreatened && !IsQueenThreatened(engine, clone, owner)) score += SavingQueenScore;
            return score;
        }

        /// <summary>
        /// How far forward a move carried the piece, in track steps. A backwards move wraps around the
        /// track, so anything past the longest die (three) is not counted as progress.
        /// </summary>
        static int ForwardSteps(int trackLength, int fromArc, int toArc)
        {
            if (trackLength <= 0) return 0;
            int delta = ((toArc - fromArc) % trackLength + trackLength) % trackLength;
            return delta <= 3 ? delta : 0;
        }

        static Piece FindPiece(GameState state, PieceOwner owner, PieceType type)
        {
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner == owner && piece.type == type) return piece;
                }
            }
            return null;
        }
    }
}
