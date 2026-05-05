using UnityEngine;

namespace rancher_minimap
{
    /// <summary>
    /// RectTransform normalization helpers for cloned vanilla map UI.
    ///
    /// This intentionally lives outside MapProjection: MapProjection is pure world/map
    /// coordinate math, while these helpers mutate Unity UI objects so cloned vanilla
    /// map branches can be mounted into the minimap HUD predictably.
    /// </summary>
    internal static class MapRectTransforms
    {
        public static void NormalizeCloneRoot(GameObject obj)
        {
            if (obj == null)
                return;

            var rect = obj.GetComponent<RectTransform>();
            if (rect == null)
                return;

            var width = Mathf.Abs(rect.rect.width);
            var height = Mathf.Abs(rect.rect.height);
            if (width < 1f) width = Mathf.Abs(rect.sizeDelta.x);
            if (height < 1f) height = Mathf.Abs(rect.sizeDelta.y);
            if (width < 1f) width = 100f;
            if (height < 1f) height = 100f;

            UiRectTransforms.CenterOnParent(rect, Vector2.zero);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
