using UnityEngine;

namespace NeonCatch
{
    // Baut beim Play-Drücken AUTOMATISCH die komplette Spielwelt auf – ohne
    // dass irgendetwas in der Szene eingerichtet oder gespeichert sein muss:
    //
    //  - Burggraben mit Wasser, Steinen, Pflanzen, Fischen und Gras
    //  - Tiere: Pferde, Rehe, Tiger und Hühner auf der Wiese, Hunde und
    //    Katzen im Burghof, dazu Vögel
    //  - Teleport-Hütten: 2 im Burghof + 5 in der Landschaft (Hineingehen =
    //    Reise zu einer zufälligen anderen Hütte, danach 15 s sichtbare Sperre)
    //  - 4 stationäre Kanonen: baut KanonenEcken in jeder Szene selbst –
    //    eine pro Map-Ecke, ±60° zur Map-Mitte begrenzt (man kann sich nicht
    //    aus der Map schießen); Linksklick einsteigen, Maus zielt, Linksklick
    //    schießt mit Rauchwolke, der Spieler fliegt in der Kugel mit
    //  - setzt dem Spieler automatisch den Tag "Player"
    //
    // Alle Prefabs werden aus Assets/Resources/KI/ geladen. Das Script startet
    // sich über [RuntimeInitializeOnLoadMethod] selbst – einfach Play drücken.
    public class KI_SzeneAufbau : MonoBehaviour
    {
        // Map-Größe wie im Spiel: von (0,0) bis (120,180)
        const float mapBreite = 120f;
        const float mapLaenge = 180f;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            // Nicht doppelt aufbauen (z.B. wenn schon ein System in der Szene hängt)
            if (FindFirstObjectByType<KI_SzeneAufbau>() != null) return;
            if (FindFirstObjectByType<BurggrabenKomplett>() != null) return;

            var go = new GameObject("KI_SzeneAufbau");
            go.AddComponent<KI_SzeneAufbau>();
        }

        void Awake()
        {
            // Der Spieler braucht den Tag "Player", sonst funktionieren
            // Schwimmen, Falltüren, Kanonen, Gras-Reaktion und Reh-Flucht nicht
            GameObject spieler = GameObject.Find("Spieler");
            if (spieler != null && !spieler.CompareTag("Player"))
                spieler.tag = "Player";

            // Flaeche_1 soll weg (Teleport-Plattform bei der Burg)
            GameObject flaeche1 = GameObject.Find("Flaeche_1");
            if (flaeche1 != null) Destroy(flaeche1);

            // Das vorhandene Terrain (die echte, begehbare Wiese) auf den
            // Layer "Grass" legen, BEVOR die Tiere gebaut werden – ihre
            // Spawn-Suche filtert per Raycast exakt auf diesen Layer
            MarkiereTerrainAlsGrass();

            // Map-Grenzen von der ECHTEN Terrain-Größe ableiten statt einer
            // geschätzten Zahl – der feste 120×180-Wert stammte aus einer Zeit,
            // bevor das echte (vom LandschaftBuilder gebaute) Terrain bekannt
            // war, und hatte mit dessen tatsächlicher Position/Größe nichts zu
            // tun (dadurch konnten Tiere z.B. am echten Terrain-Rand landen,
            // obwohl sie innerhalb der angenommenen Grenzen lagen).
            SetzeMapGrenzenVonTerrain();
            BurggrabenMittelalter.randAbstand = 10f;
            // maxBodenHoehe (absoluter Welt-Höhen-Schwellwert) bewusst AUS
            // gelassen: der stammte aus derselben veralteten Annahme und würde
            // beim echten (viel höher liegenden) Terrain falsch greifen. Die
            // echte Steilhang-/Terrain-Rand-Prüfung (IstAmTerrainRandOderZuSteil,
            // nutzt Terrain.GetSteepness) ersetzt ihn bereits präziser.
            BurggrabenMittelalter.maxBodenHoehe = float.MaxValue;

            BaueBurggraben();
            BaueTiere();
            BaueFalltueren();
            // Kanonen baut jetzt KanonenEcken (4 Stück, eine pro Map-Ecke,
            // läuft in jeder Szene selbst) – nicht mehr doppelt bauen

            // Rosa/Lila-Reparatur übernimmt ShaderRosaFix – der startet sich
            // in JEDER Szene selbst (auch ohne KI_SzeneAufbau)

            // Tiere schlafen legen, wenn der Spieler weit weg ist (gegen Lag)
            StartCoroutine(TiereSchlafenLassen());

            Debug.Log("KI_SzeneAufbau: Spielwelt komplett aufgebaut.");
        }

