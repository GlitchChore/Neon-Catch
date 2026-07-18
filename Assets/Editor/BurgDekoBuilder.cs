using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace NeonCatch.Editor
{
    // Dekoriert eine BESTEHENDE Burg (vom BurgBuilder gebaut) – es wird nichts
    // gelöscht oder neu gebaut, nur dazugestellt:
    //  - Brunnen (Steinring + Wasser + Holzgestell mit Dach)
    //  - Markt: Tische mit Bänken, dazu Fässer/Kisten aus dem Kit
    //  - Fackeln mit echtem Punktlicht: auf dem Wehrgang verteilt, dazu genau
    //    ein symmetrisches Paar links/rechts am Tor
    //  - Fahnen auf den 4 Ecktürmen und beidseitig über dem Tor
    //  - Innendeko in allen Häusern (Tische, Stühle, Betten, Regale, Kerzen …)
    // Maßstab: alle selbstgebauten Deko-Teile skalieren automatisch mit der
    // gemessenen Wandhöhe (≈ Raumhöhe) der Burg – Proportionen und Detailgrad
    // bleiben gleich, nur die Größe passt zur Burg.
    // Alles landet unter "Burg/Deko-Extra". Nur diese Gruppe wird bei erneutem
    // Ausführen ersetzt – der Rest der Burg bleibt unangetastet.
    public class BurgDekoBuilder : EditorWindow
    {
        const string ASSET_PATH = "Assets/3D Objekte/Burg/";
        const string MAT_DIR    = "Assets/Deko-Materialien";
        const string GROUP_NAME = "Deko-Extra";

        // --- UI ---
        bool addBrunnen = true;
        bool addMarkt   = true;
        bool addFackeln = true;
        bool addFahnen  = true;
        bool addInnen   = true;
        bool addVerstecke = true;
        bool addGruenInsel = true;

        // Cache für die Pack-Prefab-Suche (pro Bau-Lauf geleert)
        readonly Dictionary<string, List<GameObject>> packCache = new Dictionary<string, List<GameObject>>();

        // --- Materialien ---
        Material stoneMat, woodMat, roofMat, waterMat, flagMat, flameMat;
        Material stoffMat, leinenMat, teppichMat;
        Material brotMat, kaeseMat, apfelMat, heuMat, grasMat;

        // Deko-Maßstab: 1.0 entspricht einer Raumhöhe (Wandhöhe) von 2.5 m.
        // Wird in Build() aus der gemessenen Wandhöhe abgeleitet, damit die
        // Möbel zur Burg passen statt zu fest verdrahteten Meter-Maßen.
        float k = 1f;

        [MenuItem("NeonCatch/Burg Deko %#d")]
        static void Open() => GetWindow<BurgDekoBuilder>("Burg Deko");

        void OnGUI()
        {
            GUILayout.Label("Burg Deko", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            addBrunnen = EditorGUILayout.Toggle("Brunnen", addBrunnen);
            addMarkt   = EditorGUILayout.Toggle("Markt (Tische + Bänke)", addMarkt);
            addFackeln = EditorGUILayout.Toggle("Fackeln mit Licht", addFackeln);
            addFahnen  = EditorGUILayout.Toggle("Fahnen auf Türmen", addFahnen);
            addInnen   = EditorGUILayout.Toggle("Innendeko (Häuser)", addInnen);
            addVerstecke = EditorGUILayout.Toggle("Verstecke (4 Deckungen)", addVerstecke);
            addGruenInsel = EditorGUILayout.Toggle("Grün-Inseln (Bäume + Sträucher)", addGruenInsel);

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Stellt Deko ZUSÄTZLICH in die bestehende Burg – nichts wird gelöscht.\n" +
                "Größen passen sich automatisch der Burg an (gemessene Wandhöhe).\n" +
                "Alles kommt in die Gruppe 'Deko-Extra'; nur diese wird bei erneutem Bauen ersetzt.",
                MessageType.None);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Deko dazu bauen", GUILayout.Height(45)))
                Build();
            GUI.backgroundColor = Color.white;
        }

        // ─────────────────────────────────────────────
        // internal statt private: der MapMittelalterBuilder ruft das direkt
        // auf einer unsichtbaren Instanz auf (kein Fenster nötig)
        internal void Build()
        {
            GameObject burg = null;
            // Nur Root-Objekte durchsuchen (statt veraltetem FindObjectsByType)
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == "Burg")
                    burg = go;

            if (burg == null)
            {
                Debug.LogError("BurgDekoBuilder: Keine 'Burg' in der Szene – erst mit dem Burg Builder bauen.");
                return;
            }

            var wallSrc = Load("wall.fbx");
            if (wallSrc == null) return;
            var (ts, wh) = MeasureDimensions(wallSrc);

            // Deko-Maßstab: aus der Wandhöhe (2.5 m Raumhöhe = Maßstab 1), zusätzlich
            // durch die Kachelbreite begrenzt, damit Möbelgruppen auch in die
            // kleinsten Räume (1 Kachel tief) passen und nicht durch Wände ragen
            k = Mathf.Clamp(Mathf.Min(wh / 2.5f, ts / 2.1f), 0.05f, 2f);

            // Burg-Größe ableiten und auf das Kachelraster runden. Wichtig: die
            // rohen Renderer-Bounds sind zu groß (Tor-Durchfahrt, Leitern an den
            // Türmen ragen nach außen) – dadurch standen Fackeln/Fahnen neben
            // der Mauer in der Luft. Abrunden aufs Raster ergibt die echte Wandlinie.
            float half = 12f * ts;
            var basis = burg.transform.Find("Mauerweg");
            if (basis == null) basis = burg.transform.Find("Außenmauern");
            if (basis != null)
            {
                Bounds b = CalcBounds(basis);
                if (b.extents.x > ts)
                    half = Mathf.Floor(b.extents.x / ts + 0.01f) * ts;
            }
            float   safeR = half - ts * 3f;
            Vector3 o     = burg.transform.position;

            LoadMaterials();
            packCache.Clear(); // neue Packs seit dem letzten Lauf mitnehmen
            kerzenNr = 0;

            Undo.SetCurrentGroupName("Burg Deko");
            int undoGroup = Undo.GetCurrentGroup();

            // Nur die eigene Deko-Gruppe ersetzen, sonst nichts anfassen
            var old = burg.transform.Find(GROUP_NAME);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var deko = Child(burg, GROUP_NAME);
            Undo.RegisterCreatedObjectUndo(deko, "Deko-Extra");

            Vector3 brunnenPos = o + new Vector3(-safeR * 0.35f, 0, -safeR * 0.5f);
            Vector3 marktPos   = o + new Vector3(0, 0, safeR * 0.72f);

            // ── Brunnen ──────────────────────────────────
            if (addBrunnen)
                BuildBrunnen(Child(deko, "Brunnen"), brunnenPos);

            // ── Markt: Tische + Bänke + Kit-Fässer ───────
            if (addMarkt)
            {
                var markt = Child(deko, "Markt");
                BuildTisch(markt, marktPos + new Vector3(-1.6f * k, 0, 0), 10f);
                BuildEssen(markt, marktPos + new Vector3(-1.6f * k, 0.82f * k, 0));
                BuildTisch(markt, marktPos + new Vector3( 1.8f * k, 0, 0.4f * k), -15f);
                BuildEssen(markt, marktPos + new Vector3( 1.8f * k, 0.82f * k, 0.4f * k));
                // Kit-Fässer/-Kisten: Abstand teils in Kachelmaß, damit die
                // ts-großen Kit-Objekte nicht in den k-skalierten Tischen stecken
                PlaceKit(markt, "detail-barrel.fbx", marktPos + new Vector3(-(1.9f * k + 0.55f * ts), 0, 0.8f * k), 30f);
                PlaceKit(markt, "detail-crate.fbx",  marktPos + new Vector3(2.2f * k + 0.55f * ts, 0, -0.6f * k), 10f);
                PlaceKit(markt, "detail-crate-small.fbx", marktPos + new Vector3(2.2f * k + 0.55f * ts, 0, 1.0f * k), 55f);
                // zweiter kleiner Sitzplatz bei der Ost-Kaserne
                BuildTisch(markt, o + new Vector3(safeR * 0.35f, 0, -safeR * 0.5f), 80f);
                BuildEssen(markt, o + new Vector3(safeR * 0.35f, 0.82f * k, -safeR * 0.5f));
            }

            // ── Fackeln mit Licht ────────────────────────
            // Alle Fackeln stehen auf dem Wehrgang (Mauerweg), 4 pro Seite.
            // Die t-Werte weichen den Mitteltürmen (t=0), den Zugangstreppen
            // (t=±0.5·half) und den Ecktürmen (t=±half) aus.
            if (addFackeln)
            {
                var fackeln = Child(deko, "Fackeln");
                float walkY = wh + 0.02f;
                float[] tPts = { -0.75f, -0.3f, 0.3f, 0.75f };
                for (int s = 0; s < 4; s++)
                {
                    // Nach innen versetzt: direkt an der inneren Zinnenreihe,
                    // fest auf den Mauerweg-Platten – nicht frei in der Luft
                    Vector3 inn = -SidePos(s, 0f, 1f) * (0.3f * ts);
                    foreach (float f in tPts)
                    {
                        Vector3 wp = SidePos(s, f * half, half) + inn;
                        BuildFackel(fackeln, o + new Vector3(wp.x, walkY, wp.z));
                    }
                }

                // Tor: genau zwei Fackeln, symmetrisch links und rechts der Durchfahrt
                BuildFackel(fackeln, o + new Vector3(-0.8f * ts, 0, -half + 1.2f * ts));
                BuildFackel(fackeln, o + new Vector3( 0.8f * ts, 0, -half + 1.2f * ts));
            }

            // ── Fahnen ───────────────────────────────────
            if (addFahnen)
            {
                var fahnen = Child(deko, "Fahnen");
                float capH = MeasureDimensions(Load("structure-wall.fbx")).height;
                float capY = wh + capH; // Oberkante der Eckturm-Kappe
                Vector3[] corners =
                {
                    new Vector3(-half, capY,  half),
                    new Vector3( half, capY,  half),
                    new Vector3( half, capY, -half),
                    new Vector3(-half, capY, -half),
                };
                foreach (var c in corners)
                    BuildFahne(fahnen, o + c);
                // beidseitig des Tors auf dem Wehrgang
                BuildFahne(fahnen, o + new Vector3(-1.5f * ts, wh, -half));
                BuildFahne(fahnen, o + new Vector3( 1.5f * ts, wh, -half));
            }

            // ── Verstecke: 4 Deckungs-Spots im Hof ───────
            // Prefabs aus "Medieval Props Lite" / "Simple Nature Pack" werden
            // automatisch gesucht; Fallback: Burg-Kit-Teile bzw. Primitives.
            // Alle Deckungen sind hoch genug, um sich dahinter zu verstecken.
            if (addVerstecke)
            {
                var spots = Child(deko, "Verstecke");

                // 1) Südwest: Fässergruppe + Kiste
                var s1 = Child(spots, "Deckung_Fässer");
                Vector3 v1 = o + new Vector3(-safeR * 0.3f, 0, -safeR * 0.7f);
                PlacePack(s1, new[] { "barrel" }, "barrels.fbx", v1, 20f, 1.1f * k);
                // Kit-Kiste statt Pack-Box: die Pack-Boxen wurden riesig skaliert
                PlaceKit(s1, "detail-crate.fbx", v1 + new Vector3(0.35f * ts, 0, 0.1f * ts), 45f);

                // 2) Ost: Karren mit Heu + Heuballen daneben
                var s2 = Child(spots, "Deckung_Karren");
                Vector3 v2 = o + new Vector3(safeR * 0.6f, 0, safeR * 0.35f);
                if (!PlacePack(s2, new[] { "cart", "wagon" }, null, v2, -25f, 1.4f * k))
                    BuildKarren(s2, v2, -25f);
                if (!PlacePack(s2, new[] { "hay" }, null, v2 + new Vector3(-0.35f * ts, 0, 0.15f * ts), 60f, 0.9f * k))
                    BuildHeuballen(s2, v2 + new Vector3(-0.35f * ts, 0, 0.15f * ts), 60f);

                // 3) West: Baum + 2 Büsche – mit natürlicher Wiese darunter
                var s3 = Child(spots, "Deckung_Grün");
                Vector3 v3 = o + new Vector3(-safeR * 0.6f, 0, safeR * 0.35f);
                BuildWiese(s3, v3, 1.2f * ts);
                // Nur echte Nature-Pack-Bäume/-Büsche (keine blockigen Kit-Bäume)
                PlacePack(s3, new[] { "tree" }, null, v3 + Vector3.up * 0.05f, 0f, 2.6f * k, 3, "trunk", "nature");
                PlacePack(s3, new[] { "bush", "shrub" }, null, v3 + new Vector3(0.4f * ts, 0.05f, 0.25f * ts), 90f, 0.9f * k, 0, "none", "nature");
                PlacePack(s3, new[] { "bush", "shrub" }, null, v3 + new Vector3(-0.35f * ts, 0.05f, -0.3f * ts), 200f, 0.9f * k, 1, "none", "nature");

                // 4) Südost: Kistenstapel + Fass (Kit-Kisten – keine Riesen-Boxen)
                var s4 = Child(spots, "Deckung_Kisten");
                Vector3 v4 = o + new Vector3(safeR * 0.35f, 0, -safeR * 0.75f);
                PlaceKit(s4, "detail-crate.fbx", v4, 10f);
                PlaceKit(s4, "detail-crate-ropes.fbx", v4 + new Vector3(0.3f * ts, 0, 0.2f * ts), 70f);
                PlacePack(s4, new[] { "barrel" }, "detail-barrel.fbx", v4 + new Vector3(-0.25f * ts, 0, 0.25f * ts), 0f, 1.0f * k);
            }

            // ── Schilder: Wegweiser am Tor und beim Markt ──
            {
                var schilder = Child(deko, "Schilder");
                PlacePack(schilder, new[] { "pointer", "sign" }, null,
                    o + new Vector3(1.2f * ts, 0, -half + 1.6f * ts), 200f, 1.4f * k, 0);
                if (addMarkt)
                    PlacePack(schilder, new[] { "pointer", "sign" }, null,
                        marktPos + new Vector3(-2.6f * k, 0, -2.2f * k), 150f, 1.4f * k, 1);
            }

            // ── Grün-Inseln: Bäume + Sträucher auf Gras ──
            // 3 Wiesen-Inseln im Hof, jede mit einem Pack-Baum und 2 Büschen.
            // Baum-/Busch-Sorte wechselt pro Insel (variant), Bäume blocken nur
            // am Stamm, Büsche sind durchlaufbar.
            if (addGruenInsel)
            {
                var gruen = Child(deko, "Grünanlagen");
                Vector2[] inseln =
                {
                    new Vector2(-0.78f, 0.45f),
                    new Vector2( 0.78f, 0.50f),
                    new Vector2( 0.12f, 0.90f),
                };
                for (int i = 0; i < inseln.Length; i++)
                {
                    Vector3 p = o + new Vector3(inseln[i].x * safeR, 0, inseln[i].y * safeR);
                    BuildWiese(gruen, p, 0.85f * ts);
                    PlacePack(gruen, new[] { "tree" }, null,
                        p + Vector3.up * 0.05f, i * 95f, 2.6f * k, i, "trunk", "nature");
                    PlacePack(gruen, new[] { "bush", "shrub" }, null,
                        p + new Vector3(0.35f * ts, 0.05f, -0.2f * ts), i * 40f, 0.85f * k, i, "none", "nature");
                    PlacePack(gruen, new[] { "bush", "shrub" }, null,
                        p + new Vector3(-0.3f * ts, 0.05f, 0.25f * ts), i * 40f + 120f, 0.85f * k, i + 1, "none", "nature");
                }
            }

            // ── Innendeko: Möbel in allen Häusern ────────
            if (addInnen)
                BuildInnenDeko(Child(deko, "Innendeko"), o, safeR, ts, wh);

            // Materialien der platzierten Pack-Objekte automatisch reparieren.
            // force=true: auch Pack-eigene Spezial-Shader werden auf URP/Lit
            // gezwungen – sonst bleiben einzelne Bäume lila.
            // Danach automatisch weiße Boden-Flächen begrünen.
            var fixErg = MaterialReparatur.Fix(new[] { deko }, true);
            MaterialFixer.BodenBegruenen();

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = deko;
            Debug.Log($"[BurgDekoBuilder] Deko dazugebaut (Maßstab {k:F2}) – Gruppe 'Burg/Deko-Extra'. " +
                      $"Materialien: {fixErg.repariert} umgestellt, {fixErg.ersetzt} ersetzt.");
        }

        // ─── Innendeko: Möbel in den Häusern ─────────────
        // Gebäude-Positionen/-Maße müssen zu BuildInterior im BurgBuilder passen.
        // Wichtig: die Nordost-Ecke ist in mehrstöckigen Häusern frei zu halten
        // (Leiter im EG, Deckenöffnung in den Obergeschossen). Die Türen liegen
        // jeweils in der Mitte der Türseite – dort bleibt der Laufweg frei.
        void BuildInnenDeko(GameObject parent, Vector3 o, float safeR, float ts, float wh)
        {
            float y1 = wh + 0.04f;       // Boden 1. OG (Deckenplatten liegen bei +0.02)
            float y2 = 2f * wh + 0.04f;  // Boden 2. OG (nur Keep)

            // Wandabstand: nie näher an eine Wand als 0.35 Kacheln ODER die halbe
            // Möbelgröße – so ragt nichts durch Wände nach außen
            float wm = Mathf.Max(0.9f * k, 0.35f * ts);

            // ── Keep (4×4, 3 Stockwerke, Tür Süd) ──
            Vector3 K = o + new Vector3(0, 0, safeR * 0.4f);
            var keep = Child(parent, "Keep");
            // EG: Halle – Teppich, großer Tisch mit Bänken, Regal, Fass
            BuildTeppich(keep, K + new Vector3(0, 0.03f, 0), new Vector2(3.4f, 2.4f) * k, 0f);
            BuildTisch(keep, K + new Vector3(-0.4f * k, 0, 0), 0f);
            BuildKerze(keep, K + new Vector3(-0.7f * k, 0.82f * k, 0));
            BuildEssen(keep, K + new Vector3(-0.2f * k, 0.82f * k, 0));
            BuildRegal(keep, K + new Vector3(-1.2f * k, 0, 2f * ts - wm), 180f);
            PlaceKit(keep, "detail-barrel.fbx", K + new Vector3(-2f * ts + 0.3f * ts, 0, -2f * ts + 0.3f * ts), 20f);
            // Rüstkammer-Ecke (Modular Castle): Rüstungs-Mannequin + Waffenständer
            PlacePack(keep, new[] { "mannequin" }, null,
                K + new Vector3(-2f * ts + wm, 0, 1.0f * k), 90f, 1.7f * k, 0, "box", "castle");
            PlacePack(keep, new[] { "stand" }, null,
                K + new Vector3(-2f * ts + wm, 0, -1.0f * k), 90f, 1.5f * k, 0, "box", "castle");
            // 1. OG: Schlafkammer – 2 Betten, Truhe, Tischchen mit Stuhl, Regal
            BuildBett(keep, K + new Vector3(-2f * ts + wm, y1, 0.4f * k), 0f);
            BuildBett(keep, K + new Vector3(-2f * ts + wm, y1, -2f * ts + wm + 1.2f * k), 0f);
            BuildTruhe(keep, K + new Vector3(-0.4f * k, y1, -2f * ts + wm), 0f);
            BuildRegal(keep, K + new Vector3(2f * ts - wm, y1, -1.2f * k), 270f);
            BuildTeppich(keep, K + new Vector3(0.3f * k, y1 + 0.03f, 0.2f * k), new Vector2(2.2f, 1.6f) * k, 0f);
            BuildTischKlein(keep, K + new Vector3(0.9f * k, y1, 0.4f * k), 30f);
            BuildStuhl(keep, K + new Vector3(0.9f * k, y1, 1.2f * k), 180f);
            BuildKerze(keep, K + new Vector3(0.9f * k, y1 + 0.78f * k, 0.4f * k));
            BuildKerzenstaender(keep, K + new Vector3(-0.9f * k, y1, -0.9f * k));
            // 2. OG: Stube – kleiner Tisch, 2 Stühle, Kerze + Essen, Teppich, Truhe
            BuildTeppich(keep, K + new Vector3(-0.2f * k, y2 + 0.03f, 0), new Vector2(2.6f, 1.8f) * k, 0f);
            BuildTischKlein(keep, K + new Vector3(-0.4f * k, y2, 0), 0f);
            BuildStuhl(keep, K + new Vector3(-0.4f * k, y2, 0.85f * k), 180f);
            BuildStuhl(keep, K + new Vector3(-0.4f * k, y2, -0.85f * k), 0f);
            BuildKerze(keep, K + new Vector3(-0.65f * k, y2 + 0.78f * k, 0));
            BuildEssen(keep, K + new Vector3(-0.2f * k, y2 + 0.78f * k, 0));
            BuildTruhe(keep, K + new Vector3(-2f * ts + wm, y2, -0.8f * k), 90f);

            // ── Kasernen (3×2, 2 Stockwerke, Tür Nord) ──
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 C = o + new Vector3(sx * safeR * 0.55f, 0, -safeR * 0.3f);
                var kas = Child(parent, sx < 0 ? "KaserneWest" : "KaserneOst");
                float hw = 1.5f * ts, hd = 1f * ts;
                // EG: Stube
                BuildTischKlein(kas, C + new Vector3(-0.8f * k, 0, -0.2f * k), 15f);
                BuildKerze(kas, C + new Vector3(-1.05f * k, 0.78f * k, -0.3f * k));
                BuildEssen(kas, C + new Vector3(-0.7f * k, 0.78f * k, -0.15f * k));
                BuildHocker(kas, C + new Vector3(-0.8f * k, 0, 0.55f * k));
                BuildHocker(kas, C + new Vector3(-1.5f * k, 0, -0.5f * k));
                BuildRegal(kas, C + new Vector3(0.8f * k, 0, -hd + wm * 0.7f), 0f);
                PlaceKit(kas, "detail-crate-small.fbx", C + new Vector3(-hw + 0.25f * ts, 0, -hd + 0.25f * ts), 40f);
                // Waffenständer (Modular Castle) – gehört in jede Kaserne
                PlacePack(kas, new[] { "stand" }, null,
                    C + new Vector3(hw - wm, 0, 0.4f * k), 270f, 1.5f * k, (int)sx + 1, "box", "castle");
                // OG: Schlafsaal. Die Nordost-Ecke hat KEINEN Boden (Deckenöffnung
                // für die Leiter) – das dritte Bett steht deshalb quer an der
                // Südwand in der Ost-Hälfte statt längs an der Ostwand, wo es
                // vorher über der Öffnung in der Luft hing.
                BuildBett(kas, C + new Vector3(-hw + wm, y1, 0.1f * k), 0f);
                BuildBett(kas, C + new Vector3(-0.2f * k, y1, 0.1f * k), 0f);
                BuildBett(kas, C + new Vector3(hw - wm - 0.5f * k, y1, -hd + wm), 90f);
                BuildKerzenstaender(kas, C + new Vector3(0.4f * k, y1, -hd + wm * 0.7f));
            }

            // ── Wachhäuser (2×1, Tür West-Haus: Ost / Ost-Haus: West) ──
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 W = o + new Vector3(sx * safeR * 0.28f, 0, 0);
                var wach = Child(parent, sx < 0 ? "WachhausWest" : "WachhausOst");
                float tischX = sx < 0 ? -ts + wm : ts - wm; // weg von der Tür
                BuildTischKlein(wach, W + new Vector3(tischX, 0, 0), 90f);
                BuildKerze(wach, W + new Vector3(tischX, 0.78f * k, 0.2f * k));
                BuildEssen(wach, W + new Vector3(tischX, 0.78f * k, -0.1f * k));
                BuildHocker(wach, W + new Vector3(tischX, 0, 0.7f * k));
                BuildHocker(wach, W + new Vector3(tischX, 0, -0.7f * k));
                PlaceKit(wach, "detail-barrel.fbx", W + new Vector3(sx < 0 ? ts - 0.3f * ts : -ts + 0.3f * ts, 0, -0.25f * ts), 10f);
            }

            // ── Lagerhäuser (2×2, Tür Süd) – voll mit Vorräten ──
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 L = o + new Vector3(sx * safeR * 0.5f, 0, safeR * 0.78f);
                var lager = Child(parent, sx < 0 ? "LagerNordWest" : "LagerNordOst");
                PlaceKit(lager, "barrels.fbx", L + new Vector3(0, 0, 0.45f * ts), 0f);
                PlaceKit(lager, "detail-crate.fbx", L + new Vector3(0.6f * ts, 0, 0.6f * ts), 15f);
                PlaceKit(lager, "detail-crate-small.fbx", L + new Vector3(0.65f * ts, 0, 0.05f * ts), 70f);
                PlaceKit(lager, "detail-barrel.fbx", L + new Vector3(-0.6f * ts, 0, 0.1f * ts), 0f);
                PlaceKit(lager, "detail-crate-ropes.fbx", L + new Vector3(-0.6f * ts, 0, 0.6f * ts), 30f);
                BuildRegal(lager, L + new Vector3(-ts + 0.35f * k, 0, -0.6f * ts), 90f);
            }

            // ── Torwachen (2×1, Tür Nord) ──
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 T = o + new Vector3(sx * safeR * 0.6f, 0, -safeR * 0.65f);
                var tor = Child(parent, sx < 0 ? "TorwacheWest" : "TorwacheOst");
                BuildTischKlein(tor, T + new Vector3(-0.5f * ts, 0, 0), 0f);
                BuildKerze(tor, T + new Vector3(-0.5f * ts - 0.3f * k, 0.78f * k, 0.15f * k));
                BuildEssen(tor, T + new Vector3(-0.5f * ts + 0.1f * k, 0.78f * k, 0));
                BuildHocker(tor, T + new Vector3(-0.5f * ts - 0.7f * k, 0, 0));
                BuildHocker(tor, T + new Vector3(-0.5f * ts + 0.7f * k, 0, 0));
                PlaceKit(tor, "detail-barrel.fbx", T + new Vector3(0.7f * ts, 0, -0.2f * ts), 45f);
            }
        }

        // ─── Möbel-Bausteine (skalieren mit k) ───────────
        void BuildStuhl(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Stuhl");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cube, "Sitz", grp, pos + new Vector3(0, 0.46f, 0) * k,
                new Vector3(0.45f, 0.06f, 0.45f) * k, woodMat);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Prim(PrimitiveType.Cube, "Bein", grp, pos + new Vector3(sx * 0.18f, 0.22f, sz * 0.18f) * k,
                        new Vector3(0.05f, 0.44f, 0.05f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Lehne", grp, pos + new Vector3(0, 0.75f, -0.2f) * k,
                new Vector3(0.45f, 0.55f, 0.05f) * k, woodMat);
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        void BuildHocker(GameObject parent, Vector3 pos)
        {
            Prim(PrimitiveType.Cylinder, "Hocker", parent, pos + new Vector3(0, 0.225f, 0) * k,
                new Vector3(0.38f, 0.225f, 0.38f) * k, woodMat);
        }

        void BuildTischKlein(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "TischKlein");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cube, "Platte", grp, pos + new Vector3(0, 0.745f, 0) * k,
                new Vector3(1.3f, 0.07f, 0.85f) * k, woodMat);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Prim(PrimitiveType.Cube, "Bein", grp, pos + new Vector3(sx * 0.55f, 0.355f, sz * 0.32f) * k,
                        new Vector3(0.09f, 0.71f, 0.09f) * k, woodMat);
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // Großer Tisch mit zwei Bänken
        void BuildTisch(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Tisch");
            grp.transform.position = pos;

            Prim(PrimitiveType.Cube, "Platte", grp, pos + new Vector3(0, 0.78f, 0) * k,
                new Vector3(2.0f, 0.08f, 0.9f) * k, woodMat);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Prim(PrimitiveType.Cube, "Bein", grp, pos + new Vector3(sx * 0.85f, 0.37f, sz * 0.3f) * k,
                        new Vector3(0.1f, 0.74f, 0.1f) * k, woodMat);

            for (int sz = -1; sz <= 1; sz += 2)
            {
                Prim(PrimitiveType.Cube, "Bank", grp, pos + new Vector3(0, 0.45f, sz * 0.75f) * k,
                    new Vector3(2.0f, 0.06f, 0.35f) * k, woodMat);
                for (int sx = -1; sx <= 1; sx += 2)
                    Prim(PrimitiveType.Cube, "Bankfuss", grp, pos + new Vector3(sx * 0.8f, 0.21f, sz * 0.75f) * k,
                        new Vector3(0.1f, 0.42f, 0.3f) * k, woodMat);
            }

            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // Bett: Länge entlang Z, Kopfende bei +Z
        void BuildBett(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Bett");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cube, "Rahmen", grp, pos + new Vector3(0, 0.15f, 0) * k,
                new Vector3(1.05f, 0.30f, 2.05f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Matratze", grp, pos + new Vector3(0, 0.36f, 0) * k,
                new Vector3(0.95f, 0.18f, 1.95f) * k, leinenMat);
            Prim(PrimitiveType.Cube, "Decke", grp, pos + new Vector3(0, 0.47f, -0.35f) * k,
                new Vector3(0.97f, 0.08f, 1.20f) * k, stoffMat);
            Prim(PrimitiveType.Cube, "Kissen", grp, pos + new Vector3(0, 0.48f, 0.70f) * k,
                new Vector3(0.60f, 0.12f, 0.40f) * k, leinenMat);
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // Regal: offene Seite zeigt lokal nach +Z
        void BuildRegal(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Regal");
            grp.transform.position = pos;
            for (int sx = -1; sx <= 1; sx += 2)
                Prim(PrimitiveType.Cube, "Seite", grp, pos + new Vector3(sx * 0.55f, 0.9f, 0) * k,
                    new Vector3(0.08f, 1.8f, 0.35f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Rueckwand", grp, pos + new Vector3(0, 0.9f, -0.17f) * k,
                new Vector3(1.10f, 1.80f, 0.04f) * k, woodMat);
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cube, "Brett", grp, pos + new Vector3(0, 0.45f + i * 0.5f, 0) * k,
                    new Vector3(1.05f, 0.05f, 0.33f) * k, woodMat);

            // Inhalt auf den Brettern (ohne Collider, rein dekorativ):
            // unten Vorräte, Mitte Essen, oben Krüge und Schachteln
            var inhalt = Child(grp, "Inhalt");
            // Brett unten (Oberkante ~0.475)
            Prim(PrimitiveType.Cube, "Kistchen", inhalt, pos + new Vector3(-0.28f, 0.59f, 0) * k,
                new Vector3(0.26f, 0.22f, 0.24f) * k, woodMat, new Vector3(0, 8, 0));
            Prim(PrimitiveType.Cube, "Sack", inhalt, pos + new Vector3(0.10f, 0.56f, 0.02f) * k,
                new Vector3(0.22f, 0.17f, 0.20f) * k, leinenMat, new Vector3(0, -12, 0));
            Prim(PrimitiveType.Cylinder, "Krug", inhalt, pos + new Vector3(0.36f, 0.58f, 0) * k,
                new Vector3(0.15f, 0.10f, 0.15f) * k, woodMat);
            // Brett Mitte (Oberkante ~0.975)
            Prim(PrimitiveType.Cube, "Brot", inhalt, pos + new Vector3(-0.30f, 1.05f, 0) * k,
                new Vector3(0.22f, 0.10f, 0.13f) * k, brotMat, new Vector3(0, 30, 0));
            Prim(PrimitiveType.Cube, "Kaese", inhalt, pos + new Vector3(0.02f, 1.04f, -0.02f) * k,
                new Vector3(0.16f, 0.09f, 0.16f) * k, kaeseMat, new Vector3(0, -20, 0));
            Prim(PrimitiveType.Sphere, "Apfel", inhalt, pos + new Vector3(0.24f, 1.03f, 0.03f) * k,
                new Vector3(0.11f, 0.11f, 0.11f) * k, apfelMat);
            Prim(PrimitiveType.Sphere, "Apfel2", inhalt, pos + new Vector3(0.38f, 1.03f, -0.04f) * k,
                new Vector3(0.11f, 0.11f, 0.11f) * k, apfelMat);
            // Brett oben (Oberkante ~1.475)
            Prim(PrimitiveType.Cylinder, "Krug2", inhalt, pos + new Vector3(-0.33f, 1.58f, 0) * k,
                new Vector3(0.15f, 0.10f, 0.15f) * k, woodMat);
            Prim(PrimitiveType.Cylinder, "Krug3", inhalt, pos + new Vector3(-0.12f, 1.58f, 0.04f) * k,
                new Vector3(0.15f, 0.10f, 0.15f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Schachtel", inhalt, pos + new Vector3(0.25f, 1.55f, 0) * k,
                new Vector3(0.30f, 0.14f, 0.22f) * k, stoffMat, new Vector3(0, 15, 0));
            Prim(PrimitiveType.Cube, "Schachtel2", inhalt, pos + new Vector3(0.22f, 1.66f, 0.02f) * k,
                new Vector3(0.22f, 0.09f, 0.16f) * k, teppichMat, new Vector3(0, -10, 0));
            RemoveColliders(inhalt);

            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        void BuildTeppich(GameObject parent, Vector3 pos, Vector2 size, float rotY)
        {
            // Echter Pack-Teppich (Carpet_01 aus Medieval Props), passend über
            // die Grundfläche skaliert; sonst der bisherige einfarbige
            if (PlacePackFlach(parent, new[] { "carpet" }, pos, rotY, Mathf.Max(size.x, size.y)) != null)
                return;

            var go = Prim(PrimitiveType.Cube, "Teppich", parent, pos,
                new Vector3(size.x, 0.02f * k, size.y), teppichMat, new Vector3(0, rotY, 0));
            RemoveColliders(go);
        }

        // Kerze: pos = Fußpunkt (z.B. Tischplatte), kleines warmes Licht.
        // Nutzt bevorzugt eine Pack-Kerze (Candle_01…/candle1…), sonst Eigenbau.
        void BuildKerze(GameObject parent, Vector3 pos)
        {
            var grp = Child(parent, "Kerze");
            grp.transform.position = pos;

            // "candle" matcht auch die candleholder-Modelle – die hier
            // ausfiltern, sonst steht ein Mini-Kerzenständer auf dem Tisch
            GameObject packKerze = null;
            var kerzenListe = FindPackAll(new[] { "candle" })
                .FindAll(g2 => !g2.name.ToLower().Contains("holder"));
            if (kerzenListe.Count > 0)
            {
                var src2 = kerzenListe[kerzenNr++ % kerzenListe.Count];
                packKerze = (GameObject)PrefabUtility.InstantiatePrefab(src2, grp.transform);
                packKerze.transform.SetPositionAndRotation(pos, Quaternion.identity);
                float h2 = MeasureDimensions(src2).height;
                if (h2 > 0.01f)
                    packKerze.transform.localScale = src2.transform.localScale * (0.28f * k / h2);
                RemoveColliders(packKerze);
            }
            if (packKerze == null)
            {
                var w = Prim(PrimitiveType.Cylinder, "Wachs", grp, pos + new Vector3(0, 0.11f, 0) * k,
                    new Vector3(0.06f, 0.11f, 0.06f) * k, leinenMat);
                RemoveColliders(w);
                var f = Prim(PrimitiveType.Sphere, "Flamme", grp, pos + new Vector3(0, 0.26f, 0) * k,
                    new Vector3(0.07f, 0.10f, 0.07f) * k, flameMat);
                RemoveColliders(f);
            }

            var lichtGo = new GameObject("Licht");
            lichtGo.transform.SetParent(grp.transform, false);
            lichtGo.transform.position = pos + new Vector3(0, 0.35f, 0) * k;
            var licht = lichtGo.AddComponent<Light>();
            licht.type      = LightType.Point;
            licht.color     = new Color(1f, 0.68f, 0.35f);
            licht.intensity = 1.3f;
            licht.range     = Mathf.Max(2f, 5f * k);
            licht.shadows   = LightShadows.None;
        }
        int kerzenNr; // wechselnde Kerzen-Variante

        // Kerzenständer: bevorzugt das Modular-Castle-Modell (candleholder1/2),
        // passend skaliert und mit Licht obendrauf; sonst Eigenbau.
        void BuildKerzenstaender(GameObject parent, Vector3 pos)
        {
            var packStaender = PlacePackGo(parent, new[] { "candleholder" }, pos, 0f, 1.25f * k, kerzenNr++, "none");
            if (packStaender != null)
            {
                var lichtGo = new GameObject("Licht");
                lichtGo.transform.SetParent(packStaender.transform, true);
                lichtGo.transform.position = pos + new Vector3(0, 1.3f, 0) * k;
                var licht = lichtGo.AddComponent<Light>();
                licht.type      = LightType.Point;
                licht.color     = new Color(1f, 0.68f, 0.35f);
                licht.intensity = 1.3f;
                licht.range     = Mathf.Max(2f, 5f * k);
                licht.shadows   = LightShadows.None;
                return;
            }

            var grp = Child(parent, "Kerzenständer");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cylinder, "Fuss", grp, pos + new Vector3(0, 0.03f, 0) * k,
                new Vector3(0.30f, 0.03f, 0.30f) * k, woodMat);
            Prim(PrimitiveType.Cylinder, "Stange", grp, pos + new Vector3(0, 0.6f, 0) * k,
                new Vector3(0.06f, 0.57f, 0.06f) * k, woodMat);
            BuildKerze(grp, pos + new Vector3(0, 1.17f, 0) * k);
        }

        // ─── Pack-Prefab platzieren (mit Kit-Fallback) ───
        // Sucht ALLE Prefabs, deren Name einen der Suchbegriffe enthält und
        // deren Pfad nach einem Asset-Store-Pack aussieht; sortiert nach Name.
        // 'variant' wählt daraus abwechselnd (i % Anzahl) – für Sortenvielfalt.
        // Skaliert gleichmäßig auf targetH (keine Verzerrung).
        // collider: "box" (Standard), "trunk" (nur Stamm-Kapsel, für Bäume),
        // "none" (durchlaufbar, z.B. Büsche).
        // pfadMuss: optionaler Zusatzfilter, z.B. "nature" → nur Prefabs aus
        // Nature-Packs (gegen blockige Bäume/Steine aus anderen Kits)
        List<GameObject> FindPackAll(string[] nameKeys, string pfadMuss = null)
        {
            string cacheKey = string.Join("|", nameKeys) + "#" + pfadMuss;
            if (packCache.TryGetValue(cacheKey, out var cached)) return cached;

            var list = new List<GameObject>();
            foreach (string nk in nameKeys)
                foreach (string guid in AssetDatabase.FindAssets(nk + " t:Prefab"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string pl   = path.ToLower().Replace(" ", "").Replace("-", "").Replace("_", "");
                    if (!pl.Contains("medieval") && !pl.Contains("props")
                        && !pl.Contains("nature") && !pl.Contains("lowpoly")
                        && !pl.Contains("farm") && !pl.Contains("castle")) continue;
                    if (pfadMuss != null && !pl.Contains(pfadMuss)) continue;
                    if (pl.Contains("/burg/")) continue; // eigenes Kit nicht doppelt
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null && !list.Contains(go)) list.Add(go);
                }
            list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            packCache[cacheKey] = list;
            return list;
        }

        GameObject PlacePackGo(GameObject parent, string[] nameKeys, Vector3 pos, float rotY,
            float targetH, int variant, string collider, string pfadMuss = null)
        {
            var list = FindPackAll(nameKeys, pfadMuss);
            if (list.Count == 0) return null;

            var src = list[variant % list.Count];
            var go  = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
            float h = MeasureDimensions(src).height;
            if (h > 0.01f)
                // Original-Root-Skala multiplizieren statt ersetzen – manche
                // Packs (z.B. Modular Castle "box") haben am Root schon eine
                // Skala; sie zu überschreiben machte die Teile riesig
                go.transform.localScale = src.transform.localScale * (targetH / h);

            if (collider == "trunk")
            {
                // Nur der Stamm blockiert – eine Box um die ganze Krone würde
                // den Spieler schon meterweit vor dem Baum stoppen.
                // Kapsel im LOKALEN Raum: gemessene Höhe enthält die Root-Skala
                // bereits → für lokale Werte wieder herausrechnen
                RemoveColliders(go);
                float lokalH = h / Mathf.Max(0.0001f, src.transform.localScale.y);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0, lokalH * 0.5f, 0);
                cap.height = lokalH;
                cap.radius = Mathf.Max(0.05f, lokalH * 0.06f);
            }
            else if (collider == "none")
                RemoveColliders(go);
            else
                EnsureCollider(go);
            return go;
        }

        bool PlacePack(GameObject parent, string[] nameKeys, string fallbackKit,
            Vector3 pos, float rotY, float targetH, int variant = 0, string collider = "box", string pfadMuss = null)
        {
            if (PlacePackGo(parent, nameKeys, pos, rotY, targetH, variant, collider, pfadMuss) != null)
                return true;
            if (fallbackKit == null) return false;
            PlaceKit(parent, fallbackKit, pos, rotY);
            return true;
        }

        // Flache Teile (Teppiche): über die GRUNDFLÄCHE skaliert statt über
        // die Höhe – Höhe ≈ 0 würde eine absurde Skalierung ergeben
        GameObject PlacePackFlach(GameObject parent, string[] nameKeys, Vector3 pos, float rotY,
            float targetBreite, int variant = 0)
        {
            var list = FindPackAll(nameKeys);
            if (list.Count == 0) return null;

            var src = list[variant % list.Count];
            var go  = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
            float fp = MeasureDimensions(src).footprint;
            if (fp > 0.01f)
                go.transform.localScale = src.transform.localScale * (targetBreite / fp);
            RemoveColliders(go);
            return go;
        }

        // ─── Karren (Fallback, solange kein Props-Pack da ist) ───
        void BuildKarren(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Karren");
            grp.transform.position = pos;

            Prim(PrimitiveType.Cube, "Ladefläche", grp, pos + new Vector3(0, 0.55f, 0) * k,
                new Vector3(1.5f, 0.08f, 0.8f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Brett_N", grp, pos + new Vector3(0, 0.72f, 0.4f) * k,
                new Vector3(1.5f, 0.26f, 0.05f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Brett_S", grp, pos + new Vector3(0, 0.72f, -0.4f) * k,
                new Vector3(1.5f, 0.26f, 0.05f) * k, woodMat);
            // Räder: Zylinderachse quer zum Karren
            Prim(PrimitiveType.Cylinder, "Rad_N", grp, pos + new Vector3(0, 0.35f, 0.45f) * k,
                new Vector3(0.7f, 0.03f, 0.7f) * k, woodMat, new Vector3(90, 0, 0));
            Prim(PrimitiveType.Cylinder, "Rad_S", grp, pos + new Vector3(0, 0.35f, -0.45f) * k,
                new Vector3(0.7f, 0.03f, 0.7f) * k, woodMat, new Vector3(90, 0, 0));
            // Deichsel vorne, schräg zum Boden
            Prim(PrimitiveType.Cylinder, "Deichsel", grp, pos + new Vector3(1.0f, 0.3f, 0) * k,
                new Vector3(0.06f, 0.45f, 0.06f) * k, woodMat, new Vector3(0, 0, 65));
            // Heu auf der Ladefläche
            Prim(PrimitiveType.Cube, "Heu", grp, pos + new Vector3(-0.15f, 0.78f, 0) * k,
                new Vector3(0.9f, 0.35f, 0.6f) * k, heuMat, new Vector3(0, 10, 0));

            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // ─── Heuballen-Stapel (Fallback) ─────────────────
        void BuildHeuballen(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Heuballen");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cube, "Ballen1", grp, pos + new Vector3(-0.3f, 0.21f, 0) * k,
                new Vector3(0.55f, 0.42f, 0.42f) * k, heuMat, new Vector3(0, 5, 0));
            Prim(PrimitiveType.Cube, "Ballen2", grp, pos + new Vector3(0.3f, 0.21f, 0.05f) * k,
                new Vector3(0.55f, 0.42f, 0.42f) * k, heuMat, new Vector3(0, -12, 0));
            Prim(PrimitiveType.Cube, "Ballen3", grp, pos + new Vector3(0, 0.62f, 0.02f) * k,
                new Vector3(0.55f, 0.42f, 0.42f) * k, heuMat, new Vector3(0, 30, 0));
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // ─── Natürliche Wiese ────────────────────────────
        // Statt einer perfekten runden Scheibe: 3 überlappende, verschieden
        // gedrehte flache Ellipsen (unregelmäßiger Umriss) und viele zufällig
        // verstreute Gras-Büschel aus dem Farm-Pack in verschiedenen Größen.
        void BuildWiese(GameObject parent, Vector3 pos, float r)
        {
            var grp = Child(parent, "Wiese");
            grp.transform.position = pos;

            for (int i = 0; i < 3; i++)
            {
                float ga = (i * 117f) * Mathf.Deg2Rad;
                Vector3 off = new Vector3(Mathf.Cos(ga), 0, Mathf.Sin(ga)) * r * 0.28f;
                var scheibe = Prim(PrimitiveType.Cylinder, "Rasen" + i, grp,
                    pos + off + Vector3.up * (0.045f + i * 0.004f),
                    new Vector3(r * (1.6f - i * 0.25f), 0.012f, r * (1.15f - i * 0.15f)),
                    grasMat, new Vector3(0, i * 63f, 0));
                RemoveColliders(scheibe);
            }

            // 10 Gras-Büschel, zufällig in Position, Drehung und Größe
            for (int g = 0; g < 10; g++)
            {
                Vector2 rnd = Random.insideUnitCircle * r * 0.9f;
                Vector3 gp = pos + new Vector3(rnd.x, 0.05f, rnd.y);
                PlacePack(grp, new[] { "grass" }, null, gp,
                    Random.Range(0f, 360f), Random.Range(0.2f, 0.4f) * k, g, "none");
            }
        }

        // ─── Essen (auf einer Tischplatte, pos = Plattenmitte oben) ───
        // Offsets bleiben unter 0.35·k, damit alles auch auf gedrehten
        // Tischen sicher auf der Platte liegt. Ohne Collider.
        void BuildEssen(GameObject parent, Vector3 pos)
        {
            var grp = Child(parent, "Essen");
            grp.transform.position = pos;

            Prim(PrimitiveType.Cylinder, "Teller", grp, pos + new Vector3(-0.22f, 0.015f, 0.10f) * k,
                new Vector3(0.30f, 0.015f, 0.30f) * k, leinenMat);
            Prim(PrimitiveType.Cube, "Brot", grp, pos + new Vector3(-0.22f, 0.08f, 0.10f) * k,
                new Vector3(0.22f, 0.10f, 0.13f) * k, brotMat, new Vector3(0, 25, 0));
            Prim(PrimitiveType.Cube, "Kaese", grp, pos + new Vector3(0.15f, 0.05f, -0.15f) * k,
                new Vector3(0.16f, 0.09f, 0.16f) * k, kaeseMat, new Vector3(0, -15, 0));
            Prim(PrimitiveType.Sphere, "Apfel", grp, pos + new Vector3(0.05f, 0.055f, 0.20f) * k,
                new Vector3(0.11f, 0.11f, 0.11f) * k, apfelMat);
            Prim(PrimitiveType.Cylinder, "Krug", grp, pos + new Vector3(0.30f, 0.11f, 0.05f) * k,
                new Vector3(0.14f, 0.11f, 0.14f) * k, woodMat);

            RemoveColliders(grp);
        }

        // ─── Truhe ───────────────────────────────────────
        void BuildTruhe(GameObject parent, Vector3 pos, float rotY)
        {
            var grp = Child(parent, "Truhe");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cube, "Korpus", grp, pos + new Vector3(0, 0.25f, 0) * k,
                new Vector3(0.90f, 0.50f, 0.50f) * k, woodMat);
            Prim(PrimitiveType.Cube, "Deckel", grp, pos + new Vector3(0, 0.54f, 0) * k,
                new Vector3(0.94f, 0.10f, 0.54f) * k, stoffMat);
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // ─── Brunnen ─────────────────────────────────────
        // Steinring mit Wasser, zwei Holzpfosten mit Querbalken und kleinem
        // Satteldach – skaliert mit k.
        void BuildBrunnen(GameObject parent, Vector3 pos)
        {
            Prim(PrimitiveType.Cylinder, "Steinring", parent, pos + new Vector3(0, 0.45f, 0) * k,
                new Vector3(1.7f, 0.45f, 1.7f) * k, stoneMat);
            var wasser = Prim(PrimitiveType.Cylinder, "Wasser", parent, pos + new Vector3(0, 0.40f, 0) * k,
                new Vector3(1.45f, 0.40f, 1.45f) * k, waterMat);
            RemoveColliders(wasser);

            Prim(PrimitiveType.Cylinder, "Pfosten_W", parent, pos + new Vector3(-0.95f, 1.0f, 0) * k,
                new Vector3(0.13f, 1.0f, 0.13f) * k, woodMat);
            Prim(PrimitiveType.Cylinder, "Pfosten_O", parent, pos + new Vector3( 0.95f, 1.0f, 0) * k,
                new Vector3(0.13f, 1.0f, 0.13f) * k, woodMat);
            Prim(PrimitiveType.Cylinder, "Querbalken", parent, pos + new Vector3(0, 1.9f, 0) * k,
                new Vector3(0.10f, 1.05f, 0.10f) * k, woodMat, new Vector3(0, 0, 90));

            Prim(PrimitiveType.Cube, "Dach_N", parent, pos + new Vector3(0, 2.32f, 0.42f) * k,
                new Vector3(2.4f, 0.06f, 1.1f) * k, roofMat, new Vector3(-35, 0, 0));
            Prim(PrimitiveType.Cube, "Dach_S", parent, pos + new Vector3(0, 2.32f, -0.42f) * k,
                new Vector3(2.4f, 0.06f, 1.1f) * k, roofMat, new Vector3(35, 0, 0));

            Prim(PrimitiveType.Cylinder, "Seil", parent, pos + new Vector3(0, 1.55f, 0) * k,
                new Vector3(0.03f, 0.35f, 0.03f) * k, woodMat);
            Prim(PrimitiveType.Cylinder, "Eimer", parent, pos + new Vector3(0, 1.12f, 0) * k,
                new Vector3(0.3f, 0.14f, 0.3f) * k, woodMat);
        }

        // ─── Stand-Fackel mit Punktlicht ─────────────────
        void BuildFackel(GameObject parent, Vector3 pos)
        {
            var grp = Child(parent, "Fackel");
            grp.transform.position = pos;

            Prim(PrimitiveType.Cylinder, "Stab", grp, pos + new Vector3(0, 0.85f, 0) * k,
                new Vector3(0.09f, 0.85f, 0.09f) * k, woodMat);
            var flamme = Prim(PrimitiveType.Sphere, "Flamme", grp, pos + new Vector3(0, 1.78f, 0) * k,
                new Vector3(0.24f, 0.3f, 0.24f) * k, flameMat);
            RemoveColliders(flamme);

            var lichtGo = new GameObject("Licht");
            lichtGo.transform.SetParent(grp.transform, false);
            lichtGo.transform.position = pos + new Vector3(0, 1.9f, 0) * k;
            var licht = lichtGo.AddComponent<Light>();
            licht.type      = LightType.Point;
            licht.color     = new Color(1f, 0.62f, 0.28f);
            licht.intensity = 2.2f;
            licht.range     = Mathf.Max(3f, 8f * k);
            licht.shadows   = LightShadows.None;
        }

        // ─── Fahne (Mast + rotes Banner) ─────────────────
        void BuildFahne(GameObject parent, Vector3 pos)
        {
            var grp = Child(parent, "Fahne");
            grp.transform.position = pos;

            var mast = Prim(PrimitiveType.Cylinder, "Mast", grp, pos + new Vector3(0, 1.5f, 0) * k,
                new Vector3(0.08f, 1.5f, 0.08f) * k, woodMat);
            RemoveColliders(mast);
            var banner = Prim(PrimitiveType.Cube, "Banner", grp, pos + new Vector3(0.5f, 2.6f, 0) * k,
                new Vector3(0.95f, 0.55f, 0.04f) * k, flagMat);
            RemoveColliders(banner);
        }

        // ─── Materialien ─────────────────────────────────
        void LoadMaterials()
        {
            stoneMat = AssetDatabase.LoadAssetAtPath<Material>(ASSET_PATH + "Materials/cobblestoneAlternative.mat");
            waterMat = AssetDatabase.LoadAssetAtPath<Material>(ASSET_PATH + "Materials/water.mat");
            woodMat  = FirstMaterial("fence-wood.fbx");
            roofMat  = FirstMaterial("roof.fbx");
            flagMat  = GetOrCreateMat("FahneRot", new Color(0.62f, 0.08f, 0.08f), false);
            flameMat = GetOrCreateMat("Flamme",   new Color(1f, 0.55f, 0.15f), true);
            stoffMat   = GetOrCreateMat("Stoff",   new Color(0.30f, 0.42f, 0.24f), false); // Bettdecke
            leinenMat  = GetOrCreateMat("Leinen",  new Color(0.92f, 0.88f, 0.80f), false); // Matratze/Kissen/Kerzen
            teppichMat = GetOrCreateMat("Teppich", new Color(0.45f, 0.10f, 0.10f), false);
            brotMat    = GetOrCreateMat("Brot",    new Color(0.62f, 0.44f, 0.22f), false);
            kaeseMat   = GetOrCreateMat("Kaese",   new Color(0.90f, 0.76f, 0.30f), false);
            apfelMat   = GetOrCreateMat("Apfel",   new Color(0.72f, 0.12f, 0.10f), false);
            heuMat     = GetOrCreateMat("Heu",     new Color(0.85f, 0.68f, 0.28f), false);
            grasMat    = GetOrCreateMat("Gras",    new Color(0.30f, 0.52f, 0.22f), false);

            // Prozedurale Rasen-Textur für die Wiese: die "grass01"-Texturen
            // aus den Packs sind Büschel-SPRITES mit transparentem Hintergrund
            // (wirkten weiß bzw. nach Tönung nur flach grün). Die generierte
            // Textur ist echter, kachelbarer Rasen mit Sprenkeln.
            grasMat.mainTexture = MaterialFixer.HoleGrasTextur();
            grasMat.mainTextureScale = new Vector2(3f, 3f);
            grasMat.color = Color.white;
            EditorUtility.SetDirty(grasMat);

            if (stoneMat == null) stoneMat = flagMat; // Notnagel, sollte nicht passieren
            if (woodMat  == null) woodMat  = stoneMat;
            if (roofMat  == null) roofMat  = woodMat;
            if (waterMat == null) waterMat = stoneMat;
        }

        Material FirstMaterial(string fbx)
        {
            var src = Load(fbx);
            if (src == null) return null;
            var r = src.GetComponentInChildren<Renderer>();
            return r != null && r.sharedMaterials.Length > 0 ? r.sharedMaterial : null;
        }

        static Material GetOrCreateMat(string name, Color c, bool emissive)
        {
            if (!AssetDatabase.IsValidFolder(MAT_DIR))
                AssetDatabase.CreateFolder("Assets", "Deko-Materialien");
            string path = MAT_DIR + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader) { color = c };
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 3f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ─── Hilfsmethoden ───────────────────────────────
        static GameObject Prim(PrimitiveType type, string name, GameObject parent,
            Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, true);
            go.transform.position   = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.rotation = Quaternion.Euler(euler.Value);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        void PlaceKit(GameObject parent, string fbx, Vector3 pos, float rotY)
        {
            var src = Load(fbx);
            if (src == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
            EnsureCollider(go);
        }

        static Bounds CalcBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        static void EnsureCollider(GameObject go)
        {
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;
                Bounds b = mf.sharedMesh.bounds;
                var bc = mf.gameObject.AddComponent<BoxCollider>();
                bc.center = b.center;
                bc.size   = b.size;
            }
        }

        static void RemoveColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                DestroyImmediate(c);
        }

        static (float footprint, float height) MeasureDimensions(GameObject src)
        {
            float maxXZ = 0f, maxY = 0f;
            if (src != null)
                foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    Vector3 sc = mf.transform.lossyScale;
                    Bounds  mb = mf.sharedMesh.bounds;
                    maxXZ = Mathf.Max(maxXZ, mb.size.x * Mathf.Abs(sc.x), mb.size.z * Mathf.Abs(sc.z));
                    maxY  = Mathf.Max(maxY,  mb.size.y * Mathf.Abs(sc.y));
                }
            float fp = maxXZ > 0.01f ? maxXZ : 1f;
            float h  = maxY  > 0.01f ? maxY  : fp;
            return (fp, h);
        }

        static Vector3 SidePos(int side, float t, float half)
        {
            if (side == 0) return new Vector3( t,    0,  half);
            if (side == 1) return new Vector3( half, 0, -t);
            if (side == 2) return new Vector3(-t,    0, -half);
                           return new Vector3(-half, 0,  t);
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static GameObject Load(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ASSET_PATH + name);
            if (go == null) Debug.LogWarning($"BurgDekoBuilder: '{name}' nicht gefunden.");
            return go;
        }
    }
}
