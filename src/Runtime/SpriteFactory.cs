using UnityEngine;

namespace rancher_minimap
{
    internal static class SpriteFactory
    {
        public static Sprite SolidSprite(Color color, int size = 16)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            texture.name = "RancherMinimapSolid";
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        public static Sprite DiscSprite(Color color, int size = 32)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = (size - 1) * 0.5f;
            var radius = center - 1f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(radius + 0.75f - d);
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * a));
                }
            }

            texture.Apply(false, false);
            texture.name = "RancherMinimapDisc";
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        public static Sprite RingSprite(Color color, int size = 48)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = (size - 1) * 0.5f;
            var outer = center - 1f;
            var inner = outer - 3.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var outerA = Mathf.Clamp01(outer + 0.75f - d);
                    var innerA = Mathf.Clamp01(d - inner + 0.75f);
                    var a = outerA * innerA;
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * a));
                }
            }

            texture.Apply(false, false);
            texture.name = "RancherMinimapRing";
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        public static Sprite MinimapGridSprite(int size = 512)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
            texture.name = "RancherMinimapFallbackGrid";
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

    }
}
