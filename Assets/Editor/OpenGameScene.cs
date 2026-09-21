using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Platformer.EditorTools
{
    /// <summary>
    /// Opens the game scene when the editor starts on a blank one.
    ///
    /// Unity has no "default scene" project setting: it reopens whatever was last open,
    /// and that memory lives in Library/, which is not in the repository. So a fresh clone,
    /// a deleted Library or a reimport all land the editor on the empty untitled scene with
    /// the default blue sky, and the game looks like it has vanished.
    ///
    /// This only steps in when the active scene is one Unity made up on the spot, which is
    /// exactly that case: a scene that was actually opened from disk has an asset path and
    /// is left alone, and an untitled scene with unsaved changes in it is never discarded.
    /// It also runs once per editor session, so recompiling a script mid-work never yanks
    /// the scene out from under the person using it.
    /// </summary>
    [InitializeOnLoad]
    static class OpenGameScene
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string SessionKey = "apogee_game_scene_opened";

        static OpenGameScene()
        {
            // The scene manager is not ready while the domain is still loading.
            EditorApplication.delayCall += OpenIfEditorLandedOnABlankScene;
        }

        static void OpenIfEditorLandedOnABlankScene()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) return;

            var active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path)) return; // a real scene is already open
            if (active.isDirty) return;                     // someone is building something here

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[Apogée] Scène du jeu ouverte automatiquement (l'éditeur avait démarré sur une scène vide).");
        }

        [MenuItem("Apogée/Ouvrir la scène du jeu")]
        static void OpenNow()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"[Apogée] Scène introuvable : {ScenePath}");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }
}
