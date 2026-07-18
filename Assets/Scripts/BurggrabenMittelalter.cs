using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;   // Suimono bringt eine eigene Random-Klasse mit

namespace NeonCatch
{
    // Bevölkert die Burg-Umgebung beim Start automatisch mit Tieren:
    // Pferde, Rehe, Tiger und Hühner auf der Wiese, Hunde und Katzen im
    // Burghof und Vögel am Himmel.
    //
    // An ein leeres GameObject in der Nähe der Burg hängen. Liegt ein
    // BurggrabenKomplett in der Szene, wird dessen Position als Zentrum benutzt.
    //
    // Pinguine passen weiterhin nicht ins Setting und werden aussortiert.
    public class BurggrabenMittelalter : MonoBehaviour
    {
        [Header("Pferde (Animals FREE > Prefabs > Horse)")]
        public GameObject[] pferdPrefabs;
        public int pferdAnzahl = 4;

        [Header("Rehe (Animals FREE > Prefabs > Deer)")]
        public GameObject[] rehPrefabs;
        public int rehAnzahl = 4;

        [Header("Tiger (Animals FREE > Prefabs > Tiger)")]
        public GameObject[] tigerPrefabs;
        public int tigerAnzahl = 2;

        [Header("Hühner (Animals FREE > Prefabs > Chicken)")]
        public GameObject[] huhnPrefabs;
        public int huhnAnzahl = 6;

        [Header("Hunde (Animals FREE > Prefabs > Dog) – laufen im Burghof")]
        public GameObject[] hundPrefabs;
        public int hundAnzahl = 6;

        [Header("Katzen (Animals FREE > Prefabs > Kitty) – laufen im Burghof")]
        public GameObject[] katzePrefabs;
        public int katzeAnzahl = 4;

        [Header("Vögel (Living Birds)")]
        public GameObject[] vogelPrefabs;
        public int vogelAnzahl = 16;

        [Header("Gebiet (automatisch vom Burggraben übernommen, falls vorhanden)")]
        public float wiesenRadiusInnen = 20f;  // ab hier beginnt die Wiese (außerhalb des Grabens)
        public float wiesenRadiusAussen = 40f;
        // Wurde wiesenRadiusAussen schon von außen gesetzt (z.B. anhand der
        // echten Terrain-Größe durch KI_SzeneAufbau), soll Start() das NICHT
        // mit einer geschätzten Zahl überschreiben.
        [HideInInspector] public bool wiesenRadiusAussenManuellGesetzt;
        public float tierMindestAbstand = 4f;  // Mindestabstand zwischen Bodentieren, gegen Klumpen

        [Header("Sperrzonen (z.B. Bauernhaus-Gärten – dort spawnt nichts)")]
        public Transform[] sperrZonen;
        public float sperrZonenRadius = 8f;

        [Header("Zufall (gleicher Seed = gleiche Verteilung bei jedem Start)")]
        public int zufallsSeed = 54321;

        // Diese Tiere passen nicht ins Mittelalter-Setting und werden aussortiert
        // (Hunde und Katzen sind seit dem Burghof-Update ausdrücklich erlaubt)
        static readonly string[] verboteneNamen = { "penguin", "pinguin" };

        Vector3 zentrum;
        Vector3 burgZentrum;   // Mitte des Burghofs (für Hunde und Katzen)
        float   burgRadius;    // wie weit sie sich vom Burg-Zentrum entfernen dürfen
        float   burgBodenY;    // Höhe des Burghof-Bodens (gegen Spawn auf Mauern/Dächern)
        Transform elternObjekt;
        static BurggrabenMittelalter instanz;
        readonly List<Vector3> platzierteTierPositionen = new List<Vector3>();

        // Map-Grenzen: außerhalb spawnt und wandert nichts. Werden z.B. vom
        // KI_SzeneAufbau auf die echte Map-Größe gesetzt.
        public static Vector2 mapMin = new Vector2(float.MinValue, float.MinValue);
        public static Vector2 mapMax = new Vector2(float.MaxValue, float.MaxValue);
        public static float randAbstand = 0f;              // Sicherheitsabstand zum Map-Rand
        public static float maxBodenHoehe = float.MaxValue; // höhere Stellen (Hügel, Mauern) sind tabu

        // Terrain-Rand und Steilhang: am Rand des echten Terrains beginnt laut
        // Nutzer ein Hügel – Sicherheitsabstand zum Terrain-Rand, und Stellen
        // mit mehr als maxHangSteigung Grad Neigung sind ebenfalls tabu (Tiere
        // und Falltüren sollen nicht am Steilhang stehen). 5 m statt der alten
        // 2 m: der steile Rand-Hügel beginnt laut LandschaftBuilder schon rund
        // 1,4 Kacheln vor dem Flächenrand anzusteigen – 2 m deckte oft nur den
        // allerletzten Rand ab, nicht den Anfang der Steigung selbst.
        public static float terrainRandAbstand = 5f;
        // War kurzzeitig auf 10° verschärft ("nicht bergauf/bergab") – auf
        // echtem, leicht unebenem Terrain (Perlin-Rauschen) erfüllte dann
        // kaum noch eine Stelle die Bedingung, wodurch die Spawn-Suche bei
        // allen Versuchen scheiterte und GAR KEINE Tiere mehr spawnten.
        // 16° filtert weiterhin echte Steilhänge/Hügelränder, lässt aber
        // die normale, leicht wellige Wiese wieder zu.
        public static float maxHangSteigung = 16f;

