using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// The two language buttons of the main menu: they step through the locales Unity Localization offers.
///
/// The list of locales cannot be read on frame zero. Localization keeps its locales and its tables in
/// Addressables, and on WebGL an Addressables read can only be asynchronous — asking for the list before
/// start-up has finished makes the settings block on a synchronous read, which throws there and leaves the
/// menu half-built behind an unhandled exception. So the switcher waits for
/// <c>LocalizationSettings.InitializationOperation</c> first, and the buttons do nothing until it has.
/// </summary>
public class LocaleSwitcher : MonoBehaviour
{
    // The locale list as the localization system reported it once it was up. Read once (the query is the
    // part that must not happen before initialization) and then only indexed, never re-queried on a click.
    private IList<Locale> locales;

    private int currentIndex;

    private IEnumerator Start()
    {
        // Yielding rather than awaiting: an Addressables operation is an IEnumerator, so this is the way
        // the localization system is started outside of async code — see LocalizedDropdown, which waits on
        // the same operation.
        yield return LocalizationSettings.InitializationOperation;

        locales = LocalizationSettings.AvailableLocales != null ? LocalizationSettings.AvailableLocales.Locales : null;
        if (locales == null || locales.Count == 0)
        {
            // Worth reporting, because the language buttons then never respond — but not an error: a build
            // with a single locale, or one whose tables failed to load, still has to open its menu.
            Debug.LogWarning("LocaleSwitcher: no locales are available to switch between; the language buttons are inactive.", this);
            yield break;
        }

        currentIndex = locales.IndexOf(LocalizationSettings.SelectedLocale);
        if (currentIndex < 0) currentIndex = 0;
    }

    /// <summary>Selects the next locale in the list.</summary>
    public void Next()
    {
        Step(1);
    }

    /// <summary>Selects the previous locale in the list.</summary>
    public void Previous()
    {
        Step(-1);
    }

    /// <summary>
    /// Moves <paramref name="delta"/> places through the locale list and selects what it lands on. A click
    /// that arrives before the list has been read (or in a build without locales) is ignored: the buttons
    /// are live from frame zero, the localization system is not.
    /// </summary>
    private void Step(int delta)
    {
        if (locales == null || locales.Count == 0) return;

        currentIndex = ((currentIndex + delta) % locales.Count + locales.Count) % locales.Count;
        LocalizationSettings.SelectedLocale = locales[currentIndex];
    }
}
