using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppMonomiPark.SlimeRancher.Map;
using UnityEngine;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    internal static class MapObjectNames
    {
        public static string CleanCloneName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "-";

            return value
                .Replace("(Clone)", "")
                .Replace("VanillaMapVisualClone_FromCapturedTemplate", "")
                .Replace("VanillaMapVisualClone_FromMapPrefabMapping", "")
                .Trim('/');
        }

        public static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "UnknownMap";

            var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            return new string(chars);
        }

        public static string MapKey(MapDefinition mapDefinition)
        {
            if (mapDefinition == null)
                return "-";

            try
            {
                if (!string.IsNullOrEmpty(mapDefinition.name))
                    return mapDefinition.name;
            }
            catch
            {
            }

            return mapDefinition.GetType().Name;
        }

        public static string DescribeUnityObject(Object obj)
        {
            if (obj == null)
                return "-";

            try
            {
                return obj.name;
            }
            catch
            {
                return obj.GetType().Name;
            }
        }

        public static string FormatRect(Rect rect)
        {
            return rect.ToString();
        }


        public static string DescribeGeometry(MapGeometry geometry)
        {
            return "world=" + (geometry.HasWorldArea ? FormatRect(geometry.WorldArea) : "-") +
                   " mapRect=" + (geometry.HasMapArea ? FormatRect(geometry.MapArea) : "-") +
                   " projectionMap=" + (geometry.HasProjectionMapArea ? FormatRect(geometry.ProjectionMapArea) : "-");
        }

        public static string PathOf(Transform transform)
        {
            if (transform == null)
                return "-";

            var parts = new List<string>();
            var current = transform;
            while (current != null && parts.Count < 64)
            {
                parts.Add(string.IsNullOrEmpty(current.name) ? "<unnamed>" : current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        public static string RelativePath(Transform root, Transform transform)
        {
            if (root == null || transform == null)
                return string.Empty;

            var parts = new List<string>();
            var current = transform;
            while (current != null && current != root && parts.Count < 64)
            {
                parts.Add(CleanCloneName(current.name));
                current = current.parent;
            }

            if (current != root)
                return string.Empty;

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
