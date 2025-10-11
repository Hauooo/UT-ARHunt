using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class LobbyPanelController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Transform playerListContent; // Parent for player names
    [SerializeField] private GameObject playerListItemPrefab; // A simple prefab with a TMP_Text

    private bool isTheHost = false;

    private void OnEnable()
    {
        // Subscribe to events when the panel becomes active
        GameManager.Instance.OnLobbyReady += HandleLobbyReady;
        GameManager.Instance.OnPlayerListUpdated += HandlePlayerListUpdated;

        // Hook up buttons to the GameManager
        startGameButton.onClick.AddListener(() => GameManager.Instance.StartGame());
        leaveButton.onClick.AddListener(() => GameManager.Instance.LeaveRoom());
    }

    private void OnDisable()
    {
        // IMPORTANT: Unsubscribe when the panel is hidden to prevent errors
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLobbyReady -= HandleLobbyReady;
            GameManager.Instance.OnPlayerListUpdated -= HandlePlayerListUpdated;
        }
    }

    private void HandleLobbyReady(string roomId, bool isHost)
    {
        // This is called once when we first enter the lobby
        roomCodeText.text = $"Room Code: {roomId}";
        isTheHost = isHost;
        startGameButton.gameObject.SetActive(isTheHost); // Only the host can start the game
    }

    private void HandlePlayerListUpdated(Dictionary<string, PlayerData> players)
    {
        // This is called every time a player joins or leaves

        // Clear the old list
        foreach (Transform child in playerListContent)
        {
            Destroy(child.gameObject);
        }

        // Repopulate the list with the latest data
        foreach (var player in players.Values)
        {
            GameObject newPlayerItem = Instantiate(playerListItemPrefab, playerListContent);
            // Assuming your prefab has a TMP_Text component as its root or child
            newPlayerItem.GetComponentInChildren<TMP_Text>().text = player.displayName;
        }
    }
}