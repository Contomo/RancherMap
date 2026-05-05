using UnityEngine;

namespace rancher_minimap
{
    internal static class UiRectTransforms
    {
        public static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        public static readonly Vector2 TopRight = Vector2.one;
        public static readonly Vector2 StretchMin = Vector2.zero;
        public static readonly Vector2 StretchMax = Vector2.one;

        public static void CenterOnParent(RectTransform rect, Vector2 anchoredPosition)
        {
            if (rect == null)
                return;

            rect.anchorMin = Center;
            rect.anchorMax = Center;
            rect.pivot = Center;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        public static void CenterOnParent(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            CenterOnParent(rect, anchoredPosition);
            if (rect != null)
                rect.sizeDelta = sizeDelta;
        }

        public static void StretchToParent(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = StretchMin;
            rect.anchorMax = StretchMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        public static void AnchorAt(RectTransform rect, Vector2 anchor, Vector2 pivot)
        {
            if (rect == null)
                return;

            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        public static bool ContainsWorldPoint(RectTransform rect, Vector3 worldPoint, float margin = 0f)
        {
            if (rect == null)
                return false;

            var local = (Vector2)rect.InverseTransformPoint(worldPoint);
            var bounds = rect.rect;
            if (margin > 0f)
            {
                bounds.xMin -= margin;
                bounds.xMax += margin;
                bounds.yMin -= margin;
                bounds.yMax += margin;
            }

            return bounds.Contains(local);
        }
    }
}
