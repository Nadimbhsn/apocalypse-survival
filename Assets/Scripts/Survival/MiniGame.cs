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
        /// <summary>False keeps the game out of the home screen (it can still be launched from code).</summary>
        public virtual bool ShowOnHub => true;

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
            ui.ClosePause();
            IsActive = false;
            OnExit();
            ReleaseCamera();
            if (panel != null) panel.SetActive(false);
        }

        /// <summary>
        /// Called when an upgrade is bought from the pause menu mid-game, so the game can
        /// apply it at once where that makes sense (most stats are read live anyway).
        /// </summary>
        public virtual void OnUpgradeBought(UpgradeStat stat) { }

        /// <summary>Freezes the game under the shared pause menu; quitting from it returns to the hub.</summary>
        protected void OpenPause() => ui.Pause(ReturnToHub, OnUpgradeBought);

        protected abstract void OnEnter();
        protected abstract void OnExit();

        /// <summary>Back to the hub - the standard way a mini-game ends or is quit.</summary>
        protected void ReturnToHub()
        {
            Exit();
            ui.ShowHub();
        }

        /// <summary>
        /// Points the main camera at this mini-game's world. orthoSize frames it by height;
        /// minVisibleWidth (world units) additionally zooms out on narrow screens, so a tall
        /// phone never cuts the playfield off at the sides.
        /// </summary>
        protected void TakeOverCamera(Vector3 position, float orthoSize, Color background, float minVisibleWidth = 0f)
        {
            if (cam == null) return;
            if (minVisibleWidth > 0f && cam.aspect > 0.01f)
                orthoSize = Mathf.Max(orthoSize, minVisibleWidth / (2f * cam.aspect));
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
