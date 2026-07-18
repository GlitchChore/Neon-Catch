using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using NeonCatch;

namespace NeonCatch.Editor
{
    public class BurgBuilder : EditorWindow
    {
        const string ASSET_PATH = "Assets/3D Objekte/Burg/";

        // --- UI ---
        int   wallsPerSide   = 24;
        bool  autoTileSize   = true;
        float tileSize       = 1f;
        bool  fortifiedWalls = true;
        bool  addBattlements = true;
        bool  addDeco        = true;
        int   decoCount      = 10;

        float lastDetectedSize   = 0f;
        float lastDetectedHeight = 0f;

        // --- Außenmauern ---
        GameObject outerWallSrc, outerWallHalfSrc, outerGateSrc, battlementSrc;
        GameObject battlementCornerInnerSrc, battlementCornerOuterSrc;
        GameObject towerWallBaseSrc;
        GameObject outerFloorSrc;
        GameObject barrelSrc, crateSrc;
        GameObject structureWallSrc, fenceWoodSrc;

        // --- Innengebäude ---
        GameObject bWallSrc, bWallWindowSrc, bWallDoorSrc;
        GameObject bFloorSrc;
        GameObject bStairsSrc, bLadderSrc;
        GameObject columnSrc;
        GameObject roofSrc;

        // --- Himmelswege ---
        GameObject woodFloorSrc, overhangRoundSrc, structurePoleSrc;

        [MenuItem("NeonCatch/Burg Builder %#b")]
        static void Open() => GetWindow<BurgBuilder>("Burg Builder");

