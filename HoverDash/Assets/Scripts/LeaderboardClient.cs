// LeaderboardClient.cs
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LeaderboardClient : MonoBehaviour
{
    [Header("Server Settings")]
    public string BackendBaseUrl = "https://hoverdash.onrender.com";

    [Header("Network")]
    [Tooltip("UnityWebRequest timeout in seconds (WebGL uses this).")]
    public int TimeoutSeconds = 20;

    private string sessionId;
    private const string UsedSessionErrorMarker = "Invalid or used session";

    // --- Public API ---

    /// <summary>Clear any cached session id. Call this at the start of a new run.</summary>
    public void ClearSession() => sessionId = null;

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
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = Mathf.Max(1, TimeoutSeconds);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error} - body: {req.downloadHandler?.text}");
                yield break;
            }

            var resp = JsonUtility.FromJson<StartLevelResp>(req.downloadHandler.text);
            sessionId = resp.sessionId;
            Debug.Log($"[LB] /start-level -> sessionId={sessionId}");
            onOk?.Invoke(sessionId);
        }
    }

    // Finish a level (no duration sent; server uses its own measured duration)
    public IEnumerator FinishLevel(string levelId, int stars, string name, Action<double> onDone, Action<string> onErr = null)
    {
        yield return FinishInternal(levelId, stars, name, 0f, onDone, onErr, retried: false);
    }

    // Finish a level (duration-aware; sends client frozen duration to server)
    public IEnumerator FinishLevel(string levelId, int stars, string name, float clientDurationSeconds, Action<double> onDone, Action<string> onErr = null)
    {
        yield return FinishInternal(levelId, stars, name, clientDurationSeconds, onDone, onErr, retried: false);
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
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = Mathf.Max(1, TimeoutSeconds);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error} - body: {req.downloadHandler?.text}");
                yield break;
            }

            var arr = JsonHelper.FromJson<ScoreRow>(req.downloadHandler.text);
            onOk?.Invoke(arr ?? Array.Empty<ScoreRow>());
        }
    }

    // --- Internals ---

    private IEnumerator FinishInternal(
        string levelId,
        int stars,
        string name,
        float clientDurationSeconds,
        Action<double> onDone,
        Action<string> onErr,
        bool retried)
    {
        // Make sure we have a session (but it could still be stale/used; we handle that below)
        if (string.IsNullOrEmpty(sessionId))
        {
            yield return StartLevel(levelId, _ => { }, onErr);
            if (string.IsNullOrEmpty(sessionId)) yield break;
        }

        var url = $"{BackendBaseUrl}/finish-level";
        var safeName = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name.Trim();
        if (safeName.Length > 20) safeName = safeName.Substring(0, 20);

        var payloadObj = new FinishLevelReq
        {
            levelId = levelId,
            sessionId = sessionId,
            stars = stars,
            name = safeName,
            clientDurationSeconds = clientDurationSeconds
        };
        var payload = JsonUtility.ToJson(payloadObj);

        Debug.Log($"[LB] /finish-level payload: {{ levelId:'{levelId}', sessionId:'{sessionId}', stars:{stars}, name:'{safeName}', clientDurationSeconds:{clientDurationSeconds:F3} }}");

        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = Mathf.Max(1, TimeoutSeconds);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string bodyText = req.downloadHandler?.text ?? "";
                // If we hit "Invalid or used session", refresh the session and retry once automatically
                if (!retried &&
                    req.responseCode == 400 &&
                    !string.IsNullOrEmpty(bodyText) &&
                    bodyText.Contains(UsedSessionErrorMarker, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[LB] Finish failed with used/invalid session; refreshing session and retrying once.");
                    // force a new session
                    sessionId = null;
                    yield return StartLevel(levelId, _ => { }, err => Debug.LogWarning("[LB] Retry StartLevel failed: " + err));
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        yield return FinishInternal(levelId, stars, name, clientDurationSeconds, onDone, onErr, retried: true);
                        yield break;
                    }
                    onErr?.Invoke("Finish failed: could not establish session after retry.");
                    yield break;
                }

                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error} - body: {bodyText}");
                yield break;
            }

            var resp = JsonUtility.FromJson<FinishLevelResp>(req.downloadHandler.text);
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
        public float clientDurationSeconds; // optional; server uses if > 0 and plausible
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
