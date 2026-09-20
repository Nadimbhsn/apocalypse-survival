using UnityEngine;
using Platformer.Mechanics;
using Platformer.Model;
using Platformer.Core;

namespace Platformer.Survival
{
    /// <summary>
    /// Auto-wires the survival game mode onto the existing 2D Platformer sample scene at
    /// load time, without editing any scene or prefab file: disables the sample's own win
    /// condition, patrolling enemy, hand-painted level geometry and camera confiner
    /// (SetActive/enabled = false, never destroyed - fully reversible), attaches combat to
    /// the existing Player, and spins up the runtime-built SurvivalDirector, the hub UI and
    /// the extra mini-games (Fusion, Arena).
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var model = Simulation.GetModel<PlatformerModel>();
            var player = model.player;
            if (player == null)
                player = Object.FindAnyObjectByType<PlayerController>();
            if (player == null) return; // not the gameplay scene

            DisableSampleContent();
            SetupAtmosphere();

            if (player.GetComponent<PlayerCombat>() == null)
                player.gameObject.AddComponent<PlayerCombat>();
            if (player.GetComponent<PlayerDamageFeedback>() == null)
                player.gameObject.AddComponent<PlayerDamageFeedback>();
            if (player.GetComponent<PlayerJuice>() == null)
                player.gameObject.AddComponent<PlayerJuice>();
            AdService.Ensure();

            var directorGo = new GameObject("SurvivalDirector");
            var director = directorGo.AddComponent<SurvivalDirector>();

            var uiGo = new GameObject("RuntimeUI");
            var ui = uiGo.AddComponent<RuntimeUI>();

            // Self-contained mini-games launched from the hub (the runner is the scene itself).
            var fusion = new GameObject("FusionGame").AddComponent<FusionGame>();
            var arena = new GameObject("ArenaGame").AddComponent<ArenaGame>();
            var barricade = new GameObject("BarricadeGame").AddComponent<BarricadeGame>();

            director.Configure(player, ui);
            ui.Init(director, new MiniGame[] { fusion, arena, barricade });
        }

        static void DisableSampleContent()
        {
            foreach (var victoryZone in Object.FindObjectsByType<VictoryZone>(FindObjectsInactive.Exclude))
                victoryZone.gameObject.SetActive(false);

            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Exclude))
                enemy.gameObject.SetActive(false);

            foreach (var confiner in Object.FindObjectsByType<Unity.Cinemachine.CinemachineConfiner2D>(FindObjectsInactive.Exclude))
                confiner.enabled = false;

            foreach (var token in Object.FindObjectsByType<TokenInstance>(FindObjectsInactive.Exclude))
                token.gameObject.SetActive(false);

            var level = GameObject.Find("Level");
            if (level != null) level.SetActive(false);

            var grid = GameObject.Find("Grid");
            if (grid != null) grid.SetActive(false);
        }

        static void SetupAtmosphere()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = PlaceholderVisuals.SkyColor;
            }
        }
    }
}
