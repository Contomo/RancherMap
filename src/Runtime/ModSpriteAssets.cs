using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    /// <summary>
    /// Mod-owned sprite assets that must be available before SR2 has loaded the matching vanilla UI scenes.
    ///
    /// iconCategoryWorld is embedded as raw RGBA32 because SR2's exposed Unity API supports
    /// Texture2D.LoadRawTextureData, while runtime PNG decoding is not exposed.
    /// </summary>
    internal static class ModSpriteAssets
    {
        private const int IconCategoryWorldWidth = 504;
        private const int IconCategoryWorldHeight = 504;
        private const int RgbaBytesPerPixel = 4;
        private const string IconCategoryWorldResource = "rancher_minimap.assets.iconCategoryWorld.rgba";

        public static Sprite CreateIconCategoryWorld(ICollection<Object> ownedObjects)
        {
            var data = ReadResourceBytes(IconCategoryWorldResource);
            var expectedLength = IconCategoryWorldWidth * IconCategoryWorldHeight * RgbaBytesPerPixel;
            if (data == null || data.Length != expectedLength)
            {
                Log.Error($"options: embedded iconCategoryWorld resource has invalid length={(data == null ? -1 : data.Length)} expected={expectedLength}");
                return null;
            }

            var texture = new Texture2D(IconCategoryWorldWidth, IconCategoryWorldHeight, TextureFormat.RGBA32, false);
            texture.name = "RancherMinimap_IconCategoryWorld_Texture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.LoadRawTextureData(data);
            texture.Apply(false, true);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, IconCategoryWorldWidth, IconCategoryWorldHeight),
                new Vector2(0.5f, 0.5f),
                100f);

            sprite.name = "iconCategoryWorld";
            ownedObjects?.Add(texture);
            ownedObjects?.Add(sprite);
            return sprite;
        }

        private static byte[] ReadResourceBytes(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Log.Error("options: embedded resource missing " + resourceName);
                return null;
            }

            if (stream.Length > int.MaxValue)
            {
                Log.Error("options: embedded resource too large " + resourceName);
                return null;
            }

            var data = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < data.Length)
            {
                var read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0)
                    break;

                offset += read;
            }

            if (offset != data.Length)
            {
                Log.Error($"options: embedded resource truncated {resourceName} read={offset} expected={data.Length}");
                return null;
            }

            return data;
        }
    }
}
