using UnityEditor;
using System.Linq;

public class EditorHierarchySiblingMover : Editor
{
    private enum SiblingChangeDirection
    {
        Up,
        Down,
    }
    [MenuItem("Shortcuts/Set to Next Sibling &a")]
    static void SetToNextSibling()
    {
        ChangeSibling(SiblingChangeDirection.Up);
    }

    [MenuItem("Shortcuts/Set to Previous Sibling &q")]
    static void SetToPreviousSibling()
    {
        ChangeSibling(SiblingChangeDirection.Down);
    }

    static void ChangeSibling(SiblingChangeDirection direction)
    {
        var siblings = Selection.gameObjects;
        if (siblings == null)
        {
            return;
        }
        foreach (var sibling1 in siblings)
        {
            if (siblings.Any(sibling2 => sibling2.transform.parent != sibling1.transform.parent))
            {
                // Do not try to move when siblings have different parents
                return;
            }
        }
        if (siblings[0].transform.parent != null)
        {
            Undo.RegisterCompleteObjectUndo(siblings[0].transform.parent, "ChangeSibling");
        }

        int sortOrder;
        if (direction == SiblingChangeDirection.Up)
        {
            sortOrder = 1;
        }
        else
        {
            sortOrder = -1;
        }
        foreach (var sibling in siblings.OrderBy(x => -1 * sortOrder * x.transform.GetSiblingIndex()))
        {
            var index = sibling.transform.GetSiblingIndex();
            if (index + sortOrder < 0)
            {
                continue;
            }
            sibling.transform.SetSiblingIndex(index + sortOrder);
        }
    }
}
