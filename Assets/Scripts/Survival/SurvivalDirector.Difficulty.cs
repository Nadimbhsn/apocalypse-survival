using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// How hard the runner's activities are, from how far the run has gone. Every set piece
    /// (the tower, the free fall, the flight, the archipelago, the storm, La Cadence, the
    /// Rider track) reads one level between 0 and 1, fixed when the piece is built, and
    /// turns it into its own numbers. The level climbs from the first set piece on and stops
    /// at a ceiling: past it, the pieces stay as hard as they get - demanding, never
    /// impossible. The player sees it as "NIVEAU 1" to "NIVEAU 5" when a piece begins.
    /// </summary>
    public partial class SurvivalDirector
    {
        /// <summary>Distance at which the activities start getting harder, and where they stop.</summary>
        const float ActivityRampStart = 150f, ActivityRampEnd = 1500f;
        const int ActivityTiers = 5;

        /// <summary>0 for the first pieces, 1 (the cap) from ActivityRampEnd on. A level's own harshness in the campaign.</summary>
        float ActivityLevel => inCampaign
            ? level.Harshness
            : Mathf.Clamp01((Distance - ActivityRampStart) / (ActivityRampEnd - ActivityRampStart));

        /// <summary>The level as shown to the player, 1 to 5.</summary>
        static int TierOf(float activityLevel) => 1 + Mathf.Min(ActivityTiers - 1, Mathf.FloorToInt(activityLevel * ActivityTiers));

        static bool IsSetPiece(ZoneKind kind) => kind == ZoneKind.Ascent || kind == ZoneKind.Shaft
            || kind == ZoneKind.Jetpack || kind == ZoneKind.Archipel || kind == ZoneKind.Storm
            || kind == ZoneKind.Cadence || kind == ZoneKind.Rider;
    }
}
