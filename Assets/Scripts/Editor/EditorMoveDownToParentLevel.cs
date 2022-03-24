using UnityEditor;
using System.Linq;
using UnityEngine;

public class EditorMoveDownToParentLevel : Editor
{
    [MenuItem("Shortcuts/Move down to parent level &Z")]
    static void MoveToParentLevel()
    {
        var siblings = Selection.gameObjects;
        if (siblings == null)
        {
            return;
        }
        if (siblings.Any(sibling => sibling.transform.parent == null))
        {
            return;
        }
        Transform parent = siblings[0].transform.parent;
        if (parent == null)
        {
            return;
        }


        for (int i = 0; i < siblings.Length; i++)
        {
            GameObject sibling = siblings[i];
            Transform grandparent = parent.parent;
            int parentSiblingIndex = sibling.transform.parent.GetSiblingIndex();
            Undo.SetTransformParent(sibling.transform, grandparent, "MoveToParentLevel");
            sibling.transform.SetSiblingIndex(parentSiblingIndex);
        }
    }
}
