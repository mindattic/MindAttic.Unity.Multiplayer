// Plain coloured squares, made in code, so the template needs no art, no materials and works in any render pipeline.
// Replace them with your own sprites or models.
using UnityEngine;

namespace MyGame
{
    internal static class Shapes
    {
        private static Sprite s_square;

        /// <summary>Gives <paramref name="owner"/> a SpriteRenderer drawing a square <paramref name="size"/> units wide.</summary>
        public static SpriteRenderer Square(GameObject owner, float size, int sortingOrder)
        {
            if (s_square == null)
            {
                Texture2D white = Texture2D.whiteTexture;
                s_square = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            }

            SpriteRenderer renderer = owner.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = owner.AddComponent<SpriteRenderer>();
            }

            renderer.sprite = s_square;
            renderer.sortingOrder = sortingOrder;
            owner.transform.localScale = new Vector3(size, size, 1f);
            return renderer;
        }
    }
}
