using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

/// <summary>
/// Presentation and input for the game scene. It renders the authoritative
/// <see cref="GameState"/> and answers the human half of the agent contract
/// (<see cref="IHumanInteraction"/>): a click on a selectable piece plus a destination resolves
/// <c>RequestMoveAsync</c>, and the two re-roll buttons resolve <c>RequestRerollAsync</c>. It never
/// decides a rule itself — illegal clicks are simply not offered in the first place.
/// </summary>
public class GameInteraction : MonoBehaviour, IHumanInteraction
{
    public static GameInteraction Instance;

    [SerializeField] Camera camera;
    [SerializeField] LayerMask p1mask;
    [SerializeField] LayerMask p2mask;
    [SerializeField] GameObject placePrefab;
    [SerializeField] GameObject[] kingPrefab;
    [SerializeField] GameObject[] p1soldierPrefab;
    [SerializeField] GameObject[] p1queenPrefab;
    [SerializeField] GameObject[] p2soldierPrefab;
    [SerializeField] GameObject[] p2queenPrefab;
    [SerializeField] GameObject dicePrefab;
    [SerializeField] GameObject p1capturesPos;
    [SerializeField] GameObject p2capturesPos;
    [SerializeField] GameObject diePos1;
    [SerializeField] GameObject diePos2;
    [SerializeField] GameObject diePos3;
    [SerializeField] Material[] p1SoldierPieceMaterial;
    [SerializeField] Material[] p1SoldierSelectablePieceMaterial;
    [SerializeField] Material[] p2SoldierPieceMaterial;
    [SerializeField] Material[] p2SoldierSelectablePieceMaterial;
    [SerializeField] Material[] p1QueenPieceMaterial;
    [SerializeField] Material[] p1QueenSelectablePieceMaterial;
    [SerializeField] Material[] p2QueenPieceMaterial;
    [SerializeField] Material[] p2QueenSelectablePieceMaterial;
    [SerializeField] Material[] kingPieceMaterial;
    [SerializeField] Material[] kingSelectablePieceMaterial;
    [SerializeField] TextMeshProUGUI gameStatus;

    // The scene wires this to the button that used to throw the dice by hand. Throwing happens
    // automatically now, so it is only shown while a human is asked about a sáhkku re-roll, where it
    // means "throw that die again".
    [FormerlySerializedAs("rollDiceButton")]
    [SerializeField] GameObject rerollButton;

    [SerializeField] GameObject dieHighlight1;
    [SerializeField] GameObject dieHighlight2;
    [SerializeField] GameObject dieHighlight3;
    int p1MaterialIndex = 0;
    int p2MaterialIndex = 0;
    int kingMaterialIndex = 0;

    const string LocalizationTable = "UI_Text";
    const string RerollKey = "Reroll_Die";
    const string KeepKey = "Keep_Die";
    const string ThinkingKey = "Thinking";

    List<GameObject> places = new List<GameObject>();
    List<GameObject> p1Soldiers = new List<GameObject>();
    List<GameObject> p2Soldiers = new List<GameObject>();
    GameObject p1queen;
    GameObject p2queen;
    GameObject king;
    List<GameObject> dice = new List<GameObject>();
    PieceData selectedPiece;
    GameObject keepDiceButton;

    // The status line's translated entries, read from the table once at start-up. Unity Localization keeps
    // its strings in Addressables, and on WebGL a read of one can only happen asynchronously — asking for a
    // string directly from Update() would throw there — so they are fetched once and then read from here.
    readonly Dictionary<string, string> statusText = new Dictionary<string, string>();

    /// <summary>
    /// The entries the status line can show: whose turn it is at each phase, how the match ended, and the
    /// note that a bot is thinking. These are the only keys this scene asks the table for itself.
    /// </summary>
    static readonly string[] StatusKeys =
    {
        "Player_1_Roll",
        "Player_1_Move",
        "Player_2_Roll",
        "Player_2_Move",
        "Player_1_Win_Soldier",
        "Player_1_Win_Queen",
        "Player_2_Win_Soldier",
        "Player_2_Win_Queen",
        ThinkingKey,
    };

