using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Platformer.Survival
{
    /// <summary>
    /// L'ATELIER D'ELRON: buying Étienne a new finish (or arming him in the shop) is not a
    /// click but a little scene - Elron builds his robot on the workbench, by night, the giant
    /// planet glowing in the window.
    ///  1. Assemblage: drag each part onto its dashed outline on the blue print.
    ///  2. Câblage: join each coloured wire to the terminal of its colour.
    ///  3. Mise sous tension: tap when the needle crosses the green, three times.
    ///  4. Réveil: the eye lights up, the rotor starts, Étienne lifts off in his new finish.
    /// Nothing can be failed - it is a reward, a minute long, with a button to skip it.
    /// Arming only (the shop's support-fire upgrade) is the short version: just the cannon,
    /// its wiring and the power.
    /// </summary>
    public class EtienneWorkshop : MonoBehaviour
    {
        // Each part's centre (px from the frame centre, y up) and size, from the offline painter.
        static readonly Dictionary<string, (float x, float y, int w, int h, int cw, int ch)> Parts = new()
        {
            ["scarf"] = (-153.5f, -92.5f, 180, 60, 177, 59),
            ["thruster"] = (-113.5f, -44f, 80, 80, 77, 80),
            ["body"] = (-10f, -31f, 220, 220, 218, 218),
            ["rotor"] = (-10f, 92.5f, 220, 64, 218, 63),
            ["antenna"] = (-60.5f, 98.5f, 48, 88, 45, 85),
            ["cannon"] = (64f, -102f, 100, 40, 100, 40),
            ["eye"] = (32.5f, -22.5f, 116, 116, 115, 115),
        };
        static readonly string[] DrawOrder = { "scarf", "thruster", "body", "rotor", "antenna", "cannon", "eye" };
        static readonly string[] PartNames =
        {
            "l'écharpe", "le réacteur", "le corps", "le rotor", "l'antenne", "le canon", "l'œil",
        };

        static readonly Dictionary<string, (Color plate, Color eye, Color scarf, Color dark)> Finishes = new()
        {
            ["cuivre"] = (new Color(0.85f, 0.52f, 0.30f), new Color(1f, 0.72f, 0.28f), new Color(0.82f, 0.13f, 0.11f), new Color(0.9f, 0.75f, 0.68f)),
            ["nuit"] = (new Color(0.36f, 0.42f, 0.55f), new Color(0.45f, 0.9f, 1f), new Color(0.36f, 0.2f, 0.55f), new Color(0.7f, 0.75f, 0.9f)),
            ["braise"] = (new Color(0.8f, 0.18f, 0.14f), new Color(1f, 0.55f, 0.15f), new Color(0.2f, 0.12f, 0.12f), new Color(0.85f, 0.6f, 0.6f)),
            ["prototype"] = (Color.white, new Color(0.55f, 1f, 0.55f), new Color(0.9f, 0.36f, 0.14f), Color.white),
            ["dore"] = (new Color(1f, 0.8f, 0.32f), new Color(1f, 0.96f, 0.78f), new Color(0.8f, 0.12f, 0.18f), new Color(1f, 0.85f, 0.6f)),
        };

        const float BlueprintSize = 760f;
        const float SnapDistance = 80f;

        enum Step { Assemble, Wire, Power, Wake }

        RuntimeUI ui;
        string finish;
        bool armingOnly;
        Action onDone;
        Step step;

        RectTransform frame, blueprint, tray, partsLayer;
        Text title, speech, hint;
        readonly List<WorkshopPart> loose = new();
        readonly Dictionary<string, Image> placed = new();
        float K => 0.8f * BlueprintSize / 512f;

        // wiring
        RectTransform board;
        readonly List<(Image left, Image right, Color color, RectTransform wire, bool done)> wires = new();
        int wireDrag = -1;

        // power
        RectTransform gauge, needle, greenZone;
        readonly List<Image> charges = new();
        float needleT, needleSpeed = 0.9f, greenCentre;
        int hits;
        Button powerButton;

        // wake
        Image robot;
        float wakeTime;
        Button continueButton;

        public static void Open(RuntimeUI ui, string finishId, bool armingOnly, Action onDone)
        {
            var go = new GameObject("EtienneWorkshop", typeof(RectTransform));
            go.transform.SetParent(ui.Canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.transform.SetAsLastSibling();
            var w = go.AddComponent<EtienneWorkshop>();
            w.ui = ui;
            w.finish = Finishes.ContainsKey(finishId) ? finishId : "cuivre";
            w.armingOnly = armingOnly;
            w.onDone = onDone;
            w.Build();
        }

        static Sprite Load(string name, Vector2 pivot, Rect? rect = null)
        {
            var tex = Resources.Load<Texture2D>("Workshop/" + name);
            if (tex == null) return null;
            return Sprite.Create(tex, rect ?? new Rect(0, 0, tex.width, tex.height), pivot, 100f);
        }

        void Build()
        {
            var rt = (RectTransform)transform;
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.03f, 0.03f, 1f);
            var tex = Resources.Load<Texture2D>("Workshop/workbench");
            if (tex != null)
            {
                var back = UiKit.CreateRect("Workbench", rt, Vector2.zero, Vector2.one);
                var raw = back.gameObject.AddComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
                back.gameObject.AddComponent<CoverImage>().focus = new Vector2(0.5f, 0.45f);
            }

            // Everything else lives in a portrait frame centred on the screen.
            frame = UiKit.CreateRect("Frame", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var fit = frame.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1080f / 1700f;

            title = UiKit.Outlined(UiKit.CreateText("Title", frame, "L'ATELIER D'ELRON", 60, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.98f), ApogeeTheme.Gold), 3f);
            UiKit.FitLabel(title, 60);
            hint = UiKit.Outlined(UiKit.CreateText("Hint", frame, "", 28, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.865f), new Vector2(0.72f, 0.91f), ApogeeTheme.Cream), 2f);
            UiKit.FitLabel(hint, 28);

            // The blue print, with Étienne's outline dashed on it.
            blueprint = UiKit.CreateRect("Blueprint", frame, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f));
            blueprint.sizeDelta = new Vector2(BlueprintSize, BlueprintSize);
            var bpImg = blueprint.gameObject.AddComponent<Image>();
            bpImg.sprite = Load("blueprint", new Vector2(0.5f, 0.5f));
            bpImg.raycastTarget = false;
            blueprint.localRotation = Quaternion.Euler(0f, 0f, -2f);
            partsLayer = UiKit.CreateRect("Parts", blueprint, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            partsLayer.sizeDelta = Vector2.zero;

            // The tray of loose parts on the bench.
            tray = UiKit.CreateRect("Tray", frame, new Vector2(0.03f, 0.13f), new Vector2(0.97f, 0.31f));

            // Elron talking, bottom of the screen.
            var talk = UiKit.CreateRect("Talk", frame, new Vector2(0.03f, 0.015f), new Vector2(0.97f, 0.12f));
            var timg = talk.gameObject.AddComponent<Image>();
            timg.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
            timg.type = Image.Type.Sliced;
            timg.raycastTarget = false;
            var avatar = UiKit.CreateImage("Elron", talk, new Vector2(0.02f, 0.08f), new Vector2(0.17f, 0.92f), HubArt.Get("avatar"), Color.white);
            avatar.raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("Name", talk, "ELRON", 22, TextAnchor.UpperLeft, new Vector2(0.2f, 0.6f), new Vector2(0.6f, 0.95f), ApogeeTheme.Gold), 1.2f);
            speech = UiKit.CreateText("Speech", talk, "", 28, TextAnchor.MiddleLeft, new Vector2(0.2f, 0.05f), new Vector2(0.97f, 0.65f), ApogeeTheme.Cream);
            UiKit.FitLabel(speech, 28);

            var skip = UiKit.CreateButton("Skip", frame, "PASSER", new Vector2(0.74f, 0.875f), new Vector2(0.97f, 0.91f), Finish, 20, new Color(0.22f, 0.10f, 0.08f));
            skip.transform.SetAsLastSibling();

            BeginAssemble();
        }

        Color TintOf(string part)
        {
            var f = Finishes[finish];
            return part switch
            {
                "body" => f.plate,
                "eye" => f.eye,
                "scarf" => f.scarf,
                _ => f.dark,
            };
        }

        Image MakePart(string name, RectTransform parent)
        {
            var p = Parts[name];
            var sprite = Load("part_" + name, new Vector2(0.5f, 0.5f), new Rect(0, p.h - p.ch, p.cw, p.ch));
            var img = UiKit.CreateImage("Part_" + name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), sprite, TintOf(name), false);
            img.rectTransform.sizeDelta = new Vector2(p.cw * K, p.ch * K);
            return img;
        }

        Vector2 Target(string name) => new Vector2(Parts[name].x * K, Parts[name].y * K);

        // ---- 1. assembly --------------------------------------------------------------------------

        void BeginAssemble()
        {
            step = Step.Assemble;
            Say(armingOnly ? "Un canon d'appui, ça peut servir... Pose-le sous son menton." : "Bon... à nous deux, Étienne. Pièce par pièce.");
            hint.text = "Glisse chaque pièce sur son contour";
            var toPlace = armingOnly ? new List<string> { "cannon" } : new List<string> { "eye", "rotor", "antenna", "thruster", "cannon", "scarf" };
            // The body (and, when only arming, everything but the cannon) waits on the print.
            foreach (var name in DrawOrder)
            {
                if (toPlace.Contains(name)) continue;
                var img = MakePart(name, partsLayer);
                img.rectTransform.anchoredPosition = Target(name);
                img.raycastTarget = false;
                placed[name] = img;
            }
            // The loose parts, scattered on the bench.
            var shuffled = new List<string>(toPlace);
            for (int i = 0; i < shuffled.Count; i++) { int j = Random.Range(i, shuffled.Count); (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]); }
            for (int i = 0; i < shuffled.Count; i++)
            {
                string name = shuffled[i];
                var img = MakePart(name, tray);
                float u = shuffled.Count == 1 ? 0.5f : (i + 0.5f) / shuffled.Count;
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(u, 0.5f);
                img.rectTransform.anchoredPosition = new Vector2(0f, Random.Range(-30f, 30f));
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-14f, 14f));
                float fit = Mathf.Min(1f, 170f / Mathf.Max(img.rectTransform.sizeDelta.x, img.rectTransform.sizeDelta.y));
                img.rectTransform.localScale = Vector3.one * fit;
                img.raycastTarget = true;   // UiKit images ignore touches by default
                img.raycastPadding = new Vector4(-35f, -35f, -35f, -35f);   // easy to grab with a finger
                var part = img.gameObject.AddComponent<WorkshopPart>();
                part.workshop = this;
                part.partName = name;
                part.trayScale = fit;
                loose.Add(part);
            }
        }

        /// <summary>A part let go: snaps into place if it is close enough to its outline.</summary>
        internal bool TryPlace(WorkshopPart part)
        {
            var rt = (RectTransform)part.transform;
            Vector2 local = partsLayer.InverseTransformPoint(rt.position);
            if ((local - Target(part.partName)).magnitude > SnapDistance) return false;
            rt.SetParent(partsLayer, true);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            part.SnapTo(Target(part.partName));
            placed[part.partName] = part.GetComponent<Image>();
            loose.Remove(part);
            // keep the draw order of the robot
            foreach (var name in DrawOrder) if (placed.TryGetValue(name, out var img)) img.transform.SetAsLastSibling();
            Sfx.Material();
            Sparks(rt.position, 10);
            int i = Array.IndexOf(DrawOrder, part.partName);
            Say(loose.Count == 0 ? "Parfait. Maintenant, le plus délicat..." : $"Voilà {PartNames[i]} !");
            if (loose.Count == 0) Invoke(nameof(BeginWire), 0.9f);
            return true;
        }

        // ---- 2. wiring ---------------------------------------------------------------------------

        static readonly Color[] WireColors = { new Color(0.92f, 0.25f, 0.2f), new Color(1f, 0.82f, 0.2f), new Color(0.3f, 0.6f, 1f), new Color(0.35f, 0.85f, 0.4f) };

        void BeginWire()
        {
            step = Step.Wire;
            hint.text = "Relie chaque fil à la borne de sa couleur";
            Say(armingOnly ? "Le canon tire sur le même circuit que l'œil. Les fils..." : "Les fils, maintenant. Chaque couleur à sa borne.");
            board = UiKit.CreateRect("Board", frame, new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.8f));
            var img = board.gameObject.AddComponent<Image>();
            img.color = new Color(0.08f, 0.32f, 0.18f, 0.97f);
            var outline = board.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.75f, 0.35f);
            outline.effectDistance = new Vector2(5, -5);
            // circuit traces for looks
            for (int i = 0; i < 14; i++)
            {
                var tr = UiKit.CreateImage("Trace", board, new Vector2(Random.Range(0.25f, 0.55f), Random.Range(0.05f, 0.95f)), new Vector2(0f, 0f), PlaceholderVisuals.Square(Color.white), new Color(0.85f, 0.72f, 0.3f, 0.35f), false);
                tr.rectTransform.anchorMax = tr.rectTransform.anchorMin + new Vector2(Random.Range(0.08f, 0.3f), 0.006f);
                tr.raycastTarget = false;
            }
            UiKit.Outlined(UiKit.CreateText("Label", board, armingOnly ? "CIRCUIT DU CANON" : "CIRCUIT PRINCIPAL", 26, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.9f), new Vector2(0.9f, 0.99f), new Color(0.95f, 0.9f, 0.6f)), 1.5f);

            int n = armingOnly ? 3 : 4;
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            for (int i = 0; i < n; i++) { int j = Random.Range(i, n); (order[i], order[j]) = (order[j], order[i]); }
            wires.Clear();
            for (int i = 0; i < n; i++)
            {
                float y = 0.8f - i * 0.7f / Mathf.Max(1, n - 1);
                var col = WireColors[i];
                var left = Terminal(board, new Vector2(0.1f, y), col, i, true);
                float yr = 0.8f - order[i] * 0.7f / Mathf.Max(1, n - 1);
                var right = Terminal(board, new Vector2(0.9f, yr), col, i, false);
                var wire = UiKit.CreateRect($"Wire_{i}", board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                wire.pivot = new Vector2(0f, 0.5f);
                var wimg = wire.gameObject.AddComponent<Image>();
                wimg.color = col;
                wimg.raycastTarget = false;
                wire.gameObject.SetActive(false);
                wires.Add((left, right, col, wire, false));
            }
        }

        Image Terminal(RectTransform parent, Vector2 anchor, Color col, int index, bool left)
        {
            var t = UiKit.CreateImage(left ? $"Left_{index}" : $"Right_{index}", parent, anchor, anchor, PlaceholderVisuals.Circle(Color.white), col, false);
            t.rectTransform.sizeDelta = new Vector2(72f, 72f);
            var ring = UiKit.CreateImage("Ring", t.transform, Vector2.zero, Vector2.one, PlaceholderVisuals.RimCircle(new Color(0.85f, 0.75f, 0.4f)), Color.white, false);
            ring.rectTransform.offsetMin = new Vector2(-10, -10);
            ring.rectTransform.offsetMax = new Vector2(10, 10);
            ring.raycastTarget = false;
            if (left)
            {
                t.raycastTarget = true;
                t.raycastPadding = new Vector4(-25f, -25f, -25f, -25f);
                var h = t.gameObject.AddComponent<WorkshopTerminal>();
                h.workshop = this;
                h.index = index;
            }
            return t;
        }

        internal void WireBegin(int index)
        {
            if (step != Step.Wire || wires[index].done) return;
            wireDrag = index;
            wires[index].wire.gameObject.SetActive(true);
        }

        internal void WireDrag(int index, Vector2 screen)
        {
            if (wireDrag != index) return;
            var w = wires[index];
            RectTransformUtility.ScreenPointToLocalPointInRectangle(board, screen, null, out var to);
            Stretch(w.wire, LocalIn(board, w.left.rectTransform), to);
        }

        internal void WireEnd(int index, Vector2 screen)
        {
            if (wireDrag != index) return;
            wireDrag = -1;
            var w = wires[index];
            RectTransformUtility.ScreenPointToLocalPointInRectangle(board, screen, null, out var to);
            Vector2 target = LocalIn(board, w.right.rectTransform);
            if ((to - target).magnitude < 80f)
            {
                Stretch(w.wire, LocalIn(board, w.left.rectTransform), target);
                wires[index] = (w.left, w.right, w.color, w.wire, true);
                Sfx.Bounce();
                Sparks(w.right.rectTransform.position, 8);
                int left = 0;
                foreach (var x in wires) if (!x.done) left++;
                Say(left == 0 ? "Le circuit est fermé. On tente le courant ?" : left == 1 ? "Plus qu'un..." : "Bien !");
                if (left == 0) Invoke(nameof(BeginPower), 0.8f);
            }
            else
            {
                w.wire.gameObject.SetActive(false);
                Sfx.Hit();
                Say("Pas celle-là... regarde bien la couleur.");
            }
        }

        static Vector2 LocalIn(RectTransform space, RectTransform item) => space.InverseTransformPoint(item.position);

        static void Stretch(RectTransform wire, Vector2 from, Vector2 to)
        {
            var d = to - from;
            wire.anchoredPosition = from;
            wire.sizeDelta = new Vector2(d.magnitude, 14f);
            wire.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        // ---- 3. power -----------------------------------------------------------------------------

        void BeginPower()
        {
            step = Step.Power;
            if (board != null) board.gameObject.SetActive(false);
            hint.text = "Appuie quand l'aiguille est dans le vert";
            Say("Trois impulsions, au bon moment. Doucement...");
            gauge = UiKit.CreateRect("Gauge", frame, new Vector2(0.1f, 0.21f), new Vector2(0.9f, 0.27f));
            var gimg = gauge.gameObject.AddComponent<Image>();
            gimg.sprite = HubArt.Get("ui_pill", 30f) ?? ApogeeTheme.Chip;
            gimg.type = Image.Type.Sliced;
            greenZone = UiKit.CreateRect("Green", gauge, new Vector2(0.4f, 0.15f), new Vector2(0.58f, 0.85f));
            greenZone.gameObject.AddComponent<Image>().color = new Color(0.35f, 0.9f, 0.4f, 0.85f);
            needle = UiKit.CreateRect("Needle", gauge, new Vector2(0f, -0.2f), new Vector2(0f, 1.2f));
            needle.sizeDelta = new Vector2(12f, 0f);
            needle.gameObject.AddComponent<Image>().color = ApogeeTheme.Cream;
            PlaceGreen();
            charges.Clear();
            for (int i = 0; i < 3; i++)
            {
                var c = UiKit.CreateImage($"Charge_{i}", frame, new Vector2(0.38f + i * 0.12f, 0.285f), new Vector2(0.38f + i * 0.12f, 0.285f), PlaceholderVisuals.Circle(Color.white), new Color(0.25f, 0.2f, 0.18f), false);
                c.rectTransform.sizeDelta = new Vector2(56f, 56f);
                charges.Add(c);
            }
            var pr = UiKit.CreateRect("Power", frame, new Vector2(0.18f, 0.135f), new Vector2(0.82f, 0.2f));
            var pimg = pr.gameObject.AddComponent<Image>();
            pimg.sprite = HubArt.Get("ui_play", 50f) ?? ApogeeTheme.Button;
            pimg.type = Image.Type.Sliced;
            powerButton = pr.gameObject.AddComponent<Button>();
            powerButton.targetGraphic = pimg;
            powerButton.onClick.AddListener(Pulse);
            pr.gameObject.AddComponent<ButtonPop>();
            var t = UiKit.CreateText("Label", pr, "BRANCHER !", 40, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Color(0.3f, 0.09f, 0.02f));
            t.fontStyle = FontStyle.Bold;
            if (tray != null) tray.gameObject.SetActive(false);
        }

        void PlaceGreen()
        {
            greenCentre = Random.Range(0.2f, 0.8f);
            float half = 0.09f - hits * 0.015f;
            greenZone.anchorMin = new Vector2(greenCentre - half, 0.15f);
            greenZone.anchorMax = new Vector2(greenCentre + half, 0.85f);
        }

        void Pulse()
        {
            if (step != Step.Power) return;
            float x = Mathf.PingPong(needleT, 1f);
            float half = (greenZone.anchorMax.x - greenZone.anchorMin.x) / 2f;
            if (Mathf.Abs(x - greenCentre) <= half + 0.015f)
            {
                charges[hits].color = new Color(1f, 0.85f, 0.35f);
                Sparks(charges[hits].rectTransform.position, 12);
                hits++;
                Sfx.Milestone();
                if (hits >= 3) { Invoke(nameof(BeginWake), 0.6f); step = Step.Wake; return; }
                needleSpeed *= 1.25f;
                PlaceGreen();
                Say(hits == 1 ? "Ça chauffe..." : "Encore une !");
            }
            else
            {
                Sfx.Hit();
                Say(x < greenCentre ? "Trop tôt !" : "Trop tard !");
            }
        }

        // ---- 4. wake ------------------------------------------------------------------------------

        void BeginWake()
        {
            step = Step.Wake;
            hint.text = "";
            if (gauge != null) gauge.gameObject.SetActive(false);
            if (powerButton != null) powerButton.gameObject.SetActive(false);
            foreach (var c in charges) c.gameObject.SetActive(false);
            partsLayer.gameObject.SetActive(false);
            var frames = CompanionCatalog.Frames(finish);
            robot = UiKit.CreateImage("Etienne", blueprint, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), frames != null ? frames[0] : null, Color.white);
            robot.rectTransform.sizeDelta = new Vector2(BlueprintSize * 0.85f, BlueprintSize * 0.85f);
            robot.rectTransform.anchoredPosition = new Vector2(-10f * K, 0f);
            var flip = robot.gameObject.AddComponent<UiFlipbook>();
            flip.frames = frames;
            wakeTime = Time.unscaledTime;
            Sfx.Spring();
            Sparks(robot.rectTransform.position, 40);
            var def = CompanionCatalog.Find(finish);
            title.text = armingOnly ? "ÉTIENNE EST ARMÉ !" : $"{def.Name.ToUpperInvariant()} EST PRÊT !";
            Say(armingOnly ? "Bip bip ! ... Il a l'air content de son canon." : "Bip... bip bip ! Salut, toi. On a une planète à fuir.");
            var cr = UiKit.CreateRect("Continue", frame, new Vector2(0.18f, 0.15f), new Vector2(0.82f, 0.23f));
            var cimg = cr.gameObject.AddComponent<Image>();
            cimg.sprite = HubArt.Get("ui_play", 50f) ?? ApogeeTheme.Button;
            cimg.type = Image.Type.Sliced;
            continueButton = cr.gameObject.AddComponent<Button>();
            continueButton.targetGraphic = cimg;
            continueButton.onClick.AddListener(Finish);
            cr.gameObject.AddComponent<ButtonPop>();
            var t = UiKit.CreateText("Label", cr, "EN ROUTE !", 42, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Color(0.3f, 0.09f, 0.02f));
            t.fontStyle = FontStyle.Bold;
        }

        void Finish()
        {
            if (this == null) return;
            CancelInvoke();
            var done = onDone;
            Destroy(gameObject);
            done?.Invoke();
        }

        // ---- frame ---------------------------------------------------------------------------------

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (step == Step.Power && needle != null)
            {
                needleT += dt * needleSpeed;
                float x = Mathf.PingPong(needleT, 1f);
                needle.anchorMin = new Vector2(x, -0.2f);
                needle.anchorMax = new Vector2(x, 1.2f);
            }
            if (robot != null)
            {
                // He lifts off the print and loops above it, happy.
                float t = Time.unscaledTime - wakeTime;
                float lift = Mathf.SmoothStep(0f, 1f, t / 1.2f);
                var p = new Vector2(-10f * K + Mathf.Sin(t * 1.6f) * 120f * lift, lift * 60f + Mathf.Sin(t * 3.2f) * 25f * lift);
                robot.rectTransform.anchoredPosition = p;
                robot.rectTransform.localScale = new Vector3(Mathf.Cos(t * 1.6f) >= 0f ? 1f : -1f, 1f, 1f);
                if (Random.value < dt * 3f) Sparks(robot.rectTransform.position, 3);
            }
        }

        void Say(string line)
        {
            if (speech != null) speech.text = line;
        }

        /// <summary>A little burst of golden sparks on the UI.</summary>
        void Sparks(Vector3 worldPos, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var s = UiKit.CreateImage("Spark", (RectTransform)transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PlaceholderVisuals.Circle(Color.white), new Color(1f, Random.Range(0.7f, 0.95f), 0.4f), false);
                s.raycastTarget = false;
                s.rectTransform.position = worldPos;
                s.rectTransform.sizeDelta = Vector2.one * Random.Range(8f, 18f);
                var sp = s.gameObject.AddComponent<UiSpark>();
                sp.velocity = Random.insideUnitCircle.normalized * Random.Range(200f, 600f);
            }
        }
    }

    /// <summary>A loose part on the bench: drag it, drop it on its outline.</summary>
    public class WorkshopPart : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal EtienneWorkshop workshop;
        internal string partName;
        internal float trayScale = 1f;
        bool locked;
        Vector2 snapTarget;
        float snapT = -1f;
        Canvas canvas;

        public void OnBeginDrag(PointerEventData e)
        {
            if (locked) return;
            canvas = GetComponentInParent<Canvas>();
            transform.SetAsLastSibling();
            transform.localScale = Vector3.one * 1.08f;
            transform.localRotation = Quaternion.identity;
            Sfx.Drop();
        }

        public void OnDrag(PointerEventData e)
        {
            if (locked) return;
            float s = canvas != null ? canvas.scaleFactor : 1f;
            ((RectTransform)transform).anchoredPosition += e.delta / s;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (locked) return;
            if (!workshop.TryPlace(this)) transform.localScale = Vector3.one * Mathf.Max(trayScale, 0.85f);
        }

        internal void SnapTo(Vector2 target)
        {
            locked = true;
            snapTarget = target;
            snapT = 0f;
            transform.localRotation = Quaternion.identity;
            GetComponent<Image>().raycastTarget = false;
        }

        void Update()
        {
            if (snapT < 0f) return;
            snapT += Time.unscaledDeltaTime * 6f;
            var rt = (RectTransform)transform;
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, snapTarget, Mathf.Clamp01(snapT));
            transform.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Mathf.Clamp01(snapT) * Mathf.PI));
            if (snapT >= 1f) { rt.anchoredPosition = snapTarget; transform.localScale = Vector3.one; snapT = -1f; }
        }
    }

    /// <summary>A wire's starting terminal: drag from it to the terminal of the same colour.</summary>
    public class WorkshopTerminal : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal EtienneWorkshop workshop;
        internal int index;
        public void OnBeginDrag(PointerEventData e) => workshop.WireBegin(index);
        public void OnDrag(PointerEventData e) => workshop.WireDrag(index, e.position);
        public void OnEndDrag(PointerEventData e) => workshop.WireEnd(index, e.position);
    }

    /// <summary>A spark flying off and fading (UI).</summary>
    public class UiSpark : MonoBehaviour
    {
        internal Vector2 velocity;
        float life = 0.6f;
        Image img;

        void Awake() => img = GetComponent<Image>();

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            life -= dt;
            velocity += Vector2.down * 900f * dt;
            ((RectTransform)transform).anchoredPosition += velocity * dt;
            if (img != null) { var c = img.color; c.a = Mathf.Clamp01(life / 0.6f); img.color = c; }
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
