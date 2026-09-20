using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// Keeps the UI proportionate in both orientations. The screens are laid out by screen
    /// fraction, so they adapt on their own, but the CanvasScaler needs a reference
    /// resolution with the right orientation: 1080x1920 on a phone held upright, 1920x1080
    /// in a browser window or a tablet in landscape. Without this, text and buttons come out
    /// half-size in landscape.
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public class CanvasOrientation : MonoBehaviour
    {
        static readonly Vector2 Portrait = new Vector2(1080f, 1920f);
        static readonly Vector2 Landscape = new Vector2(1920f, 1080f);

        CanvasScaler scaler;
        bool lastWasLandscape;
        bool applied;

        void Awake() => scaler = GetComponent<CanvasScaler>();

        void Update()
        {
            bool landscape = Screen.width >= Screen.height;
            if (applied && landscape == lastWasLandscape) return;
            applied = true;
            lastWasLandscape = landscape;
            scaler.referenceResolution = landscape ? Landscape : Portrait;
        }
    }
}
