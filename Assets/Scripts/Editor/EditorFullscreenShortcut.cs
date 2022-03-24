#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class EditorFullscreenShortcut
{
    static EditorFullscreenShortcut()
    {
        EditorApplication.update += Update;
    }
    static void Update()
    {
        if (EditorApplication.isPlaying && ShouldToggleMaximize())
        {
            EditorWindow.focusedWindow.maximized = !EditorWindow.focusedWindow.maximized;
        }
    }
    private static bool ShouldToggleMaximize()
    {
        return Input.GetKey(KeyCode.Space) && Input.GetKey(KeyCode.LeftShift);
    }
}
#endif