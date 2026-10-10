using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// How the dead find the hero: the station is cut into half-unit cells, and a distance
    /// field to the hero is spread out from his cell (Dijkstra) a few times a second. Each of
    /// the dead walks downhill on it, so they go round the cars and shelves, line up with the
    /// openings between rooms, and pick a way in. A barricade still standing is not a wall
    /// on the field but a costly cell: the dead go to the cheapest door and break it; when
    /// one has already fallen, they flow through it instead.
    /// </summary>
    public partial class NuitGame
    {
        const float Cell = 0.5f;
        /// <summary>How far walls and props are thickened on the grid (about the dead's radius).</summary>
        const float Clearance = 0.32f;
        /// <summary>Extra cost of crossing a cell of a standing barricade (it must be broken).</summary>
        const float DoorCost = 14f;

        int gridW, gridH;
        bool[] solid;
        int[] doorAt;
        float[] dist;
        int[] heap;
        int heapCount;
        float fieldTimer;
        int fieldSource = -1;

        static readonly int[] NX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] NY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] NCost = { 1f, 1f, 1f, 1f, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

        Vector2 CellCentre(int c) => new Vector2(WorldBounds.xMin + (c % gridW + 0.5f) * Cell, WorldBounds.yMin + (c / gridW + 0.5f) * Cell);

        int CellOf(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - WorldBounds.xMin) / Cell), 0, gridW - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((p.y - WorldBounds.yMin) / Cell), 0, gridH - 1);
            return y * gridW + x;
        }

        /// <summary>Marks what never moves: walls, props, the shutters of closed rooms, and which cells belong to a door.</summary>
        void BuildGrid()
        {
            gridW = Mathf.CeilToInt(WorldBounds.width / Cell);
            gridH = Mathf.CeilToInt(WorldBounds.height / Cell);
            int n = gridW * gridH;
            solid = new bool[n];
            doorAt = new int[n];
            dist = new float[n];
            heap = new int[n * 8 + 16];
            for (int i = 0; i < n; i++) doorAt[i] = -1;
            void Mark(Rect r, System.Action<int> act)
            {
                var g = new Rect(r.xMin - Clearance, r.yMin - Clearance, r.width + 2f * Clearance, r.height + 2f * Clearance);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((g.xMin - WorldBounds.xMin) / Cell));
                int x1 = Mathf.Min(gridW - 1, Mathf.FloorToInt((g.xMax - WorldBounds.xMin) / Cell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((g.yMin - WorldBounds.yMin) / Cell));
                int y1 = Mathf.Min(gridH - 1, Mathf.FloorToInt((g.yMax - WorldBounds.yMin) / Cell));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int c = y * gridW + x;
                        if (g.Contains(CellCentre(c))) act(c);
                    }
            }
            foreach (var w in walls) Mark(w, c => solid[c] = true);
            foreach (var o in obstacles) Mark(o, c => solid[c] = true);
            if (zones != null)
                for (int z = 1; z < zones.Length; z++) if (!zones[z].Open) Mark(GateRect(zones[z]), c => solid[c] = true);
            for (int i = 0; i < doors.Count; i++)
            {
                int di = i;
                var d = doors[i];
                // the opening itself is passable (minus the clearance at its posts); the cells
                // just in front and behind belong to the door too, so reaching them means hitting it
                Mark(DoorRect(d), c =>
                {
                    doorAt[c] = di;
                    var p = CellCentre(c);
                    float along = d.Horizontal ? Mathf.Abs(p.x - d.Pos.x) : Mathf.Abs(p.y - d.Pos.y);
                    if (along <= 1.2f - Clearance) solid[c] = false;
                });
            }
            fieldSource = -1;
            fieldTimer = 0f;
        }

        /// <summary>Spreads the distance field from the hero's cell (a few times a second, or when he changes cell).</summary>
        void UpdateField(float dt)
        {
            if (solid == null) BuildGrid();
            fieldTimer -= dt;
            int src = CellOf(heroPos);
            if (src == fieldSource && fieldTimer > 0f) return;
            fieldSource = src;
            fieldTimer = 0.3f;
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
            heapCount = 0;
            dist[src] = 0f;
            Push(src);
            while (heapCount > 0)
            {
                int c = Pop();
                float dc = dist[c];
                int cx = c % gridW, cy = c / gridW;
                for (int k = 0; k < 8; k++)
                {
                    int x = cx + NX[k], y = cy + NY[k];
                    if (x < 0 || y < 0 || x >= gridW || y >= gridH) continue;
                    int nb = y * gridW + x;
                    if (solid[nb]) continue;
                    // no cutting corners past a wall
                    if (k >= 4 && (solid[cy * gridW + x] || solid[y * gridW + cx])) continue;
                    float step = NCost[k];
                    int d = doorAt[nb];
                    if (d >= 0)
                    {
                        if (!zones[doors[d].Zone].Open) continue;
                        if (doors[d].Intact) step += DoorCost;
                    }
                    float nd = dc + step;
                    if (nd >= dist[nb]) continue;
                    dist[nb] = nd;
                    Push(nb);
                }
            }
        }

        void Push(int c)
        {
            if (heapCount >= heap.Length) return;
            int i = heapCount++;
            heap[i] = c;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (dist[heap[p]] <= dist[heap[i]]) break;
                (heap[p], heap[i]) = (heap[i], heap[p]);
                i = p;
            }
        }

        int Pop()
        {
            int top = heap[0];
            heap[0] = heap[--heapCount];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < heapCount && dist[heap[l]] < dist[heap[m]]) m = l;
                if (r < heapCount && dist[heap[r]] < dist[heap[m]]) m = r;
                if (m == i) break;
                (heap[m], heap[i]) = (heap[i], heap[m]);
                i = m;
            }
            return top;
        }

        int BestNeighbour(int c)
        {
            int cx = c % gridW, cy = c / gridW;
            int best = -1;
            float bd = dist[c];
            for (int k = 0; k < 8; k++)
            {
                int x = cx + NX[k], y = cy + NY[k];
                if (x < 0 || y < 0 || x >= gridW || y >= gridH) continue;
                int nb = y * gridW + x;
                if (solid[nb] || dist[nb] >= bd) continue;
                if (k >= 4 && (solid[cy * gridW + x] || solid[y * gridW + cx])) continue;
                bd = dist[nb]; best = nb;
            }
            return best;
        }

        /// <summary>
        /// Where one of the dead steps next on the way to the hero. Returns the index of the
        /// standing barricade it has reached and must break, or -1.
        /// </summary>
        int PathStep(Foe f, out Vector2 goal)
        {
            goal = heroPos;
            if (dist == null) return -1;
            // close and nothing in the way: straight at him
            var toHero = heroPos - f.Pos;
            if (toHero.sqrMagnitude < 6f && RoomOf(f.Pos) == RoomOf(heroPos) && WalkClear(f.Pos, heroPos)) return -1;

            int c = CellOf(f.Pos);
            if (dist[c] == float.MaxValue)
            {
                // pushed into a thickened cell: step back onto the nearest reachable one
                int cx = c % gridW, cy = c / gridW, best = -1;
                float bd = float.MaxValue;
                for (int r = 1; r <= 3 && best < 0; r++)
                    for (int y = cy - r; y <= cy + r; y++)
                        for (int x = cx - r; x <= cx + r; x++)
                        {
                            if (x < 0 || y < 0 || x >= gridW || y >= gridH) continue;
                            int nb = y * gridW + x;
                            if (dist[nb] < bd) { bd = dist[nb]; best = nb; }
                        }
                if (best >= 0) goal = CellCentre(best);
                return -1;
            }
            int next = BestNeighbour(c);
            if (next < 0) return -1;
            int d = doorAt[next];
            if (d >= 0 && doors[d].Intact && doorAt[c] != d)
            {
                // the barricade is the way: stand at it and hit it
                goal = f.Pos;
                return d;
            }
            if (d >= 0 && doors[d].Intact) return d;
            // look one cell further for a smoother line
            int after = BestNeighbour(next);
            goal = after >= 0 && (doorAt[after] < 0 || !doors[doorAt[after]].Intact) ? CellCentre(after) : CellCentre(next);
            return -1;
        }

        /// <summary>A straight walk with nothing solid in the way (walls, fences, props, closed doors and shutters).</summary>
        bool WalkClear(Vector2 a, Vector2 b)
        {
            foreach (var w in walls) if (SegHits(a, b, Grow(w))) return false;
            foreach (var o in obstacles) if (SegHits(a, b, Grow(o))) return false;
            foreach (var d in doors) if ((d.Intact || !zones[d.Zone].Open) && SegHits(a, b, Grow(DoorRect(d)))) return false;
            for (int z = 1; z < zones.Length; z++) if (!zones[z].Open && SegHits(a, b, Grow(GateRect(zones[z])))) return false;
            return true;
        }

        static Rect Grow(Rect r) => new Rect(r.xMin - 0.25f, r.yMin - 0.25f, r.width + 0.5f, r.height + 0.5f);
    }
}
