using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppMonomiPark.SlimeRancher.Map;
using Il2CppMonomiPark.SlimeRancher.Splines;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    internal static class MapPortalLineOverlays
    {
        private const string OverlayRootName = "RMM_PortalLineOverlay";

        private static readonly HashSet<int> NativeLogRoots = new HashSet<int>();

        public static void Refresh(GameObject obj, string reason, bool enabled)
        {
            if (obj == null)
                return;

            Remove(obj.transform);
            ApplyNativeGraphicsState(obj, reason, enabled);
        }

        public static void Update(GameObject rootObject, bool enabled)
        {
            if (rootObject == null)
                return;

            if (!enabled)
            {
                ApplyDisabledPortalLineState(rootObject, "hud.update-disabled");
                return;
            }

            var id = rootObject.GetInstanceID();
            if (!NativeLogRoots.Contains(id))
            {
                NativeLogRoots.Add(id);
                Log.Every("portal-native-state-" + id, 8f,
                    "hud: portal lines renderer=native-enabled" +
                    " root=" + MapObjectNames.PathOf(rootObject.transform));
            }
        }

        public static void Remove(Transform root)
        {
            if (root == null)
                return;

            RemoveDirect(root);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform != null && transform != root)
                    RemoveDirect(transform);
            }
        }

        public static int EnsureNativeGraphics(GameObject root, bool enabled)
        {
            return ApplyNativeGraphicsState(root, "hud.attach-native-portal-lines", enabled);
        }

        private static int ApplyNativeGraphicsState(GameObject root, string reason, bool enabled)
        {
            if (root == null)
                return 0;

            if (!enabled)
                return ApplyDisabledPortalLineState(root, reason);

            var nativeLines = 0;
            var changed = 0;
            var active = 0;
            var missingSpline = 0;
            var assignedSpline = 0;
            var enabledSplines = 0;
            string sample = null;

            foreach (var line in root.GetComponentsInChildren<PortalLineGraphic>(true))
            {
                if (line == null)
                    continue;

                nativeLines++;

                var branch = FindPortalLineBranch(line.transform);
                if (branch != null && !branch.gameObject.activeSelf)
                {
                    branch.gameObject.SetActive(true);
                    changed++;
                }

                if (!line.gameObject.activeSelf)
                {
                    line.gameObject.SetActive(true);
                    changed++;
                }

                var graphic = line as Graphic;
                var spline = GetAssignedSpline(line) ?? FindNativeSpline(root, line);
                if (spline == null)
                {
                    missingSpline++;
                    if (sample == null)
                        sample = MapVisualDiagnostics.DescribePortalLineNative(line, graphic) + " native=missing-spline";
                }
                else
                {
                    if (GetAssignedSpline(line) == null && ReflectionTools.SetFieldOrProperty(line, spline, "_spline", "spline", "Spline"))
                        assignedSpline++;

                    if (!spline.enabled)
                    {
                        spline.enabled = true;
                        enabledSplines++;
                        changed++;
                    }
                }

                if (!line.enabled)
                {
                    line.enabled = true;
                    changed++;
                }

                if (graphic != null)
                {
                    graphic.raycastTarget = false;
                    if (!graphic.enabled)
                    {
                        graphic.enabled = true;
                        changed++;
                    }

                    graphic.canvasRenderer.SetAlpha(graphic.color.a);
                    graphic.SetVerticesDirty();
                    graphic.SetMaterialDirty();
                }

                if (line.gameObject.activeInHierarchy)
                    active++;

                if (sample == null && spline != null)
                    sample = MapVisualDiagnostics.DescribePortalLineNative(line, graphic) +
                             " nativeSpline=" + MapObjectNames.PathOf(spline.transform);
            }

            if (nativeLines > 0)
            {
                Log.Info("map-capture: portal line renderer=native-enabled" +
                         " native=" + nativeLines +
                         " changed=" + changed +
                         " active=" + active +
                         " assignedSpline=" + assignedSpline +
                         " enabledSpline=" + enabledSplines +
                         " missingSpline=" + missingSpline +
                         " reason=" + reason +
                         " sample=" + (sample ?? "-"));
            }

            return changed;
        }

        private static int ApplyDisabledPortalLineState(GameObject root, string reason)
        {
            if (root == null)
                return 0;

            var changed = 0;
            var branches = 0;
            var graphics = 0;
            var lineGraphics = 0;
            var splines = 0;
            string sample = null;

            foreach (var line in root.GetComponentsInChildren<PortalLineGraphic>(true))
            {
                if (line == null || line.transform == null)
                    continue;

                lineGraphics++;

                var branch = FindPortalLineBranch(line.transform);
                if (branch != null)
                {
                    branches++;
                    sample = sample ?? MapObjectNames.PathOf(branch);
                    if (branch.gameObject.activeSelf)
                    {
                        branch.gameObject.SetActive(false);
                        changed++;
                    }
                }

                if (line.enabled)
                {
                    line.enabled = false;
                    changed++;
                }

                var graphic = line as Graphic;
                if (graphic != null)
                {
                    graphics++;
                    if (graphic.enabled)
                    {
                        graphic.enabled = false;
                        changed++;
                    }
                    graphic.canvasRenderer.SetAlpha(0f);
                }

                if (line.gameObject.activeSelf)
                {
                    line.gameObject.SetActive(false);
                    changed++;
                }
            }

            foreach (var spline in root.GetComponentsInChildren<Spline>(true))
            {
                if (spline == null || spline.transform == null || !IsPortalLinePath(spline.transform))
                    continue;

                splines++;
                sample = sample ?? MapObjectNames.PathOf(spline.transform);
                if (spline.enabled)
                {
                    spline.enabled = false;
                    changed++;
                }
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == root.transform || !IsPortalLineBranch(transform))
                    continue;

                branches++;
                sample = sample ?? MapObjectNames.PathOf(transform);

                foreach (var graphic in transform.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic == null)
                        continue;

                    graphics++;
                    graphic.raycastTarget = false;
                    if (graphic.enabled)
                    {
                        graphic.enabled = false;
                        changed++;
                    }
                    graphic.canvasRenderer.SetAlpha(0f);
                }

                if (transform.gameObject.activeSelf)
                {
                    transform.gameObject.SetActive(false);
                    changed++;
                }
            }

            if (changed > 0 || lineGraphics > 0 || splines > 0 || branches > 0)
            {
                Log.Info("map-capture: portal line renderer=hard-disabled" +
                         " changed=" + changed +
                         " native=" + lineGraphics +
                         " splines=" + splines +
                         " branches=" + branches +
                         " graphics=" + graphics +
                         " reason=" + reason +
                         " sample=" + (sample ?? "-"));
            }

            return changed;
        }

        private static bool IsPortalLineBranch(Transform transform)
        {
            if (transform == null)
                return false;

            var path = MapObjectNames.PathOf(transform);
            if (path.IndexOf("/zone_links/", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var clean = MapObjectNames.CleanCloneName(transform.name);
            return clean.IndexOf("PortalLine", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPortalLinePath(Transform transform)
        {
            if (transform == null)
                return false;

            var path = MapObjectNames.PathOf(transform);
            return path.IndexOf("/zone_links/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   path.IndexOf("/PortalLine", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Transform FindPortalLineBranch(Transform lineTransform)
        {
            Transform candidate = null;
            var current = lineTransform;
            while (current != null)
            {
                var clean = MapObjectNames.CleanCloneName(current.name);
                if (clean.IndexOf("PortalLineSpline", StringComparison.OrdinalIgnoreCase) >= 0)
                    return current;

                if (candidate == null && clean.IndexOf("PortalLine", StringComparison.OrdinalIgnoreCase) >= 0)
                    candidate = current;

                current = current.parent;
            }

            return candidate ?? lineTransform;
        }

        private static Spline GetAssignedSpline(PortalLineGraphic line)
        {
            if (line == null)
                return null;

            try
            {
                return ReflectionTools.GetFieldOrPropertyQuiet(line, "_spline", "spline", "Spline") as Spline;
            }
            catch
            {
                return null;
            }
        }

        private static Spline FindNativeSpline(GameObject root, PortalLineGraphic line)
        {
            if (line == null)
                return null;

            var parent = line.transform != null ? line.transform.parent : null;
            if (parent != null)
            {
                var parentSpline = parent.GetComponent<Spline>();
                if (parentSpline != null)
                    return parentSpline;
            }

            var ownSpline = line.GetComponent<Spline>();
            if (ownSpline != null)
                return ownSpline;

            if (root == null || line.transform == null)
                return null;

            Spline best = null;
            var bestScore = int.MinValue;
            var linePath = MapObjectNames.PathOf(line.transform);
            foreach (var spline in root.GetComponentsInChildren<Spline>(true))
            {
                if (spline == null || spline.transform == null)
                    continue;

                var path = MapObjectNames.PathOf(spline.transform);
                if (path.IndexOf("/zone_links/", StringComparison.OrdinalIgnoreCase) < 0 ||
                    path.IndexOf("/PortalLineSpline", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var score = CommonPrefixLength(linePath, path);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = spline;
                }
            }

            return best;
        }

        private static void RemoveDirect(Transform root)
        {
            if (root == null)
                return;

            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null)
                    continue;

                if (string.Equals(child.name, OverlayRootName, StringComparison.Ordinal) ||
                    string.Equals(child.name, "RMM_ClippedPortalDots", StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        private static int CommonPrefixLength(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;

            var length = Math.Min(a.Length, b.Length);
            var count = 0;
            while (count < length && a[count] == b[count])
                count++;
            return count;
        }
    }
}
