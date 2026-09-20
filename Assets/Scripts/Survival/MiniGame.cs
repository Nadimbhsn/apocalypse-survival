using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Base class for the self-contained mini-games launched from the hub (everything
    /// except the runner, which is the scene's native game and driven by SurvivalDirector).
    /// A mini-game builds its own UI panel into the shared canvas once, then Enter()/Exit()
    /// toggle it. World-space mini-games can borrow the main camera with TakeOverCamera,
    /// which parks Cinemachine so the runner's follow logic doesn't fight them.
    /// </summary>
    public abstract class MiniGame : MonoBehaviour
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }
        /// <summary>Short "best score" line shown on the hub card, or null.</summary>
        public virtual string BestLine => null;

        protected RuntimeUI ui;
        protected GameObject panel;
        protected Camera cam;
        Behaviour cinemachineBrain;
        float savedOrthoSize;
        Color savedBackground;

        public bool IsActive { get; private set; }

        public void Setup(RuntimeUI runtimeUi)
        {
            ui = runtimeUi;
            cam = Camera.main;
            if (cam != null) cinemachineBrain = cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            BuildUi();
            if (panel != null) panel.SetActive(false);
        }

        protected abstract void BuildUi();

        public void Enter()
        {
            IsActive = true;
            Time.timeScale = 1f;
            if (panel != null) panel.SetActive(true);
            OnEnter();
        }

        public void Exit()
        {
            if (!IsActive) return;
            IsActive = false;
            OnExit();
            ReleaseCamera();
            if (panel != null) panel.SetActive(false);
        }

        protected abstract void OnEnter();
        protected abstract void OnExit();

        /// <summary>Back to the hub - the standard way a mini-game ends or is quit.</summary>
        protected void ReturnToHub()
        {
            Exit();
            ui.ShowHub();
        }

        protected void TakeOverCamera(Vector3 position, float orthoSize, Color background)
        {
            if (cam == null) return;
            if (cinemachineBrain != null && cinemachineBrain.enabled)
            {
                savedOrthoSize = cam.orthographicSize;
                savedBackground = cam.backgroundColor;
                cinemachineBrain.enabled = false;
            }
            cam.transform.position = position;
            cam.orthographicSize = orthoSize;
            cam.backgroundColor = background;
        }

        protected void ReleaseCamera()
        {
            if (cam == null || cinemachineBrain == null || cinemachineBrain.enabled) return;
            cam.orthographicSize = savedOrthoSize;
            cam.backgroundColor = savedBackground;
            cinemachineBrain.enabled = true;
        }
    }
}
