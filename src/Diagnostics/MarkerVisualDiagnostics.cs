using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace rancher_minimap
{
    /// <summary>
    /// Diagnostics for marker UI clones. These probes intentionally do not mutate marker objects;
    /// they only compare source, cached template, and runtime-instance visual state.
    /// </summary>
    internal static class MarkerVisualDiagnostics
    {
#if RMM_DIAGNOSTICS
        private const int MaxDetailedImages = 14;
        private const int MaxEventsPerStage = 32;
        private static readonly HashSet<string> LoggedSignatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> StageEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public static void LogSource(MinimapSettings settings, RectTransform sourceRoot, int markerId, string markerKind, string source)
        {
            LogRoot(settings, "capture-source", markerId, markerKind, sourceRoot != null ? sourceRoot.gameObject : null, source);
        }

        public static void LogCloneStage(MinimapSettings settings, string stage, int markerId, string markerKind, GameObject root, string source)
        {
            LogRoot(settings, stage, markerId, markerKind, root, source);
        }

        public static void LogRuntimeInstance(MinimapSettings settings, MarkerSnapshot marker, GameObject root, string source)
        {
            LogRoot(settings, "runtime-instance", marker.Id, marker.Kind, root, source);
        }

        private static void LogRoot(MinimapSettings settings, string stage, int markerId, string markerKind, GameObject root, string source)
        {
            if (!IsEnabled(settings) || root == null)
                return;

            var images = CollectImages(root);
            var graphics = CollectGraphics(root);
            if (!ShouldInspect(root, markerKind, images, graphics))
                return;

            var signature = BuildSignature(stage, markerId, markerKind, root, images, graphics);
            if (!LoggedSignatures.Add(signature))
                return;

            if (!TryReserveStageEvent(stage))
                return;

            Log.Info(BuildReport(stage, markerId, markerKind, root, source, images, graphics));
        }

        private static bool IsEnabled(MinimapSettings settings)
        {
            return settings != null && settings.DiagnosticsEnabled && settings.MarkerVisualDiagnosticsEnabled;
        }

        private static bool ShouldInspect(GameObject root, string markerKind, List<Image> images, List<Graphic> graphics)
        {
            if (ContainsInterestingTerm(markerKind) || ContainsInterestingTerm(root.name) || ContainsInterestingTerm(MapObjectNames.PathOf(root.transform)))
                return true;

            var activeImages = CountVisibleImages(images);
            var enabledGraphics = CountVisibleGraphics(graphics);
            if (images != null && images.Count >= 3)
                return true;
            if (activeImages > 1 || enabledGraphics > 1)
                return true;

            if (images != null)
            {
                foreach (var image in images)
                {
                    if (image == null)
                        continue;
                    if (ContainsInterestingTerm(image.sprite != null ? image.sprite.name : null) ||
                        ContainsInterestingTerm(MapObjectNames.PathOf(image.transform)))
                        return true;
                }
            }

            return false;
        }

        private static string BuildSignature(string stage, int markerId, string markerKind, GameObject root, List<Image> images, List<Graphic> graphics)
        {
            var builder = new StringBuilder();
            builder.Append(stage).Append('|').Append(markerId).Append('|').Append(markerKind ?? "-");
            builder.Append("|root=").Append(root.activeSelf).Append('/').Append(root.activeInHierarchy);
            builder.Append("|img=").Append(images != null ? images.Count : 0).Append('/').Append(CountVisibleImages(images)).Append(" self=").Append(CountSelfActiveImages(images));
            builder.Append("|gfx=").Append(graphics != null ? graphics.Count : 0).Append('/').Append(CountVisibleGraphics(graphics));

            if (images != null)
            {
                var shown = 0;
                foreach (var image in images)
                {
                    if (image == null)
                        continue;

                    builder.Append('|')
                        .Append(MapObjectNames.RelativePath(root.transform, image.transform))
                        .Append(':')
                        .Append(image.gameObject.activeSelf ? 'A' : '-')
                        .Append(image.gameObject.activeInHierarchy ? 'H' : '-')
                        .Append(image.enabled ? 'E' : '-')
                        .Append(':')
                        .Append(image.sprite != null ? image.sprite.name : "-")
                        .Append(':')
                        .Append(image.color.a.ToString("F2"));

                    shown++;
                    if (shown >= MaxDetailedImages)
                        break;
                }
            }

            return builder.ToString();
        }

        private static string BuildReport(string stage, int markerId, string markerKind, GameObject root, string source, List<Image> images, List<Graphic> graphics)
        {
            var builder = new StringBuilder();
            builder.Append("marker-visual: ")
                .Append(stage)
                .Append(" id=").Append(markerId)
                .Append(" kind=").Append(string.IsNullOrEmpty(markerKind) ? "-" : markerKind)
                .Append(" source=").Append(string.IsNullOrEmpty(source) ? "-" : source)
                .Append(" root=").Append(MapObjectNames.PathOf(root.transform))
                .Append(" rootActive=").Append(root.activeSelf).Append('/').Append(root.activeInHierarchy)
                .Append(" images=").Append(images != null ? images.Count : 0)
                .Append(" visibleImages=").Append(CountVisibleImages(images))
                .Append(" selfActiveImages=").Append(CountSelfActiveImages(images))
                .Append(" graphics=").Append(graphics != null ? graphics.Count : 0)
                .Append(" visibleGraphics=").Append(CountVisibleGraphics(graphics));

            if (images != null)
            {
                var shown = 0;
                foreach (var image in images)
                {
                    if (image == null)
                        continue;

                    var rect = image.rectTransform != null ? image.rectTransform.rect : default;
                    builder.AppendLine();
                    builder.Append("  image path=").Append(MapObjectNames.RelativePath(root.transform, image.transform))
                        .Append(" sibling=").Append(SiblingIndexOf(image.transform))
                        .Append(" active=").Append(image.gameObject.activeSelf).Append('/').Append(image.gameObject.activeInHierarchy)
                        .Append(" enabled=").Append(image.enabled)
                        .Append(" alpha=").Append(image.color.a.ToString("F2"))
                        .Append(" sprite=").Append(MapGraphicClassifier.SpriteNameOf(image))
                        .Append(" mat=").Append(MapGraphicClassifier.MaterialNameOf(image))
                        .Append(" rect=").Append(rect.width.ToString("F0")).Append('x').Append(rect.height.ToString("F0"));

                    shown++;
                    if (shown >= MaxDetailedImages)
                    {
                        builder.AppendLine();
                        builder.Append("  ... image details truncated");
                        break;
                    }
                }
            }

            return builder.ToString();
        }

        private static bool TryReserveStageEvent(string stage)
        {
            if (string.IsNullOrEmpty(stage))
                stage = "-";

            StageEventCounts.TryGetValue(stage, out var count);
            if (count >= MaxEventsPerStage)
            {
                if (count == MaxEventsPerStage)
                    Log.Info("marker-visual: stage=" + stage + " detail cap reached; suppressing further marker visual dumps");
                StageEventCounts[stage] = count + 1;
                return false;
            }

            StageEventCounts[stage] = count + 1;
            return true;
        }

        private static List<Image> CollectImages(GameObject root)
        {
            var images = new List<Image>();
            if (root == null)
                return images;

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image != null)
                    images.Add(image);
            }

            return images;
        }

        private static List<Graphic> CollectGraphics(GameObject root)
        {
            var graphics = new List<Graphic>();
            if (root == null)
                return graphics;

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic != null)
                    graphics.Add(graphic);
            }

            return graphics;
        }

        private static int CountVisibleImages(List<Image> images)
        {
            if (images == null)
                return 0;

            var count = 0;
            foreach (var image in images)
            {
                if (image != null && image.gameObject.activeInHierarchy && image.enabled && image.color.a > 0.01f)
                    count++;
            }

            return count;
        }


        private static int CountSelfActiveImages(List<Image> images)
        {
            if (images == null)
                return 0;

            var count = 0;
            foreach (var image in images)
            {
                if (image != null && image.gameObject.activeSelf && image.enabled && image.color.a > 0.01f)
                    count++;
            }

            return count;
        }

        private static int CountVisibleGraphics(List<Graphic> graphics)
        {
            if (graphics == null)
                return 0;

            var count = 0;
            foreach (var graphic in graphics)
            {
                if (graphic != null && graphic.gameObject.activeInHierarchy && graphic.enabled && graphic.color.a > 0.01f)
                    count++;
            }

            return count;
        }

        private static bool ContainsInterestingTerm(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.IndexOf("bee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("drone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("mouth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int SiblingIndexOf(Transform transform)
        {
            if (transform == null)
                return -1;

            try { return transform.GetSiblingIndex(); }
            catch { return -1; }
        }
#else
        public static void LogSource(MinimapSettings settings, RectTransform sourceRoot, int markerId, string markerKind, string source) { }
        public static void LogCloneStage(MinimapSettings settings, string stage, int markerId, string markerKind, GameObject root, string source) { }
        public static void LogRuntimeInstance(MinimapSettings settings, MarkerSnapshot marker, GameObject root, string source) { }
#endif
    }
}
