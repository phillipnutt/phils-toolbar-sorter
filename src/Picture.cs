using System;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace PhilsToolbarSorter
{
    // A copy of a toolbar button's picture for a row of the window, kept in step each frame.
    // Most buttons are one texture, which the copy takes every frame (mods swap it to show
    // on and off). KSP's own apps (Contracts, Messages...) make that texture see-through and
    // draw an animated sprite over it instead (ApplicationLauncherButton.SetSprite: a
    // SpriteRenderer driven by an Animator); the row shows the frame that sprite is on.
    // Anything else drawn in the picture (Messages' count) is copied as it is; no scripts.
    public class Picture
    {
        RectTransform holder;
        RawImage copy;
        Image frame;
        SpriteRenderer animated;
        CanvasGroup fade;
        ApplicationLauncherButton source;

        public static Picture Of(ApplicationLauncherButton b, RectTransform tile)
        {
            var p = new Picture { source = b };
            p.holder = Ui.Node(tile, "Picture").Stretch();
            p.fade = p.holder.gameObject.AddComponent<CanvasGroup>();
            p.fade.blocksRaycasts = false;
            p.fade.interactable = false;
            if (b == null || b.sprite == null) return p;
            try
            {
                // The animated sprite, if any, underneath; the copy (count text and all) on top.
                p.animated = b.sprite.GetComponentInChildren<SpriteRenderer>(true);
                if (p.animated != null)
                {
                    p.frame = Ui.Node(p.holder, "Frame").Stretch().gameObject.AddComponent<Image>();
                    p.frame.preserveAspect = true;
                    p.frame.raycastTarget = false;
                }

                // Copied under a switched-off holder, so nothing on it starts up before it's trimmed.
                p.holder.gameObject.SetActive(false);
                GameObject c = UnityEngine.Object.Instantiate(b.sprite.gameObject, p.holder, false);
                c.name = "Copy";
                Trim(c);
                var rt = (RectTransform)c.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                foreach (Graphic g in c.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                p.copy = c.GetComponent<RawImage>();
                c.SetActive(true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhilsToolbarSorter] Couldn't copy a button's picture: {e.Message}");
            }
            p.holder.gameObject.SetActive(true);
            p.Refresh(1f);
            return p;
        }

        // Keep only UI pictures and layout (the animated sprite is shown by Frame instead).
        static void Trim(GameObject c)
        {
            foreach (Component k in c.GetComponentsInChildren<Component>(true))
            {
                if (k == null || k is Transform || k is CanvasRenderer || k is Graphic || k is LayoutElement) continue;
                try { UnityEngine.Object.DestroyImmediate(k); }
                catch { }
            }
        }

        // The sprite the row shows for an animated button (for the in-game check).
        public Sprite Frame => frame != null && frame.enabled ? frame.sprite : null;

        public void Refresh(float alpha)
        {
            if (fade != null) fade.alpha = alpha;
            if (source == null || source.sprite == null) return;
            if (copy != null)
            {
                RawImage s = source.sprite;
                if (copy.texture != s.texture) copy.texture = s.texture;
                copy.uvRect = s.uvRect;
                copy.color = s.color;
            }
            if (frame != null)
            {
                bool show = animated != null && animated.enabled && animated.sprite != null;
                frame.enabled = show;
                if (show)
                {
                    if (frame.sprite != animated.sprite) frame.sprite = animated.sprite;
                    frame.color = animated.color;
                }
            }
        }
    }
}
