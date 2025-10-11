using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomItem : MonoBehaviour
{
    [SerializeField] private TMP_Text roomNameText;
    private string roomId;
    private RoomManager manager;

    public void Setup(string id, string name, RoomManager roomManager)
    {
        roomId = id;
        manager = roomManager;
        roomNameText.text = name;

        GetComponent<Button>().onClick.AddListener(() => manager.JoinRoom(roomId));
    }
}
