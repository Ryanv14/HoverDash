// NamePrompt.cs

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
        okButton.onClick.AddListener(() =>
        {
            string name = (input != null ? input.text : "").Trim();
            if (string.IsNullOrEmpty(name)) name = "Anonymous";
            if (name.Length > 20) name = name.Substring(0, 20);
            Hide();
            onConfirm?.Invoke(name);   
        });

        cancelButton.onClick.AddListener(() =>
        {
            Hide();
            onCancel?.Invoke();
        });
    }

    public void Show(string prefill, Action<string> onConfirm, Action onCancel = null)
    {
        this.onConfirm = onConfirm;
        this.onCancel = onCancel;

        if (input != null)
            input.text = string.IsNullOrWhiteSpace(prefill) ? "" : prefill;

        gameObject.SetActive(true);
        if (input != null) input.Select();
    }

    public void Hide() => gameObject.SetActive(false);
}
