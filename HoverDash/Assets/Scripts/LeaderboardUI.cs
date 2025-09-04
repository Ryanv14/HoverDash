// LeaderboardUI.cs

using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LeaderboardUI : MonoBehaviour
{
    [Header("List Setup")]
    [SerializeField] private Transform content;               // ScrollView/Content
    [SerializeField] private LeaderboardRow rowPrefab;        // Prefab with LeaderboardRow
    [SerializeField] private int maxRows = 100;

    [Header("Auto Layout (optional)")]
    [SerializeField] private bool autoFixLayout = true;       // auto-add layout components
    [SerializeField] private float rowHeight = 48f;           // preferred row height
    [SerializeField] private float spacing = 8f;              // space between rows
    [SerializeField] private bool leftAlign = true;           // alignment of rows

    [Header("States (optional)")]
    [SerializeField] private GameObject loadingGroup;
    [SerializeField] private GameObject emptyGroup;
    [SerializeField] private GameObject errorGroup;
    [SerializeField] private TMP_Text errorText;

    private void Awake()
    {
        if (autoFixLayout) EnsureContentLayout();
    }

    private void OnEnable()
    {
        if (autoFixLayout) EnsureContentLayout();
    }

    public void ShowLoading(string message = null)
    {
        gameObject.SetActive(true);
        Clear();
        Toggle(loadingGroup, true);
        Toggle(emptyGroup, false);
        Toggle(errorGroup, false);
        if (errorText && !string.IsNullOrEmpty(message)) errorText.text = message;
    }

    public void ShowError(string message)
    {
        gameObject.SetActive(true);
        Clear();
        Toggle(loadingGroup, false);
        Toggle(emptyGroup, false);
        Toggle(errorGroup, true);
        if (errorText) errorText.text = message;
    }

    public void Show(LeaderboardClient.ScoreRow[] rows)
    {
        gameObject.SetActive(true);
        Toggle(loadingGroup, false);
        Toggle(errorGroup, false);
        Clear();

        int count = rows == null ? 0 : Mathf.Min(maxRows, rows.Length);
        if (count == 0)
        {
            Toggle(emptyGroup, true);
            return;
        }
        Toggle(emptyGroup, false);

        for (int i = 0; i < count; i++)
        {
            var row = Instantiate(rowPrefab, content);
            ConfigureRowLayout(row);          
            row.Bind(i + 1, rows[i]);
        }

        // Rebuild so layout updates immediately
        var crt = content as RectTransform;
        if (crt) LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
    }

    public void Hide() => gameObject.SetActive(false);

    private void Clear()
    {
        if (!content) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    private void Toggle(GameObject go, bool on)
    {
        if (go && go.activeSelf != on) go.SetActive(on);
    }

    // Layout helpers 
    private void EnsureContentLayout()
    {
        if (!content) return;

        // Ensure a VerticalLayoutGroup on Content
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (!vlg) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childAlignment = leftAlign ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        // Ensure a ContentSizeFitter so Content grows with children
        var csf = content.GetComponent<ContentSizeFitter>();
        if (!csf) csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Tidy Content RectTransform (top-stretch)
        var rt = content as RectTransform;
        if (rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
            rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
        }
    }

    private void ConfigureRowLayout(LeaderboardRow row)
    {
        if (!row) return;

        var go = row.gameObject;

        // Make row stretch horizontally, fixed preferred height
        var rt = go.GetComponent<RectTransform>();
        if (rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, rowHeight);
        }

        // Ensure LayoutElement with height
        var le = go.GetComponent<LayoutElement>();
        if (!le) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight;
        le.minHeight = rowHeight;

        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        if (!hlg)
        {
            hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 4);
        }
    }
}
