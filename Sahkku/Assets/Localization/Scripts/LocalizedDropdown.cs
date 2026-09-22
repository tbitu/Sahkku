using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using static GameSettings;

[Serializable]
public struct PieceModelOption
{
    public PieceModel model;
    public LocalizedString localizedName;
}

public class LocalizedDropdown : MonoBehaviour
{
    public enum Target
    {
        Player1,
        Player2,
        King
    }

    public TMP_Dropdown dropdown;
    public List<PieceModelOption> options;

    [Tooltip("Which static setting this dropdown controls")]
    public Target target;

    // The pass over the string table, so it can be given up when the dropdown goes away or the language
    // changes out from under it.
    private Coroutine refresh;

    private void OnEnable()
    {
        if (dropdown == null)
        {
            Debug.LogWarning("LocalizedDropdown has no dropdown to fill; its options are unavailable.", this);
            return;
        }

        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);

        // The model names stand in until the table has been read. Unity Localization keeps its strings in
        // Addressables, which on WebGL can only be read asynchronously: reading one here would throw, and
        // until the table arrives the dropdown would otherwise still show TextMeshPro's "Option A".
        ApplyOptions(FallbackLabels());

        StartRefresh();
    }

    private void OnDisable()
    {
        StopRefresh();
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
    }

    private void OnLocaleChanged(Locale locale)
    {
        // The labels are the part that is translated, so a new language means a new pass over the table.
        StartRefresh();
    }

    void StartRefresh()
    {
        StopRefresh();
        if (isActiveAndEnabled)
            refresh = StartCoroutine(RefreshOptionsWhenReady());
    }

    void StopRefresh()
    {
        if (refresh == null) return;
        StopCoroutine(refresh);
        refresh = null;
    }

    /// <summary>
    /// Puts the localized labels on the dropdown once they can be read. The localization system's own
    /// start-up is awaited first — its tables are Addressables, and on WebGL every read of one has to be
    /// asynchronous — and then each option's entry in turn. An option whose entry cannot be read keeps the
    /// model name it was given, so the list is never left empty.
    /// </summary>
    IEnumerator RefreshOptionsWhenReady()
    {
        yield return LocalizationSettings.InitializationOperation;

        List<string> labels = FallbackLabels();
        for (int i = 0; i < options.Count; i++)
        {
            LocalizedString localizedName = options[i].localizedName;
            if (localizedName == null || localizedName.IsEmpty)
                continue;

            AsyncOperationHandle<string> entry = localizedName.GetLocalizedStringAsync();
            yield return entry;

            string localized = entry.IsValid() ? entry.Result : null;
            if (!string.IsNullOrEmpty(localized))
                labels[i] = localized;
        }

        ApplyOptions(labels);
    }

    /// <summary>
    /// The model names, which stand in until the table has been read and stay put wherever an entry is
    /// missing from it.
    /// </summary>
    List<string> FallbackLabels()
    {
        List<string> labels = new List<string>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            labels.Add(options[i].model.ToString());
        }
        return labels;
    }

    void ApplyOptions(List<string> labels)
    {
        if (dropdown == null) return;

        dropdown.ClearOptions();
        dropdown.AddOptions(labels);

        // Sync dropdown selection to whatever the current setting is
        int currentIndex = options.FindIndex(o => o.model == GetCurrentModel());
        dropdown.value = currentIndex >= 0 ? currentIndex : 0;

        dropdown.RefreshShownValue();
    }

    private void OnDropdownValueChanged(int index)
    {
        if (index >= 0 && index < options.Count)
        {
            SetCurrentModel(options[index].model);
        }
    }

    private PieceModel GetCurrentModel()
    {
        switch (target)
        {
            case Target.Player1: return p1Model;
            case Target.Player2: return p2Model;
            case Target.King: return kingModel;
            default: throw new ArgumentOutOfRangeException();
        }
    }

    private void SetCurrentModel(PieceModel model)
    {
        switch (target)
        {
            case Target.Player1: p1Model = model; break;
            case Target.Player2: p2Model = model; break;
            case Target.King: kingModel = model; break;
        }
    }
}