        void Awake()
        {
            instanz = this;
        }

        // Zusätzliche Sperrzonen, die andere Systeme zur Laufzeit anmelden
        // (z.B. die Teleport-Hütten: dort sollen keine Tiere hineinlaufen)
        struct ZusatzZone { public Vector3 position; public float radius; }
        static readonly List<ZusatzZone> zusatzZonen = new List<ZusatzZone>();

        public static void SperrzoneHinzufuegen(Vector3 position, float radius)
        {
            zusatzZonen.Add(new ZusatzZone { position = position, radius = radius });
        }

        // Beim Play-Start leeren – wichtig, falls Domain-Reload im Editor
        // deaktiviert ist (sonst stapeln sich die Zonen über mehrere Runden)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ZusatzZonenZuruecksetzen()
        {
            zusatzZonen.Clear();
        }

        // Prüft, ob eine Position tabu ist: auf einem Weg (BurgWege) oder in
        // einer Sperrzone wie einem Bauernhaus-Garten. Auch AnimalLagFix nutzt das.
        //
        // aufTerrain=false: für Figuren, die NICHT auf dem rohen Terrain laufen,
        // sondern auf einem Boden-Mesh darüber (z.B. Hunde/Katzen auf dem
        // Burghof-Boden – die Burg steht oft auf einem absichtlich hügeligen
        // Terrain-Untergrund). Ohne diesen Schalter sperrte die Terrain-
        // Neigungsprüfung unten fälschlich den GESAMTEN Burghof, weil die
        // Heightmap unter dem flachen Hof-Boden trotzdem steil sein kann –
        // genau das ließ Hunde/Katzen dort nie loslaufen ("gehen nicht").
        public static bool IstGesperrt(Vector3 position, bool aufTerrain = true)
        {
            foreach (ZusatzZone zone in zusatzZonen)
            {
                Vector3 d = zone.position - position;
                d.y = 0f;
                if (d.sqrMagnitude < zone.radius * zone.radius) return true;
            }

            // Außerhalb der Map und am Rand (Hügel!) ist alles tabu
            if (position.x < mapMin.x + randAbstand || position.x > mapMax.x - randAbstand ||
                position.z < mapMin.y + randAbstand || position.z > mapMax.y - randAbstand) return true;

            if (maxBodenHoehe < float.MaxValue)
            {
                // Gibt es hier überhaupt Boden? Wo kein Collider unter der
                // Stelle liegt, ist das Ende der Spielwelt – dort spawnt nichts.
                if (!Physics.Raycast(position + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                        100f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    return true;

                // Zu hoch gelegene Stellen (Hügel am Rand, Mauern, Dächer) sind tabu
                if (hit.point.y > maxBodenHoehe) return true;
            }

            if (BurgWege.IstAufWeg(position)) return true;

            if (aufTerrain && IstAmTerrainRandOderZuSteil(position)) return true;

            if (instanz != null && instanz.sperrZonen != null)
            {
                foreach (Transform zone in instanz.sperrZonen)
                {
                    if (zone == null) continue;
                    Vector3 abstand = zone.position - position;
                    abstand.y = 0f;
                    if (abstand.magnitude < instanz.sperrZonenRadius) return true;
                }
            }
            return false;
        }

        static Terrain terrainCache;
        static bool terrainGesucht;

        // Für andere Scripts (z.B. AnimalLagFix, für flüssiges Boden-Folgen
        // ohne teuren Raycast jeden Frame – Terrain.SampleHeight ist ein
        // billiger Heightmap-Lookup)
        public static Terrain AktivesTerrain
        {
            get
            {
                if (!terrainGesucht)
                {
                    terrainGesucht = true;
                    // Gezielt unter "Landschaft" suchen (wie die Editor-Tools
                    // selbst das tun) – robuster als zu raten, falls mehrere
                    // Terrains in der Szene liegen
                    GameObject landschaft = GameObject.Find("Landschaft");
                    terrainCache = landschaft != null ? landschaft.GetComponentInChildren<Terrain>() : null;
                    if (terrainCache == null)
                        terrainCache = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindFirstObjectByType<Terrain>();
                }
                return terrainCache;
            }
        }

        // Prüft anhand der ECHTEN Terrain-Neigungsdaten, ob eine Stelle zu nah
        // am Terrain-Rand liegt (dort beginnt laut Nutzer ein Hügel) oder zu
        // steil ist. Gibt es kein Terrain in der Szene, greift diese Prüfung
        // nicht (dann zählen nur die anderen Regeln).
        static bool IstAmTerrainRandOderZuSteil(Vector3 position)
        {
            Terrain terrain = AktivesTerrain;
            if (terrain == null) return false;

            Vector3 lokal = position - terrain.transform.position;
            Vector3 groesse = terrain.terrainData.size;

            // Außerhalb des Terrains liegt diese Prüfung nicht in ihrer
            // Zuständigkeit (das regeln Map-Grenzen/Boden-Existenz-Check oben)
            if (lokal.x < 0f || lokal.x > groesse.x || lokal.z < 0f || lokal.z > groesse.z)
                return false;

            // Zu nah am Terrain-Rand (dort steigt der Hügel an)
            if (lokal.x < terrainRandAbstand || lokal.x > groesse.x - terrainRandAbstand ||
                lokal.z < terrainRandAbstand || lokal.z > groesse.z - terrainRandAbstand)
                return true;

            // Zu steil (echte Hangneigung aus den Terrain-Daten, in Grad)
            float u = lokal.x / groesse.x;
            float v = lokal.z / groesse.z;
            return terrain.terrainData.GetSteepness(u, v) > maxHangSteigung;
        }

        void Start()
        {
            if (zufallsSeed != 0) Random.InitState(zufallsSeed);
            platzierteTierPositionen.Clear();

            // Fehlende Prefab-Listen aus Resources/KI nachladen: die in der
            // Szene gespeicherte Komponente kann älter sein als neu dazu-
            // gekommene Tierarten (Hühner, Hunde, Katzen) – ihre Listen wären
            // dann leer, obwohl die Prefabs längst im Projekt liegen
            pferdPrefabs = MitFallback(pferdPrefabs, "Pferd");
            rehPrefabs   = MitFallback(rehPrefabs,   "Reh");
            tigerPrefabs = MitFallback(tigerPrefabs, "Tiger");
            huhnPrefabs  = MitFallback(huhnPrefabs,  "Huhn");
            hundPrefabs  = MitFallback(hundPrefabs,  "Hund");
            katzePrefabs = MitFallback(katzePrefabs, "Katze");

            // Keine Sperrzonen eingetragen? Dann die echten Bauernhäuser
            // (Gruppe "Bauernhöfe") automatisch übernehmen – sonst spawnen
            // und fliegen Tiere mitten durch die offenen Häuser
            if (sperrZonen == null || sperrZonen.Length == 0)
            {
                GameObject hoefeGruppe = GameObject.Find("Bauernhöfe");
                if (hoefeGruppe != null)
                {
                    var zonen = new List<Transform>();
                    foreach (Transform hof in hoefeGruppe.transform)
                        zonen.Add(hof);
                    if (zonen.Count > 0)
                    {
                        sperrZonen = zonen.ToArray();
                        sperrZonenRadius = 6f;   // deckt Hütte + Garten + Zaun ab
                    }
                }
            }

            // Zentrum und Radien vom Burggraben übernehmen, falls einer existiert
            zentrum = transform.position;
            BurggrabenKomplett graben = FindFirstObjectByType<BurggrabenKomplett>();
            if (graben != null)
            {
                zentrum = graben.transform.position;
                float aussen = graben.innenRadius + graben.grabenBreite;
                wiesenRadiusInnen = aussen + 3f;
                if (!wiesenRadiusAussenManuellGesetzt) wiesenRadiusAussen = aussen + 25f;
            }

            // Sicherheitsnetz: ist das ECHTE Terrain kleiner als der aus der
            // (angenommenen) Grabengeometrie berechnete innere Radius, würde
            // Random.Range(innen, aussen) mit innen > aussen einen leeren/
            // umgekehrten Bereich ergeben – dann spawnt (und wandert) gar
            // nichts mehr. Mindestens 10 m nutzbare Breite garantieren.
            if (wiesenRadiusInnen > wiesenRadiusAussen - 10f)
                wiesenRadiusInnen = Mathf.Max(2f, wiesenRadiusAussen - 10f);

            elternObjekt = new GameObject("Mittelalter_Tiere").transform;
            elternObjekt.position = zentrum;

            // zielHoehe: Tiere werden auf Weltmaß geschrumpft (Spieler ist
            // 0.5 m groß – die Pack-Modelle sind in Lebensgröße riesig).
            // Auf Wunsch doppelt so groß wie vorher (außer Vögel und Fische).
            SpawneWiesenTiere(pferdPrefabs, pferdAnzahl, "Pferd", tempo: 1.0f, fluchtRadius: 5f, zielHoehe: 1.10f);
            SpawneWiesenTiere(rehPrefabs,   rehAnzahl,   "Reh",   tempo: 1.4f, fluchtRadius: 6f, zielHoehe: 0.90f);
            SpawneWiesenTiere(tigerPrefabs, tigerAnzahl, "Tiger", tempo: 1.8f, fluchtRadius: 0f, zielHoehe: 0.80f);
            SpawneWiesenTiere(huhnPrefabs,  huhnAnzahl,  "Huhn",  tempo: 0.8f, fluchtRadius: 3f, zielHoehe: 0.30f);

            // Hunde und Katzen leben im Burghof, nicht auf der Wiese
            SucheBurgGeometrie();
            SpawneBurgTiere(hundPrefabs,  hundAnzahl,  "Hund",  tempo: 1.2f, fluchtRadius: 0f,   zielHoehe: 0.44f);
            SpawneBurgTiere(katzePrefabs, katzeAnzahl, "Katze", tempo: 1.0f, fluchtRadius: 2.5f, zielHoehe: 0.32f);

            SpawneVoegel();
        }

        // ------------------------------------------------------------------
        // Pferde, Rehe und Tiger wandern auf der Wiese um die Burg
        // ------------------------------------------------------------------
        void SpawneWiesenTiere(GameObject[] prefabs, int anzahl, string tierName, float tempo, float fluchtRadius, float zielHoehe)
        {
            GameObject[] erlaubt = NurMittelalterTiere(prefabs, tierName);
            if (erlaubt.Length == 0) return;

            int platziert = 0;
            for (int i = 0; i < anzahl; i++)
            {
                // Nur echte Grass-Fläche, mit Mindestabstand zu bereits
                // platzierten Tieren (gegen Klumpen-Bildung)
                Vector3? platzOpt = SuchePlatzAufWiese(mitMindestabstand: true);
                if (!platzOpt.HasValue) continue;   // kein gültiger Platz: Tier weglassen
                Vector3 pos = platzOpt.Value;
                platzierteTierPositionen.Add(pos);

                GameObject tier = Instantiate(erlaubt[Random.Range(0, erlaubt.Length)], pos,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), elternObjekt);
                tier.name = tierName + "_" + (i + 1);
                SkaliereAufHoehe(tier, zielHoehe);

                // Mitgelieferte Scripte der Tier-Prefabs abschalten, damit sie nicht
                // gegen unser Wandern steuern – der Animator bleibt aktiv
                foreach (MonoBehaviour mb in tier.GetComponentsInChildren<MonoBehaviour>())
                    mb.enabled = false;

                // Lag-freies Verhalten: Idle/Walk per Coroutine statt Update
                var verhalten = tier.AddComponent<AnimalLagFix>();
                verhalten.walkSpeed = tempo;
                // Wichtig: wurde früher vergessen – deshalb floh z.B. das Reh
                // NICHT vor dem Spieler (der Parameter kam nie am Tier an)
                verhalten.fluchtRadius = fluchtRadius;
                platziert++;
            }

            // Diagnose: fällt die Trefferquote stark ab, ist meist die Wiese
            // durch Sperrzonen/Steilhang-Grenze zu knapp geworden – lieber
            // sofort in der Console sehen als "Tiere sind verschwunden" raten
            if (platziert < anzahl)
                Debug.LogWarning($"BurggrabenMittelalter: nur {platziert}/{anzahl} '{tierName}' platziert " +
                                  "(kein gültiger Wiesen-Platz gefunden – Terrain zu klein/steil oder zu viele Sperrzonen?).");
        }

        // ------------------------------------------------------------------
        // Hunde und Katzen wandern im Burghof (innerhalb der Burgmauern)
        // ------------------------------------------------------------------

        // Misst den echten Burghof aus: Zentrum und Radius aus dem Mauerring
        // des "Burg"-Objekts. Ohne Burg in der Szene dient das Graben-Zentrum
        // mit kleinem Radius als Ersatz.
        void SucheBurgGeometrie()
        {
            GameObject burgGo = GameObject.Find("Burg");
            if (burgGo != null)
            {
                Transform mauern = burgGo.transform.Find("Mauerweg");
                if (mauern == null) mauern = burgGo.transform.Find("Außenmauern");
                Bounds b = BoundsVonRenderern(mauern != null ? mauern : burgGo.transform);

                burgZentrum = new Vector3(burgGo.transform.position.x, 0f, burgGo.transform.position.z);
                // 0.7: sicher innerhalb der Mauern bleiben, nicht in/auf ihnen
                burgRadius  = Mathf.Max(3f, Mathf.Min(b.extents.x, b.extents.z) * 0.7f);
            }
            else
            {
                burgZentrum = zentrum;
                burgRadius  = 8f;
            }

            // Hof-Höhe MESSEN statt vom Objekt-Ursprung abzulesen: steht die
            // Burg auf erhöhtem Terrain, läge der Ursprung (oft y=0) deutlich
            // unter dem echten Hof-Boden – der Höhenfilter hätte dann JEDEN
            // Platz für "Mauer/Dach" gehalten und alles verworfen (genau das
            // ließ Hunde und Katzen komplett verschwinden).
            burgBodenY = BurghofBodenHoehe(burgZentrum, burgRadius, zentrum.y);
        }

        // Tiefsten Boden im Burghof finden: Mitte + 4 Punkte auf halbem
        // Radius, der niedrigste Treffer ist der Hof-Boden (die Mitte allein
        // könnte das Dach eines Turms in der Burgmitte sein)
        public static float BurghofBodenHoehe(Vector3 burgZentrum, float radius, float ersatzY)
        {
            float halb = radius * 0.5f;
            Vector3[] messpunkte =
            {
                Vector3.zero,
                new Vector3(halb, 0f, 0f), new Vector3(-halb, 0f, 0f),
                new Vector3(0f, 0f, halb), new Vector3(0f, 0f, -halb),
            };

            float tiefster = float.MaxValue;
            foreach (Vector3 versatz in messpunkte)
            {
                Vector3 start = new Vector3(burgZentrum.x + versatz.x, ersatzY + 30f,
                                            burgZentrum.z + versatz.z);
                if (Physics.Raycast(start, Vector3.down, out RaycastHit hit,
                        100f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    tiefster = Mathf.Min(tiefster, hit.point.y);
            }
            return tiefster < float.MaxValue ? tiefster : ersatzY;
        }

        static Bounds BoundsVonRenderern(Transform t)
        {
            Renderer[] rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            foreach (Renderer r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        void SpawneBurgTiere(GameObject[] prefabs, int anzahl, string tierName, float tempo, float fluchtRadius, float zielHoehe)
        {
            GameObject[] erlaubt = NurMittelalterTiere(prefabs, tierName);
            if (erlaubt.Length == 0) return;

            int platziert = 0;
            for (int i = 0; i < anzahl; i++)
            {
                Vector3? platzOpt = SuchePlatzImBurghof();
                if (!platzOpt.HasValue) continue;
                Vector3 pos = platzOpt.Value;
                platzierteTierPositionen.Add(pos);

                GameObject tier = Instantiate(erlaubt[Random.Range(0, erlaubt.Length)], pos,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), elternObjekt);
                tier.name = tierName + "_" + (i + 1);
                SkaliereAufHoehe(tier, zielHoehe);

                foreach (MonoBehaviour mb in tier.GetComponentsInChildren<MonoBehaviour>())
                    mb.enabled = false;

                var verhalten = tier.AddComponent<AnimalLagFix>();
                verhalten.walkSpeed      = tempo;
                verhalten.fluchtRadius   = fluchtRadius;
                // Im Hof bleiben: kurze Wege, Heimat-Leine so lang wie der Hof
                verhalten.maxHeimAbstand = burgRadius;
                verhalten.maxStrecke     = Mathf.Max(4f, burgRadius * 0.8f);
                // Der Burghof-Boden ist ein Mesh ÜBER dem Terrain – die Boden-
                // höhe muss per Raycast kommen, nicht aus der Terrain-Heightmap
                // (sonst sinken die Tiere auf die Terrain-Höhe unter dem Boden)
                verhalten.bodenPerRaycast = true;
                platziert++;
            }

            if (platziert < anzahl)
                Debug.LogWarning($"BurggrabenMittelalter: nur {platziert}/{anzahl} '{tierName}' im Burghof platziert " +
                                  "(kein freier Platz gefunden – Burghof zu klein oder zugebaut?).");
        }

        // Freier Platz auf dem Burghof-Boden: per Raycast gefunden, nur auf
        // Hof-Höhe (nicht auf Mauern, Türmen oder Dächern), mit kleinem
        // Mindestabstand zu bereits platzierten Tieren
        Vector3? SuchePlatzImBurghof()
        {
            LayerMask alleAusserWasser = ~(1 << 4);

            for (int versuch = 0; versuch < 60; versuch++)
            {
                Vector2 kreis = Random.insideUnitCircle * burgRadius;
                Vector3 testXZ = new Vector3(burgZentrum.x + kreis.x, 0f, burgZentrum.z + kreis.y);

                Vector3 start = new Vector3(testXZ.x, burgBodenY + 20f, testXZ.z);
                if (!Physics.Raycast(start, Vector3.down, out RaycastHit hit,
                        40f, alleAusserWasser, QueryTriggerInteraction.Ignore))
                    continue;

                // Nur der Hof-Boden zählt: alles deutlich über Hof-Höhe ist
                // Mauer, Turm, Treppe oder Dach – dort spawnt nichts
                if (hit.point.y > burgBodenY + 1.2f) continue;

                Vector3 pos = hit.point;

                bool zuNah = false;
                foreach (Vector3 andere in platzierteTierPositionen)
                {
                    if (Vector3.Distance(andere, pos) < 2.5f) { zuNah = true; break; }
                }
                if (zuNah) continue;

                return pos;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Vögel ziehen Kreise am Himmel über der Burg
        // ------------------------------------------------------------------
        void SpawneVoegel()
        {
            GameObject[] erlaubt = NurMittelalterTiere(vogelPrefabs, "Vogel");
            if (erlaubt.Length == 0) return;

            for (int i = 0; i < vogelAnzahl; i++)
            {
                GameObject vogel = Instantiate(erlaubt[Random.Range(0, erlaubt.Length)], elternObjekt);
                vogel.name = "Vogel_" + (i + 1);

                // Eigene KI-Skripte der Vogel-Prefabs (Living Birds) LÖSCHEN,
                // nicht nur abschalten: das Pack ruft per SendMessage (z.B.
                // lb_CrowProximity → "CrowIsClose") Methoden auch auf
                // deaktivierten Scripts auf – die krachen dann mit
                // NullReferenceException, weil ihr Start() nie lief.
                // Collider/Rigidbody ebenfalls weg (Trigger-Quelle).
                foreach (MonoBehaviour mb in vogel.GetComponentsInChildren<MonoBehaviour>())
                    Destroy(mb);
                foreach (Collider c in vogel.GetComponentsInChildren<Collider>())
                    Destroy(c);
                foreach (Rigidbody rb in vogel.GetComponentsInChildren<Rigidbody>())
                    Destroy(rb);

                // Streifgebiet in die Map-Mitte legen und so klein halten, dass
                // es komplett innerhalb der Map-Grenzen bleibt
                Vector3 flugZentrum = zentrum;
                float gebietRadius = wiesenRadiusAussen * 0.7f;
                if (mapMax.x < float.MaxValue)
                {
                    flugZentrum = new Vector3((mapMin.x + mapMax.x) * 0.5f, zentrum.y,
                                              (mapMin.y + mapMax.y) * 0.5f);
                    gebietRadius = Mathf.Min(mapMax.x - mapMin.x, mapMax.y - mapMin.y) * 0.5f
                                    - randAbstand - 2f;
                }

                var flug = vogel.AddComponent<VogelFlug>();
                flug.zentrum      = flugZentrum;
                flug.gebietRadius = Mathf.Max(10f, gebietRadius);
                flug.flugTempo    = Random.Range(3.5f, 5f);

                // Verteilt starten – NICHT alle am selben Punkt: jeder Vogel
                // beginnt irgendwo im Gebiet, in zufälliger Flughöhe
                Vector2 startKreis = Random.insideUnitCircle * flug.gebietRadius * 0.9f;
                Vector3 startXZ = flugZentrum + new Vector3(startKreis.x, 0f, startKreis.y);
                float startBoden = BodenHoehe(startXZ);
                vogel.transform.position = new Vector3(startXZ.x,
                    startBoden + Random.Range(flug.flugHoeheMin, flug.flugHoeheMax), startXZ.z);
            }
        }

        // ------------------------------------------------------------------
        // Hilfsfunktionen
        // ------------------------------------------------------------------

        // Leere Prefab-Liste? Dann das passende Prefab aus Resources/KI laden
        static GameObject[] MitFallback(GameObject[] vorhandene, string resourceName)
        {
            if (vorhandene != null && vorhandene.Length > 0) return vorhandene;
            GameObject prefab = Resources.Load<GameObject>("KI/" + resourceName);
            return prefab != null ? new[] { prefab } : new GameObject[0];
        }

        // Filtert unpassende Tiere (Pinguine) heraus
        // Tier-Prefabs sind in Lebensgröße importiert (der Spieler ist nur
        // 0.5 m groß) – auf die gewünschte Weltmaß-Höhe schrumpfen
        static void SkaliereAufHoehe(GameObject go, float zielHoehe)
        {
            float hoehe = 0f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                hoehe = Mathf.Max(hoehe, r.bounds.size.y);
            if (hoehe > 0.01f)
                go.transform.localScale *= zielHoehe / hoehe;
        }

        GameObject[] NurMittelalterTiere(GameObject[] prefabs, string tierName)
        {
            var liste = new System.Collections.Generic.List<GameObject>();
            if (prefabs != null)
            {
                foreach (GameObject prefab in prefabs)
                {
                    if (prefab == null) continue;
                    string name = prefab.name.ToLowerInvariant();
                    bool erlaubt = true;
                    foreach (string tabu in verboteneNamen)
                    {
                        if (name.Contains(tabu))
                        {
                            Debug.LogWarning("BurggrabenMittelalter: '" + prefab.name + "' ist kein Mittelalter-Tier und wird ignoriert.");
                            erlaubt = false;
                            break;
                        }
                    }
                    if (erlaubt) liste.Add(prefab);
                }
            }
            if (liste.Count == 0)
                Debug.LogWarning("BurggrabenMittelalter: Keine gültigen Prefabs für '" + tierName + "' zugewiesen.");
            return liste.ToArray();
        }

        // Sucht einen Platz NUR auf dem echten Terrain (die Wiese) – geprüft
        // direkt am getroffenen Collider-TYP (TerrainCollider), nicht am
        // Layer. Ein Layer-Name allein wäre nicht robust genug: wurde der
        // Layer "Grass" gerade erst per Datei geschrieben, hat Unity ihn im
        // laufenden Editor evtl. noch nicht übernommen, und die Suche würde
        // sonst auf irgendeinem anderen Objekt (z.B. dem Burgboden) landen –
        // genau das sah wie "alle Tiere am selben Ort, schweben, teleportieren"
        // aus, weil sie eigentlich auf dem Burgboden statt der Wiese standen.
        // mitMindestabstand: hält zusätzlich tierMindestAbstand zu bereits
        // platzierten Tieren ein (gegen Klumpen-Bildung).
        Vector3? SuchePlatzAufWiese(bool mitMindestabstand)
        {
            LayerMask alleAusserWasser = ~(1 << 4);

            for (int versuch = 0; versuch < 60; versuch++)
            {
                float winkel = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(wiesenRadiusInnen, wiesenRadiusAussen);
                Vector3 testXZ = zentrum + new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)) * radius;

                if (!Physics.Raycast(testXZ + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                        100f, alleAusserWasser, QueryTriggerInteraction.Ignore))
                    continue;   // kein Boden hier – nächster Versuch

                if (!(hit.collider is TerrainCollider)) continue;   // nicht die Wiese – nächster Versuch

                Vector3 pos = hit.point;
                if (IstGesperrt(pos)) continue;

                if (mitMindestabstand)
                {
                    bool zuNah = false;
                    foreach (Vector3 andere in platzierteTierPositionen)
                    {
                        if (Vector3.Distance(andere, pos) < tierMindestAbstand) { zuNah = true; break; }
                    }
                    if (zuNah) continue;
                }

                return pos;
            }
            return null;
        }

        // Bodenhöhe per Raycast, Wasser-Layer (4) wird ignoriert
        public static float BodenHoehe(Vector3 position)
        {
            Vector3 start = position + Vector3.up * 30f;
            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, 100f,
                    ~(1 << 4), QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return position.y;
        }
    }

    // ======================================================================
    // Vogel: fliegt tief in wechselnden Bahnen, landet zwischendurch und
    // sitzt (~80 % Luft, ~20 % Boden), weicht Bäumen/Häusern/Mauern aus und
    // fliegt sofort auf, wenn der Spieler zu nahe kommt. Nutzt die echten
    // Living-Birds-Animator-Parameter (flying/perched/landing, alles Bools).
    // Coroutine-basiert wie AnimalLagFix: im Sitzen kostet der Vogel
    // praktisch keine CPU, Hindernis-Checks passieren nur beim Anflug eines
    // neuen Wegpunkts, nie jeden Frame.
    // ======================================================================
    public class VogelFlug : MonoBehaviour
    {
        public Vector3 zentrum;
        public float gebietRadius = 20f;     // wie weit der Vogel umherstreift
        public float flugHoeheMin = 2f;      // "tiefer fliegen" statt der alten 8-16 m
        public float flugHoeheMax = 5f;
        public float flugTempo = 4f;
        public float fluchtRadius = 6f;      // Spieler/Bot näher als das: sofort auffliegen
        public float fluchtTempoFaktor = 1.8f;

        Animator animator;
        bool hatFlying, hatPerched, hatLanding;
        Transform player;
        float bodenVersatz;   // Ankerpunkt-Korrektur, wie bei AnimalLagFix

        void OnEnable()
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            StartCoroutine(FlugSitzZyklus());
        }

        IEnumerator FlugSitzZyklus()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.type != AnimatorControllerParameterType.Bool) continue;
                    if (p.name == "flying") hatFlying = true;
                    else if (p.name == "perched") hatPerched = true;
                    else if (p.name == "landing") hatLanding = true;
                }
            }

