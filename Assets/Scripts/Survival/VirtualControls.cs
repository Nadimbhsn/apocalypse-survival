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
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob;
        public float deadZone = 0.12f;

        RectTransform rect;
        int activePointer = int.MinValue;

        void Awake() => rect = GetComponent<RectTransform>();

        public void OnPointerDown(PointerEventData e)
        {
            if (activePointer != int.MinValue) return;
            activePointer = e.pointerId;
            Apply(e);
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

        void Apply(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local)) return;
            float radius = rect.rect.width * 0.5f - (knob != null ? knob.rect.width * 0.5f : 0f);
            float x = Mathf.Clamp(local.x / Mathf.Max(1f, radius), -1f, 1f);
            if (knob != null) knob.anchoredPosition = new Vector2(x * radius, 0f);
            MobileInput.TouchMoveX = Mathf.Abs(x) < deadZone ? 0f : x;
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
