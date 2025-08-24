// DeviceID.cs

using UnityEngine;

/// Provides a stable, anonymous playerId for the device.
/// - Stores a GUID in PlayerPrefs on first run
/// - Uses SystemInfo.deviceUniqueIdentifier when available (non-WebGL)
/// - Ensures length 8..64 to satisfy your server validator
public static class DeviceId
{
    private const string Key = "device_id_cached";

    public static string GetOrCreate()
    {
        // 1) Prefer cached GUID (works on all platforms, including WebGL)
        var cached = PlayerPrefs.GetString(Key, "");
        if (!string.IsNullOrEmpty(cached))
            return cached;

        string id = null;

#if !UNITY_WEBGL
        // 2) Use Unity's deviceUniqueIdentifier when available
        id = SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrEmpty(id) || id == "Unknown")
            id = System.Guid.NewGuid().ToString("N");
#else
        // 2) WebGL has no reliable deviceUniqueIdentifier → use GUID
        id = System.Guid.NewGuid().ToString("N");
#endif

        // 3) Normalize length for server (8..64)
        if (id.Length < 8) id = (id + System.Guid.NewGuid().ToString("N")).Substring(0, 32);
        if (id.Length > 64) id = id.Substring(0, 64);

        PlayerPrefs.SetString(Key, id);
        PlayerPrefs.Save();
        return id;
    }
}