            // Fuß-Versatz einmalig messen (wie bei AnimalLagFix) – manche
            // Modelle haben den Ankerpunkt nicht an den Füßen
            yield return null;
            float tiefsterPunkt = float.MaxValue;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                tiefsterPunkt = Mathf.Min(tiefsterPunkt, r.bounds.min.y);
            if (tiefsterPunkt < float.MaxValue)
                bodenVersatz = Mathf.Clamp(transform.position.y - tiefsterPunkt, -1f, 1f);

            bool geflohen = false;
            while (true)
            {
                // ---- FLIEGEN: im Schnitt ~80 % der Zeit (8-16s von 10-20s Zyklus).
                // Direkt nach einer Flucht schneller unterwegs, danach normal. ----
                yield return Fliege(Random.Range(8f, 16f), geflohen ? fluchtTempoFaktor : 1f);
                // ---- SITZEN: im Schnitt ~20 % der Zeit, bricht bei Spieler-Nähe sofort ab ----
                geflohen = false;
                yield return Sitze(Random.Range(2f, 4f), () => geflohen = true);
            }
        }

        IEnumerator Fliege(float dauer, float tempoFaktor)
        {
            SetzeAnimation(fliegt: true, sitzt: false);
            float tempo = flugTempo * tempoFaktor;

            float rest = dauer;
            while (rest > 0f)
            {
                if (!WaehleFlugziel(out Vector3 ziel)) { yield return null; continue; }

                while (Vector3.Distance(transform.position, ziel) > 0.5f && rest > 0f)
                {
                    Vector3 richtung = (ziel - transform.position).normalized;
                    transform.position += richtung * tempo * Time.deltaTime;
                    if (richtung.sqrMagnitude > 0.0001f)
                        transform.rotation = Quaternion.Slerp(transform.rotation,
                            Quaternion.LookRotation(richtung), Time.deltaTime * 3f);

                    rest -= Time.deltaTime;
                    yield return null;
                }
            }
        }

