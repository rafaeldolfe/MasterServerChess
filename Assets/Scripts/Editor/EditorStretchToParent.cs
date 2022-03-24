using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.Timeline;
using UnityEngine;

public class EditorStretchToParent : Editor
{
    [MenuItem("uGUI/Stretch To Parent #q")]
    static void StretchToParent()
    {
        foreach (Transform transform in Selection.transforms)
        {
            RectTransform t = transform as RectTransform;
            Undo.RecordObject(transform, "Stretch To Parent");

            if (t == null) return;

            t.anchorMin = new Vector2(0, 0);
            t.anchorMax = new Vector2(1, 1);
            t.offsetMin = t.offsetMax = new Vector2(0, 0);
        }
    }
}