        void OnGUI()
        {
            GUILayout.Label("Burg Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            wallsPerSide   = EditorGUILayout.IntSlider("Mauern pro Seite", wallsPerSide, 4, 40);
            fortifiedWalls = EditorGUILayout.Toggle("Verstärkte Außenmauern", fortifiedWalls);

            EditorGUILayout.Space(4);
            autoTileSize = EditorGUILayout.Toggle("Kachel-Grösse auto", autoTileSize);
            if (!autoTileSize)
                tileSize = EditorGUILayout.FloatField("  Kachel-Grösse (m)", tileSize);
            else if (lastDetectedSize > 0f)
                EditorGUILayout.LabelField("  Erkannte Grösse", $"{lastDetectedSize:F3} m  |  Höhe: {lastDetectedHeight:F3} m");

            EditorGUILayout.Space(4);
            addBattlements = EditorGUILayout.Toggle("Zinnen auf Außenmauern", addBattlements);
            addDeco        = EditorGUILayout.Toggle("Deko", addDeco);
            if (addDeco)
                decoCount = EditorGUILayout.IntSlider("  Anzahl", decoCount, 1, 30);

            EditorGUILayout.Space(8);
            float ts   = autoTileSize && lastDetectedSize > 0f ? lastDetectedSize : tileSize;
            float side = wallsPerSide * ts;
            EditorGUILayout.HelpBox(
                $"Burg: {side:F1} × {side:F1} m  |  Innen: Keep (4×4, 3 Stk.) + 2× Kaserne (3×2, 2 Stk.)  |  Boden: volle Innenfläche",
                MessageType.None);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Burg bauen", GUILayout.Height(45)))
                Build();
            GUI.backgroundColor = Color.white;
        }

        // ─────────────────────────────────────────────
        void LoadAssets()
        {
            string outerWall     = fortifiedWalls ? "wall-fortified.fbx" : "wall.fbx";
            string outerWallHalf = fortifiedWalls ? "wall-fortified-half.fbx" : "wall-half.fbx";
            string outerGate     = fortifiedWalls ? "wall-fortified-gate.fbx" : "wall-gate.fbx";

            outerWallSrc      = Load(outerWall);
            outerWallHalfSrc  = Load(outerWallHalf);
            outerGateSrc      = Load(outerGate);
            battlementSrc     = Load("battlement.fbx");
            battlementCornerInnerSrc = Load("battlement-corner-inner.fbx");
            battlementCornerOuterSrc = Load("battlement-corner-outer.fbx");
            towerWallBaseSrc  = Load("wall-fortified.fbx");
            outerFloorSrc     = Load("floor-flat.fbx");
            barrelSrc         = Load("detail-barrel.fbx");
            crateSrc          = Load("detail-crate.fbx");
            structureWallSrc  = Load("structure-wall.fbx");
            fenceWoodSrc      = Load("fence-wood.fbx");

            bWallSrc       = Load("wall-pane.fbx");
            bWallWindowSrc = Load("wall-pane-window.fbx");
            // wall-paint-flat als Eingangs-Stück: rein optisch (flache bemalte Wand),
            // die Tür-Platzierung entfernt alle Collider, damit man hindurchgehen kann
            bWallDoorSrc   = Load("wall-paint-flat.fbx");
            bFloorSrc      = Load("floor.fbx");
            bStairsSrc     = Load("stairs-stone.fbx");
            bLadderSrc     = Load("ladder.fbx");
            columnSrc      = Load("column.fbx");
            roofSrc        = Load("roof.fbx");

            woodFloorSrc     = Load("wood-floor.fbx");
            overhangRoundSrc = Load("overhang-round.fbx");
            structurePoleSrc = Load("structure-pole.fbx");
        }

        // internal statt private: der MapMittelalterBuilder ruft das direkt
        // auf einer unsichtbaren Instanz auf (kein Fenster nötig)
        internal void Build()
        {
            LoadAssets();

            if (outerWallSrc == null)
            {
                Debug.LogError("BurgBuilder: Außenwand-FBX fehlt – Abbruch.");
                return;
            }

            float ts = tileSize, wh = tileSize;
            if (autoTileSize)
            {
                (ts, wh) = MeasureDimensions(outerWallSrc);
                lastDetectedSize   = ts;
                lastDetectedHeight = wh;
                Repaint();
            }

            Undo.SetCurrentGroupName("Burg bauen");
            int undoGroup = Undo.GetCurrentGroup();

            // Nur Root-Objekte durchsuchen (statt veraltetem FindObjectsByType)
            foreach (GameObject ex in SceneManager.GetActiveScene().GetRootGameObjects())
                if (ex.name == "Burg")
                    Undo.DestroyObjectImmediate(ex);

            GameObject root = new GameObject("Burg");
            Undo.RegisterCreatedObjectUndo(root, "Burg");

            float half    = wallsPerSide * ts / 2f;
            int   midWall = wallsPerSide / 2;

            // ── Außenmauern ──────────────────────────────
            var wallsGo = Child(root, "Außenmauern");
            string[] sideNames = { "Nord", "Ost", "Süd", "West" };

            for (int s = 0; s < 4; s++)
            {
                var sideGo = Child(wallsGo, sideNames[s]);
                var rot    = Quaternion.Euler(0, s * 90f, 0);

                for (int w = 0; w < wallsPerSide; w++)
                {
                    float   t      = -half + (w + 0.5f) * ts;
                    Vector3 pos    = SidePos(s, t, half);
                    bool    isGate   = s == 2 && w == midWall;
                    bool    isMid    = w == midWall && !isGate;
                    bool    isAccess = (w == wallsPerSide / 4 || w == 3 * wallsPerSide / 4);

                    if (isGate && outerGateSrc != null)
                    {
                        // Torbogen hat ein Loch in der Mitte – ein Bounding-Box-Collider würde
                        // die Durchfahrt komplett blockieren, daher richtiger MeshCollider
                        var gateGo = Place(outerGateSrc, pos, rot, sideGo);
                        if (gateGo != null) UseMeshColliders(gateGo);
                        // Bodenplatten in der ganzen Durchfahrt: die allgemeine Innenboden-
                        // Schleife deckt nur die Fläche innerhalb der Mauern ab, nicht die
                        // Wandreihe selbst. Das Tor-Asset ist zudem tiefer als eine normale
                        // Wand (Durchfahrt), eine einzelne Kachel würde also nicht die ganze
                        // Länge des Tunnels abdecken – daher so viele Kacheln wie nötig,
                        // entlang der Torrichtung aneinandergereiht.
                        if (outerFloorSrc != null)
                        {
                            float   gateFp    = MeasureDimensions(outerFloorSrc).footprint;
                            float   gateDepth = MeasureLocalBounds(outerGateSrc).z;
                            int     tileCount = Mathf.Max(1, Mathf.RoundToInt(gateDepth / ts));
                            Vector3 normal    = rot * Vector3.forward;
                            float   startOff  = -(tileCount - 1) * ts * 0.5f;
                            for (int gi = 0; gi < tileCount; gi++)
                            {
                                Vector3 tp = pos + normal * (startOff + gi * ts);
                                PlaceFloorTile(outerFloorSrc, new Vector3(tp.x, 0.02f, tp.z), ts, gateFp, sideGo);
                            }
                        }
                        // Zinnen beidseitig auf dem Torbogen, wie beim Mittelturm
                        if (addBattlements && battlementSrc != null)
                        {
                            Place(battlementSrc, pos + Vector3.up * wh, rot, sideGo);
                            Place(battlementSrc, pos + Vector3.up * wh,
                                Quaternion.Euler(0, s * 90f + 180f, 0), sideGo);
                        }
                        // Turm über dem Tor genau wie die anderen Türme (ohne Leitern)
                        PlaceTowerCap(sideGo, pos, rot, ts, wh);
                    }
                    else if (isMid && towerWallBaseSrc != null)
                    {
                        // Mittelturm: Wall-Fort-Segment statt runder Turmbasis,
                        // Zinnen wie auf normaler Mauer, obendrauf Structure-Wall + Leiter.
                        // Basis UND Kappe ohne Kollision: man soll unter den Türmen
                        // hindurchgehen können (nur das Geländer bleibt solide).
                        RemoveColliders(Place(towerWallBaseSrc, pos, rot, sideGo));
                        if (addBattlements && battlementSrc != null)
                        {
                            Place(battlementSrc, pos + Vector3.up * wh, rot, sideGo);
                            Place(battlementSrc, pos + Vector3.up * wh,
                                Quaternion.Euler(0, s * 90f + 180f, 0), sideGo);
                        }
                        PlaceTowerCap(sideGo, pos, rot, ts, wh, begehbareKappe: true);
                    }
                    else
                    {
                        bool isEndStart = w == 0;
                        bool isEndLast  = w == wallsPerSide - 1;
                        if (isEndStart && outerWallHalfSrc != null)
                        {
                            // Der Eckturm (Ecke s) ist ein Wandsegment MIT GLEICHER
                            // Ausrichtung wie diese Seite und belegt hier eine halbe
                            // Kachel der Wandlinie – ein volles Endstück würde parallel
                            // in ihm stecken (Z-Fighting). Daher: halbes Wandstück in
                            // der turmabgewandten Kachelhälfte.
                            // PIVOT-UNABHÄNGIG platziert: Die Geometrie-Mitte des
                            // Half-Meshes wird gemessen und exakt auf die Ziel-Mitte
                            // gelegt – egal, ob der Pivot des Assets in der Mesh-Mitte
                            // oder am Voll-Kachel-Zentrum sitzt (sonst: Eck-Spalt!).
                            // Am ANDEREN Ende (w==letztes, Ecke s+1) steht der Turm quer
                            // zur Seite und ist dort nur wanddick – dort bleibt das
                            // volle Stück, das den Turm nur senkrecht kreuzt.
                            Vector3 along  = rot * Vector3.right; // Richtung wachsendes w
                            Vector3 ziel   = pos + along * (ts * 0.25f);
                            Vector3 offset = rot * MeasureBoundsCenterXZ(outerWallHalfSrc);
                            Place(outerWallHalfSrc, ziel - offset, rot, sideGo);
                        }
                        else
                            Place(outerWallSrc, pos, rot, sideGo);

                        if (addBattlements && battlementSrc != null)
                        {
                            // Mauerstücke direkt neben den Ecktürmen (w==0 / w==letztes)
                            // bleiben komplett ohne Zinnen – dort stehen die Ecktürme,
                            // Eck-Zinnen sind nicht erwünscht.
                            bool isEnd = isEndStart || isEndLast;
                            if (!isEnd)
                            {
                                // Innen: immer. Außen: nicht am Zugangspunkt (dort steht die Treppe,
                                // eine äußere Zinne würde den Aufgang blockieren).
                                Place(battlementSrc, pos + Vector3.up * wh,
                                    Quaternion.Euler(0, s * 90f + 180f, 0), sideGo);
                                if (!isAccess)
                                    Place(battlementSrc, pos + Vector3.up * wh, rot, sideGo);
                            }
                        }
                    }
                }
            }

            // ── Ecktürme ─────────────────────────────────
            var towersGo = Child(root, "Ecktürme");
            Vector3[] corners = {
                new Vector3(-half, 0,  half),
                new Vector3( half, 0,  half),
                new Vector3( half, 0, -half),
                new Vector3(-half, 0, -half),
            };
            for (int c = 0; c < 4; c++)
            {
                var     crot = Quaternion.Euler(0, c * 90f, 0);
                Vector3 cp   = corners[c];

                // Wall-Fort-Segment statt runder Turmbasis (kein "tower"-Zwischenstück mehr).
                // Basis OHNE Kollision: man soll unter den Türmen hindurchgehen können.
                if (towerWallBaseSrc != null) RemoveColliders(Place(towerWallBaseSrc, cp, crot, towersGo));

                // Keine Zinnen am Eckturm – die Kappe (Structure-Wall + Geländer) reicht.
                // Die Kappen-Baugruppe ist als Einheit um 180° gedreht gegenüber der
                // Turmbasis/Zinnen. begehbareKappe: true, sonst blockiert die Kappe
                // weiterhin die "Ecken"-Brückenplatten des Mauerwegs.
                Quaternion capRot = crot * Quaternion.Euler(0, 180f, 0);
                PlaceTowerCap(towersGo, cp, capRot, ts, wh, begehbareKappe: true);
            }

            // ── Mauerweg (begehbarer Wehrgang oben auf den Mauern) ──
            BuildWallWalkway(root, half, ts, wh);

            // ── Innenboden (volle Fläche bis zur Mauer) ──
            if (outerFloorSrc != null)
            {
                var floorGo = Child(root, "Boden");
                float outerFp = MeasureDimensions(outerFloorSrc).footprint;
                for (int x = 0; x < wallsPerSide; x++)
                    for (int z = 0; z < wallsPerSide; z++)
                    {
                        float xp = -half + (x + 0.5f) * ts;
                        float zp = -half + (z + 0.5f) * ts;
                        PlaceFloorTile(outerFloorSrc, new Vector3(xp, 0.02f, zp), ts, outerFp, floorGo);
                    }
            }

            // ── Innengebäude ─────────────────────────────
            BuildInterior(root.transform, half, ts, wh);

            // ── Himmelswege (Stege + Plattformen in der Luft) ──
            BuildSkyWays(root, half, ts, wh);

            // ── Deko ─────────────────────────────────────
            if (addDeco && (barrelSrc != null || crateSrc != null))
            {
                var decoGo = Child(root, "Deko");
                float spread = Mathf.Max(0.5f, half - ts * 2f);
                for (int i = 0; i < decoCount; i++)
                {
                    var pos = new Vector3(UnityEngine.Random.Range(-spread, spread), 0.005f, UnityEngine.Random.Range(-spread, spread));
                    var rot = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                    var src = (i % 3 == 0 && barrelSrc != null) ? barrelSrc : (crateSrc ?? barrelSrc);
                    Place(src, pos, rot, decoGo);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
            Debug.Log($"[BurgBuilder] Fertig – {wallsPerSide * 4} Mauern | 4 Ecktürme | 4 Mitteltürme | Keep + 2 Kasernen | Boden voll.");
        }

        // ─── Mauerweg ────────────────────────────────────
        void BuildWallWalkway(GameObject root, float half, float ts, float wh)
        {
            if (outerFloorSrc == null) return;
            var walkGo = Child(root, "Mauerweg");
            float walkY = wh + 0.005f;
            float outerFp = MeasureDimensions(outerFloorSrc).footprint;
            string[] sideNames2 = { "Nord", "Ost", "Süd", "West" };

            for (int s = 0; s < 4; s++)
            {
                var sideWalk = Child(walkGo, sideNames2[s]);
                for (int w = 0; w < wallsPerSide; w++)
                {
                    float t    = -half + (w + 0.5f) * ts;
                    bool  endS = w == 0;
                    bool  endL = w == wallsPerSide - 1;

                    if (endS || endL)
                    {
                        // Endplatte: die Eck-Brückenplatte belegt bereits ts/2 der
                        // Wandlinie – eine volle Platte läge auf gleicher Höhe in ihr
                        // (Z-Fighting). Daher halbe Platte, um ts/4 von der Ecke weg.
                        t += endS ? ts * 0.25f : -ts * 0.25f;
                        Vector3 hp   = SidePos(s, t, half);
                        var     tile = Place(outerFloorSrc, new Vector3(hp.x, walkY, hp.z), Quaternion.identity, sideWalk);
                        if (tile != null && outerFp > 0.01f)
                        {
                            float sHalf = ts * 0.5f / outerFp;
                            float sFull = ts / outerFp;
                            // Seiten 0/2 verlaufen entlang X, Seiten 1/3 entlang Z
                            tile.transform.localScale = (s % 2 == 0)
                                ? new Vector3(sHalf, 1f, sFull)
                                : new Vector3(sFull, 1f, sHalf);
                            FixOpaque(tile);
                        }
                    }
                    else
                    {
                        Vector3 pos = SidePos(s, t, half);
                        PlaceFloorTile(outerFloorSrc, new Vector3(pos.x, walkY, pos.z), ts, outerFp, sideWalk);
                    }
                }
            }

            // Brückenplatten an den 4 Ecken (damit man nahtlos von Seite zu Seite gehen kann)
            var cornerWalk = Child(walkGo, "Ecken");
            Vector3[] cBridges = {
                new Vector3(-half, walkY,  half),
                new Vector3( half, walkY,  half),
                new Vector3( half, walkY, -half),
                new Vector3(-half, walkY, -half),
            };
            foreach (var cb in cBridges)
                PlaceFloorTile(outerFloorSrc, cb, ts, outerFp, cornerWalk);

            // Zugangstreppen: von Innenhof auf die Mauer hochsteigen (je 2 pro Seite, Viertelspunkte)
            if (bStairsSrc != null)
            {
                var accessGo = Child(walkGo, "Zugangstreppen");
                float stairsH     = MeasureDimensions(bStairsSrc).height;
                float stairsScale = stairsH > 0.01f ? wh / stairsH : 1f;

                for (int s = 0; s < 4; s++)
                {
                    var stairRot = Quaternion.Euler(0, s * 90f, 0);
                    Vector3 inward = stairRot * Vector3.back; // Richtung Innenhof, weg von der Mauer
                    int a1 = wallsPerSide / 4;
                    int a2 = 3 * wallsPerSide / 4;
                    int[] pts = { a1, a2 };
                    foreach (int aw in pts)
                    {
                        float   t   = -half + (aw + 0.5f) * ts;
                        Vector3 wp  = SidePos(s, t, half);
                        Vector3 wallPos = new Vector3(wp.x, 0.02f, wp.z);
                        // Vor der Mauer stehend, nicht in ihr eingebettet
                        Vector3 basePos = wallPos + inward * (ts * 0.9f);

                        var stairGo = Place(bStairsSrc, basePos, stairRot, accessGo);
                        if (stairGo != null)
                        {
                            stairGo.transform.localScale = Vector3.one * stairsScale;
                            FixOpaque(stairGo);
                            // Echter Treppen-Mesh-Collider statt einer Bounding-Box: eine Box
                            // wäre so hoch wie die ganze Treppe und würde den Aufgang blockieren
                            // (man käme ohne Sprung nicht hoch).
                            UseMeshColliders(stairGo);
                        }
                    }
                }
            }
        }

        // ─── Innengebäude ────────────────────────────────
        void BuildInterior(Transform parent, float half, float ts, float wh)
        {
            var go = Child(parent.gameObject, "Innengebäude");

            // Positionen sicher innerhalb der Mauern (ts = Kachelbreite als Sicherheitsabstand)
            float safeR = half - ts * 3f;

            // Hauptgebäude (Keep): 4×4, 3 Stockwerke, nördlich der Mitte → Tür zeigt nach Süden zum Innenhof
            PlaceBuilding(go, "Keep",
                center: new Vector3(0, 0,  safeR * 0.4f),
                w: 4, d: 4, floors: 3, ts: ts, wh: wh, doorSide: 2, topExitSide: 2);

            // West-Kaserne: südlich der Mitte → Tür zeigt nach Norden zum Innenhof
            PlaceBuilding(go, "KaserneWest",
                center: new Vector3(-safeR * 0.55f, 0, -safeR * 0.3f),
                w: 3, d: 2, floors: 2, ts: ts, wh: wh, doorSide: 0, topExitSide: 1);

            // Ost-Kaserne: südlich der Mitte → Tür zeigt nach Norden zum Innenhof
            PlaceBuilding(go, "KaserneOst",
                center: new Vector3( safeR * 0.55f, 0, -safeR * 0.3f),
                w: 3, d: 2, floors: 2, ts: ts, wh: wh, doorSide: 0, topExitSide: 3);

            // ── Kleine Einstöckige Häuser ─────────────────
            // Wachhäuser links/rechts neben dem Keep (Tür Richtung Innenhof)
            PlaceBuilding(go, "Wachhaus_West",
                center: new Vector3(-safeR * 0.28f, 0, 0),
                w: 2, d: 1, floors: 1, ts: ts, wh: wh, doorSide: 1);

            PlaceBuilding(go, "Wachhaus_Ost",
                center: new Vector3( safeR * 0.28f, 0, 0),
                w: 2, d: 1, floors: 1, ts: ts, wh: wh, doorSide: 3);

            // Lagerhäuser im Norden (neben Keep, Tür nach Süden zum Innenhof)
            PlaceBuilding(go, "Lager_NordWest",
                center: new Vector3(-safeR * 0.5f, 0, safeR * 0.78f),
                w: 2, d: 2, floors: 1, ts: ts, wh: wh, doorSide: 2);

            PlaceBuilding(go, "Lager_NordOst",
                center: new Vector3( safeR * 0.5f, 0, safeR * 0.78f),
                w: 2, d: 2, floors: 1, ts: ts, wh: wh, doorSide: 2);

            // Südliche Wachhäuser beim Tor (Tür nach Norden Richtung Innenhof)
            PlaceBuilding(go, "Torwache_West",
                center: new Vector3(-safeR * 0.6f, 0, -safeR * 0.65f),
                w: 2, d: 1, floors: 1, ts: ts, wh: wh, doorSide: 0);

            PlaceBuilding(go, "Torwache_Ost",
                center: new Vector3( safeR * 0.6f, 0, -safeR * 0.65f),
                w: 2, d: 1, floors: 1, ts: ts, wh: wh, doorSide: 0);

        }

        // doorSide: 0=Nord, 1=Ost, 2=Süd, 3=West
        // topExitSide: wie doorSide, aber für einen Ausgang im OBERSTEN Stockwerk
        // (Anschluss an die Himmelswege); -1 = kein oberer Ausgang.
        // Bei Ost/West-Ausgängen wird das Wandstück zi==0 (Südende) benutzt, weil an
        // der Nordost-Ecke die Deckenöffnung für die Leiter liegt (dort fehlt der Boden).
        void PlaceBuilding(GameObject parent, string label, Vector3 center, int w, int d, int floors, float ts, float wh, int doorSide = 2, int topExitSide = -1)
        {
            var bGo = Child(parent, label);
            float hw  = w * ts / 2f;
            float hd  = d * ts / 2f;

            for (int f = 0; f < floors; f++)
            {
                float baseY    = f * wh;
                bool  isGround = f == 0;
                var   fGo      = Child(bGo, $"Stockwerk{f}");

                // Deckenplatten für OG (NE-Ecke ausgelassen = Öffnung über der Leiter).
                // Leicht angehoben (+0.02), sonst koplanar mit den Wand-Oberkanten.
                // Das Plattenfeld wird exakt zwischen die WAND-INNENSEITEN gespannt
                // (bis Wandmitte gab es Z-Fighting an den Raumkanten) und pro Achse
                // exakt skaliert (die alte gleichmäßige Skala auf die größere
                // Mesh-Achse ließ bei nicht-quadratischem Boden-Mesh Schlitze).
                // Minimale Überlappung (+0.5%) plus Schachbrett-Höhenversatz (3 mm):
                // von unten keine sichtbaren Fugen, und die überlappenden Flächen
                // sind nie koplanar → kein Flimmern/Vibrieren.
                if (!isGround && bFloorSrc != null)
                {
                    // Wanddicke = KLEINERE Grundflächen-Achse des Wandstücks
                    // (nicht blind die Z-Achse: je nach Mesh-Ausrichtung wäre das
                    // die Wandlänge ≈ eine ganze Kachel → der Boden würde weit
                    // vor der Wand enden). Zusätzlich hart gedeckelt.
                    float wallThick = 0.1f;
                    if (bWallSrc != null)
                    {
                        var (wxDim, wzDim, _) = MeasureLocalBounds(bWallSrc);
                        wallThick = Mathf.Min(wxDim, wzDim);
                    }
                    wallThick = Mathf.Clamp(wallThick, 0.01f, 0.25f * ts);

                    var (fx, fz, _) = MeasureLocalBounds(bFloorSrc);
                    float sizeX = (w * ts - wallThick) / w;
                    float sizeZ = (d * ts - wallThick) / d;
                    float sX = sizeX / fx * 1.005f;
                    float sZ = sizeZ / fz * 1.005f;
                    float x0 = -(w * ts - wallThick) * 0.5f; // Innenkante West
                    float z0 = -(d * ts - wallThick) * 0.5f; // Innenkante Süd
                    for (int xi = 0; xi < w; xi++)
                        for (int zi = 0; zi < d; zi++)
                        {
                            if (xi == w - 1 && zi == d - 1) continue;
                            float yJ = ((xi + zi) % 2) * 0.003f;
                            var p = center + new Vector3(
                                x0 + (xi + 0.5f) * sizeX,
                                baseY + 0.02f + yJ,
                                z0 + (zi + 0.5f) * sizeZ);
                            var tile = Place(bFloorSrc, p, Quaternion.identity, fGo);
                            if (tile != null)
                            {
                                tile.transform.localScale = new Vector3(sX, 1f, sZ);
                                FixOpaque(tile);
                            }
                        }
                }

                // Nord-Wand  (rot=0°, Außenseite zeigt +Z)
                for (int xi = 0; xi < w; xi++)
                {
                    float xp     = center.x - hw + (xi + 0.5f) * ts;
                    var   p      = new Vector3(xp, center.y + baseY, center.z + hd);
                    bool  isDoor = isGround && doorSide == 0 && xi == w / 2 && bWallDoorSrc != null;
                    bool  isExit = floors > 1 && f == floors - 1 && topExitSide == 0 && xi == w / 2 && bWallDoorSrc != null;
                    bool  isWin  = !isGround && xi % 2 == 0 && bWallWindowSrc != null;
                    var   piece  = (isDoor || isExit) ? bWallDoorSrc : isWin ? bWallWindowSrc : bWallSrc;
                    var   wGo    = Place(piece, p, Quaternion.Euler(0, 0, 0), fGo);
                    // Tür-/Ausgangsöffnung ohne Collider: das Stück ist rein optisch
                    // (bemalte Fläche ohne echtes Loch), ein Collider würde den
                    // Durchgang blockieren
                    if ((isDoor || isExit) && wGo != null) RemoveColliders(wGo);
                }

                // Süd-Wand  (rot=180°, Außenseite zeigt -Z)
                for (int xi = 0; xi < w; xi++)
                {
                    float xp     = center.x - hw + (xi + 0.5f) * ts;
                    var   p      = new Vector3(xp, center.y + baseY, center.z - hd);
                    bool  isDoor = isGround && doorSide == 2 && xi == w / 2 && bWallDoorSrc != null;
                    bool  isExit = floors > 1 && f == floors - 1 && topExitSide == 2 && xi == w / 2 && bWallDoorSrc != null;
                    bool  isWin  = !isGround && xi % 2 == 1 && bWallWindowSrc != null;
                    var   piece  = (isDoor || isExit) ? bWallDoorSrc : isWin ? bWallWindowSrc : bWallSrc;
                    var   wGo    = Place(piece, p, Quaternion.Euler(0, 180, 0), fGo);
                    if ((isDoor || isExit) && wGo != null) RemoveColliders(wGo);
                }

                // Ost-Wand  (rot=90°, Außenseite zeigt +X)
                for (int zi = 0; zi < d; zi++)
                {
                    float zp     = center.z - hd + (zi + 0.5f) * ts;
                    var   p      = new Vector3(center.x + hw, center.y + baseY, zp);
                    bool  isDoor = isGround && doorSide == 1 && zi == d / 2 && bWallDoorSrc != null;
                    bool  isExit = floors > 1 && f == floors - 1 && topExitSide == 1 && zi == 0 && bWallDoorSrc != null;
                    bool  isWin  = !isGround && zi % 2 == 0 && bWallWindowSrc != null;
                    var   piece  = (isDoor || isExit) ? bWallDoorSrc : isWin ? bWallWindowSrc : bWallSrc;
                    var   wGo    = Place(piece, p, Quaternion.Euler(0, 90, 0), fGo);
                    if ((isDoor || isExit) && wGo != null) RemoveColliders(wGo);
                }

                // West-Wand  (rot=270°, Außenseite zeigt -X)
                for (int zi = 0; zi < d; zi++)
                {
                    float zp     = center.z - hd + (zi + 0.5f) * ts;
                    var   p      = new Vector3(center.x - hw, center.y + baseY, zp);
                    bool  isDoor = isGround && doorSide == 3 && zi == d / 2 && bWallDoorSrc != null;
                    bool  isExit = floors > 1 && f == floors - 1 && topExitSide == 3 && zi == 0 && bWallDoorSrc != null;
                    bool  isWin  = !isGround && zi % 2 == 1 && bWallWindowSrc != null;
                    var   piece  = (isDoor || isExit) ? bWallDoorSrc : isWin ? bWallWindowSrc : bWallSrc;
                    var   wGo    = Place(piece, p, Quaternion.Euler(0, 270, 0), fGo);
                    if ((isDoor || isExit) && wGo != null) RemoveColliders(wGo);
                }

                // Leiter an der Ost-Wand (Nordost-Ecke, gegenüber der Tür) – so weit nach
                // innen versetzt, dass ihre Rückseite VOR der Innenfläche der Wand liegt
                // (halbe Wanddicke + halbe Leitertiefe + kleiner Spalt), sonst ragt sie
                // durch die Wand und ist von außerhalb des Hauses sichtbar
                if (f < floors - 1 && bLadderSrc != null)
                {
                    float ladderDepth = MeasureLocalBounds(bLadderSrc).z;
                    float wallThick   = bWallSrc != null ? MeasureLocalBounds(bWallSrc).z : 0.1f;
                    float inset       = wallThick * 0.5f + ladderDepth * 0.5f + 0.03f;
                    Vector3 ladP = new Vector3(center.x + hw - inset, center.y + baseY + 0.05f, center.z + hd - ts * 0.5f);
                    PlaceLadder(ladP, Quaternion.Euler(0, 270, 0), fGo, ts, wh);
                }
            }

            // Säulen an den 4 Gebäudeecken (Erdgeschoss)
            if (columnSrc != null)
            {
                var colGo = Child(bGo, "Säulen");
                Vector3[] colCorners = {
                    center + new Vector3(-hw, 0, -hd),
                    center + new Vector3( hw, 0, -hd),
                    center + new Vector3( hw, 0,  hd),
                    center + new Vector3(-hw, 0,  hd),
                };
                foreach (var cp in colCorners)
                    Place(columnSrc, cp, Quaternion.identity, colGo);
            }

            // Ein einzelnes skaliertes Dachobjekt (Spitze) über dem ganzen Gebäude
            PlaceRoof(bGo, center, w, d, floors * wh, ts);
        }

        void PlaceRoof(GameObject parent, Vector3 center, int w, int d, float topY, float ts)
        {
            if (roofSrc == null) return;
            var roofGo = Child(parent, "Dach");

            // EIN einzelnes Dach-Objekt, skaliert auf Gebäudebreite + 1 Tile Überhang
            var go = Place(roofSrc, new Vector3(center.x, center.y + topY, center.z), Quaternion.identity, roofGo);
            if (go == null) return;

            var (srcFp, _) = MeasureDimensions(roofSrc);
            if (srcFp > 0.01f)
            {
                float targetW = w * ts + ts;   // Gebäudebreite + Überhang
                float targetD = d * ts + ts;
                float sx = targetW / srcFp;
                float sz = targetD / srcFp;
                go.transform.localScale = new Vector3(sx, Mathf.Max(sx, sz), sz);
            }
            FixOpaque(go);
        }

        // ─── Turm-Kappe ──────────────────────────────────
        // Ersetzt die Turmspitze (towerTop): ein einzelnes structure-wall-Objekt obendrauf
        // (gleiche Ausrichtung wie der Turm/die Mauer), darauf fence-wood als Geländer
        // auf allen 4 Seiten. Die früheren 2 Turm-Leitern pro Turm sind entfernt.
        void PlaceTowerCap(GameObject parent, Vector3 basePos, Quaternion rot, float ts, float wh,
                           bool begehbareKappe = false)
        {
            float capY = wh; // direkt auf der Wall-Fort-Basis, kein "tower"-Zwischenstück mehr

            var sw = Place(structureWallSrc, basePos + Vector3.up * capY, rot, parent);
            if (sw != null)
            {
                FixOpaque(sw);
                // Die Kappe liegt genau auf Mauerweg-Höhe (walkY ≈ wh) und blockierte dort
                // bisher die "Ecken"-Brückenplatten, die den Wehrgang nahtlos um die Ecke
                // führen sollen ("an den Ecken kann man nicht gehen") – deshalb an den
                // Ecktürmen bewusst ohne Kollision, das Geländer (fence-wood) bleibt solide.
                if (begehbareKappe) RemoveColliders(sw);
            }
            float capH = structureWallSrc != null ? MeasureDimensions(structureWallSrc).height : 0f;

            // Echte Breite/Tiefe der Kappen-Box statt der Kachelgröße ts – structure-wall
            // ist ein eigenes Asset mit eigenen Maßen, nicht zwingend so groß wie ts.
            var (capX, capZ, _) = structureWallSrc != null
                ? MeasureLocalBounds(structureWallSrc)
                : (ts, ts, 0f);

            for (int side = 0; side < 4; side++)
            {
                var sideRot = rot * Quaternion.Euler(0, side * 90f, 0);
                // Seite 0/2 = Breitseite der Box (quer zur lokalen Z-Achse) → halbe Tiefe;
                // Seite 1/3 = Stirnseite (quer zur lokalen X-Achse) → halbe Breite.
                float halfExtent = (side % 2 == 0) ? capZ * 0.5f : capX * 0.5f;

                Vector3 sidePos = basePos + Vector3.up * (capY + capH) + sideRot * new Vector3(0f, 0f, halfExtent);
                var fw = Place(fenceWoodSrc, sidePos, sideRot, parent);
                if (fw != null)
                {
                    FixOpaque(fw);
                    // Seite 1/3 liegt IN Wehrgang-Richtung (entlang der Mauer) – ein
                    // solides Geländer dort würde den Wehrgang trotz begehbarer Kappe
                    // weiter versperren, man käme gar nicht erst bis zum Raum. Seite
                    // 0/2 (quer zur Mauer, nach außen/innen) bleibt als Absturzsicherung
                    // solide.
                    if (begehbareKappe && (side == 1 || side == 3)) RemoveColliders(fw);
                }
            }
        }

        // ─── Himmelswege ─────────────────────────────────
        // Für jedes mehrstöckige Haus (Keep, beide Kasernen) gibt es im obersten
        // Stockwerk einen Ausgang (wall-paint-flat ohne Collider, siehe PlaceBuilding)
        // und davor einen Holzsteg (wood-floor) in der Luft mit fence-wood-Geländer.
        // 2 overhang-round-Plattformen bilden eine Kette auf der Keep-Achse (vom
        // größten Haus aus) und sind mit einem Steg verbunden. Die Kette endet an
        // Plattform 2: von dort führen links und rechts Leitern bis zum Boden.
        // Die Kasernen-Stege liegen ein Stockwerk tiefer und enden an der ersten
        // Plattform mit einer Leiter hinauf. Stege und Plattformen werden mit
        // structure-pole-Stützen zum Boden abgestützt.
        void BuildSkyWays(GameObject root, float half, float ts, float wh)
        {
            if (woodFloorSrc == null || overhangRoundSrc == null) return;
            var go = Child(root, "Himmelswege");

            float safeR = half - ts * 3f;   // wie in BuildInterior
            float skyY  = 2f * wh;          // Laufhöhe = Boden des obersten Keep-Stockwerks
            float kasY  = wh;               // Laufhöhe = Boden des obersten Kasernen-Stockwerks

            // Anschlusspunkte – müssen zu den Gebäudedaten in BuildInterior passen
            float keepWallZ = safeR * 0.4f - 2f * ts;      // Keep-Südwand (d=4 → hd=2ts)
            float exitX     = 0.5f * ts;                   // Keep-Wandstück xi==w/2 liegt bei +ts/2 (w=4)
            float kasZ      = -safeR * 0.3f;
            float exitZ     = kasZ - 0.5f * ts;            // Ost/West-Ausgang nutzt Wandstück zi==0 (d=2)
            float kwEastX   = -safeR * 0.55f + 1.5f * ts;  // Ostwand KaserneWest (w=3 → hw=1.5ts)
            float koWestX   =  safeR * 0.55f - 1.5f * ts;  // Westwand KaserneOst

            // Plattformen: overhang-round auf 2 Kacheln Durchmesser skaliert
            var (platFp, _) = MeasureDimensions(overhangRoundSrc);
            float platScale = platFp > 0.01f ? (2f * ts) / platFp : 1f;
            float platR     = ts;
            float floorTop  = MeasureYRange(woodFloorSrc).maxY; // Lauffläche über Kachel-Pivot

            Vector3 p1 = new Vector3(exitX, skyY, exitZ);
            Vector3 p2 = p1 + new Vector3(0, 0, -3.5f * ts);

            PlacePlatform(go, p1, platScale, skyY + floorTop);
            PlacePlatform(go, p2, platScale, skyY + floorTop);

            // Keep-Ausgang → Plattform 1 (gleiche Höhe)
            BuildSkyPath(go, new Vector3(exitX, skyY, keepWallZ), new Vector3(exitX, skyY, p1.z + platR), ts);
            // Plattform-Kette 1 → 2 (Ende der Kette)
            BuildSkyPath(go, new Vector3(exitX, skyY, p1.z - platR), new Vector3(exitX, skyY, p2.z + platR), ts);
            // Kasernen-Stege (eine Ebene tiefer), enden unter dem Rand von Plattform 1
            BuildSkyPath(go, new Vector3(kwEastX, kasY, exitZ), new Vector3(p1.x - platR, kasY, exitZ), ts);
            BuildSkyPath(go, new Vector3(koWestX, kasY, exitZ), new Vector3(p1.x + platR, kasY, exitZ), ts);

            // Leitern von den Kasernen-Stegen hinauf auf Plattform 1
            if (bLadderSrc != null)
            {
                float lD = MeasureLocalBounds(bLadderSrc).z;
                PlaceLadder(new Vector3(p1.x - platR - lD * 0.5f - 0.02f, kasY + 0.05f, p1.z),
                    Quaternion.Euler(0, 270, 0), go, ts, wh);
                PlaceLadder(new Vector3(p1.x + platR + lD * 0.5f + 0.02f, kasY + 0.05f, p1.z),
                    Quaternion.Euler(0, 90, 0), go, ts, wh);

                // Abstieg am Ketten-Ende: von Plattform 2 links und rechts Leitern
                // bis zum Boden – gestapelte Segmente (je ein Wandhöhen-Stück) statt
                // einer hochskalierten Leiter, damit nichts verzerrt wird.
                int segs = Mathf.Max(1, Mathf.RoundToInt(skyY / wh));
                for (int i = 0; i < segs; i++)
                {
                    float ly = i * wh + 0.05f;
                    PlaceLadder(new Vector3(p2.x - platR - lD * 0.5f - 0.02f, ly, p2.z),
                        Quaternion.Euler(0, 270, 0), go, ts, wh);
                    PlaceLadder(new Vector3(p2.x + platR + lD * 0.5f + 0.02f, ly, p2.z),
                        Quaternion.Euler(0, 90, 0), go, ts, wh);
                }
            }
        }

        // Gerader Holzsteg von 'from' nach 'to' (gleiche Höhe), mit Geländer auf beiden
        // Seiten und alle 3 Kacheln einer Stütze bis zum Boden.
        void BuildSkyPath(GameObject parent, Vector3 from, Vector3 to, float ts)
        {
            Vector3 delta = to - from; delta.y = 0f;
            float dist = delta.magnitude;
            if (dist < 0.05f || woodFloorSrc == null) return;

            Vector3 dir = delta / dist;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            int n = Mathf.Max(1, Mathf.RoundToInt(dist / ts));
            float step = dist / n; // Kacheln exakt über die Distanz verteilen (keine Lücke am Ende)

            float fp       = MeasureDimensions(woodFloorSrc).footprint;
            float scAlong  = fp > 0.01f ? step / fp : 1f;
            float scAcross = fp > 0.01f ? ts / fp : 1f;
            float floorTop = MeasureYRange(woodFloorSrc).maxY;

            for (int i = 0; i < n; i++)
            {
                Vector3 c = from + dir * ((i + 0.5f) * step);
                var tile = Place(woodFloorSrc, c, rot, parent);
                if (tile != null)
                {
                    tile.transform.localScale = new Vector3(scAcross, 1f, scAlong);
                    FixOpaque(tile);
                }

                // Geländer links und rechts, Länge entlang des Wegs
                if (fenceWoodSrc != null)
                {
                    Vector3 right = rot * Vector3.right;
                    var fr = Place(fenceWoodSrc, c + right * (ts * 0.45f) + Vector3.up * floorTop,
                        rot * Quaternion.Euler(0, 90, 0), parent);
                    if (fr != null) FixOpaque(fr);
                    var fl = Place(fenceWoodSrc, c - right * (ts * 0.45f) + Vector3.up * floorTop,
                        rot * Quaternion.Euler(0, 270, 0), parent);
                    if (fl != null) FixOpaque(fl);
                }

                // Stütze alle 3 Kacheln
                if (i % 3 == 1)
                    PlaceSupport(parent, new Vector3(c.x, 0, c.z), from.y);
            }
        }

        // Eine overhang-round-Plattform, deren Lauffläche auf surfaceY liegt,
        // plus Stütze vom Boden bis zur Plattform-Unterkante.
        void PlacePlatform(GameObject parent, Vector3 pos, float scaleXZ, float surfaceY)
        {
            var (mnY, mxY) = MeasureYRange(overhangRoundSrc);
            var plat = Place(overhangRoundSrc, new Vector3(pos.x, surfaceY - mxY, pos.z), Quaternion.identity, parent);
            if (plat == null) return;
            plat.transform.localScale = new Vector3(scaleXZ, 1f, scaleXZ);
            FixOpaque(plat);
            // Runde Form: echter Mesh-Collider statt (eckiger) Bounding-Box
            UseMeshColliders(plat);

            PlaceSupport(parent, new Vector3(pos.x, 0, pos.z), surfaceY - (mxY - mnY));
        }

        // structure-pole vom Boden (y=0) bis targetTopY hochskaliert.
        void PlaceSupport(GameObject parent, Vector3 groundPos, float targetTopY)
        {
            if (structurePoleSrc == null || targetTopY <= 0.05f) return;
            var (mnY, mxY) = MeasureYRange(structurePoleSrc);
            float h  = Mathf.Max(0.01f, mxY - mnY);
            float sy = targetTopY / h;
            // Pivot-unabhängig: Unterkante exakt auf den Boden legen
            var pole = Place(structurePoleSrc, new Vector3(groundPos.x, -mnY * sy, groundPos.z), Quaternion.identity, parent);
            if (pole == null) return;
            pole.transform.localScale = new Vector3(1f, sy, 1f);
            FixOpaque(pole);
        }

        // ─── Boden-Kacheln (Lücken/Wackeln vermeiden) ────
        // Skaliert jede Kachel exakt auf targetSize, egal wie groß das Quell-Mesh
        // wirklich ist – dadurch schließen die Kacheln lückenlos aneinander an
        // und der CharacterController "hakt" nicht mehr an ungleichen Kanten.
        static void PlaceFloorTile(GameObject src, Vector3 pos, float targetSize, float srcFootprint, GameObject parent)
        {
            var tile = Place(src, pos, Quaternion.identity, parent);
            if (tile == null) return;
            if (srcFootprint > 0.01f)
            {
                float s = targetSize / srcFootprint;
                tile.transform.localScale = new Vector3(s, 1f, s);
            }
            FixOpaque(tile);
        }

        // ─── Leiter ──────────────────────────────────────
        void PlaceLadder(Vector3 pos, Quaternion rot, GameObject parent, float ts, float wh)
        {
            if (bLadderSrc == null) return;
            var go = Place(bLadderSrc, pos, rot, parent);
            if (go == null) return;
            FixOpaque(go);

            // Trigger-Zone an den tatsächlichen Leiter-Maßen bemessen, nicht an der
            // Kachelgröße ts (andere Objektskala). Wichtig: Die Zone muss VOR der
            // Leiter weit genug herausragen – der Spieler prallt am soliden
            // Leiter-Collider ab und kommt mit Kapselradius + Skin nur bis ca.
            // Leitertiefe/2 + 0.2 heran. Eine Zone von nur 1.6× Leitertiefe lag
            // bei dünnen Leitern komplett hinter dieser Position → OnTriggerEnter
            // feuerte nie, Klettern startete nie. Die Zone wird deshalb einseitig
            // nach vorne (lokal +Z, Kletterseite) verlängert – nach hinten bleibt
            // sie knapp, sonst könnte man durch die Wand von der falschen Seite
            // klettern.
            var (ladX, ladZ, _) = MeasureLocalBounds(bLadderSrc);
            float front = Mathf.Max(0.35f, ladZ);
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, wh / 2f, front * 0.5f);
            trigger.size   = new Vector3(ladX * 1.3f, wh * 1.1f, ladZ * 1.6f + front);

            go.AddComponent<Ladder>();
        }

        // ─── Hilfsmethoden ───────────────────────────────
        // Wie MeasureDimensions, aber X/Z getrennt statt zusammengeführt – nötig, um
        // rechteckige (nicht-quadratische) Teile wie Wand-/Leiter-Assets korrekt an
        // der richtigen Kante auszurichten statt an der längeren Seite zu schätzen.
        static (float x, float z, float y) MeasureLocalBounds(GameObject src)
        {
            float maxX = 0f, maxZ = 0f, maxY = 0f;
            foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Vector3 sc = mf.transform.lossyScale;
                Bounds  mb = mf.sharedMesh.bounds;
                maxX = Mathf.Max(maxX, mb.size.x * Mathf.Abs(sc.x));
                maxZ = Mathf.Max(maxZ, mb.size.z * Mathf.Abs(sc.z));
                maxY = Mathf.Max(maxY, mb.size.y * Mathf.Abs(sc.y));
            }
            return (maxX > 0.01f ? maxX : 1f, maxZ > 0.01f ? maxZ : 1f, maxY > 0.01f ? maxY : 1f);
        }

        // Horizontale Geometrie-Mitte des Assets relativ zu seinem Pivot
        // (Asset-Raum, inkl. Kind-Transforms; Y bleibt 0, damit Teile weiter
        // mit der Unterkante am Boden aufsetzen). Nötig für Teile mit
        // unbekannter Pivot-Konvention, z.B. die halben Wandstücke.
        static Vector3 MeasureBoundsCenterXZ(GameObject src)
        {
            Vector3 mn = new Vector3(float.MaxValue, 0, float.MaxValue);
            Vector3 mx = new Vector3(float.MinValue, 0, float.MinValue);
            foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix; // Prefab-Root im Ursprung
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    mn.x = Mathf.Min(mn.x, p.x); mn.z = Mathf.Min(mn.z, p.z);
                    mx.x = Mathf.Max(mx.x, p.x); mx.z = Mathf.Max(mx.z, p.z);
                }
            }
            if (mn.x > mx.x) return Vector3.zero;
            return new Vector3((mn.x + mx.x) * 0.5f, 0f, (mn.z + mx.z) * 0.5f);
        }

