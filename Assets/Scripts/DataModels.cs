using System;
using System.Collections.Generic;

// ── Challenge Types ───────────────────────────────────────────────────────────
public enum ChallengeType
{
    None,           // no challenge — collect instantly
    MCQ,            // multiple choice question
    MemoryMatch,    // built-in minigame: flip tiles to find pairs
    OrderSequence,  // built-in minigame: arrange items in correct order
    ARMCQ,           // AR-based multiple choice (e.g. find the correct object in AR)
}

[Serializable]
public class MCQOption
{
    public string text;       // e.g. "Paris"
    public bool isCorrect;    // only one should be true
}

[Serializable]
public class ChallengeData
{
    public ChallengeType type = ChallengeType.None;

    // ── MCQ fields ─────────────────────────────────────────────────────
    public string question;               // e.g. "What is the capital of France?"
    public List<MCQOption> options;       // 2–4 answer options
    public int maxAttempts = 3;           // how many tries before failing
    public int bonusPoints = 50;          // extra points for correct answer

    // ── Minigame fields ────────────────────────────────────────────────
    public string minigameId;             // e.g. "MemoryMatch_Easy"
    public int timeLimitSeconds = 60;     // countdown timer for minigame

    // ── AR mode flag ───────────────────────────────────────────────────
    public bool useARMode = false;        // if true, MCQ options spawn in AR space
}

// ── Extend existing TreasureData ─────────────────────────────────────────────
// Add this field to your existing TreasureManagerGPS_Multiplayer.TreasureData:
//
//   public ChallengeData challenge;   ← ADD THIS
//
// Since TreasureData is in TreasureManagerGPS_Multiplayer.cs, add it there.
