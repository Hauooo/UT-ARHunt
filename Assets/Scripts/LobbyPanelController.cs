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
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerListItemPrefab;

    private bool isTheHost = false;

    private void OnEnable()
    {
        // Subscribe to events
        GameManager.Instance.OnLobbyReady += HandleLobbyReady;
        GameManager.Instance.OnPlayerListUpdated += HandlePlayerListUpdated;

        // Avoid stacking listeners
        startGameButton.onClick.RemoveAllListeners();
        leaveButton.onClick.RemoveAllListeners();

        startGameButton.onClick.AddListener(() => GameManager.Instance.StartGame());
        leaveButton.onClick.AddListener(() => GameManager.Instance.LeaveRoom());

        // IMPORTANT: Refresh immediately in case OnLobbyReady already fired
        var gm = GameManager.Instance;
        if (gm != null && !string.IsNullOrEmpty(gm.CurrentRoomId))
        {
            HandleLobbyReady(gm.CurrentRoomId, gm.IsHost);
        }
        else
        {
            roomCodeText.text = "Room Code: ...";
            startGameButton.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLobbyReady -= HandleLobbyReady;
            GameManager.Instance.OnPlayerListUpdated -= HandlePlayerListUpdated;
        }
    }

    private void HandleLobbyReady(string roomId, bool isHost)
    {
        roomCodeText.text = $"Room Code: {roomId}";
        isTheHost = isHost;
        startGameButton.gameObject.SetActive(isTheHost);
    }

    private void HandlePlayerListUpdated(Dictionary<string, PlayerData> players)
    {
        foreach (Transform child in playerListContent)
        {
            Destroy(child.gameObject);
        }

        foreach (var player in players.Values)
        {
            GameObject newPlayerItem = Instantiate(playerListItemPrefab, playerListContent);
            newPlayerItem.GetComponentInChildren<TMP_Text>().text = player.displayName;
        }
    }
}