    // The human's pending decision. The match controller awaits these tasks; a click resolves them.
    TaskCompletionSource<RerollDecision> rerollDecision;
    TaskCompletionSource<Move> pendingMove;
    IReadOnlyList<Move> pendingLegalMoves;

    // The registration that cancels a pending decision when the match ends. It is released as soon as the
    // decision settles, so a whole match does not accumulate one registration per decision.
    CancellationTokenRegistration rerollCancellation;
    CancellationTokenRegistration moveCancellation;

    public Vector2 boardScalar = new Vector2(1.0f, 1.0f);

    /// <summary>False until the board, the dice and the re-roll buttons exist; the controller waits for it.</summary>
    public bool IsReady { get; private set; }

    Vector3 GetScaledBoardPosition(int x, int y)
    {
        return new Vector3(x * boardScalar.x, 0.0f, y * boardScalar.y);
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        p1MaterialIndex = (int)GameSettings.p1Model;
        p2MaterialIndex = (int)GameSettings.p2Model;
        kingMaterialIndex = (int)GameSettings.kingModel;

        int y = 0;
        for (int x = 0; x < GameLogic.BOARD_SIZE_X; ++x)
        {
            GameObject newPlace = Instantiate(placePrefab, GetScaledBoardPosition(x, y), Quaternion.identity);
            newPlace.name = places.Count.ToString();
            places.Add(newPlace);
        }

        y = 1;
        for (int x = GameLogic.BOARD_SIZE_X - 1; x >= 0; --x)
        {
            GameObject newPlace = Instantiate(placePrefab, GetScaledBoardPosition(x, y), Quaternion.identity);
            newPlace.name = places.Count.ToString();
            places.Add(newPlace);
        }

        y = 2;
        for (int x = 0; x < GameLogic.BOARD_SIZE_X; ++x)
        {
            GameObject newPlace = Instantiate(placePrefab, GetScaledBoardPosition(x, y), Quaternion.identity);
            newPlace.name = places.Count.ToString();
            places.Add(newPlace);
        }

        for (int i = 0; i < GameLogic.BOARD_SIZE_X; ++i)
        {
            p1Soldiers.Add(Instantiate(p1soldierPrefab[p1MaterialIndex]));
            p2Soldiers.Add(Instantiate(p2soldierPrefab[p2MaterialIndex]));
        }

        p1queen = Instantiate(p1queenPrefab[p1MaterialIndex]);
        p2queen = Instantiate(p2queenPrefab[p2MaterialIndex]);
        king = Instantiate(kingPrefab[kingMaterialIndex]);

        HideAllModels();

        dice.Add(Instantiate(dicePrefab, diePos1.transform.position, Quaternion.identity));
        dice.Add(Instantiate(dicePrefab, diePos2.transform.position, Quaternion.identity));
        dice.Add(Instantiate(dicePrefab, diePos3.transform.position, Quaternion.identity));

        SetupRerollButtons();

        // The status line's text is read in the background: Start() must not wait on the string table, or
        // the board would never be reported ready and the match would wait for a presentation that is
        // already finished.
        StartCoroutine(LoadStatusText());

        UpdatePieces();
        IsReady = true;
    }

    void OnDestroy()
    {
        CancelPendingDecisions();
    }

    // ------------------------------------------------------------------ the human agent's half

    public Task<RerollDecision> RequestRerollAsync(GameState state, CancellationToken cancellationToken)
    {
        ReleaseCancellation(ref rerollCancellation);
        rerollDecision = NewCompletion<RerollDecision>();
        ShowRerollButtons(true);
        rerollCancellation = RegisterCancellation(rerollDecision, cancellationToken);
        return rerollDecision.Task;
    }