        IEnumerator Sitze(float dauer, System.Action beiFlucht)
        {
            // Landeplatz suchen: echter Boden, kein Weg/Haus/Steilhang
            Vector3? landeplatz = SucheLandeplatz();
            if (!landeplatz.HasValue) yield break;   // kein Platz: gleich weiterfliegen

            SetzeAnimation(fliegt: false, sitzt: false);
            if (hatLanding) animator.SetBool("landing", true);

            Vector3 start = transform.position;
            float sinkzeit = 0.8f;
            for (float t = 0f; t < sinkzeit; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, landeplatz.Value, t / sinkzeit);
                yield return null;
            }
            transform.position = landeplatz.Value;

            if (hatLanding) animator.SetBool("landing", false);
            SetzeAnimation(fliegt: false, sitzt: true);

            var kurzWarten = new WaitForSeconds(0.3f);
            float rest = dauer;
            while (rest > 0f)
            {
                if (PlayerZuNah(fluchtRadius)) { beiFlucht(); yield break; }   // sofort auffliegen
                yield return kurzWarten;
                rest -= 0.3f;
            }
        }

        // Wegpunkt innerhalb des Gebiets, in Flughöhe über dem echten Boden,
        // mit Hindernis-Check (SphereCast) auf dem direkten Weg dorthin
        bool WaehleFlugziel(out Vector3 ziel)
        {
            // Mehr Versuche als bei den Boden-Tieren: das Gebiet ist groß
            // (ganze Landschaft), und mit wenigen Versuchen blieben Vögel
            // statistisch eher in der Nähe der Burg hängen, weil dort öfter
            // schnell ein gültiger Punkt gefunden wurde
            for (int versuch = 0; versuch < 16; versuch++)
            {
                Vector2 kreis = Random.insideUnitCircle * gebietRadius;
                Vector3 kandidatXZ = zentrum + new Vector3(kreis.x, 0f, kreis.y);
                if (BurggrabenMittelalter.IstGesperrt(kandidatXZ)) continue;

                float bodenY = BurggrabenMittelalter.BodenHoehe(kandidatXZ);
                Vector3 kandidat = new Vector3(kandidatXZ.x,
                    bodenY + Random.Range(flugHoeheMin, flugHoeheMax), kandidatXZ.z);

                Vector3 richtung = kandidat - transform.position;
                float distanz = richtung.magnitude;
                if (distanz < 0.1f) { ziel = kandidat; return true; }

                // Hindernis (Baum/Haus/Mauer) auf dem direkten Weg dorthin?
                if (Physics.SphereCast(transform.position, 0.5f, richtung.normalized,
                        out RaycastHit hit, distanz, ~(1 << 4), QueryTriggerInteraction.Ignore))
                {
                    // Bis kurz vor das Hindernis fliegen, statt komplett aufzugeben –
                    // wirkt natürlicher als sofort ein neues Ziel zu würfeln
                    float frei = Mathf.Max(1f, hit.distance - 1f);
                    ziel = transform.position + richtung.normalized * frei;
                    return true;
                }

                ziel = kandidat;
                return true;
            }
            ziel = transform.position;
            return false;
        }

        // Landeplatz suchen: echter Boden im Gebiet, nicht auf Weg/Haus/Steilhang
        Vector3? SucheLandeplatz()
        {
            for (int versuch = 0; versuch < 10; versuch++)
            {
                Vector2 kreis = Random.insideUnitCircle * gebietRadius;
                Vector3 kandidat = zentrum + new Vector3(kreis.x, 0f, kreis.y);
                if (BurggrabenMittelalter.IstGesperrt(kandidat)) continue;

                kandidat.y = BurggrabenMittelalter.BodenHoehe(kandidat) + bodenVersatz;
                return kandidat;
            }
            return null;
        }

        bool PlayerZuNah(float radius)
        {
            if (player == null) return false;
            return (player.position - transform.position).sqrMagnitude < radius * radius;
        }

        void SetzeAnimation(bool fliegt, bool sitzt)
        {
            if (animator == null) return;
            if (hatFlying)  animator.SetBool("flying", fliegt);
            if (hatPerched) animator.SetBool("perched", sitzt);
        }
    }
}
