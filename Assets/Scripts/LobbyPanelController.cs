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
        if (GameManager.Instance == null)
        {
            Debug.LogError("[LobbyPanelController] GameManager.Instance is null.");
            return;
        }

        if (roomCodeText == null) Debug.LogError("[LobbyPanelController] roomCodeText not assigned.");
        if (startGameButton == null) Debug.LogError("[LobbyPanelController] startGameButton not assigned.");
        if (leaveButton == null) Debug.LogError("[LobbyPanelController] leaveButton not assigned.");
        if (playerListContent == null) Debug.LogError("[LobbyPanelController] playerListContent not assigned.");
        if (playerListItemPrefab == null) Debug.LogError("[LobbyPanelController] playerListItemPrefab not assigned.");

        GameManager.Instance.OnLobbyReady += HandleLobbyReady;
        GameManager.Instance.OnPlayerListUpdated += HandlePlayerListUpdated;

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(() => GameManager.Instance.StartGame());
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.RemoveAllListeners();
            leaveButton.onClick.AddListener(() => GameManager.Instance.LeaveRoom());
        }

        // If UI refs exist, set a visible baseline so it isn't "empty"
        if (roomCodeText != null)
            roomCodeText.text = "Room Code: ...";

        if (startGameButton != null)
            startGameButton.gameObject.SetActive(false);

        // Pull current state in case OnLobbyReady already fired
        var gm = GameManager.Instance;
        if (gm != null && !string.IsNullOrEmpty(gm.CurrentRoomId))
            HandleLobbyReady(gm.CurrentRoomId, gm.IsHost);
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