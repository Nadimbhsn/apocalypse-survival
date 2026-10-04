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
    /// sixty to seventy-five beats - about half a minute of music - with no empty stretch
    /// between its sections, yet a breath between two moves.
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
spike 3
spike 5
spike 7 2
coins 7
spike 10
spike 12 3
pit 15 16.25
plat 18 20 1
coin 0 20.8 3.3
spike 22.5
plat 24 26 1
plat 26 28 2
coin 1 29 3.9
spike 30.5
pad 33.3
pit 33.7 35.8
check 38
arp 38
strip 40.5 47 0
orb 40.5 2.3
orb 42.5 2.3
orb 44.5 2.3
orb 46.5 2.3
spike 49
pad 50.3
pit 50.7 57.9
orb 51.68 4.05
orb 53.68 4.05
orb 55.68 4.05
coin 2 54.68 5.8
spike 60
end 62
",
            },
            new CadenceMap
            {
                Name = "CONTRETEMPS", Level = 2, Script = @"
spike 3
spike 5
spike 7 2
coins 7
plat 10 12 1
coin 0 12.8 3.3
spike 15 3
pit 18 19.25
check 21
ceil 22 36 4.6
flip 23
strip 25 37 0
cspike 27
cspike 29.5
cspike 32 2
coin 1 34.5 2.25
normal 36
check 40
arp 40
speed 41 8.3
spike 44
spike 46 2
plat 48.5 51.5 1
spike 50 1 1
pad 54
pit 54.3 56.4
strip 57.5 60 0
orb 58 2.3
coin 2 59 4.2
spike 63 3
end 66
",
            },
            new CadenceMap
            {
                Name = "ENVOL", Level = 3, Script = @"
spike 3
spike 5 2
spike 8
spike 10 3
plat 13 15 1
coin 0 15.8 3.3
check 18
ceil 19.5 46 6.5
ship 20
strip 22 44 0
cstrip 22 44 6.5
fpillar 24.5 3.4
cpillar 27 3.0
fpillar 29.5 3.4
cpillar 32 3.0
fpillar 35 2.0
cpillar 35 4.6
fpillar 38 3.8
cpillar 40.5 2.4
coin 1 43 5.3
cube 46
check 49
medkit 49
arp 49
ceil 50 62 4.6
flip 51
strip 53 63 0
cspike 55
cspike 57.5
cspike 60 2
coin 2 61.5 2.25
normal 62
spike 67
end 69
",
            },
            new CadenceMap
            {
                Name = "PULSAR", Level = 4, Script = @"
start 8.3
spike 3
spike 5
spike 7 2
spike 10
spike 12 3
pit 15 16.25
plat 18 20 1
coin 0 20.8 3.3
pad 23.3
pit 23.7 25.8
check 28
ceil 29.5 52 6.5
ship 30
strip 32 50 0
cstrip 32 50 6.5
fpillar 34 3.6
cpillar 36.5 2.8
fpillar 39 3.6
cpillar 41.5 2.8
fpillar 44 2.2
cpillar 44 4.5
fpillar 46.5 1.8
cpillar 46.5 4.0
coin 1 48.5 5.3
cube 52
check 55
medkit 55
arp 55
spike 57
pad 58.3
pit 58.7 65.9
orb 59.68 4.05
orb 61.68 4.05
orb 63.68 4.05
coin 2 64.68 6.0
spike 68
spike 70 2
end 72
",
            },
            new CadenceMap
            {
                Name = "APOGÉE", Level = 5, Script = @"
start 8.3
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
ceil 22 34 4.6
flip 23
strip 25 35 0
cspike 27
cspike 29 2
cspike 31.5 2
coin 0 33.5 2.25
normal 34
check 38
medkit 38
ceil 39.5 58 6.5
ship 40
strip 42 56 0
cstrip 42 56 6.5
fpillar 44 3.6
cpillar 46.5 2.6
fpillar 49 3.8
cpillar 51 2.4
fpillar 53 2.0
cpillar 53 4.4
coin 1 55 5.4
cube 58
check 61
arp 61
speed 62 9.2
spike 64
pad 65.3
pit 65.7 72.9
orb 66.68 4.05
orb 68.68 4.05
orb 70.68 4.05
coin 2 69.68 6.0
spike 75
end 77
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
