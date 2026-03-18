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
            var gamem = GameManager.Instance;
            if (gamem != null && !string.IsNullOrEmpty(gamem.CurrentRoomId))
            {
                HandleLobbyReady(gamem.CurrentRoomId, gamem.IsHost);
                HandlePlayerListUpdated(gamem.CurrentPlayers); // ✅ immediate populate
                Debug.Log($"[LobbyPanelController] Hydrating lobby. roomId={gamem.CurrentRoomId}, cachedPlayers={gamem.CurrentPlayers?.Count ?? 0}");
            }
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
        Debug.Log("[LobbyPanelController] HandlePlayerListUpdated called.");

        if (playerListContent == null)
        {
            Debug.LogError("[LobbyPanelController] playerListContent is NULL.");
            return;
        }

        if (playerListItemPrefab == null)
        {
            Debug.LogError("[LobbyPanelController] playerListItemPrefab is NULL.");
            return;
        }

        int incomingCount = players?.Count ?? 0;
        Debug.Log($"[LobbyPanelController] Incoming players count: {incomingCount}");

        foreach (Transform child in playerListContent)
            Destroy(child.gameObject);

        if (players == null || players.Count == 0)
        {
            Debug.LogWarning("[LobbyPanelController] No players to display (list is empty).");
            Debug.Log($"[LobbyPanelController] UI children after clear: {playerListContent.childCount}");
            return;
        }

        foreach (var kvp in players)
        {
            string uid = kvp.Key;
            PlayerData player = kvp.Value;

            GameObject newPlayerItem = Instantiate(playerListItemPrefab, playerListContent);
            var label = newPlayerItem.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                label.text = player.displayName;
                Debug.Log($"[LobbyPanelController] Displayed player: uid={uid}, name={player.displayName}");
            }
            else
            {
                Debug.LogError($"[LobbyPanelController] TMP_Text missing on playerListItemPrefab for uid={uid}");
            }
        }

        Debug.Log($"[LobbyPanelController] Player list render complete. Spawned UI items: {playerListContent.childCount}");
    }
}