// DeviceID.cs
using UnityEngine;

public static class DeviceId
{
    private const string Key = "device_id_cached";

    public static string GetOrCreate()
    {
        // Prefer a cached GUID so the ID stays stable between runs
        var cached = PlayerPrefs.GetString(Key, "");
        if (!string.IsNullOrEmpty(cached))
            return cached;

        string id = null;

#if !UNITY_WEBGL
        // Unity provides a deviceUniqueIdentifier on most platforms
        id = SystemInfo.deviceUniqueIdentifier;
        // Fallback if Unity can't give a usable value
        if (string.IsNullOrEmpty(id) || id == "Unknown")
            id = System.Guid.NewGuid().ToString("N");
#else
        // WebGL has no reliable unique identifier → just generate a GUID
        id = System.Guid.NewGuid().ToString("N");
#endif

        // Clamp to server's accepted length (8–64 characters)
        if (id.Length < 8) id = (id + System.Guid.NewGuid().ToString("N")).Substring(0, 32);
        if (id.Length > 64) id = id.Substring(0, 64);

        PlayerPrefs.SetString(Key, id);
        PlayerPrefs.Save();
        return id;
    }
}
