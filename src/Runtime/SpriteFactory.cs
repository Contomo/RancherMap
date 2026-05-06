using UnityEngine;

namespace rancher_minimap
{
    internal static class SpriteFactory
    {
        public static Sprite SolidSprite(Color color, int size = 16)
        {
            var texture = CreateTexture(size, "RancherMinimapSolid");
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return CreateCenteredSprite(texture, size);
        }

        public static Sprite DiscSprite(Color color, int size = 32)
        {
            var texture = CreateTexture(size, "RancherMinimapDisc");
            WriteDisc(texture, color, ringWidth: 0f);
            texture.Apply(false, false);
            return CreateCenteredSprite(texture, size);
        }

        public static Sprite RingSprite(Color color, int size = 48)
        {
            var texture = CreateTexture(size, "RancherMinimapRing");
            WriteDisc(texture, color, ringWidth: 3.5f);
            texture.Apply(false, false);
            return CreateCenteredSprite(texture, size);
        }

        public static Sprite CircleMaskSprite(int size = 1024)
        {
            var texture = CreateTexture(size, "RancherMinimapCircleMask");
            WriteDisc(texture, Color.white, ringWidth: 0f);
            texture.Apply(false, false);
            return CreateCenteredSprite(texture, size);
        }

        public static Sprite MinimapGridSprite(int size = 512)
        {
            var texture = CreateTexture(size, "RancherMinimapFallbackGrid");
            var background = new Color(0.045f, 0.075f, 0.07f, 0.92f);
            var major = new Color(0.20f, 0.52f, 0.45f, 0.60f);
            var minor = new Color(0.14f, 0.32f, 0.30f, 0.35f);
            var edge = new Color(0.48f, 0.78f, 0.62f, 0.75f);
            var axis = new Color(0.78f, 0.88f, 0.62f, 0.62f);
            var center = size / 2;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var color = background;
                    var border = x < 3 || y < 3 || x >= size - 3 || y >= size - 3;
                    var axisLine = Mathf.Abs(x - center) <= 1 || Mathf.Abs(y - center) <= 1;
                    var majorLine = x % 128 < 2 || y % 128 < 2;
                    var minorLine = x % 64 < 1 || y % 64 < 1;

                    if (minorLine) color = Color.Lerp(color, minor, minor.a);
                    if (majorLine) color = Color.Lerp(color, major, major.a);
                    if (axisLine) color = Color.Lerp(color, axis, axis.a);
                    if (border) color = edge;

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply(false, false);
            return CreateCenteredSprite(texture, size);
        }

        private static Texture2D CreateTexture(int size, string name)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Sprite CreateCenteredSprite(Texture2D texture, int size)
        {
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static void WriteDisc(Texture2D texture, Color color, float ringWidth)
        {
            var size = texture.width;
            var center = (size - 1) * 0.5f;
            var outer = center - 1f;
            var inner = ringWidth > 0f ? outer - ringWidth : -1f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var outerAlpha = Mathf.Clamp01(outer + 0.75f - distance);
                    var innerAlpha = inner >= 0f ? Mathf.Clamp01(distance - inner + 0.75f) : 1f;
                    var alpha = color.a * outerAlpha * innerAlpha;
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
                }
            }
        }
    }
}
