// MainMenuManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    // Scene names must match those in Build Settings
    [SerializeField] private string level1Scene = "Level1";
    [SerializeField] private string level2Scene = "Level2";
    [SerializeField] private string zenLevelScene = "ZenLevel";

    // UI buttons call these to jump into a level
    public void OnPlayLevel1() => SceneManager.LoadScene(level1Scene);
    public void OnPlayLevel2() => SceneManager.LoadScene(level2Scene);
    public void OnPlayZenLevel() => SceneManager.LoadScene(zenLevelScene);
}