        // Unter-/Oberkante des Assets relativ zu seinem Pivot (Asset-Raum, inkl.
        // Kind-Transforms) – nötig, um Teile mit unbekanntem Pivot exakt auf einer
        // Zielhöhe aufsetzen zu lassen (z.B. Plattformen, skalierte Stützen).
        static (float minY, float maxY) MeasureYRange(GameObject src)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix; // Prefab-Root steht im Ursprung
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = m.MultiplyPoint3x4(corner).y;
                    mn = Mathf.Min(mn, y);
                    mx = Mathf.Max(mx, y);
                }
            }
            if (mn > mx) { mn = 0f; mx = 1f; }
            return (mn, mx);
        }

        static (float footprint, float height) MeasureDimensions(GameObject src)
        {
            float maxXZ = 0f, maxY = 0f;
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

        static void FixOpaque(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.materials;
                foreach (Material m in mats)
                {
                    if (m == null) continue;
                    m.SetFloat("_Surface", 0);
                    m.SetFloat("_ZWrite", 1);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    m.SetOverrideTag("RenderType", "Opaque");
                    m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.DisableKeyword("_ALPHATEST_ON");
                }
                r.materials = mats;
            }
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

        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject Place(GameObject src, Vector3 pos, Quaternion rot, GameObject parent)
        {
            if (src == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, rot);
            Undo.RegisterCreatedObjectUndo(go, "Burg Teil");
            EnsureCollider(go);
            return go;
        }

        // Jedes platzierte Teil bekommt eine feste Kollision (falls noch keine vorhanden ist),
        // damit der Spieler nicht durch Wände, Türme, Säulen etc. laufen kann.
        // Die FBX-Assets selbst bringen i.d.R. keine Collider mit.
        //
        // BoxCollider statt MeshCollider: ein nicht-konvexer MeshCollider lässt den
        // CharacterController an den Nahtstellen zwischen benachbarten Teilen (z.B. Boden-
        // Kacheln) hängen bleiben ("Aufhängen" beim Gehen). Die Bounding-Box ist weniger
        // exakt, dafür bewegt sich der Spieler zuverlässig darüber/dagegen.
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

        // Ersetzt die automatischen Bounding-Box-Collider durch echte (nicht-konvexe)
        // MeshCollider – für Teile mit einem Loch (z.B. Torbogen), bei denen eine Box
        // die Durchfahrt/den Durchgang fälschlich blockieren würde.
        static void UseMeshColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                DestroyImmediate(c);

            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }
        }

        // Entfernt alle automatisch erzeugten Collider – für rein dekorative Teile,
        // die begehbar sein müssen, obwohl ihr Mesh keine echte Öffnung hat (z.B. ein
        // Türblatt-Modell ohne Loch).
        static void RemoveColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                DestroyImmediate(c);
        }

        static GameObject Load(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ASSET_PATH + name);
            if (go == null) Debug.LogWarning($"BurgBuilder: '{name}' nicht gefunden.");
            return go;
        }
    }
}