        // ------------------------------------------------------------------
        // Burggraben (nutzt BurggrabenKomplett, Prefabs aus Resources/KI)
        // ------------------------------------------------------------------
        void BaueBurggraben()
        {
            var graben = gameObject.AddComponent<BurggrabenKomplett>();
            graben.steinPrefabs = Lade("Rock_A", "Rock_B", "Rock_C", "Rock_D");
            graben.pflanzenPrefabs = Lade("Wasserpflanze");
            graben.fischPrefabs    = Lade("Sardine");
            graben.fischGroesse    = 2f;   // Sardinen-Modell ist klein
            graben.grasPrefabs     = Lade("Gras_1", "Gras_2", "Wasserpflanze");

            // Weniger Objekte gegen Lag
            graben.steinAnzahl    = 12;
            graben.pflanzenAnzahl = 6;
            graben.fischAnzahl    = 20;   // 10 im Uhrzeigersinn, 10 dagegen (siehe PlatziereFische)
            graben.grasAnzahl     = 15;

            // Kein Graben-Boden-Mesh und kein generierter Wiesen-Ring mehr –
            // es gibt bereits ein echtes Terrain als Wiese in der Szene
            // (siehe MarkiereTerrainAlsGrass), das wird stattdessen benutzt.
            graben.baueGrabenBoden = false;
            graben.baueWiesenBoden = false;
        }

        // ------------------------------------------------------------------
        // Tiere (nutzt BurggrabenMittelalter)
        // ------------------------------------------------------------------
        void BaueTiere()
        {
            var tiere = gameObject.AddComponent<BurggrabenMittelalter>();
            tiere.pferdPrefabs         = Lade("Pferd");
            tiere.rehPrefabs           = Lade("Reh");
            tiere.tigerPrefabs         = Lade("Tiger");
            tiere.huhnPrefabs          = Lade("Huhn");
            tiere.hundPrefabs          = Lade("Hund");    // Animals FREE: Dog_001
            tiere.katzePrefabs         = Lade("Katze");   // Animals FREE: Kitty_001
            tiere.vogelPrefabs         = Lade("Vogel_Spatz", "Vogel_Kraehe", "Vogel_Rotkehlchen");

            // Wiese: 4 Rehe, 2 Tiger, 4 Pferde, 6 Hühner – Burghof: 6 Hunde, 4 Katzen
            tiere.pferdAnzahl         = 4;
            tiere.rehAnzahl           = 4;
            tiere.tigerAnzahl         = 2;
            tiere.huhnAnzahl          = 6;
            tiere.hundAnzahl          = 6;
            tiere.katzeAnzahl         = 4;
            tiere.vogelAnzahl         = 18;

            // Such-Radius an die tatsächliche Terrain-Größe anpassen, statt
            // eine geschätzte Zahl zu benutzen – sonst würde außerhalb des
            // echten Terrains gesucht und ständig nichts gefunden
            float terrainRadius = TerrainWeitesteEcke();
            if (terrainRadius > 0f)
            {
                tiere.wiesenRadiusAussen = terrainRadius;
                tiere.wiesenRadiusAussenManuellGesetzt = true;
            }

            // Echte Bauernhöfe (vom LandschaftBuilder gebaut, Gruppe
            // "Bauernhöfe" mit Kindern "Bauernhof1/2/3") als Sperrzonen
            // eintragen – sonst liefen Tiere mangels Wand-Kollision einfach
            // durch die offene Tür mitten durchs Haus
            GameObject hoefeGruppe = GameObject.Find("Bauernhöfe");
            if (hoefeGruppe != null)
            {
                var zonen = new System.Collections.Generic.List<Transform>();
                foreach (Transform hof in hoefeGruppe.transform)
                    zonen.Add(hof);
                if (zonen.Count > 0)
                {
                    tiere.sperrZonen = zonen.ToArray();
                    tiere.sperrZonenRadius = 6f;   // deckt Hütte + Garten + Zaun ab
                }
            }
        }