    public Task<Move> RequestMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
    {
        selectedPiece = null;
        pendingLegalMoves = legalMoves;
        ReleaseCancellation(ref moveCancellation);
        pendingMove = NewCompletion<Move>();
        ShowRerollButtons(false);
        UpdatePieces();
        moveCancellation = RegisterCancellation(pendingMove, cancellationToken);
        return pendingMove.Task;
    }

    /// <summary>Called by the re-roll buttons; also usable by keyboard/touch front-ends.</summary>
    public void ChooseReroll(RerollDecision decision)
    {
        ResolvePendingReroll(decision);
    }

    static TaskCompletionSource<T> NewCompletion<T>()
    {
        // Run the continuations asynchronously, so resolving from a click hands control back to the
        // match loop on the next main-thread pump instead of re-entering it inside the click handler.
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Registers the cancellation that ends the decision when the match itself is cancelled.</summary>
    static CancellationTokenRegistration RegisterCancellation<T>(TaskCompletionSource<T> pending, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled) return default(CancellationTokenRegistration);
        return cancellationToken.Register(delegate { pending.TrySetCanceled(cancellationToken); });
    }

    /// <summary>
    /// Drops a token registration once the decision it belonged to has settled, so a match does not keep one
    /// registration (and the task source it holds) alive per decision until the scene is gone.
    /// </summary>
    static void ReleaseCancellation(ref CancellationTokenRegistration registration)
    {
        CancellationTokenRegistration released = registration;
        registration = default(CancellationTokenRegistration);
        released.Dispose();
    }

    void ResolvePendingReroll(RerollDecision decision)
    {
        TaskCompletionSource<RerollDecision> pending = rerollDecision;
        if (pending == null) return;

        rerollDecision = null;
        ShowRerollButtons(false);
        ReleaseCancellation(ref rerollCancellation);
        pending.TrySetResult(decision);
    }

    void ResolvePendingMove(Move move)
    {
        TaskCompletionSource<Move> pending = pendingMove;
        if (pending == null) return;

        pendingMove = null;
        pendingLegalMoves = null;
        ReleaseCancellation(ref moveCancellation);
        pending.TrySetResult(move);
    }

    /// <summary>
    /// Completes whatever the human still owes the match and drops the registrations behind it. The
    /// controller is awaiting these tasks, so they have to be completed before the scene goes away: a
    /// cancelled task unwinds the match loop, while a registration that is merely dropped would leave it
    /// waiting on a decision nobody can make any more.
    /// </summary>
    void CancelPendingDecisions()
    {
        TaskCompletionSource<RerollDecision> reroll = rerollDecision;
        rerollDecision = null;
        ReleaseCancellation(ref rerollCancellation);
        if (reroll != null) reroll.TrySetCanceled();

        TaskCompletionSource<Move> move = pendingMove;
        pendingMove = null;
        pendingLegalMoves = null;
        ReleaseCancellation(ref moveCancellation);
        if (move != null) move.TrySetCanceled();
    }

    bool IsPendingLegal(Move move)
    {
        if (pendingLegalMoves == null) return false;
        foreach (Move candidate in pendingLegalMoves)
        {
            if (candidate.pieceId == move.pieceId && candidate.targetPlaceIndex == move.targetPlaceIndex) return true;
        }
        return false;
    }

