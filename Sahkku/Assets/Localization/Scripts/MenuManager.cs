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

    /// <summary>The border the options table's label and control columns share, from the panel's middle.</summary>
    const float TableEdgeX = -60.0f;

    // The options table is exactly as tall as its four rows, so its start options sit above it instead.
    const float ThrowForStartRowY = -80.0f;
    const float MenStartRowY = -165.0f;

    private void Start()
    {
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
        GameSettings.p2AgentType = GameSettings.AgentType.HeuristicBot;
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

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        gameOptionsPanel.SetActive(false);
    }

    public void ShowGameOptions()
    {
        mainMenuPanel.SetActive(false);
        gameOptionsPanel.SetActive(true);
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
    /// Adds the two starting-player rows (whether to throw for the start, and who starts when that throw is
    /// off) to the options screen by copying the existing "even odds" row. Growing the rows at runtime keeps
    /// every build from having to hand-wire two more scene objects.
    ///
    /// They are placed in the free band above the options table rather than appended to the two table
    /// columns: those columns are exactly as tall as their four authored rows, so a fifth row would be laid
    /// out past the bottom of the table and onto the play/back buttons below it.
    /// </summary>
    void AddStartOptions()
    {
        if (oddToggle == null || gameOptionsPanel == null)
        {
            Debug.LogWarning("MenuManager has no options row to copy; the starting-player options are unavailable.", this);
            return;
        }

        AddOptionRow("ThrowForStart", "Throw_For_Start", ThrowForStartRowY, GameSettings.throwForStartingPlayer, ToggleThrowForStart);
        menStartToggle = AddOptionRow("MenStart", "Men_Start", MenStartRowY, GameSettings.startingPlayer == GameSettings.Player.Two, ToggleMenStart);

        // A thrown start picks the starting player itself, so the manual choice is not on offer next to it.
        menStartToggle.interactable = !GameSettings.throwForStartingPlayer;
    }

    /// <summary>
    /// Copies a row of the options table: the "even odds" toggle against the setting column's edge, whose
    /// label is against the text column's edge. The table is 692 wide with a 200-wide text column and a
    /// 320-wide setting column, both centred in it, so the edge the two columns share is 60 left of the
    /// panel's middle. Returns the new toggle.
    /// </summary>
    Toggle AddOptionRow(string name, string localizationKey, float rowY, bool initialValue, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        Toggle toggle = Instantiate(oddToggle, gameOptionsPanel.transform);
        toggle.name = name + "_Toggle";
        PlaceRowObject(toggle.transform, new Vector2(0.0f, 0.5f), TableEdgeX, rowY);
        toggle.onValueChanged = new Toggle.ToggleEvent(); // drop the copied row's scene wiring ...
        toggle.isOn = initialValue;                       // ... so setting the start state stays silent
        toggle.onValueChanged.AddListener(onChanged);

        CopyLabel(oddToggle, gameOptionsPanel.transform, localizationKey, name + "_Text (TMP)", rowY);
        return toggle;
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
    /// </summary>
    static void CopyLabel(Toggle source, Transform panel, string localizationKey, string labelName, float rowY)
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

        UnityEngine.Localization.Components.LocalizeStringEvent localize =
            label.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
        if (localize != null)
        {
            localize.enabled = false;
        }

        TMPro.TextMeshProUGUI text = label.GetComponent<TMPro.TextMeshProUGUI>();
        if (text != null)
        {
            text.text = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase
                .GetLocalizedString("UI_Text", localizationKey);
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