        // Map-Grenzen aus der ECHTEN Terrain-Position/-Größe berechnen, statt
        // der geschätzten mapBreite/mapLaenge-Konstanten
        void SetzeMapGrenzenVonTerrain()
        {
            Terrain terrain = FindeLandschaftTerrain();
            if (terrain == null)
            {
                // Kein echtes Terrain gefunden: alte Schätzwerte als Fallback
                BurggrabenMittelalter.mapMin = new Vector2(0f, 0f);
                BurggrabenMittelalter.mapMax = new Vector2(mapBreite, mapLaenge);
                return;
            }

            Vector3 min = terrain.transform.position;
            Vector3 max = min + terrain.terrainData.size;
            BurggrabenMittelalter.mapMin = new Vector2(min.x, min.z);
            BurggrabenMittelalter.mapMax = new Vector2(max.x, max.z);
        }

        // Sucht das Terrain gezielt unter dem Objekt "Landschaft" (genau wie
        // BurggrabenBuilder das selbst tut) statt über Terrain.activeTerrain/
        // FindFirstObjectByType zu raten – bei mehreren Terrains in der Szene
        // (z.B. Testreste) konnte das sonst das falsche erwischen und dadurch
        // Tiere/Falltüren komplett ohne gültigen Spawn-Platz zurücklassen.
        static Terrain FindeLandschaftTerrain()
        {
            GameObject landschaft = GameObject.Find("Landschaft");
            Terrain terrain = landschaft != null ? landschaft.GetComponentInChildren<Terrain>() : null;
            if (terrain == null)
                terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
            return terrain;
        }

        // Entfernung von der Burgmitte zur am weitesten entfernten Ecke des
        // vorhandenen Terrains – 0, falls kein Terrain in der Szene liegt.
        // Wird für Tiere UND Falltüren als Such-Radius benutzt, damit beide
        // Systeme dieselbe Wiese abdecken.
        float TerrainWeitesteEcke()
        {
            Terrain terrain = FindeLandschaftTerrain();
            if (terrain == null) return 0f;

            Vector3 min = terrain.transform.position;
            Vector3 max = min + terrain.terrainData.size;
            Vector3 zentrum = transform.position;
            float weitesteEcke = 0f;
            foreach (Vector3 ecke in new[] {
                new Vector3(min.x, 0f, min.z), new Vector3(max.x, 0f, min.z),
                new Vector3(min.x, 0f, max.z), new Vector3(max.x, 0f, max.z) })
                weitesteEcke = Mathf.Max(weitesteEcke, Vector3.Distance(zentrum, ecke));
            return weitesteEcke;
        }

        // Das vorhandene Terrain in der Szene bekommt den Layer "Grass", damit
        // die Tiere per Raycast genau darauf spawnen (siehe
        // BurggrabenMittelalter.SuchePlatzAufWiese) – ohne eigene, neu gebaute
        // Wiesenfläche.
        void MarkiereTerrainAlsGrass()
        {
            Terrain terrain = FindeLandschaftTerrain();
            if (terrain == null)
            {
                Debug.LogWarning("KI_SzeneAufbau: Kein Terrain in der Szene gefunden – Tiere finden keinen Spawn-Platz.");
                return;
            }

            // WICHTIG: Die Tier-/Falltür-Suche erkennt die Wiese direkt am
            // TerrainCollider (robuster als der Layer, der nach einem reinen
            // Datei-Edit der ProjectSettings evtl. noch nicht aktiv ist).
            // Ohne Collider fehlt der Wiese also komplett die Erkennung.
            TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
            if (terrainCollider == null)
            {
                terrainCollider = terrain.gameObject.AddComponent<TerrainCollider>();
                terrainCollider.terrainData = terrain.terrainData;
            }

            // Layer "Grass" zusätzlich setzen (falls vorhanden) – rein
            // informativ für andere Systeme, wird für die Erkennung selbst
            // nicht mehr zwingend gebraucht
            int grassLayer = LayerMask.NameToLayer("Grass");
            if (grassLayer >= 0)
            {
                terrain.gameObject.layer = grassLayer;
                terrainCollider.gameObject.layer = grassLayer;
            }
        }

