// NamePromptUI.cs

using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

public class NamePromptUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Button okButton;
    [SerializeField] private Button cancelButton;

    private Action<string> onConfirm;
    private Action onCancel;

    private void Awake()
    {
        // wire up the buttons once
        okButton.onClick.AddListener(() =>
        {
            // pull text (handles missing input), trim, then apply simple fallback + length cap
            string name = (input != null ? input.text : "").Trim();
            if (string.IsNullOrEmpty(name)) name = "Anonymous";
            if (name.Length > 20) name = name.Substring(0, 20);

            Hide();                 // close first to avoid any UI race with the callback
            onConfirm?.Invoke(name);
        });

        cancelButton.onClick.AddListener(() =>
        {
            Hide();                 // close and notify (if someone cares)
            onCancel?.Invoke();
        });
    }

    public void Show(string prefill, Action<string> onConfirm, Action onCancel = null)
    {
        // store callbacks for this showing
        this.onConfirm = onConfirm;
        this.onCancel = onCancel;

        // optional prefill (blank if whitespace/null)
        if (input != null)
            input.text = string.IsNullOrWhiteSpace(prefill) ? "" : prefill;

        gameObject.SetActive(true);

        // give focus so the user can type immediately
        if (input != null) input.Select();
    }

    public void Hide() => gameObject.SetActive(false);
}