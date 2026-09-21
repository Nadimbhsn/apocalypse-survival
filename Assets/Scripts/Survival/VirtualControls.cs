using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// Horizontal on-screen joystick: press anywhere on the pad and drag left/right; the
    /// knob follows and MobileInput.TouchMoveX gets -1..1 (with a small dead zone). Tracks
    /// its own pointer id so a thumb on the jump button never steals it (multi-touch).
    ///
    /// The centre is wherever the thumb first lands, not the middle of the pad. With a
    /// fixed centre, going left meant physically dragging to the pad's left extreme, which
    /// on a phone sits in the strip the OS reserves for its own back/home swipes: the
    /// system stole the touch and the character stopped answering. Taking the press point
    /// as the origin means both directions are always an equal, short drag from wherever
    /// the thumb happens to be, and neither one ever needs to reach the screen edge.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob;
        public float deadZone = 0.12f;
        /// <summary>Thumb travel, in reference pixels, for a full deflection.</summary>
        public float travel = 115f;

        RectTransform rect;
        int activePointer = int.MinValue;
        float originX;

        void Awake() => rect = GetComponent<RectTransform>();

        /// <summary>How far the knob may slide from the pad's middle without leaving it.</summary>
        float KnobLimit => Mathf.Max(1f, rect.rect.width * 0.5f - (knob != null ? knob.rect.width * 0.5f : 0f));

        public void OnPointerDown(PointerEventData e)
        {
            if (activePointer != int.MinValue) return;
            if (!ToLocal(e, out var local)) return;
            activePointer = e.pointerId;
            originX = Mathf.Clamp(local.x, -KnobLimit, KnobLimit);
            MobileInput.TouchMoveX = 0f;
            if (knob != null) knob.anchoredPosition = new Vector2(originX, 0f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != activePointer) return;
            Apply(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != activePointer) return;
            Release();
        }

        void OnDisable() => Release();

        void Release()
        {
            activePointer = int.MinValue;
            MobileInput.TouchMoveX = 0f;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        bool ToLocal(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out local);

        void Apply(PointerEventData e)
        {
            // Drag events keep coming from the object that got the press even once the
            // thumb has left the pad, so a long drag still reads correctly.
            if (!ToLocal(e, out var local)) return;
            float x = Mathf.Clamp((local.x - originX) / Mathf.Max(1f, travel), -1f, 1f);
            MobileInput.TouchMoveX = Mathf.Abs(x) < deadZone ? 0f : x;
            if (knob != null)
                knob.anchoredPosition = new Vector2(Mathf.Clamp(originX + x * travel, -KnobLimit, KnobLimit), 0f);
        }
    }

    /// <summary>A button that reports press and release (for jump = variable height, fire = hold to keep shooting).</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action onDown, onUp;
        Image image;
        Color baseColor;
        bool held;

        void Awake()
        {
            image = GetComponent<Image>();
            if (image != null) baseColor = image.color;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (held) return;
            held = true;
            if (image != null) image.color = baseColor * 1.35f;
            onDown?.Invoke();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!held) return;
            held = false;
            if (image != null) image.color = baseColor;
            onUp?.Invoke();
        }

        void OnDisable()
        {
            if (!held) return;
            held = false;
            if (image != null) image.color = baseColor;
            onUp?.Invoke();
        }
    }
}