        // ------------------------------------------------------------------
        // 10 Falltüren, gleichmäßig verteilt (nutzt FalltuerSpawner)
        // ------------------------------------------------------------------
        void BaueFalltueren()
        {
            // Teleport-Hütten (kleine Häuschen zum Hineingehen): 2 im
            // Burghof + 5 in der Landschaft, baut der Spawner selbst
            var falltueren = gameObject.AddComponent<FalltuerSpawner>();
            falltueren.anzahlBurg       = 2;
            falltueren.anzahlLandschaft = 5;
            falltueren.sperrZeit        = 15f;

            // Gleicher Such-Radius wie bei den Tieren: Falltüren sollen genau
            // auf derselben Wiese landen, nicht in einem schmäleren Ring
            float terrainRadius = TerrainWeitesteEcke();
            if (terrainRadius > 0f)
            {
                falltueren.radiusAussen = terrainRadius;
                falltueren.radiusAussenManuellGesetzt = true;
            }
        }

        // ------------------------------------------------------------------
        // 4 stationäre Kanonen (CannonStation): 2 im Burghof auf gegenüber-
        // liegenden freien Plätzen, 2 draußen auf der Landschaft, ebenfalls
        // weit auseinander. Nur die 2 Landschafts-Kanonen sind auf ±45° um die
        // Blickrichtung zur Map-Mitte begrenzt – damit kann man sich nicht aus
        // der Map schießen. Die Burghof-Kanonen dürfen sich fast frei drehen.
        // ------------------------------------------------------------------
        void BaueVierKanonen()
        {
            GameObject kugelPrefab = Resources.Load<GameObject>("KI/KanonenKugel_Rund");
            if (kugelPrefab == null) kugelPrefab = LadeEinzeln("KanonenKugel");

            var belegtePlaetze = new System.Collections.Generic.List<Vector3>();

            // 2 Kanonen im Burghof: Suche startet an gegenüberliegenden Seiten
            // der kleinen Innenfläche, damit sie nicht in derselben Ecke landen
            Vector3 burgZentrum = transform.position;
            Vector3? hofA = SucheFreienKanonenPlatz(burgZentrum + new Vector3(5f, 0f, 5f), 5f, belegtePlaetze, 6f);
            Vector3? hofB = SucheFreienKanonenPlatz(burgZentrum + new Vector3(-5f, 0f, -5f), 5f, belegtePlaetze, 6f);
            if (hofA.HasValue)
            {
                belegtePlaetze.Add(hofA.Value);
                BaueKanone("Kanone_Burghof_1", hofA.Value, Random.Range(0f, 360f), 0f, kugelPrefab);
            }
            if (hofB.HasValue)
            {
                belegtePlaetze.Add(hofB.Value);
                BaueKanone("Kanone_Burghof_2", hofB.Value, Random.Range(0f, 360f), 0f, kugelPrefab);
            }

            // 2 Kanonen draußen an weit auseinanderliegenden Ecken der
            // restlichen Map, mit Dreh-Begrenzung zur Map-Mitte hin
            Vector3 mapMitte = new Vector3(mapBreite * 0.5f, 0f, mapLaenge * 0.5f);
            Vector3 ankerA = new Vector3(mapBreite * 0.82f, 0f, mapLaenge * 0.18f);
            Vector3 ankerB = new Vector3(mapBreite * 0.18f, 0f, mapLaenge * 0.82f);
            Vector3? landA = SucheFreienKanonenPlatz(ankerA, 10f, belegtePlaetze, 30f);
            Vector3? landB = SucheFreienKanonenPlatz(ankerB, 10f, belegtePlaetze, 30f);
            if (landA.HasValue)
            {
                belegtePlaetze.Add(landA.Value);
                float yaw = WinkelZuPunkt(landA.Value, mapMitte);
                BaueKanone("Kanone_Landschaft_1", landA.Value, yaw, 45f, kugelPrefab);
            }
            if (landB.HasValue)
            {
                belegtePlaetze.Add(landB.Value);
                float yaw = WinkelZuPunkt(landB.Value, mapMitte);
                BaueKanone("Kanone_Landschaft_2", landB.Value, yaw, 45f, kugelPrefab);
            }
        }

