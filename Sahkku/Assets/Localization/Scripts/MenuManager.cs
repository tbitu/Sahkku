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

    private void Start()
    {
        AddThrowForStartOption();
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

    /// <summary>Chooses between "throw for the starting player" and the starting player picked here.</summary>
    public void ToggleThrowForStart(bool value)
    {
        GameSettings.throwForStartingPlayer = value;
        Debug.Log("Throw for starting player: " + GameSettings.throwForStartingPlayer);
    }

    /// <summary>Picks who starts when the throw is switched off.</summary>
    public void SetStartingPlayer(int value)
    {
        GameSettings.startingPlayer = value == 1 ? GameSettings.Player.Two : GameSettings.Player.One;
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
    /// Adds the "throw for start" row to the options screen by copying the existing "even odds" row.
    /// Growing the option list at runtime keeps the setting next to the rules it interacts with
    /// without every build having to keep a hand-wired scene object in sync.
    /// </summary>
    void AddThrowForStartOption()
    {
        if (oddToggle == null)
        {
            Debug.LogWarning("MenuManager has no even-odds toggle to copy; the throw-for-start option is unavailable.", this);
            return;
        }

        Toggle toggle = Instantiate(oddToggle, oddToggle.transform.parent);
        toggle.name = "ThrowForStart_Toggle";
        toggle.isOn = GameSettings.throwForStartingPlayer;
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.onValueChanged.AddListener(ToggleThrowForStart);

        CopyLabel(oddToggle, toggle, "Throw_For_Start");
    }

    /// <summary>
    /// Mirrors the label of a copied control. The options screen keeps its labels in a separate column
    /// from its controls, so the label has to be copied into that column.
    /// </summary>
    static void CopyLabel(Toggle source, Toggle copy, string localizationKey)
    {
        Transform controlColumn = source.transform.parent;
        Transform labelColumn = controlColumn == null ? null : controlColumn.parent;
        if (labelColumn == null) return;

        Transform template = labelColumn.Find("Odds_Text (TMP)");
        if (template == null)
        {
            Debug.LogWarning("The even-odds label could not be found; the new option has no label.", source);
            return;
        }

        GameObject label = Instantiate(template.gameObject, labelColumn);
        label.name = "ThrowForStart_Text (TMP)";

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

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
