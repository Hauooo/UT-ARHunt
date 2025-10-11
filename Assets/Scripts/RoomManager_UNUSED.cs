using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;

public class RoomManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text statusText;
    public Button createRoomButton;
    public Transform roomListContent;
    public GameObject roomItemPrefab;

    private DatabaseReference dbRef;
    private Dictionary<string, RoomItem> activeRooms = new Dictionary<string, RoomItem>();

    void Start()
    {
        dbRef = FirebaseDatabase.DefaultInstance.RootReference;

        createRoomButton.onClick.AddListener(CreateRoom);
        ListenForRooms();
    }

    private void ListenForRooms()
    {
        DatabaseReference roomsRef = dbRef.Child("rooms");

        roomsRef.ChildAdded += (sender, args) =>
        {
            if (args.DatabaseError != null) return;
            string roomId = args.Snapshot.Key;
            string roomName = args.Snapshot.Child("roomName").Value?.ToString() ?? "Unnamed Room";

            if (activeRooms.ContainsKey(roomId)) return;

            GameObject roomObj = Instantiate(roomItemPrefab, roomListContent);
            RoomItem roomItem = roomObj.GetComponent<RoomItem>();
            roomItem.Setup(roomId, roomName, this);
            activeRooms[roomId] = roomItem;
        };

        roomsRef.ChildRemoved += (sender, args) =>
        {
            string roomId = args.Snapshot.Key;
            if (activeRooms.ContainsKey(roomId))
            {
                Destroy(activeRooms[roomId].gameObject);
                activeRooms.Remove(roomId);
            }
        };
    }

    private void CreateRoom()
    {
        string roomId = dbRef.Child("rooms").Push().Key;
        string userId = AuthManager.Instance.UserId;
        string roomName = "Room by " + userId.Substring(0, 5);

        var roomData = new Dictionary<string, object>
        {
            { "hostId", userId },
            { "roomName", roomName },
            { "createdAt", ServerValue.Timestamp }
        };

        dbRef.Child("rooms").Child(roomId).SetValueAsync(roomData).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                statusText.text = $"Created {roomName}";
                JoinRoom(roomId);
            }
            else
            {
                statusText.text = "Room creation failed.";
            }
        });
    }

    public void JoinRoom(string roomId)
    {
        string userId = AuthManager.Instance.UserId;
        string playerName = "Player_" + Random.Range(100, 999);

        var playerData = new Dictionary<string, object>
        {
            { "name", playerName },
            { "score", 0 }
        };

        dbRef.Child("rooms").Child(roomId).Child("players").Child(userId)
            .SetValueAsync(playerData)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted)
                {
                    statusText.text = $"Joined {roomId}";
                    // Transition to treasure scene
                    UnityEngine.SceneManagement.SceneManager.LoadScene("TreasureScene");
                    PlayerPrefs.SetString("RoomID", roomId); // Store for next scene
                }
                else
                {
                    statusText.text = "Failed to join room.";
                }
            });
    }
}
