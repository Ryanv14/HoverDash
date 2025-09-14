// LeaderboardClient.cs
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LeaderboardClient : MonoBehaviour
{
    [Header("Server Settings")]
    [Tooltip("Base URL without trailing slash, e.g. https://hoverdash.onrender.com")]
    public string BackendBaseUrl = "https://hoverdash.onrender.com";

    [Header("Network")]
    [Tooltip("UnityWebRequest timeout in seconds (WebGL uses this).")]
    public int TimeoutSeconds = 20;

    [Header("Debug")]
    [Tooltip("Log helpful client-side debug messages to the Console.")]
    public bool EnableDebugLogs = true;

    private string sessionId;

    // Optional: let other scripts inspect/debug
    public string CurrentSessionId => sessionId;

    private void Awake()
    {
        BackendBaseUrl = NormalizeBaseUrl(BackendBaseUrl);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BackendBaseUrl = NormalizeBaseUrl(BackendBaseUrl);
    }
#endif

    // --- Public API ----------------------------------------------------------

    // Begin a run/session
    public IEnumerator StartLevel(string levelId, Action<string> onOk = null, Action<string> onErr = null)
    {
        var url = BuildUrl("/start-level");
        var payload = JsonUtility.ToJson(new StartLevelReq { levelId = levelId });

        using (var req = new UnityWebRequest(url, "POST"))
        {
            var body = Encoding.UTF8.GetBytes(payload);
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

            if (EnableDebugLogs)
                Debug.Log($"[LB] /start-level -> sessionId={sessionId}");

            onOk?.Invoke(sessionId);
        }
    }

    // Finish (server measures entirely)
    public IEnumerator FinishLevel(string levelId, int stars, string name, Action<double> onDone, Action<string> onErr = null)
    {
        return FinishInternal(levelId, stars, name, 0f, 0f, onDone, onErr);
    }

    // Finish with frozen duration only
    public IEnumerator FinishLevel(string levelId, int stars, string name, float clientDurationSeconds, Action<double> onDone, Action<string> onErr = null)
    {
        return FinishInternal(levelId, stars, name, clientDurationSeconds, 0f, onDone, onErr);
    }

    // Finish with frozen duration AND frozen score (preferred)
    public IEnumerator FinishLevel(string levelId, int stars, string name, float clientDurationSeconds, float clientScore, Action<double> onDone, Action<string> onErr = null)
    {
        return FinishInternal(levelId, stars, name, clientDurationSeconds, clientScore, onDone, onErr);
    }

    // Ensure session exists (useful if Finish might be called before Start)
    public IEnumerator EnsureSession(string levelId, Action onOk = null, Action<string> onErr = null)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            onOk?.Invoke();
            yield break;
        }
        yield return StartLevel(levelId, _ => onOk?.Invoke(), onErr);
    }

    // Fetch leaderboard for a level
    public IEnumerator GetLeaderboard(string levelId, Action<ScoreRow[]> onOk, Action<string> onErr)
    {
        var url = BuildUrl($"/leaderboard/{UnityWebRequest.EscapeURL(levelId)}");
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

    // Clear the local session (optional helper)
    public void ClearSession() => sessionId = null;

    // --- Internals -----------------------------------------------------------

    private IEnumerator FinishInternal(string levelId, int stars, string name, float clientDurationSeconds, float clientScore, Action<double> onDone, Action<string> onErr)
    {
        // Start session if missing
        if (string.IsNullOrEmpty(sessionId))
        {
            yield return StartLevel(levelId, _ => { }, onErr);
            if (string.IsNullOrEmpty(sessionId))
                yield break;
        }

        var url = BuildUrl("/finish-level");
        var safeName = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name.Trim();
        if (safeName.Length > 20) safeName = safeName.Substring(0, 20);

        var payloadObj = new FinishLevelReq
        {
            levelId = levelId,
            sessionId = sessionId,
            stars = stars,
            name = safeName,
            clientDurationSeconds = clientDurationSeconds,
            clientScore = clientScore
        };

        if (EnableDebugLogs)
            Debug.Log($"[LB] /finish-level payload: {{ levelId:'{levelId}', sessionId:'{sessionId}', stars:{stars}, name:'{safeName}', clientDurationSeconds:{clientDurationSeconds:0.###}, clientScore:{clientScore:0.###} }}");

        // try once, then if specific 400 error, refresh session and retry once
        bool didRetry = false;

        while (true)
        {
            var json = JsonUtility.ToJson(payloadObj);

            using (var req = new UnityWebRequest(url, "POST"))
            {
                var body = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = Mathf.Max(1, TimeoutSeconds);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var resp = JsonUtility.FromJson<FinishLevelResp>(req.downloadHandler.text);
                    onDone?.Invoke(resp.score);
                    yield break;
                }

                // Handle the common case: server says session is invalid/used.
                var errBody = req.downloadHandler?.text ?? "";
                bool looksUsed = req.responseCode == 400 && errBody.Contains("Invalid or used session", StringComparison.OrdinalIgnoreCase);

                if (looksUsed && !didRetry)
                {
                    if (EnableDebugLogs)
                        Debug.LogWarning("[LB] Finish failed with used/invalid session; refreshing session and retrying once.");

                    // refresh session and retry once
                    didRetry = true;
                    yield return StartLevel(levelId, _ => { }, onErr);
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error} - body: {errBody}");
                        yield break;
                    }

                    // update payload to new session
                    payloadObj.sessionId = sessionId;
                    continue;
                }

                // generic error
                onErr?.Invoke($"HTTP error: {req.responseCode} - {req.error} - body: {errBody}");
                yield break;
            }
        }
    }

    private string BuildUrl(string path)
    {
        if (string.IsNullOrEmpty(path)) return BackendBaseUrl;
        if (path[0] != '/') path = "/" + path;
        return BackendBaseUrl + path;
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        url = url.Trim();
        while (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);
        return url;
    }

    // --- DTOs ----------------------------------------------------------------

    [Serializable] private class StartLevelReq { public string levelId; }
    [Serializable] private class StartLevelResp { public string sessionId; }

    [Serializable]
    private class FinishLevelReq
    {
        public string levelId;
        public string sessionId;
        public int stars;
        public string name;
        public float clientDurationSeconds;
        public float clientScore; // snapshot score from client (optional)
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
