using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Sahkku.Rules;

/// <summary>
/// Unity-facing gameplay driver for sáhkku. All rule decisions are delegated to the pure-C#
/// <see cref="RulesEngine"/> (defined by Assets/Resources/SahkkuRules.json); this class only
/// orchestrates input, timers, presentation and audio, and translates engine events to sounds.
/// </summary>
public class GameLogic : MonoBehaviour
{
    public static GameLogic Instance;

    public bool keyboardDebug = true;

    GameState state;
    RulesEngine engine;
    UnityRandomSource randomSource;
    IActionSelector actionSelector;

    float aiTimer = 0.0f;
    float aiSpeed = 1.0f;
    bool aiCanAct = false;
    int playerPieceIndex = 0;
    bool tryRollDice = false;

    public static int BOARD_SIZE_X { get { return RuleSetProvider.Load().board.width; } }
    public static int BOARD_SIZE_Y { get { return RuleSetProvider.Load().board.height; } }

    public List<Place> places { get { return state.places; } }
    public List<DieFace> dice { get { return state.dice; } }
    public TurnPhase turnPhase { get { return state.turnPhase; } set { state.turnPhase = value; } }
    public int currentActiveDie { get { return state.currentActiveDie; } set { state.currentActiveDie = value; } }
    public bool gameOver { get { return state.gameOver; } }
    public GameSettings.Player winner { get { return state.winner == PieceOwner.P2 ? GameSettings.Player.Two : GameSettings.Player.One; } }
    public int p1captures { get { return state.p1Captures; } }
    public int p2captures { get { return state.p2Captures; } }

    void Awake()
    {
        Instance = this;
        InitGame();
    }

