using UnityEngine;

public class QuitButton : MonoBehaviour
{
    // Hook this method to a UI Button's OnClick()
    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