    /// <summary>
    /// The re-roll choice needs two buttons, so the "keep the dice" one is grown from the button the
    /// scene already has. That way neither button depends on scene wiring added by hand.
    /// </summary>
    void SetupRerollButtons()
    {
        if (rerollButton == null)
        {
            Debug.LogWarning("GameInteraction has no re-roll button; a human's re-roll choice cannot be shown.", this);
            return;
        }

        SetButtonLabel(rerollButton, RerollKey);

        keepDiceButton = Instantiate(rerollButton, rerollButton.transform.parent);
        keepDiceButton.name = "KeepDice";
        SetButtonLabel(keepDiceButton, KeepKey);

        RectTransform rerollRect = rerollButton.GetComponent<RectTransform>();
        RectTransform keepRect = keepDiceButton.GetComponent<RectTransform>();
        if (rerollRect != null)
        {
            rerollRect.anchoredPosition = new Vector2(-170.0f, rerollRect.anchoredPosition.y);
            keepRect.anchoredPosition = new Vector2(170.0f, rerollRect.anchoredPosition.y);
        }

        // The copy inherited the original's scene wiring (which meant "throw the die again"); the
        // "keep the dice" button has to answer the other way.
        Button keepChoice = keepDiceButton.GetComponent<Button>();
        if (keepChoice != null)
        {
            keepChoice.onClick = new Button.ButtonClickedEvent();
            keepChoice.onClick.AddListener(KeepDice);
        }

        ShowRerollButtons(false);
    }

    void SetButtonLabel(GameObject button, string localizationKey)
    {
        if (button == null) return;

        // The label means something different per state, so it is driven from here. The text is written
        // straight away and the button's localizer is then pointed at this state's entry: Unity
        // Localization reads its tables from Addressables, and on WebGL that read can only be asynchronous,
        // so looking the string up here would throw and leave Start() — and with it IsReady — unfinished.
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.text = FallbackLabel(localizationKey);

        LocalizeStringEvent localize = button.GetComponentInChildren<LocalizeStringEvent>(true);
        if (localize != null && localize.StringReference != null)
        {
            // Left enabled, the localizer both fills the label in when the entry arrives and keeps it in
            // step with the selected language.
            localize.enabled = true;
            localize.StringReference.SetReference(LocalizationTable, localizationKey);
        }
    }

    /// <summary>
    /// Readable text for a button label until its entry has been read from the table. The two re-roll
    /// choices say what that button means in the state it is shown in; anything else keeps its key.
    /// </summary>
    static string FallbackLabel(string localizationKey)
    {
        if (localizationKey == RerollKey) return "Throw Again";
        if (localizationKey == KeepKey) return "Keep Dice";
        return localizationKey;
    }

    void ShowRerollButtons(bool visible)
    {
        if (rerollButton != null) rerollButton.SetActive(visible);
        if (keepDiceButton != null) keepDiceButton.SetActive(visible);
    }

    public void KeepDice()
    {
        ResolvePendingReroll(RerollDecision.KeepDiceAndProceed);
    }

    // ------------------------------------------------------------------ rendering and input

    void Update()
    {
        UpdateStatusText();
        UpdateDieHighlight();
        UpdateRerollButtons();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            BackToMainMenu();
        }

