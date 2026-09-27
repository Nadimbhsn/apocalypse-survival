namespace Platformer.Survival
{
    /// <summary>One La Cadence level: a name, its difficulty (1 to 5) and its script.</summary>
    public class CadenceMap
    {
        public string Name;
        public int Level;
        public string Script;
    }

    /// <summary>
    /// The La Cadence levels, one per difficulty level of the runner's activities: the
    /// further the run has gone, the harder the level it meets (see ActivityLevel). Each is
    /// about ninety beats - forty seconds of music - packed tight.
    ///
    /// A level is written as a script, one command per line, in beats of the 140 BPM track
    /// (the run speed is constant between speed portals, so a beat is a place):
    ///   start V            run speed at the gate (default 6.4 m/s)
    ///   speed B V          speed portal
    ///   pit A B            hole in the floor
    ///   ceil A B H         ceiling whose underside is H m up
    ///   spike B [N] [H]    N spikes on the floor (or on a ledge H high)
    ///   cspike B [N] [H]   N spikes hanging from a ceiling at H (default 4.6)
    ///   strip A B H / cstrip A B H   a bed of spikes, standing / hanging
    ///   plat A B TOP       block to run on
    ///   fpillar B TOP / cpillar B BOTTOM   pillars up from the floor / down from the ship ceiling
    ///   pad B, orb B H     yellow pad, jump orb
    ///   flip B, normal B   gravity portals; ship B, cube B   flight portals
    ///   check B            checkpoint (always on the floor, gravity down, not flying)
    ///   coin I B H         secret coin I (0 to 2); coins B   a row of ordinary coins
    ///   medkit B, arp B    a heart; where the music gains its arpeggio
    ///   end B              the finish gate
    ///
    /// Every script is checked by a bot that plays it with the game's physics: each level
    /// is finishable from the start and from every checkpoint, and every secret coin can
    /// be taken on a line that still finishes.
    /// </summary>
    public static class CadenceMaps
    {
        public static readonly CadenceMap[] All =
        {
            new CadenceMap
            {
                Name = "PREMIER SOUFFLE", Level = 1, Script = @"
# jumps, singles to triples, and a pit
spike 4
coins 4
spike 6
spike 9 2
spike 12
spike 14
spike 17 3
coins 17.3
pit 20 21.25
# blocks, stairs and a pad over a pit
plat 23 25 1
coin 0 25.8 3.3
spike 28
coins 28
plat 30 32 1
plat 32 34 2
coin 1 35 3.9
spike 36.5
pad 39.3
pit 39.7 41.8
check 44
# orbs over a bed of spikes, then block hops
arp 46
strip 46.5 53 0
orb 46.5 2.3
orb 48.5 2.3
orb 50.5 2.3
orb 52.5 2.3
spike 56
coins 56
plat 58.2 59.0 1
spike 59.6
plat 60.2 61.0 1
spike 61.4
spike 64.5 2
check 68
# finale: a pad into three orbs over the widest pit
spike 70
coins 70
pad 71.3
pit 71.7 78.9
orb 72.68 4.05
orb 74.68 4.05
orb 76.68 4.05
coin 2 75.68 5.8
spike 82.5
spike 85 2
spike 88
end 92
",
            },
            new CadenceMap
            {
                Name = "CONTRETEMPS", Level = 2, Script = @"
# quick jumps
spike 3
spike 5
spike 7 2
coins 7
plat 10 12 1
coin 0 12.8 3.3
spike 15
spike 17
spike 19 3
coins 19.3
pit 22 23.25
check 26
# upside down: walk the ceiling, jump down past its spikes
ceil 28 43.9 4.6
ceil 45.1 52 4.6
flip 29
strip 31 48 0
cspike 33
cspike 36
cspike 38.5 2
coin 1 41.5 2.25
cspike 48
normal 50
check 55
# faster: the same moves, a third quicker
arp 56
speed 58 8.3
spike 61
coins 61
spike 63 2
spike 65
plat 67.5 70.5 1
spike 69 1 1
pad 73
pit 73.3 75.4
strip 76.5 79 0
orb 77 2.3
coin 2 78 4.2
spike 82 3
coins 82.3
pad 84
fpillar 85 2.0
spike 88.5
end 91
",
            },
            new CadenceMap
            {
                Name = "ENVOL", Level = 3, Script = @"
# a short run-up
spike 4
coins 4
spike 6 2
spike 9
spike 11
spike 14 3
plat 17 19 1
coin 0 19.8 3.3
check 22
# the ship: hold to climb, release to dive between the pillars
ceil 23.5 58 6.5
ship 24
strip 26 56 0
cstrip 26 56 6.5
fpillar 29 3.4
cpillar 32 3.0
fpillar 35 3.4
cpillar 38 3.0
fpillar 41.5 2.0
cpillar 41.5 4.6
fpillar 44.5 3.8
cpillar 47 2.4
fpillar 49.5 1.8
cpillar 49.5 4.3
coin 1 51.5 5.3
cpillar 54.5 2.6
cube 58
check 62
medkit 62
# and upside down
arp 62
ceil 63 78.9 4.6
ceil 80.1 87 4.6
flip 64
strip 66 83 0
cspike 68
cspike 71
cspike 73.5 2
coin 2 76.5 2.25
cspike 83
normal 85
spike 89.5
end 93
",
            },
            new CadenceMap
            {
                Name = "PULSAR", Level = 4, Script = @"
start 8.3
# fast from the first beat
spike 4
coins 4
spike 6
spike 8 2
spike 11
spike 13
spike 15 3
pit 18 19.25
plat 21 23 1
coin 0 23.8 3.3
spike 25.5
pad 28.3
pit 28.7 30.8
check 33
# a tighter flight: a pillar every two and a half beats
ceil 34.5 64 6.5
ship 35
strip 37 62 0
cstrip 37 62 6.5
fpillar 39 3.6
cpillar 41.5 2.8
fpillar 44 3.6
cpillar 46.5 2.8
fpillar 49 2.2
cpillar 49 4.5
fpillar 51.5 3.8
cpillar 54 2.2
fpillar 56.5 1.8
cpillar 56.5 4.0
coin 1 58.5 5.3
cpillar 60.5 2.6
cube 64
check 67
medkit 67
# orbs, then the pad and orbs over the long pit
arp 67
strip 69.5 76 0
orb 69.5 2.3
orb 71.5 2.3
orb 73.5 2.3
orb 75.5 2.3
spike 79
pad 80.3
pit 80.7 87.9
orb 81.68 4.05
orb 83.68 4.05
orb 85.68 4.05
coin 2 86.68 6.0
spike 90
end 92
",
            },
            new CadenceMap
            {
                Name = "APOGÉE", Level = 5, Script = @"
start 8.3
# no warm-up
spike 3
spike 5 2
spike 7
spike 9 3
pit 12 13.5
plat 15 16 1
spike 16.6
plat 17.2 18 1
spike 18.4
check 21
# upside down, spikes closer together
ceil 22 36.9 4.6
ceil 38.1 45 4.6
flip 23
strip 25 42 0
cspike 27
cspike 29 2
cspike 31.5
cspike 33.5 2
coin 0 35.5 2.25
cspike 41
normal 43
check 47
medkit 47
# the tightest flight
ceil 48.5 73 6.5
ship 49
strip 51 71 0
cstrip 51 71 6.5
fpillar 53 3.6
cpillar 55.5 2.8
fpillar 58 3.8
cpillar 60.5 2.4
fpillar 63 2.0
cpillar 63 4.4
coin 1 65 5.4
cpillar 67.5 2.6
fpillar 69.5 3.4
cube 73
check 76
# faster still, and home over the pit
arp 76
speed 77 9.2
spike 79
pad 80.3
pit 80.7 87.9
orb 81.68 4.05
orb 83.68 4.05
orb 85.68 4.05
coin 2 84.68 6.0
spike 90
end 92
",
            },
        };

        /// <summary>The level for a difficulty level of 1 to 5 (clamped).</summary>
        public static CadenceMap ForLevel(int level)
        {
            CadenceMap best = All[0];
            foreach (var m in All) if (m.Level <= level && m.Level >= best.Level) best = m;
            return best;
        }
    }
}
