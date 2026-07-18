using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace NeonCatch.Editor
{
    // Baut eine mittelalterliche Low-Poly-Landschaft rund um die Burg –
    // KOMPLETT AUF FLÄCHE 1 ("Flaeche_1"): Die Fläche wird automatisch
    // vermessen, das Terrain deckt genau sie ab, und alle Hügel/Höfe/Wälder
    // werden in den verfügbaren Ring zwischen Burggraben und Flächenrand
    // eingepasst (mit weichem Auslauf am Rand – nichts ragt hinaus).
    //
    //  - Terrain: großer Hügel im Norden + 3 mittlere; ALLES GRÜN mit einer
    //    prozedural erzeugten, kachelbaren Rasen-Textur; nur die Wege und
    //    die Grabensohle sind Erde
    //  - Burggraben + Holzbrücke vor dem Tor
    //  - WEGE-NETZ: Ring um die Burg, Weg vom Tor, Stichwege zu Höfen/Höhlen
    //  - WALD: 50× Big Oak Tree + 20× Nature-Starter-Kit + 20× Polyeler,
    //    zufällig über die ganze Map verteilt (nicht in der Burg), dazu
    //    Büsche und Gras-Büschel auf den Wiesen – jeder Neubau würfelt neu
    //  - 3 BAUERNHÖFE: Hütte + Gemüsefeld + Zaun, drumherum NUR WIESE und
    //    einzelne Bäume am Feld (kein Wald direkt am Hof)
    //  - HÖHLEN: runde Gras-Kuppel über einem begehbaren Gang im Hügel
    //    (innen Hohlraum), Steine markieren die Öffnung, 2 Fackeln
    // Die Flaeche_1-Platte selbst wird unsichtbar geschaltet (das Terrain
    // übernimmt Optik + Kollision; die Teleport-Position 1 bleibt erhalten).
    public class LandschaftBuilder : EditorWindow
    {
        const string ASSET_PATH = "Assets/3D Objekte/Burg/";
        const string MAT_DIR    = "Assets/Deko-Materialien";
        const string LAND_DIR   = "Assets/Landschaft";
        const string ROOT_NAME  = "Landschaft";
        const int    RES        = 257;

        // --- UI ---
        float grabenTiefe    = 2f;
        bool  addWald        = true;
        bool  addWege        = true;
        bool  addBauernhoefe = true;

        // --- Zustand ---
        float ts, wh, k;
        Material woodMat, felsMat, heuMat, grasMat, flameMat;
        GameObject[] steinPrefabs = new GameObject[0];
        readonly Dictionary<string, List<GameObject>> packCache = new Dictionary<string, List<GameObject>>();

        struct Hoehle
        {
            public Vector2 ent;
            public Vector2 dirIn;
            public float   len;
            public float   breite;
            public float   rotY;
        }

        [MenuItem("NeonCatch/Landschaft Builder")]
        static void Open() => GetWindow<LandschaftBuilder>("Landschaft");

        void OnGUI()
        {
            GUILayout.Label("Landschaft Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            grabenTiefe    = EditorGUILayout.Slider("Burggraben-Tiefe", grabenTiefe, 0.5f, 5f);
            addWald        = EditorGUILayout.Toggle("Wald (50 Big Oak + 2×20 Sorten)", addWald);
            addWege        = EditorGUILayout.Toggle("Wege-Netz", addWege);
            addBauernhoefe = EditorGUILayout.Toggle("3 Bauernhöfe", addBauernhoefe);

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Alles bleibt auf Fläche 1: die Platte wird vermessen, das Terrain deckt genau\n" +
                "sie ab, am Rand läuft alles weich aus. Wiese mit echter Gras-Textur, kein Grau.\n" +
                "Ein vorhandenes 'Landschaft'-Objekt wird ersetzt – die Burg bleibt unangetastet.",
                MessageType.None);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Landschaft bauen", GUILayout.Height(45)))
                Build();
            GUI.backgroundColor = Color.white;
        }

        // ─────────────────────────────────────────────
        // internal statt private: der MapMittelalterBuilder ruft das direkt
        // auf einer unsichtbaren Instanz auf (kein Fenster nötig)
        internal void Build()
        {
            GameObject burg = null;
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == "Burg")
                    burg = go;
            if (burg == null)
            {
                Debug.LogError("LandschaftBuilder: Keine 'Burg' in der Szene – erst mit dem Burg Builder bauen.");
                return;
            }

            var wallSrc = Load("wall.fbx");
            if (wallSrc == null) return;
            (ts, wh) = MeasureDimensions(wallSrc);
            k = Mathf.Clamp(Mathf.Min(wh / 2.5f, ts / 2.1f), 0.05f, 2f);

            float half = 12f * ts;
            var basis = burg.transform.Find("Mauerweg");
            if (basis == null) basis = burg.transform.Find("Außenmauern");
            if (basis != null)
            {
                Bounds bb = CalcBounds(basis);
                if (bb.extents.x > ts) half = Mathf.Floor(bb.extents.x / ts + 0.01f) * ts;
            }
            Vector3 o = burg.transform.position;

            // ── Fläche 1 vermessen und unsichtbar schalten ──
            // (Unity-Plane ist 10×10 bei Skala 1; Position/Skala statt Renderer-
            // Bounds, damit es auch bei bereits ausgeblendeter Platte klappt)
            var flaeche = GameObject.Find("Flaeche_1");
            Vector3 C; float P;
            if (flaeche != null)
            {
                C = flaeche.transform.position;
                P = flaeche.transform.localScale.x * 5f;
                // Platte KOMPLETT entfernen (Renderer + Mesh + Collider löschen,
                // nicht nur ausblenden): Sie lag exakt auf Terrain-Höhe (Flimmern,
                // Wiese teils unsichtbar) und verdeckte das Grabenwasser.
                // Das GameObject bleibt als Teleport-Punkt für Taste 1 bestehen.
                var mr = flaeche.GetComponent<MeshRenderer>();
                if (mr != null) Undo.DestroyObjectImmediate(mr);
                var mfK = flaeche.GetComponent<MeshFilter>();
                if (mfK != null) Undo.DestroyObjectImmediate(mfK);
                var mc = flaeche.GetComponent<Collider>();
                if (mc != null) Undo.DestroyObjectImmediate(mc);
            }
            else
            {
                Debug.LogWarning("LandschaftBuilder: 'Flaeche_1' nicht gefunden – nutze 25 m um die Burg.");
                C = o; P = 25f;
            }

            LoadMaterials();
            packCache.Clear();

            // Nur die Buchstaben-Felsen (Rock_A–D), nicht die nummerierten
            // (Rock_04 / Stone_01)
            steinPrefabs = LadeAlle(new[]
            {
                "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_A.prefab",
                "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_B.prefab",
                "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_C.prefab",
                "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_D.prefab",
            });

            foreach (GameObject ex in SceneManager.GetActiveScene().GetRootGameObjects())
                if (ex.name == ROOT_NAME)
                    DestroyImmediate(ex);

            var root = new GameObject(ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, ROOT_NAME);

            // ── Layout: alles in den Ring zwischen Graben und Flächenrand ──
            float maxR = Mathf.Min(P - Mathf.Abs(o.x - C.x), P - Mathf.Abs(o.z - C.z)) - 0.5f * ts;
            float ringIn  = half + 1.0f * ts;
            float ringOut = ringIn + 1.8f * ts;
            float ringWeg = ringOut + 1.2f * ts;
            float aussen  = maxR - ringWeg;
            float gateX   = -0.5f * ts;

            if (aussen < 1.5f * ts)
                Debug.LogWarning("LandschaftBuilder: Sehr wenig Platz zwischen Burg und Flächenrand – Hügel/Höfe fallen klein aus.");

            // Hügel: {Richtung} → Position/Radius aus dem verfügbaren Platz
            Vector2[] hDir = {
                new Vector2(0f, 1f),                       // Nord (groß)
                new Vector2(-1f, 0.2f).normalized,         // West
                new Vector2(1f, 0.35f).normalized,         // Ost
                new Vector2(0.55f, -1f).normalized,        // Südost
            };
            var huegel = new List<Vector4>();
            if (aussen > 1.5f * ts)
            {
                float rBig = aussen * 0.42f;
                huegel.Add(V4(hDir[0] * (ringWeg + aussen * 0.55f), rBig, Mathf.Min(4f * wh, rBig * 0.8f)));
                for (int i = 1; i < 4; i++)
                {
                    float r = aussen * 0.32f;
                    huegel.Add(V4(hDir[i] * (ringWeg + aussen * 0.5f), r, Mathf.Min(2.2f * wh, r * 0.7f)));
                }
            }

            // Keine Höhlen mehr (gelöscht) – die Hügel selbst (huegel-Liste
            // oben) bleiben unverändert normale, geschlossene Hügel. Die Liste
            // bleibt bewusst leer statt komplett entfernt: mehrere Funktionen
            // (PaintTerrain, BuildWald, PlatzFrei, Wege-Verbindung) erwarten
            // sie als Parameter für Vermeidungszonen – eine leere Liste ist
            // dort überall ein sicheres No-Op.
            var hoehlen = new List<Hoehle>();

            // Bauernhöfe zwischen den Hügeln – drumherum bleibt NUR Wiese.
            // Sicherung: Liegt ein Hof zu nah an einem Hügel (→ Höhle im Haus!),
            // wird seine Richtung schrittweise weggedreht, bis der Platz frei ist.
            float farmR = Mathf.Min(3.5f * ts, aussen * 0.45f);
            float farmDist = ringWeg + aussen * 0.5f;
            var fDir = new List<Vector2>
            {
                new Vector2(-0.7f, -0.7f).normalized,
                new Vector2( 0.7f,  0.7f).normalized,
                new Vector2(-0.35f, -1f).normalized, // südlich, zwischen Tor-Weg und SO-Hügel
            };
            var hoefeList = new List<Vector2>();
            if (addBauernhoefe && aussen > 1.5f * ts)
                for (int i = 0; i < fDir.Count; i++)
                {
                    Vector2 d = fDir[i];
                    for (int v = 0; v < 9; v++)
                    {
                        // WICHTIG: im QUADRAT-Maß platzieren (wie Graben/Weg-Ring),
                        // nicht im Kreis-Maß – sonst rutschen diagonale Höfe in
                        // den Burggraben (Quadrat-Abstand = nur 0.7× Kreis-Abstand)
                        Vector2 pos2 = d / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) * farmDist;
                        bool frei = true;
                        foreach (var hg in huegel)
                            if (Vector2.Distance(pos2, new Vector2(hg.x, hg.y)) < hg.z + farmR + ts)
                            { frei = false; break; }
                        if (frei)
                        {
                            fDir[i] = d;
                            hoefeList.Add(pos2);
                            break;
                        }
                        // um 20° weiterdrehen und erneut prüfen
                        float ca = Mathf.Cos(20f * Mathf.Deg2Rad), sa = Mathf.Sin(20f * Mathf.Deg2Rad);
                        d = new Vector2(d.x * ca - d.y * sa, d.x * sa + d.y * ca).normalized;
                    }
                }
            Vector2[] hoefe = hoefeList.ToArray();

            // Wege
            var wege = new List<(Vector2 a, Vector2 b)>();
            if (addWege)
            {
                wege.Add((new Vector2(gateX, -(ringOut - 0.5f * ts)), new Vector2(gateX, -(maxR - 1f * ts))));
                // Hof-Wege enden VOR der Hüttentür, nicht mitten im Haus
                foreach (var f in hoefe)   wege.Add((RingPunkt(f, ringWeg), f - f.normalized * 1.8f * ts));
                foreach (var h in hoehlen) wege.Add((RingPunkt(h.ent, ringWeg), h.ent + (-h.dirIn) * 1.2f * ts));
            }

            // Kleine Zufalls-Hügel überall auf der Wiese (weichen Höfen,
            // Höhlen-Vorplätzen und Wegen aus)
            var kleineHuegel = new List<Vector4>();
            for (int t2 = 0; t2 < 50 && kleineHuegel.Count < 8; t2++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rad = Random.Range(ringWeg + 2f * ts, maxR - 3f * ts);
                Vector2 c2 = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                if (Mathf.Max(Mathf.Abs(c2.x), Mathf.Abs(c2.y)) < ringWeg + 1.5f * ts) continue;

                bool frei = true;
                foreach (var f in hoefe)
                    // gleicher Radius wie die Hof-Einebnung – sonst ragen
                    // halb plattgedrückte Buckel in die Hof-Fläche
                    if (Vector2.Distance(c2, f) < farmR * 1.9f + 1.0f * ts) { frei = false; break; }
                if (frei)
                    foreach (var h in hoehlen)
                        if (Vector2.Distance(c2, h.ent) < 3f * ts) { frei = false; break; }
                if (frei)
                    foreach (var (a, b) in wege)
                        if (DistSeg(c2, a, b) < 1.5f * ts) { frei = false; break; }
                if (!frei) continue;

                kleineHuegel.Add(V4(c2, Random.Range(1.2f, 2.2f) * ts, Random.Range(0.4f, 0.9f) * wh));
            }

            float baseH = grabenTiefe + 2f;
            float randH = 3.5f * wh; // Höhe des steilen Rand-Hügels (Map-Ende)
            float maxHuegelH = 0f;
            foreach (var hg in huegel) maxHuegelH = Mathf.Max(maxHuegelH, hg.w);
            float heightRange = baseH + maxHuegelH + randH + wh;

            float   size = 2f * P;
            Vector3 tPos = new Vector3(C.x - P, -baseH, C.z - P);

            // ── Terrain formen ───────────────────────────
            var td = new TerrainData();
            td.heightmapResolution = RES;
            td.size = new Vector3(size, heightRange, size);

            float[,] hm = new float[RES, RES];
            for (int zi = 0; zi < RES; zi++)
                for (int xi = 0; xi < RES; xi++)
                {
                    float wx = tPos.x + (float)xi / (RES - 1) * size;
                    float wz = tPos.z + (float)zi / (RES - 1) * size;
                    Vector2 p = new Vector2(wx - o.x, wz - o.z);

                    float h = baseH + (Mathf.PerlinNoise(wx / (6f * ts) + 31.7f, wz / (6f * ts) + 17.3f) - 0.5f) * 0.5f * wh;

                    foreach (var hg in huegel)
                        h += Bump(p.x, p.y, hg.x, hg.y, hg.z, hg.w);
                    foreach (var hg in kleineHuegel)
                        h += Bump(p.x, p.y, hg.x, hg.y, hg.z, hg.w);

                    foreach (var f in hoefe)
                    {
                        // Größer eingeebnet: der Garten mit Zaun liegt jetzt
                        // etwas abseits vom Haus und braucht ebenen Boden
                        float d = Vector2.Distance(p, f);
                        float flachR = farmR * 1.9f;
                        if (d < flachR)
                            h = Mathf.Lerp(h, baseH, Mathf.Clamp01((flachR - d) / (1.2f * ts)));
                    }

                    float rectD = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                    if (rectD < ringIn) h = baseH;
                    if (rectD >= ringIn && rectD <= ringOut)
                    {
                        float bank = 0.6f * ts;
                        float e = Mathf.Clamp01(Mathf.Min((rectD - ringIn) / bank, (ringOut - rectD) / bank));
                        h = Mathf.Lerp(h, baseH - grabenTiefe, Mathf.SmoothStep(0f, 1f, e));
                    }

                    // Weg-Einebnung NUR außerhalb des Burggrabens – sonst zieht
                    // der Tor-Weg die Grabensohle unter der Brücke wieder hoch
                    if (rectD > ringOut)
                    {
                        foreach (var (a, b) in wege)
                            if (DistSeg(p, a, b) < 0.8f * ts) h = Mathf.Lerp(h, baseH, 0.75f);
                        if (addWege && Mathf.Abs(rectD - ringWeg) < 0.8f * ts)
                            h = Mathf.Lerp(h, baseH, 0.6f);
                    }

                    // Rand-Hügel: steiler Abschluss am Flächenrand ("Ende der Map")
                    // – steigt auf den letzten ~1.4 Kacheln fast senkrecht an,
                    // damit man nicht hinausläuft und die Map sichtbar endet
                    float pd  = Mathf.Max(Mathf.Abs(wx - C.x), Mathf.Abs(wz - C.z));
                    float rim = Mathf.Clamp01((pd - (P - 1.8f * ts)) / (1.4f * ts));
                    h += randH * Mathf.SmoothStep(0f, 1f, rim);

                    hm[zi, xi] = Mathf.Clamp01(h / heightRange);
                }
            td.SetHeights(0, 0, hm);

            PaintTerrain(td, tPos, o, size, ringIn, ringOut, ringWeg, hoehlen, wege);

            if (!AssetDatabase.IsValidFolder(LAND_DIR))
                AssetDatabase.CreateFolder("Assets", "Landschaft");
            AssetDatabase.DeleteAsset(LAND_DIR + "/Terrain.asset");
            AssetDatabase.CreateAsset(td, LAND_DIR + "/Terrain.asset");

            var terrainGo = Terrain.CreateTerrainGameObject(td);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(root.transform, false);
            terrainGo.transform.position = tPos;
            var terrain = terrainGo.GetComponent<Terrain>();

            // ── Brücke ───────────────────────────────────
            BuildBruecke(Child(root, "Brücke"),
                new Vector3(o.x + gateX, o.y + 0.02f, o.z - (ringIn + ringOut) * 0.5f),
                ringOut - ringIn + 1.0f * ts);

            // ── Bauernhöfe (nur Wiese drumherum) ─────────
            if (hoefe.Length > 0)
            {
                var farmGo = Child(root, "Bauernhöfe");
                float f = Mathf.Clamp(farmR / (3.5f * ts), 0.6f, 1f); // Feld ggf. kompakter
                for (int i = 0; i < hoefe.Length; i++)
                {
                    float rotY = Mathf.Atan2(fDir[i].x, fDir[i].y) * Mathf.Rad2Deg;
                    BuildBauernhof(farmGo, i, o + new Vector3(hoefe[i].x, 0, hoefe[i].y), rotY, f);
                }
            }

            // ── Wald: 50 Big Oak + 20 + 20 Sorten, zufällig verteilt ──
            if (addWald)
                BuildWald(Child(root, "Wald"), terrain, o, ringWeg, aussen, maxR, farmR, hoehlen, hoefe, wege);

            // Materialien der platzierten Pack-Objekte automatisch reparieren.
            // force=true: auch Pack-eigene Spezial-Shader (z.B. Polyeler-Bäume)
            // werden auf URP/Lit gezwungen – sonst bleiben einzelne Bäume lila.
            // Danach automatisch weiße Boden-Flächen begrünen – kein manueller
            // Klick im Material Fixer mehr nötig.
            var fixErg = MaterialReparatur.Fix(new[] { root }, true);
            MaterialFixer.BodenBegruenen();

            Selection.activeGameObject = root;
            Debug.Log($"[LandschaftBuilder] Fertig auf Fläche 1 – {huegel.Count} große + {kleineHuegel.Count} kleine Hügel, " +
                      $"{hoefe.Length} Höfe, Wald-Flecken, Versteck-Steinhaufen, steiler Rand. " +
                      $"Materialien: {fixErg.repariert} umgestellt, {fixErg.ersetzt} ersetzt.");
        }

        static Vector4 V4(Vector2 xz, float r, float h) => new Vector4(xz.x, xz.y, r, h);

        static Vector2 RingPunkt(Vector2 ziel, float r)
        {
            if (Mathf.Abs(ziel.x) >= Mathf.Abs(ziel.y))
                return new Vector2(Mathf.Sign(ziel.x) * r, Mathf.Clamp(ziel.y, -r, r));
            return new Vector2(Mathf.Clamp(ziel.x, -r, r), Mathf.Sign(ziel.y) * r);
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        static float Bump(float px, float pz, float cx, float cz, float r, float h)
        {
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz)) / r;
            return d >= 1f ? 0f : h * (Mathf.Cos(d * Mathf.PI) + 1f) * 0.5f;
        }

        // ─── Bemalung: NUR Gras (Textur) + Erde auf Wegen ─
        void PaintTerrain(TerrainData td, Vector3 tPos, Vector3 o, float size,
            float ringIn, float ringOut, float ringWeg,
            List<Hoehle> hoehlen, List<(Vector2 a, Vector2 b)> wege)
        {
            // Rasen = NSK2 ground01 (echte Bodentextur; grass01/02 sind
            // dagegen Halm-Sprites mit Alpha → wirkten weiß!).
            // Erde = NSK2 ground02, damit die Wege zum Rasen passen.
            var erdeTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NatureStarterKit2/Textures/ground02.tga");
            var gras = MakeLayer("Gras", new Color(0.32f, 0.52f, 0.22f), MaterialFixer.HoleGrasTextur());
            var erde = MakeLayer("Erde", new Color(0.45f, 0.33f, 0.20f), erdeTex);
            td.terrainLayers = new[] { gras, erde };

            int ares = 256;
            td.alphamapResolution = ares;
            float[,,] map = new float[ares, ares, 2];

            for (int zi = 0; zi < ares; zi++)
                for (int xi = 0; xi < ares; xi++)
                {
                    float u = (float)xi / (ares - 1);
                    float v = (float)zi / (ares - 1);
                    Vector2 p = new Vector2(tPos.x + u * size - o.x, tPos.z + v * size - o.z);

                    float dirt = 0f;
                    float rectD = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                    if (rectD > ringIn + 0.4f * ts && rectD < ringOut - 0.4f * ts) dirt = 1f;
                    if (addWege && Mathf.Abs(rectD - ringWeg) < 0.7f * ts) dirt = 1f;
                    foreach (var (a, b) in wege)
                        if (DistSeg(p, a, b) < 0.7f * ts) dirt = 1f;
                    foreach (var h in hoehlen)
                        if (DistSeg(p, h.ent - h.dirIn * 1.2f * ts, h.ent + h.dirIn * h.len) < h.breite * 0.5f) dirt = 1f;

                    map[zi, xi, 0] = 1f - dirt;
                    map[zi, xi, 1] = dirt;
                }
            td.SetAlphamaps(0, 0, map);
        }

        TerrainLayer MakeLayer(string name, Color c, Texture2D textur)
        {
            if (!AssetDatabase.IsValidFolder(LAND_DIR))
                AssetDatabase.CreateFolder("Assets", "Landschaft");

            Texture2D tex = textur;
            if (tex == null)
            {
                string texPath = $"{LAND_DIR}/{name}_Farbe.asset";
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null)
                {
                    tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color[16];
                    for (int i = 0; i < 16; i++) px[i] = c;
                    tex.SetPixels(px);
                    tex.Apply();
                    AssetDatabase.CreateAsset(tex, texPath);
                }
            }

            string layerPath = $"{LAND_DIR}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = textur != null ? new Vector2(3f * ts, 3f * ts) : new Vector2(50f, 50f);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // ─── Bauernhof: Hütte mit Boden, Tür + Einrichtung; Garten abseits ─
        // f < 1 staucht das Layout, wenn wenig Platz ist.
        void BuildBauernhof(GameObject parent, int idx, Vector3 pos, float rotY, float f)
        {
            var grp = Child(parent, "Bauernhof" + (idx + 1));
            grp.transform.position = pos;

            var wandSrc    = Load("wall-pane-wood.fbx");
            var fensterSrc = Load("wall-pane-wood-window.fbx");
            // Tür: EXAKT dasselbe Stück wie bei den Burg-Häusern
            // (wall-paint-flat, rein optisch, Collider entfernt → begehbar)
            var tuerSrc    = Load("wall-paint-flat.fbx");
            var dachSrc    = Load("roof.fbx");
            var bodenSrc   = Load("floor.fbx");
            float wandH    = MeasureDimensions(wandSrc).height;

            // Wände. Tür wie bei den Burg-Häusern: echtes Türstück, Collider
            // entfernt, damit man hindurchgehen kann
            for (int s = 0; s < 4; s++)
            {
                var rot = Quaternion.Euler(0, s * 90f, 0);
                for (int w = 0; w < 2; w++)
                {
                    float t = -ts + (w + 0.5f) * ts;
                    bool istTuer = s == 2 && w == 0;
                    var piece = istTuer && tuerSrc != null ? tuerSrc
                              : (s == 0 && fensterSrc != null && w == 1) ? fensterSrc : wandSrc;
                    var wandGo = PlaceKit(grp, piece, pos + SidePos(s, t, ts), rot);
                    if (istTuer && wandGo != null) RemoveColliders(wandGo);
                }
            }

            // Boden innen (wie in den Burg-Häusern), exakt auf Kachelmaß
            if (bodenSrc != null)
            {
                float bfp = MeasureDimensions(bodenSrc).footprint;
                for (int xi = -1; xi <= 1; xi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                    {
                        var tile = PlaceKit(grp, bodenSrc,
                            pos + new Vector3(xi * 0.5f * ts, 0.02f, zi * 0.5f * ts), Quaternion.identity);
                        if (tile != null && bfp > 0.01f)
                        {
                            tile.transform.localScale = new Vector3(ts / bfp, 1f, ts / bfp);
                            FixOpaque(tile);
                        }
                    }
            }

            // EIN Dach-Objekt über dem ganzen Haus, skaliert auf Hausbreite
            // + 1 Kachel Überhang – exakt wie bei den Burg-Häusern (PlaceRoof)
            if (dachSrc != null)
            {
                var dach = PlaceKit(grp, dachSrc, pos + Vector3.up * wandH, Quaternion.identity);
                if (dach != null)
                {
                    float fp = MeasureDimensions(dachSrc).footprint;
                    if (fp > 0.01f)
                    {
                        float sD = (2f * ts + ts) / fp; // 2×2-Haus + Überhang
                        dach.transform.localScale = new Vector3(sD, sD, sD);
                    }
                    FixOpaque(dach);
                }
            }

            // Einrichtung: Bett, Möbel, Sack und ein Kerzenständer mit Licht
            PlacePack(grp, new[] { "bed" }, null,
                pos + new Vector3(-0.5f * ts, 0.05f, 0.4f * ts), 180f, 0.55f * k, idx, "box", "castle");
            PlacePack(grp, new[] { "furniture" }, null,
                pos + new Vector3(0.5f * ts, 0.05f, -0.3f * ts), 20f, 0.75f * k, idx);
            PlacePack(grp, new[] { "bag" }, null,
                pos + new Vector3(0.55f * ts, 0.05f, 0.5f * ts), 60f, 0.5f * k, idx, "none");
            var staender = PlacePackGo(grp, new[] { "candleholder" },
                pos + new Vector3(-0.55f * ts, 0.05f, -0.5f * ts), 0f, 1.1f * k, idx, "none");
            var lichtGo = new GameObject("Licht");
            lichtGo.transform.SetParent((staender != null ? staender.transform : grp.transform), true);
            lichtGo.transform.position = pos + new Vector3(-0.55f * ts, 1.2f * k, -0.5f * ts);
            var licht = lichtGo.AddComponent<Light>();
            licht.type      = LightType.Point;
            licht.color     = new Color(1f, 0.68f, 0.35f);
            licht.intensity = 1.3f;
            licht.range     = Mathf.Max(2f, 5f * k);
            licht.shadows   = LightShadows.None;

            // GARTEN vor der Tür (Tür sitzt lokal bei -Z, siehe SidePos(2,...)
            // und der Türplatzierung s==2 oben) statt seitlich bei +X: die
            // Richtung "vor der Tür" zeigt nach der Hof-Rotation (rotY, aus
            // fDir abgeleitet) IMMER nach innen zur Burg – seitlich (+X) konnte
            // je nach Hof-Ausrichtung dagegen über den Kartenrand hinausragen.
            // Rot90 dreht die komplette alte (+X-abseits-)Anordnung um 90°,
            // damit sie stattdessen Richtung -Z (zur Tür) liegt: (gz,0) → (0,-gz).
            Vector3 Rot90(float x, float z) => new Vector3(z, 0.02f, -x);
            // Komponiert die +90°-Drehung des Positions-Mappings sauber mit
            // der ursprünglichen Stück-Rotation (kein bloßes Vertauschen).
            Quaternion Rot90Rot(Quaternion rot) => Quaternion.Euler(0, 90, 0) * rot;

            float gz = 4.2f * ts * f; // Garten-Mitte, deutlich vor der Tür
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                {
                    Vector3 pp = pos + Rot90(gz + (c - 1) * 0.55f * ts * f, (r - 0.5f) * 0.7f * ts * f);
                    string[] keys = (r + c) % 2 == 0 ? new[] { "cabbage" } : new[] { "tomatoplant", "tomato" };
                    PlacePack(grp, keys, null, pp, (r * 3 + c) * 60f, 0.35f * k, c, "none");
                }
            var zaunSrc = Load("fence-wood.fbx");
            if (zaunSrc != null)
            {
                // Geschlossenes Zaun-Rechteck 2×2 Kacheln: Die Zaunstücke sind
                // nativ genau 1 Kachel lang – bei Halbmaß-Abständen (±ts/2 um
                // die Ecken) BERÜHREN sich die Ecken exakt, kein Loch mehr.
                // Genau EIN Stück fehlt (Seite zum Haus hin) = Eingang.
                float zr = 1.0f * ts; // halbe Rechteck-Kante – passt zu 2 Zaunstücken pro Seite
                // Nordseite (vorher: Nordseite)
                PlaceKit(grp, zaunSrc, pos + Rot90(gz - 0.5f * ts,  zr), Rot90Rot(Quaternion.identity));
                PlaceKit(grp, zaunSrc, pos + Rot90(gz + 0.5f * ts,  zr), Rot90Rot(Quaternion.identity));
                // Südseite (vorher: Südseite)
                PlaceKit(grp, zaunSrc, pos + Rot90(gz - 0.5f * ts, -zr), Rot90Rot(Quaternion.identity));
                PlaceKit(grp, zaunSrc, pos + Rot90(gz + 0.5f * ts, -zr), Rot90Rot(Quaternion.identity));
                // Ostseite (vorher: Ostseite)
                PlaceKit(grp, zaunSrc, pos + Rot90(gz + zr,  0.5f * ts), Rot90Rot(Quaternion.Euler(0, 90, 0)));
                PlaceKit(grp, zaunSrc, pos + Rot90(gz + zr, -0.5f * ts), Rot90Rot(Quaternion.Euler(0, 90, 0)));
                // Westseite: nur EIN Stück – das andere fehlt = Eingang, zum Haus hin
                PlaceKit(grp, zaunSrc, pos + Rot90(gz - zr, -0.5f * ts), Rot90Rot(Quaternion.Euler(0, 90, 0)));
            }
            PlacePack(grp, new[] { "wateringcan" }, null,
                pos + Rot90(gz - 0.9f * ts * f, 0.8f * ts * f), 120f, 0.35f * k, 0, "none");

            // Einzelne Bäume am Garten (nur echte Nature-Pack-Bäume)
            PlacePack(grp, new[] { "tree" }, null,
                pos + Rot90(gz + 1.0f * ts * f, 1.8f * ts * f), idx * 80f, 2.4f * k, idx, "trunk", "nature");
            PlacePack(grp, new[] { "tree" }, null,
                pos + Rot90(-1.6f * ts * f, -1.4f * ts * f), idx * 80f + 150f, 2.1f * k, idx + 2, "trunk", "nature");

            // Vorräte an der Hütte (Kiste als Kit-Teil – die Pack-"box" war
            // die Riesen-Kiste, die nicht in die Umgebung passte)
            PlacePack(grp, new[] { "barrel" }, "detail-barrel.fbx", pos + new Vector3(-1.3f * ts, 0, 0.6f * ts), 20f + idx * 40f, 1.0f * k, idx);
            PlaceKit(grp, Load("detail-crate.fbx"), pos + new Vector3(-1.3f * ts, 0, -0.4f * ts), Quaternion.Euler(0, 65f, 0));

            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

        // ─── Wald: 50× Big Oak + 20× Starter-Kit + 20× Polyeler ───
        // Zufällig über die Map verteilt (Wege/Höfe bleiben frei),
        // dazu Büsche, Gras-Büschel und Buchstaben-Felsen auf den Wiesen.
        // Jeder Neubau würfelt eine neue Verteilung.
        void BuildWald(GameObject parent, Terrain terrain, Vector3 o,
            float ringWeg, float aussen, float maxR, float farmR,
            List<Hoehle> hoehlen, Vector2[] hoefe, List<(Vector2 a, Vector2 b)> wege)
        {
            float rMin = ringWeg + 1.5f * ts;
            float rMax = maxR - 1.2f * ts;

            // Feste Baum-Sorten mit Stückzahlen, direkt per Pfad geladen:
            //   100× Big Oak Tree, 40× Nature-Starter-Kit-Bäume, 40× Polyeler
            // (auf Wunsch doppelt so viele UND doppelt so groß wie vorher –
            // gilt nur für die Landschaft, NICHT für die Bäume im Burghof,
            // die kommen separat aus BurgDekoBuilder)
            var sorten = new (GameObject[] prefabs, int anzahl, float minH, float maxH)[]
            {
                (LadeAlle(new[] {
                    "Assets/ALP_Assets/Big Oak Tree FREE/Prefabs/OakBigTree01_pr.prefab",
                }), 100, 6.0f, 7.6f),
                (LadeAlle(new[] {
                    "Assets/NatureStarterKit2/Nature/tree01.prefab",
                    "Assets/NatureStarterKit2/Nature/tree02.prefab",
                    "Assets/NatureStarterKit2/Nature/tree03.prefab",
                    "Assets/NatureStarterKit2/Nature/tree04.prefab",
                }), 40, 4.4f, 6.0f),
                (LadeAlle(new[] {
                    "Assets/Polyeler/EssentialNaturePack/Prefabs/SawtoothOak.prefab",
                    "Assets/Polyeler/EssentialNaturePack/Prefabs/PlaneTree.prefab",
                }), 40, 4.8f, 6.4f),
            };

            int nr = 0;
            foreach (var (prefabs, anzahl, minH, maxH) in sorten)
            {
                if (prefabs.Length == 0) continue;
                int gesetzt = 0;
                for (int t2 = 0; t2 < anzahl * 5 && gesetzt < anzahl; t2++)
                {
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    float rad = Random.Range(rMin, rMax);
                    Vector2 p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > rMax) continue;
                    if (!PlatzFrei(p, ringWeg, farmR, hoehlen, hoefe, wege)) continue;

                    Vector3 wp = o + new Vector3(p.x, 0, p.y);
                    wp.y = SampleY(terrain, wp);
                    PlatziereBaum(parent, prefabs[gesetzt % prefabs.Length], wp, Random.Range(minH, maxH) * k);
                    gesetzt++;
                    nr++;
                }
            }

            // Wiese beleben: Büsche + viele Gras-Büschel zwischen den Flecken
            for (int i = 0; i < 18; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rad = Random.Range(rMin, rMax);
                Vector2 p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > rMax) continue;
                if (!PlatzFrei(p, ringWeg, farmR, hoehlen, hoefe, wege)) continue;

                Vector3 wp = o + new Vector3(p.x, 0, p.y);
                wp.y = SampleY(terrain, wp);
                PlacePack(parent, new[] { "bush", "shrub" }, null, wp,
                    Random.Range(0f, 360f), Random.Range(0.7f, 1.1f) * k, i, "none", "nature");
            }
            for (int i = 0; i < 45; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rad = Random.Range(rMin, rMax);
                Vector2 p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > rMax) continue;
                if (!PlatzFrei(p, ringWeg, farmR, hoehlen, hoefe, wege)) continue;

                Vector3 wp = o + new Vector3(p.x, 0, p.y);
                wp.y = SampleY(terrain, wp);
                PlacePack(parent, new[] { "grass" }, null, wp, Random.Range(0f, 360f), 0.3f * k, i, "none");
            }

            int felsenGesamt = BuildVersteckFelsen(parent, terrain, o, ringWeg, farmR, rMin, rMax, hoefe, wege);

            Debug.Log($"[LandschaftBuilder] Wald: {nr} Bäume verteilt (100 Big Oak + 40 Starter-Kit + 40 Polyeler, soweit Platz), " +
                      $"{felsenGesamt} Versteck-Steine.");
        }

        // ─── Versteck-Steine: 15 Stück insgesamt, in Gruppen von max. 3 ───
        // Mal 3 nebeneinander, mal 2, mal nur einer – nie mehr als 3 an einem
        // Fleck. Über die ganze Landschaft verteilt (Wiese/Wald-Ring zwischen
        // Weg-Ring und Flächenrand), nie in der Burg selbst.
        int BuildVersteckFelsen(GameObject parent, Terrain terrain, Vector3 o,
            float ringWeg, float farmR, float rMin, float rMax,
            Vector2[] hoefe, List<(Vector2 a, Vector2 b)> wege)
        {
            // Summe = 15, jede Gruppe maximal 3 Steine
            int[] clusterGroessen = { 3, 2, 1, 3, 2, 1, 3 };
            var keineHoehlen = new List<Hoehle>();   // PlatzFrei braucht den Parameter, es gibt aber keine mehr
            int gesamt = 0;

            foreach (int groesse in clusterGroessen)
            {
                // Cluster-Mittelpunkt suchen: irgendwo auf der Landschaft,
                // nicht auf Wegen/Höfen
                Vector2 zentrum = Vector2.zero;
                bool gefunden = false;
                for (int versuch = 0; versuch < 20; versuch++)
                {
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    float rad = Random.Range(rMin, rMax);
                    Vector2 c = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    if (Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y)) > rMax) continue;
                    if (!PlatzFrei(c, ringWeg, farmR, keineHoehlen, hoefe, wege)) continue;
                    zentrum = c;
                    gefunden = true;
                    break;
                }
                if (!gefunden) continue;

                // Streuradius wächst mit der Cluster-Größe – große Haufen
                // breiter verteilt, einzelne Steine bleiben knapp am Punkt
                float streuung = (0.3f + groesse * 0.08f) * ts;
                for (int i = 0; i < groesse; i++)
                {
                    Vector2 jitter = Random.insideUnitCircle * streuung;
                    Vector2 p = zentrum + jitter;
                    Vector3 wp = o + new Vector3(p.x, 0, p.y);
                    wp.y = SampleY(terrain, wp);
                    PlatziereStein(parent, wp, Random.Range(0f, 360f), Random.Range(0.5f, 1.0f) * wh, gesamt);
                    gesamt++;
                }
            }
            return gesamt;
        }

        // Prefabs direkt per Pfad laden (für Sorten, die die Stichwort-Suche
        // nicht erfasst, z.B. das Big-Oak-Pack unter ALP_Assets)
        static GameObject[] LadeAlle(string[] pfade)
        {
            var list = new List<GameObject>();
            foreach (string p in pfade)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) list.Add(go);
                else Debug.LogWarning($"LandschaftBuilder: Baum-Prefab fehlt: {p}");
            }
            return list.ToArray();
        }

        // Buchstaben-Fels (Rock_A–D) platzieren, gleichmäßig skaliert
        GameObject PlatziereStein(GameObject parent, Vector3 pos, float rotY, float targetH, int variant)
        {
            if (steinPrefabs.Length == 0) return null;
            var src = steinPrefabs[variant % steinPrefabs.Length];
            var go  = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
            float h = MeasureDimensions(src).height;
            if (h > 0.01f)
                // Original-Skalierung des Prefab-Roots MULTIPLIZIEREN statt
                // ersetzen – manche Packs (z.B. Modular Castle "box") haben am
                // Root schon eine Skala; sie zu überschreiben machte die Teile
                // riesig
                go.transform.localScale = src.transform.localScale * (targetH / h);
            EnsureCollider(go);
            return go;
        }

        // Baum mit Stamm-Collider platzieren, gleichmäßig auf Zielhöhe skaliert
        GameObject PlatziereBaum(GameObject parent, GameObject src, Vector3 pos, float targetH)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            float h = MeasureDimensions(src).height;
            if (h > 0.01f)
                // Original-Skalierung des Prefab-Roots MULTIPLIZIEREN statt
                // ersetzen – manche Packs (z.B. Modular Castle "box") haben am
                // Root schon eine Skala; sie zu überschreiben machte die Teile
                // riesig
                go.transform.localScale = src.transform.localScale * (targetH / h);

            RemoveColliders(go);
            // Kapsel im LOKALEN Raum: gemessene Höhe h enthält die Root-Skala
            // bereits → für lokale Werte wieder herausrechnen
            float lokalH = h / Mathf.Max(0.0001f, src.transform.localScale.y);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, lokalH * 0.5f, 0);
            cap.height = lokalH;
            cap.radius = Mathf.Max(0.05f, lokalH * 0.06f);
            return go;
        }

        bool PlatzFrei(Vector2 p, float ringWeg, float farmR,
            List<Hoehle> hoehlen, Vector2[] hoefe, List<(Vector2 a, Vector2 b)> wege)
        {
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) < ringWeg + 1.0f * ts) return false;
            foreach (var (a, b) in wege)
                if (DistSeg(p, a, b) < 1.2f * ts) return false;
            foreach (var f in hoefe)
                if (Vector2.Distance(p, f) < farmR * 1.9f + 1.0f * ts) return false;
            foreach (var h in hoehlen)
                if (Vector2.Distance(p, h.ent) < 2.5f * ts) return false;
            return true;
        }

        // ─── Brücke ──────────────────────────────────────
        void BuildBruecke(GameObject parent, Vector3 center, float span)
        {
            var list = FindPackAll(new[] { "bridge" });
            if (list.Count > 0)
            {
                var src = list[0];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
                var (bx, bz, _) = MeasureLocalBounds(src);
                float longest = Mathf.Max(bx, bz);
                go.transform.SetPositionAndRotation(center, Quaternion.Euler(0, bx > bz ? 90f : 0f, 0));
                if (longest > 0.01f)
                    go.transform.localScale = src.transform.localScale * (span / longest);
                EnsureCollider(go);
                return;
            }

            var grp = Child(parent, "Holzbrücke");
            Prim(PrimitiveType.Cube, "Fahrbahn", grp, center + new Vector3(0, 0.05f * wh, 0),
                new Vector3(1.4f * ts, 0.08f * wh, span), woodMat);
            Prim(PrimitiveType.Cube, "Geländer_W", grp, center + new Vector3(-0.65f * ts, 0.35f * wh, 0),
                new Vector3(0.08f * ts, 0.5f * wh, span), woodMat);
            Prim(PrimitiveType.Cube, "Geländer_O", grp, center + new Vector3(0.65f * ts, 0.35f * wh, 0),
                new Vector3(0.08f * ts, 0.5f * wh, span), woodMat);
            Prim(PrimitiveType.Cylinder, "Stütze_W", grp, center + new Vector3(-0.5f * ts, -grabenTiefe * 0.5f, 0),
                new Vector3(0.15f * ts, grabenTiefe * 0.55f, 0.15f * ts), woodMat);
            Prim(PrimitiveType.Cylinder, "Stütze_O", grp, center + new Vector3(0.5f * ts, -grabenTiefe * 0.5f, 0),
                new Vector3(0.15f * ts, grabenTiefe * 0.55f, 0.15f * ts), woodMat);
        }

        // ─── Fackel (Pack-Fackel + Licht, sonst Eigenbau) ─
        // Größe an der Wandhöhe orientiert (nicht am Möbel-Maßstab k), damit
        // die Fackeln zum Höhleneingang in 1.9 Wandhöhen passen
        void PlaceFackel(GameObject parent, Vector3 pos)
        {
            float gr = Mathf.Max(1.3f * k, 0.55f * wh);
            var go = PlacePackGo(parent, new[] { "torch" }, pos, 0f, gr, 0, "none");
            if (go == null)
            {
                BuildFackel(parent, pos);
                return;
            }
            var lichtGo = new GameObject("Licht");
            lichtGo.transform.SetParent(go.transform, true);
            lichtGo.transform.position = pos + new Vector3(0, gr * 0.92f, 0);
            var licht = lichtGo.AddComponent<Light>();
            licht.type      = LightType.Point;
            licht.color     = new Color(1f, 0.62f, 0.28f);
            licht.intensity = 2.2f;
            licht.range     = Mathf.Max(3f, 3.5f * wh);
            licht.shadows   = LightShadows.None;
        }

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

        // ─── Fallback-Bausteine ──────────────────────────
        void BuildFels(GameObject parent, Vector3 pos, float rotY, float s)
        {
            var grp = Child(parent, "Fels");
            grp.transform.position = pos;
            Prim(PrimitiveType.Cube, "F1", grp, pos + new Vector3(0, 0.45f, 0) * s,
                new Vector3(1.1f, 0.95f, 0.9f) * s, felsMat, new Vector3(8, 15, 5));
            Prim(PrimitiveType.Cube, "F2", grp, pos + new Vector3(0.35f, 0.3f, 0.2f) * s,
                new Vector3(0.8f, 0.65f, 0.7f) * s, felsMat, new Vector3(-6, 40, -8));
            Prim(PrimitiveType.Cube, "F3", grp, pos + new Vector3(-0.3f, 0.25f, -0.15f) * s,
                new Vector3(0.7f, 0.55f, 0.65f) * s, felsMat, new Vector3(4, -25, 10));
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

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
            Prim(PrimitiveType.Cylinder, "Rad_N", grp, pos + new Vector3(0, 0.35f, 0.45f) * k,
                new Vector3(0.7f, 0.03f, 0.7f) * k, woodMat, new Vector3(90, 0, 0));
            Prim(PrimitiveType.Cylinder, "Rad_S", grp, pos + new Vector3(0, 0.35f, -0.45f) * k,
                new Vector3(0.7f, 0.03f, 0.7f) * k, woodMat, new Vector3(90, 0, 0));
            Prim(PrimitiveType.Cylinder, "Deichsel", grp, pos + new Vector3(1.0f, 0.3f, 0) * k,
                new Vector3(0.06f, 0.45f, 0.06f) * k, woodMat, new Vector3(0, 0, 65));
            Prim(PrimitiveType.Cube, "Heu", grp, pos + new Vector3(-0.15f, 0.78f, 0) * k,
                new Vector3(0.9f, 0.35f, 0.6f) * k, heuMat, new Vector3(0, 10, 0));
            grp.transform.rotation = Quaternion.Euler(0, rotY, 0);
        }

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

        // ─── Pack-Suche ──────────────────────────────────
        // pfadMuss: optionaler Zusatzfilter, z.B. "nature" → nur Nature-Packs
        // (gegen blockige Bäume/Steine aus anderen Kits)
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
                        && !pl.Contains("farm") && !pl.Contains("castle")
                        && !pl.Contains("rpg") && !pl.Contains("polyeler")) continue;
                    if (pfadMuss != null && !pl.Contains(pfadMuss)) continue;
                    if (pl.Contains("/burg/")) continue;
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
                // Original-Skalierung des Prefab-Roots MULTIPLIZIEREN statt
                // ersetzen – manche Packs (z.B. Modular Castle "box") haben am
                // Root schon eine Skala; sie zu überschreiben machte die Teile
                // riesig
                go.transform.localScale = src.transform.localScale * (targetH / h);

            if (collider == "trunk")
            {
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
            var go = PlacePackGo(parent, nameKeys, pos, rotY, targetH, variant, collider, pfadMuss);
            if (go != null) return true;
            if (fallbackKit == null) return false;
            var kit = Load(fallbackKit);
            if (kit == null) return false;
            PlaceKit(parent, kit, pos, Quaternion.Euler(0, rotY, 0));
            return true;
        }

        GameObject PlaceKit(GameObject parent, GameObject src, Vector3 pos, Quaternion rot)
        {
            if (src == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent.transform);
            go.transform.SetPositionAndRotation(pos, rot);
            EnsureCollider(go);
            return go;
        }

        // ─── Materialien / Hilfsmethoden ─────────────────
        void LoadMaterials()
        {
            var fw = Load("fence-wood.fbx");
            var r  = fw != null ? fw.GetComponentInChildren<Renderer>() : null;
            woodMat  = r != null ? r.sharedMaterial : null;
            felsMat  = GetOrCreateMat("Fels",   new Color(0.30f, 0.29f, 0.28f));
            heuMat   = GetOrCreateMat("Heu",    new Color(0.85f, 0.68f, 0.28f));
            grasMat  = GetOrCreateMat("Gras",   new Color(0.30f, 0.52f, 0.22f));
            flameMat = GetOrCreateMat("Flamme", new Color(1f, 0.55f, 0.15f), true);
            if (woodMat == null) woodMat = felsMat;

            // Prozedurale Rasen-Textur aufs Wiesen-Material (Pack-"grass"-
            // Texturen sind Büschel-Sprites → wirkten weiß)
            grasMat.mainTexture = MaterialFixer.HoleGrasTextur();
            grasMat.mainTextureScale = new Vector2(3f, 3f);
            grasMat.color = Color.white;
            EditorUtility.SetDirty(grasMat);
        }

        static Material GetOrCreateMat(string name, Color c, bool emissive = false)
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
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
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

        static float SampleY(Terrain terrain, Vector3 worldPos)
            => terrain.SampleHeight(worldPos) + terrain.transform.position.y;

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
            if (go == null) Debug.LogWarning($"LandschaftBuilder: '{name}' nicht gefunden.");
            return go;
        }
    }
}