        ReadPointer();
    }

    void UpdateStatusText()
    {
        if (gameStatus == null) return;

        if (GameLogic.Instance.gameOver)
        {
            // The engine reports why the game ended, so the UI never re-derives a rule.
            bool soldiersExhausted = GameLogic.Instance.winReason == WinReason.OpponentSoldiersExhausted;
            bool playerOneWon = GameLogic.Instance.winner == GameSettings.Player.One;

            if (playerOneWon && soldiersExhausted)
            {
                gameStatus.text = Localize("Player_1_Win_Soldier");
            }
            else if (playerOneWon)
            {
                gameStatus.text = Localize("Player_1_Win_Queen");
            }
            else if (soldiersExhausted)
            {
                gameStatus.text = Localize("Player_2_Win_Soldier");
            }
            else
            {
                gameStatus.text = Localize("Player_2_Win_Queen");
            }
            return;
        }

        switch (GameLogic.Instance.turnPhase)
        {
            case TurnPhase.P1roll:
                gameStatus.text = Localize("Player_1_Roll");
                break;

            case TurnPhase.P1move:
                gameStatus.text = Localize("Player_1_Move");
                break;

            case TurnPhase.P2roll:
                gameStatus.text = Localize("Player_2_Roll");
                break;

            case TurnPhase.P2move:
                gameStatus.text = Localize("Player_2_Move");
                break;
        }

        // Both hotseat players see whose turn it is; a bot's side says that it is thinking.
        if (!GameLogic.Instance.IsCurrentPlayerHuman)
        {
            gameStatus.text = gameStatus.text + " — " + Localize(ThinkingKey);
        }
    }

    /// <summary>
    /// The text behind a status key. The table is read asynchronously, so until the entry arrives the key
    /// itself stands in — the line is never left blank, and the entry replaces it on a later frame.
    /// </summary>
    string Localize(string key)
    {
        return statusText.TryGetValue(key, out string text) ? text : key;
    }

    /// <summary>
    /// Reads the strings the status line is made of, once the localization system is up. Each entry is read
    /// through a <see cref="LocalizedString"/>, which on WebGL loads it asynchronously; one that is missing
    /// from the table keeps its key as the text instead.
    /// </summary>
    IEnumerator LoadStatusText()
    {
        yield return LocalizationSettings.InitializationOperation;

        foreach (string key in StatusKeys)
        {
            AsyncOperationHandle<string> entry = new LocalizedString(LocalizationTable, key).GetLocalizedStringAsync();

            // The entry is held across frames while it is read, so this coroutine takes a reference of its
            // own on the operation and hands it back once the text has been taken — or once the scene is
            // gone and the coroutine is stopped part way through. Only that one reference is released: Unity
            // Localization drops an asynchronous string operation by itself, a frame after it completed, so
            // releasing the handle a second time would take the operation's reference count below what it
            // was given.
            Addressables.ResourceManager.Acquire(entry);
            try
            {
                yield return entry;

                string translated = entry.IsValid() ? entry.Result : null;
                if (!string.IsNullOrEmpty(translated))
                {
                    statusText[key] = translated;
                }
            }
            finally
            {
                Addressables.Release(entry);
            }
        }
    }

    void UpdateDieHighlight()
    {
        bool moving = GameLogic.Instance.turnPhase == TurnPhase.P1move || GameLogic.Instance.turnPhase == TurnPhase.P2move;
        bool show = moving && !GameLogic.Instance.gameOver;

        if (dieHighlight1 != null) dieHighlight1.SetActive(show && GameLogic.Instance.currentActiveDie == 0);
        if (dieHighlight2 != null) dieHighlight2.SetActive(show && GameLogic.Instance.currentActiveDie == 1);
        if (dieHighlight3 != null) dieHighlight3.SetActive(show && GameLogic.Instance.currentActiveDie == 2);
    }

    void UpdateRerollButtons()
    {
        // The buttons are visible exactly while a human is being asked about the re-roll.
        bool pending = rerollDecision != null && !GameLogic.Instance.gameOver;
        if (rerollButton != null && rerollButton.activeSelf != pending) rerollButton.SetActive(pending);
        if (keepDiceButton != null && keepDiceButton.activeSelf != pending) keepDiceButton.SetActive(pending);
    }

    void ReadPointer()
    {
        bool interactionThisFrame = false;
        Vector2 interactionPosition = Vector2.zero;

        // A browser can hand the input system a touchscreen even when the player is on a mouse, so the
        // devices are checked in turn rather than one hiding the other: a touch that was actually pressed
        // this frame is answered, and anything else falls through to the mouse.
        if (Touchscreen.current != null &&
            Touchscreen.current.touches.Count > 0 &&
            Touchscreen.current.touches[0].press.wasPressedThisFrame)
        {
            interactionThisFrame = true;
            interactionPosition = Touchscreen.current.touches[0].position.ReadValue();
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            interactionThisFrame = true;
            interactionPosition = Mouse.current.position.ReadValue();
        }

        if (!interactionThisFrame || pendingMove == null) return;

        Ray ray = camera.ScreenPointToRay(interactionPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000.0f, GameLogic.Instance.GetCurrentPlayer() == PieceOwner.P1 ? p1mask : p2mask)) return;

        PieceData data = hit.transform.GetComponent<PieceData>();
        if (data != null)
        {
            // Both hotseat players select their own pieces exactly the same way.
            if (data.pieceInfo != null && data.pieceInfo.IsSelectable())
            {
                selectedPiece = data;
                UpdatePieces();
                for (int i = 0; i < places.Count; ++i)
                {
                    places[i].SetActive(data.pieceInfo.allowedPlaces.Contains(i));
                }
            }
            return;
        }

        if (hit.transform.tag != "Place" || selectedPiece == null) return;

        var move = new Move(selectedPiece.pieceInfo.id, int.Parse(hit.transform.name));
        if (!IsPendingLegal(move)) return;

        PieceData chosen = selectedPiece;
        selectedPiece = null;
        ResolvePendingMove(move);
        Debug.Log("Move piece (" + chosen.pieceInfo.type + ") to place " + move.targetPlaceIndex);
        UpdatePieces();
    }

    public void BackToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    public void UpdatePieces()
    {
        HideAllModels();

        int p1soldierIndex = 0;
        int p2soldierIndex = 0;

        foreach (Place place in GameLogic.Instance.places)
        {
            int pieceCountInPlace = place.pieces.Count;
            float centerOffset = (pieceCountInPlace - 1) * 0.5f;
            int currentPiece = 0;
            foreach (Piece piece in place.pieces)
            {
                Vector3 offset = Vector3.back * (currentPiece - centerOffset);

                if (selectedPiece != null)
                {
                    if (selectedPiece.pieceInfo == piece)
                    {
                        offset += Vector3.up * 0.5f;
                    }
                }

                if (piece.type == PieceType.Soldier)
                {
                    if (piece.owner == PieceOwner.P1)
                    {
                        p1Soldiers[p1soldierIndex].transform.GetComponent<MeshRenderer>().material = piece.IsSelectable() ? p1SoldierSelectablePieceMaterial[p1MaterialIndex] : p1SoldierPieceMaterial[p1MaterialIndex];
                        p1Soldiers[p1soldierIndex].transform.position = GetScaledBoardPosition(place.x, place.y) + offset;
                        p1Soldiers[p1soldierIndex].SetActive(true);
                        p1Soldiers[p1soldierIndex].name = piece.placeIndex.ToString();
                        p1Soldiers[p1soldierIndex].GetComponent<PieceData>().pieceInfo = piece;
                        p1soldierIndex++;
                    }
                    else
                    {
                        p2Soldiers[p2soldierIndex].transform.GetComponent<MeshRenderer>().material = piece.IsSelectable() ? p2SoldierSelectablePieceMaterial[p2MaterialIndex] : p2SoldierPieceMaterial[p2MaterialIndex];
                        p2Soldiers[p2soldierIndex].transform.position = GetScaledBoardPosition(place.x, place.y) + offset;
                        p2Soldiers[p2soldierIndex].SetActive(true);
                        p2Soldiers[p2soldierIndex].name = piece.placeIndex.ToString();
                        p2Soldiers[p2soldierIndex].GetComponent<PieceData>().pieceInfo = piece;
                        p2soldierIndex++;
                    }
                }
                else if (piece.type == PieceType.Queen)
                {
                    if (piece.owner == PieceOwner.P1)
                    {
                        p1queen.transform.GetComponent<MeshRenderer>().material = piece.IsSelectable() ? p1QueenSelectablePieceMaterial[p1MaterialIndex] : p1QueenPieceMaterial[p1MaterialIndex];
                        p1queen.transform.position = GetScaledBoardPosition(place.x, place.y) + offset;
                        p1queen.name = piece.placeIndex.ToString();
                        p1queen.GetComponent<PieceData>().pieceInfo = piece;
                        p1queen.SetActive(true);
                    }
                    else
                    {
                        p2queen.transform.GetComponent<MeshRenderer>().material = piece.IsSelectable() ? p2QueenSelectablePieceMaterial[p2MaterialIndex] : p2QueenPieceMaterial[p2MaterialIndex];
                        p2queen.transform.position = GetScaledBoardPosition(place.x, place.y) + offset;
                        p2queen.name = piece.placeIndex.ToString();
                        p2queen.GetComponent<PieceData>().pieceInfo = piece;
                        p2queen.SetActive(true);
                    }
                }
                else if (piece.type == PieceType.King)
                {
                    king.transform.GetComponent<MeshRenderer>().material = piece.IsSelectable() ? kingSelectablePieceMaterial[kingMaterialIndex] : kingPieceMaterial[kingMaterialIndex];
                    king.transform.position = GetScaledBoardPosition(place.x, place.y) + offset;
                    king.name = piece.placeIndex.ToString();
                    king.GetComponent<PieceData>().pieceInfo = piece;
                    king.SetActive(true);
                    king.layer = LayerMask.NameToLayer(piece.owner == PieceOwner.P1 ? "P1" : "P2");
                }
                currentPiece++;
            }
        }

        for (int i = 0; i < GameLogic.Instance.p2captures; ++i)
        {
            if (p1soldierIndex < p1Soldiers.Count)
            {
                p1Soldiers[p1soldierIndex].transform.GetComponent<MeshRenderer>().material = p1SoldierPieceMaterial[p1MaterialIndex];
                p1Soldiers[p1soldierIndex].transform.position = p2capturesPos.transform.position + Vector3.right * i;
                p1Soldiers[p1soldierIndex].SetActive(true);
                p1Soldiers[p1soldierIndex].name = "captured";
                p1soldierIndex++;
            }
        }

        for (int i = 0; i < GameLogic.Instance.p1captures; ++i)
        {
            if (p2soldierIndex < p2Soldiers.Count)
            {
                p2Soldiers[p2soldierIndex].transform.GetComponent<MeshRenderer>().material = p2SoldierPieceMaterial[p2MaterialIndex];
                p2Soldiers[p2soldierIndex].transform.position = p1capturesPos.transform.position - Vector3.right * i;
                p2Soldiers[p2soldierIndex].SetActive(true);
                p2Soldiers[p2soldierIndex].name = "captured";
                p2soldierIndex++;
            }
        }
    }

    void HideAllModels()
    {
        foreach (GameObject go in places)
        {
            go.SetActive(false);
        }
        foreach (GameObject go in p1Soldiers) go.SetActive(false);
        foreach (GameObject go in p2Soldiers) go.SetActive(false);
        p1queen.SetActive(false);
        p2queen.SetActive(false);
        king.SetActive(false);
    }

    public void RollDice(int index)
    {
        dice[index].transform.localEulerAngles = new Vector3(0.0f, 0.0f, 0.0f);
        dice[index].GetComponentInChildren<Animator>().StopPlayback();
        dice[index].GetComponentInChildren<Animator>().SetTrigger("Throw");
    }

    public void DisplayDiceResults()
    {
        for (int i = 0; i < GameLogic.Instance.dice.Count; ++i)
        {
            if (GameLogic.Instance.dice[i] == DieFace.Zero)
            {
                dice[i].transform.localEulerAngles = new Vector3(0.0f, 0.0f, 180.0f);
            }
            else if (GameLogic.Instance.dice[i] == DieFace.Two)
            {
                dice[i].transform.localEulerAngles = new Vector3(0.0f, 0.0f, 90.0f);
            }
            else if (GameLogic.Instance.dice[i] == DieFace.Three)
            {
                dice[i].transform.localEulerAngles = new Vector3(0.0f, 0.0f, 270.0f);
            }
            else
            {
                dice[i].transform.localEulerAngles = new Vector3(0.0f, 0.0f, 0.0f);
            }
        }
    }
}
