// LeaderboardClient.cs

using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class LeaderboardClient : MonoBehaviour
{
    public static LeaderboardClient Instance { get; private set; }

    [SerializeField] private string baseUrl = "http://localhost:3000"; // change to deployed URL later
    private string sessionId;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool HasSession => !string.IsNullOrEmpty(sessionId);

    [System.Serializable] private class StartReq { public string playerId; public string levelId; }
    [System.Serializable] private class StartRes { public string sessionId; }

    [System.Serializable] private class FinishReq { public string playerId; public string levelId; public string sessionId; public int stars; public string name; }
    [System.Serializable] private class FinishRes { public bool ok; public double score; public string error; }

    [System.Serializable] public class ScoreRow { public string name; public double score; }
    [System.Serializable] private class ScoreRows { public ScoreRow[] items; }

    public IEnumerator StartLevel(string levelId)
    {
        var body = JsonUtility.ToJson(new StartReq { playerId = DeviceId.GetOrCreate(), levelId = levelId });

        using (var req = new UnityWebRequest($"{baseUrl}/start-level", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[LB] StartLevel failed: {req.error} {req.downloadHandler.text}");
                yield break;
            }
            var res = JsonUtility.FromJson<StartRes>(req.downloadHandler.text);
            sessionId = res.sessionId;
            Debug.Log($"[LB] Session started. sessionId={sessionId}");
        }
    }

    public IEnumerator EnsureSession(string levelId)
    {
        if (!HasSession)
        {
            Debug.Log("[LB] No session yet; starting one…");
            yield return StartLevel(levelId);
        }
    }

    public IEnumerator FinishLevel(string levelId, int stars, string name, System.Action<double> onOk = null)
    {
        if (!HasSession)
            Debug.LogWarning("[LB] FinishLevel called without a sessionId. EnsureSession should be awaited first.");

        var body = JsonUtility.ToJson(new FinishReq
        {
            playerId = DeviceId.GetOrCreate(),
            levelId = levelId,
            sessionId = sessionId,
            stars = stars,
            name = name
        });

        using (var req = new UnityWebRequest($"{baseUrl}/finish-level", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[LB] FinishLevel failed: {req.error} {req.downloadHandler.text}");
                yield break;
            }

            var res = JsonUtility.FromJson<FinishRes>(req.downloadHandler.text);
            if (res.ok)
            {
                Debug.Log($"[LB] Submitted OK. serverScore={res.score} name={name}");
                onOk?.Invoke(res.score);
            }
            else
            {
                Debug.LogError($"[LB] Server error: {res.error}");
            }
        }
    }

    public IEnumerator GetLeaderboard(string levelId, System.Action<ScoreRow[]> onOk, System.Action<string> onError = null)
    {
        using (var req = UnityWebRequest.Get($"{baseUrl}/leaderboard/{levelId}"))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[LB] GetLeaderboard failed: {req.error}");
                onError?.Invoke(req.error);
                yield break;
            }

            string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
            var rows = JsonUtility.FromJson<ScoreRows>(wrapped);
            onOk?.Invoke(rows?.items ?? new ScoreRow[0]);
        }
    }
}
