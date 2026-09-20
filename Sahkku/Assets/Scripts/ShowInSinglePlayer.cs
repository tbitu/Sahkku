using UnityEngine;

public class ShowInSinglePlayer : MonoBehaviour
{
    void Start()
    {
        // "Single player" now means exactly one human is playing (see GameSettings.IsSinglePlayer).
        gameObject.SetActive(GameSettings.IsSinglePlayer);
    }
}