        // Sucht einen freien, flachen Platz nahe "anker": kein Weg/Hügel/
        // Map-Rand (BurggrabenMittelalter.IstGesperrt), keine Wand/Mauer im
        // Weg (CheckCapsule) und genug Abstand zu bereits gewählten Plätzen.
        Vector3? SucheFreienKanonenPlatz(Vector3 anker, float suchRadius,
            System.Collections.Generic.List<Vector3> belegt, float mindestAbstand)
        {
            for (int versuch = 0; versuch < 40; versuch++)
            {
                Vector2 kreis = Random.insideUnitCircle * suchRadius;
                Vector3 kandidat = anker + new Vector3(kreis.x, 0f, kreis.y);

                if (BurggrabenMittelalter.IstGesperrt(kandidat)) continue;
                kandidat.y = BurggrabenMittelalter.BodenHoehe(kandidat);

                // Freiraum in Kopfhöhe über der Kanone prüfen (keine Wand/Mauer/Turm)
                if (Physics.CheckCapsule(kandidat + Vector3.up * 0.5f, kandidat + Vector3.up * 2.2f,
                        0.9f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    continue;

                bool zuNah = false;
                foreach (Vector3 p in belegt)
                    if (Vector3.Distance(p, kandidat) < mindestAbstand) { zuNah = true; break; }
                if (zuNah) continue;

                return kandidat;
            }
            return null;
        }

        float WinkelZuPunkt(Vector3 von, Vector3 nach)
        {
            Vector3 d = nach - von;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        void BaueKanone(string name, Vector3 position, float blickYaw, float maxDrehWinkel, GameObject kugelPrefab)
        {
            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, blickYaw, 0f);   // feste Blickrichtung

            // Klickbarer, NICHT-Trigger Collider: Linksklick = einsteigen (OnMouseDown)
            var klick = root.AddComponent<BoxCollider>();
            klick.size   = new Vector3(1.4f, 1.4f, 2.2f);
            klick.center = new Vector3(0f, 0.7f, 0f);

            // Horizontal-Pivot (Yaw) direkt über dem Boden
            var pivotH = new GameObject("PivotHorizontal").transform;
            pivotH.SetParent(root.transform, false);
            pivotH.localPosition = new Vector3(0f, 0.35f, 0f);

            // Vertikal-Pivot (Pitch), Kind des Horizontal-Pivots
            var pivotV = new GameObject("PivotVertical").transform;
            pivotV.SetParent(pivotH, false);

            // Sichtbares Modell aus dem Cannon Mini Pack
            GameObject modellPrefab = Resources.Load<GameObject>("KI/Kanone");
            if (modellPrefab != null)
            {
                GameObject modell = Instantiate(modellPrefab);
                foreach (Rigidbody rb in modell.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (Collider c in modell.GetComponentsInChildren<Collider>()) Destroy(c);
                modell.transform.SetParent(pivotV, false);
                // Das Pack-Modell zeigt nach -Z (siehe "Small_cannon_Aim"-Marker) –
                // 180° drehen, damit +Z von pivotVertical (unsere Schussrichtung
                // für muzzle/cameraMount) mit dem sichtbaren Rohr übereinstimmt.
                // Falls die Kugel trotzdem "rückwärts" fliegt: diese 180° entfernen.
                modell.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                modell.transform.localPosition = Vector3.zero;
                modell.name = "Kanonen_Modell";
            }
            else
            {
                GameObject ersatz = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(ersatz.GetComponent<Collider>());
                ersatz.transform.SetParent(pivotV, false);
                ersatz.transform.localPosition = new Vector3(0f, 0f, 0.4f);
                ersatz.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ersatz.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
                ersatz.GetComponent<MeshRenderer>().sharedMaterial = SteinMaterial();
                ersatz.name = "Kanonen_Modell";
            }

            // Mündung: hier entsteht die Kugel, +Z = Schussrichtung
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(pivotV, false);
            muzzle.localPosition = new Vector3(0f, 0.37f, 0.75f);

            // Kameraposition beim Zielen: schräg hinter/über dem Rohr
            var camMount = new GameObject("CameraMount").transform;
            camMount.SetParent(pivotV, false);
            camMount.localPosition = new Vector3(0f, 1.4f, -2.6f);

            var station = root.AddComponent<CannonStation>();
            station.pivotHorizontal  = pivotH;
            station.pivotVertical    = pivotV;
            station.muzzle           = muzzle;
            station.cameraMount      = camMount;
            station.cannonballPrefab = kugelPrefab;

            if (maxDrehWinkel > 0.01f)
            {
                station.minYaw = -maxDrehWinkel;
                station.maxYaw = maxDrehWinkel;
            }
        }

        static Material steinMaterialCache;
        static Material SteinMaterial()
        {
            if (steinMaterialCache == null)
            {
                steinMaterialCache = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Kanonen_Stein" };
                steinMaterialCache.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.38f));
                steinMaterialCache.SetFloat("_Smoothness", 0.15f);
            }
            return steinMaterialCache;
        }

        // ------------------------------------------------------------------
        // Despawn-System: Tiere weiter als 50 m vom Spieler werden komplett
        // abgeschaltet (kein Rendern, keine Bewegung) und wachen wieder auf,
        // sobald der Spieler zurückkommt. Prüfung nur alle 2 Sekunden.
        // ------------------------------------------------------------------
        System.Collections.IEnumerator TiereSchlafenLassen()
        {
            const float schlafDistanz = 50f;

            // Warten, bis die Tiere gespawnt sind
            yield return new WaitForSeconds(1f);

            GameObject tiereEltern = GameObject.Find("Mittelalter_Tiere");
            GameObject spieler = GameObject.FindGameObjectWithTag("Player");
            if (tiereEltern == null || spieler == null) yield break;

            Transform eltern = tiereEltern.transform;
            var warte = new WaitForSeconds(2f);

            while (true)
            {
                Vector3 spielerPos = spieler.transform.position;
                foreach (Transform tier in eltern)
                {
                    bool nah = Vector3.Distance(tier.position, spielerPos) < schlafDistanz;
                    if (tier.gameObject.activeSelf != nah)
                        tier.gameObject.SetActive(nah);
                }
                yield return warte;
            }
        }

        // (Die frühere Rosa-Reparatur wohnt jetzt in ShaderRosaFix.cs und
        // startet sich in jeder Szene selbst.)

        // ------------------------------------------------------------------
        // Prefabs aus Assets/Resources/KI/ laden
        // ------------------------------------------------------------------
        GameObject[] Lade(params string[] namen)
        {
            var liste = new System.Collections.Generic.List<GameObject>();
            foreach (string name in namen)
            {
                GameObject prefab = LadeEinzeln(name);
                if (prefab != null) liste.Add(prefab);
            }
            return liste.ToArray();
        }

        GameObject LadeEinzeln(string name)
        {
            GameObject prefab = Resources.Load<GameObject>("KI/" + name);
            if (prefab == null)
                Debug.LogError("KI_SzeneAufbau: Prefab 'Resources/KI/" + name + "' fehlt!");
            return prefab;
        }
    }
}
