using Platformer.Core;
using Platformer.Mechanics;
using Platformer.Model;
using static Platformer.Core.Simulation;

namespace Platformer.Gameplay
{
    /// <summary>
    /// Fired when some entity's health reaches 0. Only results in a PlayerDeath event
    /// when the entity in question is actually the player - Health is a generic
    /// component also used by non-player entities (e.g. survival-mode zombies), and
    /// their deaths must not be mistaken for the player's.
    /// </summary>
    /// <typeparam name="HealthIsZero"></typeparam>
    public class HealthIsZero : Simulation.Event<HealthIsZero>
    {
        public Health health;
        PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        public override void Execute()
        {
            if (health == model.player.health)
                Schedule<PlayerDeath>();
        }
    }
}