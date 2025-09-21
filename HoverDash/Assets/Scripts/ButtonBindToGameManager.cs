// ButtonBindToGameManager.cs
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonBindToGameManager : MonoBehaviour
{
    public enum Action
    {
        RestartLevel,
        GoToMainMenu,
        SubmitScore   // new action
    }

    [SerializeField] private Action action;
    private Button btn;

    private void Awake()
    {
        btn = GetComponent<Button>();
        btn.onClick.AddListener(InvokeAction);
    }

    private void OnDestroy()
    {
        // Defensive cleanup in case the button is destroyed before the listener is removed
        if (btn != null) btn.onClick.RemoveListener(InvokeAction);
    }

    private void InvokeAction()
    {
        // Uses FindObjectOfType here so buttons don’t need a serialized reference
        var gm = FindObjectOfType<GameManager>(true);
        if (!gm)
        {
            Debug.LogWarning("[ButtonBindToGameManager] No GameManager found in scene.");
            return;
        }

        switch (action)
        {
            case Action.RestartLevel:
                gm.RestartLevel();
                break;

            case Action.GoToMainMenu:
                gm.GoToMainMenu();
                break;

            case Action.SubmitScore:
                gm.OnClickSubmitScore();
                break;
        }
    }
}
