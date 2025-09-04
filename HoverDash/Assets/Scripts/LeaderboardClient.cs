// LeaderboardClient.cs
using System;
using System.Collections;
using UnityEngine;

public class LeaderboardClient : MonoBehaviour
{
    private string sessionId;

    // Start a level session (called when the player actually starts)
    public IEnumerator StartLevel(string levelId)
    {
        yield return null;
        sessionId = Guid.NewGuid().ToString("N");
    }

    // Back-compat: old signature without duration
    public IEnumerator FinishLevel(string levelId, int stars, string name, Action<double> onDone)
    {
        yield return FinishLevel(levelId, stars, name, (float?)null, onDone);
    }

    // Preferred: client supplies frozen duration
    public IEnumerator FinishLevel(string levelId, int stars, string name, float clientDurationSeconds, Action<double> onDone)
    {
        yield return FinishLevel(levelId, stars, name, (float?)clientDurationSeconds, onDone);
    }

    private IEnumerator FinishLevel(string levelId, int stars, string name, float? clientDurationSeconds, Action<double> onDone)
    {
        yield return null;

        // Example score 
        double serverScore = stars * (1000.0 / Math.Max(0.0001, (clientDurationSeconds ?? 10f)));
        onDone?.Invoke(serverScore);
    }

    public IEnumerator EnsureSession(string levelId)
    {
        if (!string.IsNullOrEmpty(sessionId)) yield break;
        yield return StartLevel(levelId);
    }

    public IEnumerator GetLeaderboard(string levelId, Action<ScoreRow[]> onOk, Action<string> onErr)
    {
        yield return null;

        var sample = new[]
        {
            new ScoreRow { name = "AAA", score = 1234 },
            new ScoreRow { name = "BBB", score = 987 },
            new ScoreRow { name = "CCC", score = 650 },
        };

        onOk?.Invoke(sample);
    }

    [Serializable]
    public class ScoreRow
    {
        public string name;
        public double score;
    }
}
