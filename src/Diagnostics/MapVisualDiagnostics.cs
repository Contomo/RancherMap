using System.Text;
using Il2CppMonomiPark.SlimeRancher.Map;
using Sr2MapUI = Il2CppMonomiPark.SlimeRancher.UI.Map.MapUI;
using UnityEngine.UI;

namespace rancher_minimap
{
    internal static class MapVisualDiagnostics
    {

        public static void LogImageInventory(MinimapSettings settings, Sr2MapUI mapUi, string source)
        {
            if (!IsEnabled(settings) || mapUi == null)
                return;

            var images = mapUi.GetComponentsInChildren<Image>(true);
            var builder = new StringBuilder();
            builder.AppendLine($"map-capture: image inventory from {source}: count={images.Length}");

            var shown = 0;
            foreach (var image in images)
            {
                if (image == null)
                    continue;

                var sprite = image.sprite;
                var material = image.material;
                var rect = image.rectTransform != null ? image.rectTransform.rect : default;
                if (sprite == null && material == null && shown >= 12)
                    continue;

                builder.AppendLine($"  image path={MapObjectNames.PathOf(image.transform)} active={image.gameObject.activeInHierarchy} rect={rect.width:F0}x{rect.height:F0} sprite={(sprite != null ? sprite.name : "-")} mat={(material != null ? material.name : "-")}");
                shown++;
                if (shown >= 30)
                    break;
            }

            Log.Info(builder.ToString().TrimEnd());
        }
        private static bool IsEnabled(MinimapSettings settings)
        {
            return settings != null && settings.DiagnosticsEnabled;
        }

        public static string DescribePortalLineNative(PortalLineGraphic line, Graphic graphic)
        {
            if (line == null)
                return "-";

            var rect = graphic != null && graphic.rectTransform != null ? graphic.rectTransform.rect : default;
            return MapObjectNames.PathOf(line.transform) +
                   " enabled=" + (graphic != null && graphic.enabled) +
                   " active=" + line.gameObject.activeInHierarchy +
                   " alpha=" + (graphic != null ? graphic.color.a.ToString("F2") : "-") +
                   " rect=" + rect.width.ToString("F0") + "x" + rect.height.ToString("F0");
        }
    }
}
