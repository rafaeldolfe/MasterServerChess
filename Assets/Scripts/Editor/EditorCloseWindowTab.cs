using UnityEditor;
using UnityEngine;

public class EditorCloseWindowTab : Editor
{
    [MenuItem("Shortcuts/Close Window Tab")]
    static void CloseTab()
    {
        EditorWindow focusedWindow = EditorWindow.focusedWindow;
        if (focusedWindow != null)
        {
            CloseTab(focusedWindow);
        }
        else
        {
            Debug.LogWarning("Found no focused window to close");

        }
    }
    static void CloseTab(EditorWindow editorWindow)
    {
        editorWindow.Close();
    }
}
