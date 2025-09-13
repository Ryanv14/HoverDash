// LeaderboardClient.cs
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LeaderboardClient : MonoBehaviour
{
    [Header("Server Settings")]
    public string BackendBaseUrl = "hoverdash.onrender.com/";

    private string sessionId;

    // --- Public API ---

    // Start a level session (call when the player actually starts)
    public IEnumerator StartLevel(string levelId, Action<string> onOk = null, Action<string> onErr = null)
    {
        var url = $"{BackendBaseUrl}/start-level";
        var payload = JsonUtility.ToJson(new StartLevelReq { levelId = levelId });

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error}");
                yield break;
            }

            var json = req.downloadHandler.text;
            var resp = JsonUtility.FromJson<StartLevelResp>(json);
            sessionId = resp.sessionId;
            onOk?.Invoke(sessionId);
        }
    }

    // Finish a level (send the player's typed name here)
    public IEnumerator FinishLevel(string levelId, int stars, string name, Action<double> onDone, Action<string> onErr = null)
    {
        yield return FinishInternal(levelId, stars, name, onDone, onErr);
    }

    // Overload with clientDurationSeconds kept for compatibility (ignored by server; you can remove if not needed)
    public IEnumerator FinishLevel(string levelId, int stars, string name, float clientDurationSeconds, Action<double> onDone, Action<string> onErr = null)
    {
        yield return FinishInternal(levelId, stars, name, onDone, onErr);
    }

    // Ensure session exists (useful if you might call Finish before Start)
    public IEnumerator EnsureSession(string levelId, Action onOk = null, Action<string> onErr = null)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            onOk?.Invoke();
            yield break;
        }
        yield return StartLevel(levelId, _ => onOk?.Invoke(), onErr);
    }

    // Get leaderboard
    public IEnumerator GetLeaderboard(string levelId, Action<ScoreRow[]> onOk, Action<string> onErr)
    {
        var url = $"{BackendBaseUrl}/leaderboard/{UnityWebRequest.EscapeURL(levelId)}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error}");
                yield break;
            }

            var json = req.downloadHandler.text;
            var arr = JsonHelper.FromJson<ScoreRow>(json);  // top-level array helper
            onOk?.Invoke(arr ?? Array.Empty<ScoreRow>());
        }
    }

    // --- Internals ---

    private IEnumerator FinishInternal(string levelId, int stars, string name, Action<double> onDone, Action<string> onErr)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            // Auto-start a session if missing
            yield return StartLevel(levelId, _ => { }, onErr);
            if (string.IsNullOrEmpty(sessionId))
                yield break;
        }

        var url = $"{BackendBaseUrl}/finish-level";
        var safeName = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name.Trim();
        if (safeName.Length > 20) safeName = safeName.Substring(0, 20);

        var payload = JsonUtility.ToJson(new FinishLevelReq
        {
            levelId = levelId,
            sessionId = sessionId,
            stars = stars,
            name = safeName
        });

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error}");
                yield break;
            }

            var json = req.downloadHandler.text;
            var resp = JsonUtility.FromJson<FinishLevelResp>(json);
            onDone?.Invoke(resp.score);
        }
    }

    // --- DTOs ---

    [Serializable] private class StartLevelReq { public string levelId; }
    [Serializable] private class StartLevelResp { public string sessionId; }

    [Serializable]
    private class FinishLevelReq
    {
        public string levelId;
        public string sessionId;
        public int stars;
        public string name;
    }

    [Serializable] public class ScoreRow { public string name; public double score; }

    [Serializable] private class FinishLevelResp { public bool ok; public double score; }

    // JsonUtility helper for top-level arrays
    public static class JsonHelper
    {
        [Serializable] private class Wrapper<T> { public T[] Items; }
        public static T[] FromJson<T>(string json)
        {
            string wrapped = "{\"Items\":" + json + "}";
            var w = JsonUtility.FromJson<Wrapper<T>>(wrapped);
            return w != null ? w.Items : null;
        }
    }
}
