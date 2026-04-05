using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles UI interactions on the Scoreboard scene + renders ranking list in a ScrollRect.
/// </summary>
public class ScoreboardUIController : MonoBehaviour
{
    [Header("UI Buttons")]
    [SerializeField] private Button returnToMenuButton;
    [SerializeField] private Button shareScoreButton;

    [Header("Top Summary (optional)")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [SerializeField] private TextMeshProUGUI timeTakenText;

    [Header("Scrollable Results List")]
    [SerializeField] private Transform resultsContent;   // ScrollView/Viewport/Content
    [SerializeField] private GameObject resultRowPrefab; // Prefab with 3 TMP texts: Name, Score, Time

    [Header("Feedback")]
    [SerializeField] private TextMeshProUGUI feedbackText;

    private DatabaseReference dbRef;

    private class ResultRowData
    {
        public string uid;
        public string playerName;
        public int score;
        public long timeTakenMs;
    }

    private void Start()
    {
        SetupButtons();

        dbRef = FirebaseDatabase.GetInstance(
            "https://ut-ar-treasure-hunt-default-rtdb.asia-southeast1.firebasedatabase.app/")
            .RootReference;

        LoadMySummary();
        LoadScrollableResults();
    }

    private void SetupButtons()
    {
        if (returnToMenuButton != null)
        {
            returnToMenuButton.onClick.RemoveAllListeners();
            returnToMenuButton.onClick.AddListener(OnReturnToMenuClicked);
        }

        if (shareScoreButton != null)
        {
            shareScoreButton.onClick.RemoveAllListeners();
            shareScoreButton.onClick.AddListener(OnShareScoreClicked);
        }
    }

    private void LoadMySummary()
    {
        if (ScoreManager.Instance == null) return;

        if (playerNameText != null) playerNameText.text = ScoreManager.Instance.GetPlayerName();
        if (finalScoreText != null) finalScoreText.text = ScoreManager.Instance.GetScore().ToString();
        if (timeTakenText != null) timeTakenText.text = ScoreManager.Instance.GetFormattedTime();
    }

    private void LoadScrollableResults()
    {
        string roomId = GameManager.Instance?.CurrentRoomId;
        if (string.IsNullOrEmpty(roomId) || dbRef == null || resultsContent == null || resultRowPrefab == null)
            return;

        bool isSinglePlayer = roomId.StartsWith("-");
        string root = isSinglePlayer ? "levels" : "rooms";

        var playersRef = dbRef.Child(root).Child(roomId).Child("players");
        var scoresRef = dbRef.Child(root).Child(roomId).Child("scores");

        playersRef.GetValueAsync().ContinueWithOnMainThread(playersTask =>
        {
            if (playersTask.IsFaulted || playersTask.IsCanceled || !playersTask.Result.Exists)
            {
                Debug.LogWarning("[ScoreboardUI] Failed to load players.");
                return;
            }

            List<ResultRowData> rows = new List<ResultRowData>();

            foreach (var p in playersTask.Result.Children)
            {
                string uid = p.Key;

                string name = p.Child("displayName").Exists
                    ? p.Child("displayName").Value?.ToString()
                    : null;

                if (string.IsNullOrEmpty(name))
                    name = $"Player_{uid.Substring(0, Mathf.Min(5, uid.Length))}";

                long timeMs = 0;
                if (p.Child("timeTakenMs").Exists)
                {
                    long.TryParse(p.Child("timeTakenMs").Value?.ToString(), out timeMs);
                }
                else if (p.Child("elapsedTime").Exists)
                {
                    long legacySeconds = 0;
                    long.TryParse(p.Child("elapsedTime").Value?.ToString(), out legacySeconds);
                    timeMs = legacySeconds * 1000L;
                }

                rows.Add(new ResultRowData
                {
                    uid = uid,
                    playerName = name,
                    score = 0,
                    timeTakenMs = timeMs
                });
            }

            scoresRef.GetValueAsync().ContinueWithOnMainThread(scoresTask =>
            {
                if (!scoresTask.IsFaulted && !scoresTask.IsCanceled && scoresTask.Result.Exists)
                {
                    foreach (var s in scoresTask.Result.Children)
                    {
                        string uid = s.Key;
                        long val = 0;
                        long.TryParse(s.Value?.ToString(), out val);

                        var row = rows.FirstOrDefault(r => r.uid == uid);
                        if (row != null) row.score = (int)val;
                    }
                }

                // Sort: higher score first; tie -> faster time first
                rows = rows
                    .OrderByDescending(r => r.score)
                    .ThenBy(r => r.timeTakenMs <= 0 ? long.MaxValue : r.timeTakenMs)
                    .ToList();

                BuildRows(rows);
            });
        });
    }

    private void BuildRows(List<ResultRowData> rows)
    {
        Debug.Log($"[ScoreboardUI] Building {rows.Count} rows");
        for (int i = resultsContent.childCount - 1; i >= 0; i--)
            Destroy(resultsContent.GetChild(i).gameObject);

        foreach (var row in rows)
        {
            var go = Instantiate(resultRowPrefab, resultsContent);

            // Expected child names on prefab:
            // "NameText", "ScoreText", "TimeText"
            var nameText = go.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            var scoreText = go.transform.Find("ScoreText")?.GetComponent<TextMeshProUGUI>();
            var timeText = go.transform.Find("TimeText")?.GetComponent<TextMeshProUGUI>();

            if (nameText != null) nameText.text = row.playerName;
            if (scoreText != null) scoreText.text = row.score.ToString();
            if (timeText != null) timeText.text = FormatTime(row.timeTakenMs);
        }
    }

    private string FormatTime(long timeMs)
    {
        int totalSec = Mathf.Max(0, (int)(timeMs / 1000L));
        int min = totalSec / 60;
        int sec = totalSec % 60;
        return $"{min:D2}:{sec:D2}";
    }

    private void OnReturnToMenuClicked()
    {
        Debug.Log("[ScoreboardUI] Return to Menu button clicked");

        if (GameManager.Instance != null)
            GameManager.Instance.LeaveScoreboard();
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("MenuScene");
    }

    private void OnShareScoreClicked()
    {
        Debug.Log("[ScoreboardUI] Share Score button clicked");

        if (ScoreManager.Instance != null)
        {
            string playerName = ScoreManager.Instance.GetPlayerName();
            int score = ScoreManager.Instance.GetScore();
            string time = ScoreManager.Instance.GetFormattedTime();

            string message = $"I scored {score} points in {time} in UT ARHunt as {playerName} 🎮";
            GUIUtility.systemCopyBuffer = message;
            ShowFeedback("Score copied to clipboard! 📋");
            Debug.Log($"[ScoreboardUI] Message copied: {message}");
        }
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            Invoke(nameof(ClearFeedback), 3f);
        }
    }

    private void ClearFeedback()
    {
        if (feedbackText != null)
            feedbackText.text = "";
    }
}