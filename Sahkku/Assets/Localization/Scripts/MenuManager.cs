using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    [SerializeField]
    private GameObject mainMenuPanel;
    [SerializeField]
    private GameObject gameOptionsPanel;

    [SerializeField]
    private Toggle soundToggle;
    [SerializeField]
    private Toggle musicToggle;
    [SerializeField]
    private Toggle oddToggle;

    // Grown from the "even odds" row at start-up: the scene has no widget for it. A thrown start decides
    // the starting player, so this toggle is greyed out while the throw is on.
    private Toggle menStartToggle;

    // Grown the same way: switches player two between the deterministic bot and the LLM NPC.
    private Toggle menLlmToggle;

    // Grown the same way, but as text fields: the endpoint and model the LLM NPC is asked to use. There is
    // no input field in the scene to copy, so these are built from the even-odds row's graphics (see
    // AddInputRow). Edited values go straight into GameSettings and out to the shared llm-config.json.
    private TMP_InputField llmEndpointInput;
    private TMP_InputField llmModelInput;

    /// <summary>The string table every grown label is localized from.</summary>
    const string LocalizationTable = "UI_Text";

    /// <summary>The border the options table's label and control columns share, from the panel's middle.</summary>
    const float TableEdgeX = -60.0f;

    // The options table is exactly as tall as its four rows, so its grown rows sit above it instead.
    const float ThrowForStartRowY = -80.0f;
    const float MenStartRowY = -165.0f;
    const float LlmOpponentRowY = -250.0f;

    // The two LLM text fields sit below the play/back buttons, the only room left: the band above the table
    // holds two 80-tall toggle graphics on an 85 pitch and the table's first row starts 209 below the top.
    const float LlmEndpointRowY = -950.0f;
    const float LlmModelRowY = -1015.0f;

    /// <summary>Size of a grown text row. Shorter than the toggle graphic, because it has no labels inside it.</summary>
    const float InputRowHeight = 56.0f;
    const float InputRowWidth = 520.0f;
    const float InputPaddingX = 14.0f;

    /// <summary>
    /// How far the authored options table and its play/back buttons are pushed down to make room for the
    /// third grown row (see <see cref="MakeRoomForLlmRow"/>).
    /// </summary>
    const float TableShiftY = -100.0f;

    private void Start()
    {
        // The menu is the first thing a player sees, so it is where the shared LLM configuration is read:
        // the two text rows below then show the endpoint and model the NPC will actually be given.
        GameSettings.LoadLlmConfig();
        AddStartOptions();
        ShowMainMenu();
    }

    public void PlayVersus()
    {
        // Two humans sharing one device: the match controller simply hands the turn back and forth.
        GameSettings.p1AgentType = GameSettings.AgentType.Human;
        GameSettings.p2AgentType = GameSettings.AgentType.Human;
        ShowGameOptions();
    }

    public void PlaySolo()
    {
        GameSettings.p1AgentType = GameSettings.AgentType.Human;
        // Whichever opponent the options screen last picked: the deterministic bot, or the LLM NPC.
        bool llmOpponent = menLlmToggle != null && menLlmToggle.isOn;
        GameSettings.p2AgentType = llmOpponent ? GameSettings.AgentType.LlmBot : GameSettings.AgentType.HeuristicBot;
        ShowGameOptions ();
    }

    public void ToggleOdds()
    {
        GameSettings.evenOdds = oddToggle.isOn;
        Debug.Log("Even odds: " + GameSettings.evenOdds);
    }

    /// <summary>Chooses between "throw for the starting player" and the starting player picked below.</summary>
    public void ToggleThrowForStart(bool value)
    {
        GameSettings.throwForStartingPlayer = value;
        // A thrown start decides the starting player, so the manual pick only means something without it.
        if (menStartToggle != null) menStartToggle.interactable = !value;
        Debug.Log("Throw for starting player: " + GameSettings.throwForStartingPlayer);
    }

    /// <summary>True when the men (player two) start, false when the women (player one) start.</summary>
    public void ToggleMenStart(bool value)
    {
        SetStartingPlayer(value ? 1 : 0);
    }

    /// <summary>Picks who starts when the throw is switched off.</summary>
    public void SetStartingPlayer(int value)
    {
        GameSettings.startingPlayer = value == 1 ? GameSettings.Player.Two : GameSettings.Player.One;
        Debug.Log("Starting player: " + GameSettings.startingPlayer);
    }

    /// <summary>
    /// Switches player two's opponent. On: the LLM NPC answers through
    /// <see cref="GameSettings.GetLlmConfig"/>'s endpoint. Off: the deterministic bot plays that side —
    /// but only when that side was the LLM, so a human side (hotseat) is left alone.
    /// </summary>
    public void ToggleLlmOpponent(bool value)
    {
        if (value)
        {
            GameSettings.p2AgentType = GameSettings.AgentType.LlmBot;
        }
        else if (GameSettings.p2AgentType == GameSettings.AgentType.LlmBot)
        {
            GameSettings.p2AgentType = GameSettings.AgentType.HeuristicBot;
        }

        Debug.Log("LLM opponent: " + value + " (player two is " + GameSettings.p2AgentType + ")");
    }

    /// <summary>
    /// Commits the endpoint typed into the options screen. Reached when the field loses focus or the edit is
    /// submitted, not on every keystroke: the value is stored on <see cref="GameSettings"/> and written to
    /// the shared llm-config.json, which is what lets the headless benchmark pick up the same endpoint.
    /// </summary>
    public void ApplyLlmEndpoint(string value)
    {
        GameSettings.llmEndpointUrl = value;
        bool saved = GameSettings.SaveLlmConfig();
        Debug.Log("LLM endpoint: " + GameSettings.llmEndpointUrl + (saved ? string.Empty : " (not saved)"));
    }

    /// <summary>Commits the model name typed into the options screen; see <see cref="ApplyLlmEndpoint"/>.</summary>
    public void ApplyLlmModel(string value)
    {
        GameSettings.llmModelName = value;
        bool saved = GameSettings.SaveLlmConfig();
        Debug.Log("LLM model: " + GameSettings.llmModelName + (saved ? string.Empty : " (not saved)"));
    }

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        gameOptionsPanel.SetActive(false);
    }

    public void ShowGameOptions()
    {
        mainMenuPanel.SetActive(false);
        gameOptionsPanel.SetActive(true);
        // The panel is shared by solo and hotseat, so the row is brought in line with the side that was
        // actually chosen (Play Solo / Play Versus) rather than keeping a stale toggle state.
        SyncLlmOpponentRow();
        SyncLlmFields();
    }

    /// <summary>Mirrors player two's agent into the grown row. The handler it triggers is idempotent.</summary>
    void SyncLlmOpponentRow()
    {
        if (menLlmToggle == null) return;
        bool llmOpponent = GameSettings.p2AgentType == GameSettings.AgentType.LlmBot;
        if (menLlmToggle.isOn != llmOpponent) menLlmToggle.isOn = llmOpponent;
    }

    /// <summary>
    /// Brings the two text rows in line with the settings whenever the panel is shown. The silent setter is
    /// used so merely opening the screen never looks like an edit to the field.
    /// </summary>
    void SyncLlmFields()
    {
        if (llmEndpointInput != null) llmEndpointInput.SetTextWithoutNotify(GameSettings.llmEndpointUrl);
        if (llmModelInput != null) llmModelInput.SetTextWithoutNotify(GameSettings.llmModelName);
    }

    public void ToggleAudio(bool value)
    {
        GameSettings.muteSounds = !soundToggle.isOn;
        Debug.Log("Mute sounds: " + GameSettings.muteSounds);
    }

    public void ToggleMusic(bool value)
    {
        GameSettings.muteMusic = !musicToggle.isOn;
        Debug.Log("Mute music: " + GameSettings.muteMusic);
    }

    public void StartGame()
    {
        SceneManager.LoadScene("Game");
    }

    public void ToggleFullScreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    public void OpenRules()
    {
        string rulesPath = Application.dataPath + "/../SahkkuRules.pdf";
        Debug.Log("Opening " + rulesPath);
        Application.OpenURL(rulesPath);
    }

    /// <summary>
    /// Adds the grown rows to the options screen: whether to throw for the start, who starts when that
    /// throw is off, who plays player two, and the endpoint and model the LLM NPC is given. The toggles are
    /// copied from the existing "even odds" row and the text fields are assembled from its graphics, which
    /// keeps every build from having to hand-wire five more scene objects.
    ///
    /// The toggles are placed in the free band above the options table rather than appended to the two table
    /// columns: those columns are exactly as tall as their four authored rows, so a fifth row would be laid
    /// out past the bottom of the table and onto the play/back buttons below it. The text fields go in the
    /// free space below those buttons.
    /// </summary>
    void AddStartOptions()
    {
        if (oddToggle == null || gameOptionsPanel == null)
        {
            Debug.LogWarning("MenuManager has no options row to copy; the grown options are unavailable.", this);
            return;
        }

        // The band above the table is 218 tall (the panel is 1080 and the table's top edge sits 209 below
        // it): two rows of the 80-tall toggle graphic fit on an 85 pitch, three do not, so the third is
        // given room of its own first.
        MakeRoomForLlmRow();

        AddOptionRow("ThrowForStart", "Throw_For_Start", ThrowForStartRowY, "Throw for start", GameSettings.throwForStartingPlayer, ToggleThrowForStart);
        menStartToggle = AddOptionRow("MenStart", "Men_Start", MenStartRowY, "Men start", GameSettings.startingPlayer == GameSettings.Player.Two, ToggleMenStart);
        menLlmToggle = AddOptionRow("LlmOpponent", "Llm_Opponent", LlmOpponentRowY, "LLM Opponent", GameSettings.p2AgentType == GameSettings.AgentType.LlmBot, ToggleLlmOpponent);

        // A thrown start picks the starting player itself, so the manual choice is not on offer next to it.
        menStartToggle.interactable = !GameSettings.throwForStartingPlayer;

        llmEndpointInput = AddInputRow("LlmEndpoint", "Llm_Endpoint", LlmEndpointRowY, "Endpoint", GameSettings.llmEndpointUrl, ApplyLlmEndpoint);
        llmModelInput = AddInputRow("LlmModel", "Llm_Model", LlmModelRowY, "Model", GameSettings.llmModelName, ApplyLlmModel);
    }

    /// <summary>
    /// The band above the table has room for two of the three grown rows, because the table's first row
    /// starts 209 below the panel top and the third row would land on it. The table ("Settings", 350 tall)
    /// and the play/back buttons ("Buttons") are therefore moved down by one row pitch plus a margin, which
    /// keeps their order and spacing and leaves both well inside the 1080-tall panel.
    /// </summary>
    void MakeRoomForLlmRow()
    {
        ShiftPanelChild("Settings");
        ShiftPanelChild("Buttons");
    }

    /// <summary>Moves a direct child of the options panel down by <see cref="TableShiftY"/>.</summary>
    void ShiftPanelChild(string childName)
    {
        Transform child = gameOptionsPanel.transform.Find(childName);
        RectTransform rect = child == null ? null : child as RectTransform;
        if (rect == null)
        {
            Debug.LogWarning("MenuManager could not find the options panel's '" + childName + "' child; the LLM opponent row may overlap it.", this);
            return;
        }
        rect.anchoredPosition += new Vector2(0.0f, TableShiftY);
    }

    /// <summary>
    /// Copies a row of the options table: the "even odds" toggle against the setting column's edge, whose
    /// label is against the text column's edge. The table is 692 wide with a 200-wide text column and a
    /// 320-wide setting column, both centred in it, so the edge the two columns share is 60 left of the
    /// panel's middle. <paramref name="fallbackText"/> stands in for the label until its entry is loaded.
    /// Returns the new toggle.
    /// </summary>
    Toggle AddOptionRow(string name, string localizationKey, float rowY, string fallbackText, bool initialValue, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        Toggle toggle = Instantiate(oddToggle, gameOptionsPanel.transform);
        toggle.name = name + "_Toggle";
        PlaceRowObject(toggle.transform, new Vector2(0.0f, 0.5f), TableEdgeX, rowY);
        toggle.onValueChanged = new Toggle.ToggleEvent(); // drop the copied row's scene wiring ...
        toggle.isOn = initialValue;                       // ... so setting the start state stays silent
        toggle.onValueChanged.AddListener(onChanged);

        CopyLabel(oddToggle, gameOptionsPanel.transform, localizationKey, name + "_Text (TMP)", rowY, fallbackText);
        return toggle;
    }

    /// <summary>
    /// Adds an editable text row for a string setting: the localized label in the text column and a one-line
    /// field in the setting column. The scene has no input field to copy, so the row is assembled from the
    /// same two graphics the toggle rows reuse — the even-odds row's background image and its label's font.
    /// <paramref name="onCommitted"/> runs when the field loses focus or the edit is submitted, never on
    /// every keystroke, so a half-typed URL is not written to the shared file.
    /// </summary>
    TMP_InputField AddInputRow(string name, string localizationKey, float rowY, string fallbackText, string initialValue, UnityEngine.Events.UnityAction<string> onCommitted)
    {
        TMP_InputField input = CreateInputField(name, rowY);
        if (input != null)
        {
            input.SetTextWithoutNotify(initialValue ?? string.Empty);
            input.onEndEdit.AddListener(onCommitted);
        }

        CopyLabel(oddToggle, gameOptionsPanel.transform, localizationKey, name + "_Label (TMP)", rowY, fallbackText);
        return input;
    }

    /// <summary>
    /// Builds the field: an image row with a masked text area holding a copy of the options label as its text
    /// component. The row is created inactive so the input field is fully wired before its Awake/OnEnable
    /// runs, which is when TMP sets up the caret and the editing callbacks.
    /// </summary>
    TMP_InputField CreateInputField(string name, float rowY)
    {
        Transform template = FindLabelTemplate(oddToggle);
        if (template == null)
        {
            Debug.LogWarning("The even-odds label could not be found; the '" + name + "' field has no text.", this);
            return null;
        }

        GameObject row = new GameObject(name + "_Input", typeof(RectTransform));
        row.SetActive(false);
        row.transform.SetParent(gameOptionsPanel.transform, false);
        PlaceRowObject(row.transform, new Vector2(0.0f, 0.5f), TableEdgeX, rowY);
        ((RectTransform)row.transform).sizeDelta = new Vector2(InputRowWidth, InputRowHeight);

        Image background = FindRowBackground(oddToggle);
        if (background != null && background.sprite != null)
        {
            Image image = row.AddComponent<Image>();
            image.sprite = background.sprite;
            image.type = background.type;
            image.color = background.color;
        }

        GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(row.transform, false);
        RectTransform areaRect = (RectTransform)area.transform;
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(InputPaddingX, 4.0f);
        areaRect.offsetMax = new Vector2(-InputPaddingX, -4.0f);

        TextMeshProUGUI text = CreateInputText(template, name, areaRect);
        if (text == null)
        {
            // An input field without a text component cannot be typed into; leaving it out beats a field
            // that throws the first time it is focused.
            Debug.LogWarning("The '" + name + "' field has no text component; the field is unavailable.", this);
            Destroy(row);
            return null;
        }

        TMP_InputField input = row.AddComponent<TMP_InputField>();
        input.textViewport = areaRect;
        input.textComponent = text;
        input.lineType = TMP_InputField.LineType.SingleLine;

        row.SetActive(true);
        return input;
    }

    /// <summary>
    /// Clones the options label as the field's text — same font and material, stretched over the whole area,
    /// left-aligned. The clone's localizer is switched off, because the field holds a setting rather than a
    /// translated title and must keep whatever was typed.
    /// </summary>
    static TextMeshProUGUI CreateInputText(Transform template, string name, RectTransform parent)
    {
        GameObject label = Instantiate(template.gameObject, parent);
        label.name = name + "_Text (TMP)";
        RectTransform rect = (RectTransform)label.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        UnityEngine.Localization.Components.LocalizeStringEvent localize =
            label.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
        if (localize != null) localize.enabled = false;

        TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
        if (text == null) return null;

        text.text = string.Empty;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    /// <summary>
    /// The graphic a grown text row borrows its background from: the even-odds row's own graphic, which is
    /// the panel sprite behind the checkmarks.
    /// </summary>
    static Image FindRowBackground(Toggle source)
    {
        if (source == null) return null;
        Image graphic = source.graphic as Image;
        return graphic != null ? graphic : source.GetComponentInChildren<Image>();
    }

    /// <summary>
    /// Puts a copied row object on the options panel: <paramref name="edgeX"/> from the panel's middle,
    /// <paramref name="rowY"/> below its top, with the given pivot against that edge. The panel itself has no
    /// layout group, so these rows are never re-laid out by one.
    /// </summary>
    static void PlaceRowObject(Transform row, Vector2 pivot, float edgeX, float rowY)
    {
        RectTransform rect = row as RectTransform;
        if (rect == null) return;

        rect.anchorMin = new Vector2(0.5f, 1.0f);
        rect.anchorMax = new Vector2(0.5f, 1.0f);
        rect.pivot = pivot;
        rect.anchoredPosition = new Vector2(edgeX, rowY);
    }

    /// <summary>
    /// Mirrors the label of a copied control. The options screen keeps its labels in a separate column from
    /// its controls, so the label has to be copied out of that column; it is right-aligned against the same
    /// edge as the column's own labels.
    ///
    /// The label is localized by its copied <c>LocalizeStringEvent</c> rather than by a lookup here: Unity
    /// Localization reads its tables from Addressables, and on WebGL that can only happen asynchronously, so
    /// asking the database for a string on start-up would throw and leave the whole menu half-built. The
    /// readable <paramref name="fallbackText"/> covers the label until the entry arrives, and the localizer
    /// then keeps it in step with the selected language on its own.
    /// </summary>
    static void CopyLabel(Toggle source, Transform panel, string localizationKey, string labelName, float rowY, string fallbackText)
    {
        Transform template = FindLabelTemplate(source);
        if (template == null)
        {
            Debug.LogWarning("The even-odds label could not be found; the new option has no label.", source);
            return;
        }

        GameObject label = Instantiate(template.gameObject, panel);
        label.name = labelName;
        PlaceRowObject(label.transform, new Vector2(1.0f, 0.5f), TableEdgeX, rowY);

        TMPro.TextMeshProUGUI text = label.GetComponent<TMPro.TextMeshProUGUI>();
        if (text != null)
        {
            text.text = fallbackText;
        }

        UnityEngine.Localization.Components.LocalizeStringEvent localize =
            label.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
        if (localize != null && localize.StringReference != null)
        {
            // Pointing the localizer at this row's own entry is what starts the asynchronous read; the
            // component stays enabled so the label follows a change of language too.
            localize.StringReference.SetReference(LocalizationTable, localizationKey);
            localize.enabled = true;
        }
    }

    /// <summary>
    /// Finds the even-odds label to clone. The options table keeps each control in a column and its label in
    /// a sibling column, so the template is a direct child of one of the control column's siblings.
    /// <see cref="Transform.Find"/> only matches direct children (it never descends), so the table itself
    /// cannot be searched for the label by name - the sibling columns have to be walked instead.
    /// </summary>
    static Transform FindLabelTemplate(Toggle source)
    {
        const string TemplateName = "Odds_Text (TMP)";

        Transform controlColumn = source.transform.parent;
        Transform table = controlColumn == null ? null : controlColumn.parent;
        if (table == null) return null;

        for (int i = 0; i < table.childCount; i++)
        {
            Transform template = table.GetChild(i).Find(TemplateName);
            if (template != null) return template;
        }
        return null;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
