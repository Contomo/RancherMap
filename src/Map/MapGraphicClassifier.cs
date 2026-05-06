using System;
using UnityEngine;
using UnityEngine.UI;

namespace rancher_minimap
{
    internal static class MapGraphicClassifier
    /// <summary>
    /// Classifying what is what by doing the most advanced technique known in programming
    /// that technique is called string comparison.
    /// </summary>
    {
        public static bool IsMapBackgroundGraphic(Graphic graphic)
        {
            if (graphic == null)
                return false;

            if (IsMapBackgroundAssetName(MaterialNameOf(graphic)) ||
                IsMapBackgroundAssetName(ShaderNameOf(graphic)))
                return true;

            var image = graphic as Image;
            return IsMapBackgroundAssetName(SpriteNameOf(image));
        }

        public static bool IsMapBackgroundTransform(Transform transform, Transform mapRoot)
        {
            if (transform == null)
                return false;

            var path = NormalizePath(MapObjectNames.PathOf(transform));
            if (IsNonMapBackgroundUiPath(path))
                return false;

            var graphic = transform.GetComponent<Graphic>();
            if (IsMapBackgroundGraphic(graphic))
                return true;

            var name = CleanAssetName(transform.name);
            if (IsMapBackgroundBranchName(name))
                return true;

            return IsDirectContentBackgroundShell(transform, mapRoot, name);
        }

        public static bool IsDecorativeCloudGraphic(Graphic graphic)
        {
            if (graphic == null)
                return false;

            return IsDecorativeCloudAssetName(MaterialNameOf(graphic)) ||
                   IsDecorativeCloudAssetName(ShaderNameOf(graphic)) ||
                   IsDecorativeCloudPath(MapObjectNames.PathOf(graphic.transform));
        }

        public static bool IsDecorativeCloudPath(string path)
        {
            var normalized = NormalizePath(path);
            return normalized.EndsWith("/clouds", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/content/clouds") ||
                   normalized.Contains("/map/content/clouds");
        }

        public static bool IsDefaultUiMaterial(Graphic graphic)
        {
            return string.Equals(MaterialNameOf(graphic), "Default UI Material", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFogGraphic(Graphic graphic)
        {
            if (graphic == null)
                return false;

            var path = NormalizePath(MapObjectNames.PathOf(graphic.transform));
            if (path.Contains("/zone_fog_areas/") || path.Contains("/fog_static/"))
                return true;

            var image = graphic as Image;
            if (SpriteNameOf(image).StartsWith("map_fog_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(MaterialNameOf(graphic), "FogOfWar", StringComparison.OrdinalIgnoreCase))
                return true;

            return HasParentComponentNamed(graphic.transform, "FogMapElement");
        }

        public static string MaterialNameOf(Graphic graphic)
        {
            return graphic != null && graphic.material != null ? CleanAssetName(graphic.material.name) : string.Empty;
        }

        public static string ShaderNameOf(Graphic graphic)
        {
            var material = graphic != null ? graphic.material : null;
            var shader = material != null ? material.shader : null;
            return shader != null ? CleanAssetName(shader.name) : string.Empty;
        }

        public static string SpriteNameOf(Image image)
        {
            return image != null && image.sprite != null ? CleanAssetName(image.sprite.name) : string.Empty;
        }

        public static string CleanAssetName(string name)
        {
            var clean = MapObjectNames.CleanCloneName(name ?? string.Empty)
                .Replace("(Instance)", string.Empty)
                .Trim();

            while (clean.EndsWith("_RMM", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(0, clean.Length - 4).Trim();

            return clean;
        }

        private static bool IsMapBackgroundAssetName(string name)
        {
            return ContainsToken(name, "OscillatingWaterUI") ||
                   ContainsToken(name, "LabyrinthCloudSeaUI") ||
                   ContainsToken(name, "ShorelineWaterMap") ||
                   string.Equals(CleanAssetName(name), "tilingBG", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMapBackgroundBranchName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return ContainsToken(name, "BackgroundOverlay") ||
                   ContainsToken(name, "LabyrinthCloudSea") ||
                   ContainsToken(name, "CloudSea") ||
                   ContainsToken(name, "SlimeSea") ||
                   ContainsToken(name, "RainbowSea") ||
                   ContainsToken(name, "ShorelineWaterMap") ||
                   ContainsToken(name, "OscillatingWater");
        }

        private static bool IsDirectContentBackgroundShell(Transform transform, Transform mapRoot, string cleanName)
        {
            if (transform == null || mapRoot == null)
                return false;

            if (!string.Equals(cleanName, "Background", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cleanName, "BackgroundOverlay", StringComparison.OrdinalIgnoreCase))
                return false;

            return transform == mapRoot || transform.parent == mapRoot;
        }

        private static bool IsNonMapBackgroundUiPath(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
                return false;

            return normalizedPath.Contains("/overlay ui/") ||
                   normalizedPath.Contains("/markercontainer") ||
                   normalizedPath.Contains("/markers/") ||
                   normalizedPath.Contains("/belowmarkerscontainer/") ||
                   normalizedPath.Contains("/abovemarkerscontainer/");
        }

        private static bool IsDecorativeCloudAssetName(string name)
        {
            return ContainsToken(name, "Vignette Clouds") ||
                   ContainsToken(name, "Map Vignette Clouds");
        }

        private static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(token) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/').ToLowerInvariant();
        }

        private static bool HasParentComponentNamed(Transform transform, string componentName)
        {
            var current = transform;
            var guard = 0;
            while (current != null && guard < 32)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    var typeName = component.GetType().FullName ?? component.GetType().Name;
                    if (typeName.IndexOf(componentName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                current = current.parent;
                guard++;
            }

            return false;
        }
    }
}
