using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace rancher_minimap
{
    /// <summary>
    /// Captures the visual-only state of a cloned marker subtree so SR2 marker scripts can be
    /// stripped without losing the already-resolved sprite/layer state chosen by vanilla MapUI.
    /// </summary>
    internal sealed class MarkerVisualStateSnapshot
    {
        private readonly List<ObjectState> _objects = new List<ObjectState>();
        private readonly List<GraphicState> _graphics = new List<GraphicState>();
        private readonly List<ImageState> _images = new List<ImageState>();

        private MarkerVisualStateSnapshot()
        {
        }

        public static MarkerVisualStateSnapshot Capture(GameObject root)
        {
            var snapshot = new MarkerVisualStateSnapshot();
            if (root == null)
                return snapshot;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform != null && transform.gameObject != null)
                    snapshot._objects.Add(new ObjectState(transform.gameObject, transform.gameObject.activeSelf));
            }

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                snapshot._graphics.Add(new GraphicState(
                    graphic,
                    graphic.enabled,
                    graphic.color,
                    graphic.material));
            }

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null)
                    continue;

                snapshot._images.Add(new ImageState(
                    image,
                    image.sprite,
                    image.overrideSprite,
                    image.type,
                    image.preserveAspect,
                    image.fillCenter,
                    image.fillAmount,
                    image.fillClockwise,
                    image.fillOrigin));
            }

            return snapshot;
        }

        public void Restore(GameObject rootToKeepInactive)
        {
            foreach (var state in _objects)
            {
                if (state.GameObject != null)
                    state.GameObject.SetActive(state.ActiveSelf);
            }

            foreach (var state in _graphics)
            {
                if (state.Graphic == null)
                    continue;

                state.Graphic.enabled = state.Enabled;
                state.Graphic.color = state.Color;
                state.Graphic.material = state.Material;
            }

            foreach (var state in _images)
            {
                if (state.Image == null)
                    continue;

                state.Image.sprite = state.Sprite;
                state.Image.overrideSprite = state.OverrideSprite;
                state.Image.type = state.Type;
                state.Image.preserveAspect = state.PreserveAspect;
                state.Image.fillCenter = state.FillCenter;
                state.Image.fillAmount = state.FillAmount;
                state.Image.fillClockwise = state.FillClockwise;
                state.Image.fillOrigin = state.FillOrigin;
            }

            if (rootToKeepInactive != null)
                rootToKeepInactive.SetActive(false);
        }

        private readonly struct ObjectState
        {
            public readonly GameObject GameObject;
            public readonly bool ActiveSelf;

            public ObjectState(GameObject gameObject, bool activeSelf)
            {
                GameObject = gameObject;
                ActiveSelf = activeSelf;
            }
        }

        private readonly struct GraphicState
        {
            public readonly Graphic Graphic;
            public readonly bool Enabled;
            public readonly Color Color;
            public readonly Material Material;

            public GraphicState(Graphic graphic, bool enabled, Color color, Material material)
            {
                Graphic = graphic;
                Enabled = enabled;
                Color = color;
                Material = material;
            }
        }

        private readonly struct ImageState
        {
            public readonly Image Image;
            public readonly Sprite Sprite;
            public readonly Sprite OverrideSprite;
            public readonly Image.Type Type;
            public readonly bool PreserveAspect;
            public readonly bool FillCenter;
            public readonly float FillAmount;
            public readonly bool FillClockwise;
            public readonly int FillOrigin;

            public ImageState(
                Image image,
                Sprite sprite,
                Sprite overrideSprite,
                Image.Type type,
                bool preserveAspect,
                bool fillCenter,
                float fillAmount,
                bool fillClockwise,
                int fillOrigin)
            {
                Image = image;
                Sprite = sprite;
                OverrideSprite = overrideSprite;
                Type = type;
                PreserveAspect = preserveAspect;
                FillCenter = fillCenter;
                FillAmount = fillAmount;
                FillClockwise = fillClockwise;
                FillOrigin = fillOrigin;
            }
        }
    }
}