    void Update()
    {
        if (state.gameOver)
        {
            ClearAllAllowedPlaces();
            return;
        }

        aiCanAct = false;
        if (Time.time > aiTimer)
        {
            aiTimer = Time.time + aiSpeed;
            aiCanAct = true;
        }

        if ((Keyboard.current.spaceKey.wasPressedThisFrame && keyboardDebug) || tryRollDice || (GameSettings.singlePlayer && GetCurrentPlayer() == PieceOwner.P2 && aiCanAct))
        {
            tryRollDice = false;

            if (turnPhase == TurnPhase.P1roll || turnPhase == TurnPhase.P2roll || CanReroll())
            {
                if (turnPhase == TurnPhase.P1roll || turnPhase == TurnPhase.P2roll)
                {
                    ThrowAllDice();
                    engine.OrderDice(state);
                    currentActiveDie = 0;
                    turnPhase = (TurnPhase)((int)turnPhase + 1);
                }
                else
                {
                    RerollSingleDie();
                    engine.OrderDice(state);
                }

                if (!CheckAllowedPieceMovement())
                {
                    NextPlayerTurn();
                }
            }
        }

        if (keyboardDebug)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) playerPieceIndex = 0;
            if (Keyboard.current.digit2Key.wasPressedThisFrame) playerPieceIndex = 1;
            if (Keyboard.current.digit3Key.wasPressedThisFrame) playerPieceIndex = 2;
            if (Keyboard.current.digit4Key.wasPressedThisFrame) playerPieceIndex = 3;
            if (Keyboard.current.digit5Key.wasPressedThisFrame) playerPieceIndex = 4;
            if (Keyboard.current.digit6Key.wasPressedThisFrame) playerPieceIndex = 5;
            if (Keyboard.current.digit7Key.wasPressedThisFrame) playerPieceIndex = 6;
            if (Keyboard.current.digit8Key.wasPressedThisFrame) playerPieceIndex = 7;
        }

        if ((Keyboard.current.enterKey.wasPressedThisFrame && keyboardDebug) || (GameSettings.singlePlayer && GetCurrentPlayer() == PieceOwner.P2 && aiCanAct))
        {
            if (turnPhase == TurnPhase.P1move || turnPhase == TurnPhase.P2move)
            {
                if (GameSettings.singlePlayer && GetCurrentPlayer() == PieceOwner.P2)
                {
                    Move aiMove;
                    if (actionSelector.TryChooseAction(state, engine.GetLegalMoves(state), out aiMove))
                    {
                        MovePiece(engine.FindPiece(state, aiMove.pieceId), aiMove.targetPlaceIndex);
                    }
                }
                else
                {
                    List<Piece> potentialPieces = engine.GetPotentialPieces(state);
                    int pieceIndex = Mathf.Clamp(playerPieceIndex, 0, potentialPieces.Count - 1);
                    Piece piece = potentialPieces[pieceIndex];
                    int moveIndex = Mathf.Clamp(UnityEngine.Random.Range(0, 4), 0, piece.allowedPlaces.Count - 1);
                    MovePiece(piece, piece.allowedPlaces[moveIndex]);
                }
            }
        }
    }

    public void InitGame()
    {
        engine = new RulesEngine(RuleSetProvider.Load());
        randomSource = new UnityRandomSource();
        actionSelector = new RandomActionSelector();

        state = engine.InitGame(new EngineOptions(
            GameSettings.startingPlayer == GameSettings.Player.One ? PieceOwner.P1 : PieceOwner.P2,
            GameSettings.evenOdds));
    }

    public void InitDiceRoll()
    {
        tryRollDice = true;
    }

    public PieceOwner GetCurrentPlayer()
    {
        return state.CurrentPlayer;
    }

    public bool CanReroll()
    {
        return engine.CanReroll(state);
    }

    public void MovePiece(Piece piece, int placeIndex)
    {
        if (piece == null) return;

        if (piece.allowedPlaces.Count == 0)
        {
            Debug.LogWarning("Trying to move piece without valid places!", gameObject);
            return;
        }

        Debug.Log("Move piece (" + piece.type + ") to place " + placeIndex);

        List<RuleEvent> events = engine.ApplyMove(state, new Move(piece.id, placeIndex));
        PlayRuleEvents(events);

        GameInteraction.Instance.UpdatePieces();
    }

    void NextPlayerTurn()
    {
        engine.NextPlayerTurn(state);
        GameInteraction.Instance.UpdatePieces();
    }

    bool CheckAllowedPieceMovement()
    {
        bool anyAllowedPlaces = engine.EvaluateAllowedPlaces(state);
        GameInteraction.Instance.UpdatePieces();
        return anyAllowedPlaces;
    }

    void ClearAllAllowedPlaces()
    {
        engine.ClearAllowedPlaces(state);
        GameInteraction.Instance.UpdatePieces();
    }

    void ThrowAllDice()
    {
        engine.RollAllDice(state, randomSource);
        for (int i = 0; i < state.dice.Count; ++i)
        {
            GameInteraction.Instance.RollDice(i);
        }
        AudioManager.Instance.PlayRandomSound("BircutGolbma", 13, 0.5f);
    }

    void RerollSingleDie()
    {
        engine.RerollFirstDie(state, randomSource);
        GameInteraction.Instance.RollDice(0);
        AudioManager.Instance.PlayRandomSound("BircutOkta", 8, 0.5f);
    }

    void PlayRuleEvents(List<RuleEvent> events)
    {
        foreach (RuleEvent ruleEvent in events)
        {
            switch (ruleEvent.kind)
            {
                case RuleEventKind.SoldierCaptured:
                    AudioManager.Instance.PlayRandomSound("GodditGalgu", 3);
                    break;

                case RuleEventKind.KingRecruited:
                    AudioManager.Instance.PlaySound("FasketGonagas1", 3);
                    break;

                case RuleEventKind.QueenCaptured:
                    // The win sound is played by the following GameWon event.
                    break;

                case RuleEventKind.GameWon:
                    AudioManager.Instance.PlaySound("Riskut");
                    break;

                case RuleEventKind.PieceMoved:
                    PlayMoveSound(ruleEvent.pieceType, ruleEvent.owner);
                    break;
            }
        }
    }

    void PlayMoveSound(PieceType type, PieceOwner owner)
    {
        if (type == PieceType.Soldier)
        {
            if (owner == PieceOwner.P1) AudioManager.Instance.PlayRandomSound("MuorraGalguOkta", 5);
            else AudioManager.Instance.PlayRandomSound("MuorraOlmmaiOkta", 9);
        }
        else
        {
            if (owner == PieceOwner.P1) AudioManager.Instance.PlayRandomSound("MuorraDronnetSamiOkta", 5);
            else AudioManager.Instance.PlayRandomSound("MuorraDronnetDaccaOkta", 4);
        }
    }
}
