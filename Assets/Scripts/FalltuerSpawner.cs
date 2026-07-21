using UnityEngine;
using Random = UnityEngine.Random;   // Suimono bringt eine eigene Random-Klasse mit

namespace NeonCatch
{
    // Baut beim Start automatisch die Teleport-Hütten: kleine Häuschen aus
    // DENSELBEN Bauteilen wie die Burg-Häuser (Assets/3D Objekte/Burg, als
    // Bauteil_* nach Resources/KI kopiert) – nur kleiner: 1 Kästchen groß.
    // Tür bleibt eine offene Durchgangs-Tür, Dach ist das Kit-Dach.
    //
    // INNEN ist es stockdunkel wie Nebel (schwarze Auskleidung, kein Licht) –
    // man erkennt nichts, und beim Betreten wird man SOFORT zu einer
    // zufälligen anderen Hütte teleportiert (AutoFalltuer), danach 15 s
    // Sperre mit sichtbarem Countdown. Außen schmücken Fackeln, ein Wappen
    // und Banner-Fahnen die Hütte.
    //
    // Platzierung: 2 im Burghof + 5 in der Landschaft, NUR auf dem echten
    // Haupt-Terrain (Map 1), nie in anderen Häusern (Freiraum-Prüfung), und
    // Tiere meiden die Hütten (Sperrzonen).
    public class FalltuerSpawner : MonoBehaviour
    {
        [Header("Anzahl (zusammen 7)")]
        public int anzahlBurg = 2;             // Hütten im Burghof
        public int anzahlLandschaft = 5;       // Hütten draußen auf der Wiese
        public float mindestAbstandBurg = 4f;
        public float mindestAbstandLandschaft = 12f;

        [Header("Hütten-Maße (Spieler ist 0.5 m groß; passt auf 1 Kästchen)")]
        public float huettenBreite = 1.0f;
        public float huettenTiefe  = 1.0f;
        public float wandHoehe     = 0.8f;
        public float tuerBreite    = 0.45f;
        public float tuerHoehe     = 0.65f;
        public float dachHoehe     = 0.4f;

        [Header("Weitergereichte Tür-Einstellungen")]
        public AudioClip reinSound;            // optional
        public AudioClip rausSound;            // optional
        public float sperrZeit = 15f;          // Sekunden Sperre nach jeder Reise

        [Header("Gebiet (radiusAussen automatisch von der Terrain-Größe übernommen)")]
        public float radiusInnen = 20f;        // Landschafts-Hütten: erst außerhalb des Grabens
        public float radiusAussen = 35f;
        [HideInInspector] public bool radiusAussenManuellGesetzt;

        [Header("Zufall (gleicher Seed = gleiche Plätze bei jedem Start)")]
        public int zufallsSeed = 99999;

        Vector3 zentrum;
        Vector3 burgZentrum;
        float   burgRadius;
        float   burgBodenY;

        static Material wandMaterial, dachMaterial, innenMaterial;
        static readonly Color[] bannerFarben =
        {
            new Color(0.72f, 0.14f, 0.12f),   // Rot
            new Color(0.15f, 0.30f, 0.62f),   // Blau
            new Color(0.85f, 0.68f, 0.15f),   // Gelb
            new Color(0.16f, 0.45f, 0.22f),   // Grün
        };

        void Start()
        {
            if (zufallsSeed != 0) Random.InitState(zufallsSeed);

            zentrum = transform.position;
            BurggrabenKomplett graben = FindFirstObjectByType<BurggrabenKomplett>();
            if (graben != null)
            {
                zentrum = graben.transform.position;
                float aussen = graben.innenRadius + graben.grabenBreite;
                radiusInnen = aussen + 4f;
                if (!radiusAussenManuellGesetzt)
                    radiusAussen = aussen + 20f;
            }

            SucheBurgGeometrie();

            Transform eltern = new GameObject("Teleport_Huetten").transform;
            eltern.position = zentrum;

            var belegtePlaetze = new System.Collections.Generic.List<Vector3>();
            int nummer = 1;

            // ---- Hütten im Burghof (dort, wo wirklich Platz ist) ----
            float sektorBurg = 360f / Mathf.Max(1, anzahlBurg);
            for (int i = 0; i < anzahlBurg; i++)
            {
                Vector3? platz = SuchePlatzImBurghof(belegtePlaetze, i * sektorBurg + 45f);
                if (!platz.HasValue) continue;
                belegtePlaetze.Add(platz.Value);
                BaueHuette(platz.Value, nummer++, eltern, burgZentrum);
            }

            // ---- Hütten draußen auf der Landschaft (nur auf Map 1) ----
            float sektorLand = 360f / Mathf.Max(1, anzahlLandschaft);
            for (int i = 0; i < anzahlLandschaft; i++)
            {
                Vector3? platz = SuchePlatzInLandschaft(belegtePlaetze, i * sektorLand);
                if (!platz.HasValue) continue;
                belegtePlaetze.Add(platz.Value);
                BaueHuette(platz.Value, nummer++, eltern, zentrum);
            }

            // Tiere sollen NICHT in die Hütten laufen: jede Hütte als
            // Sperrzone anmelden (erst jetzt – die Ausgangs-Plätze oben
            // wurden sonst fälschlich als "gesperrt" abgelehnt)
            foreach (Vector3 platz in belegtePlaetze)
                BurggrabenMittelalter.SperrzoneHinzufuegen(platz, 1.4f);

            int gebaut = nummer - 1;
            if (gebaut < anzahlBurg + anzahlLandschaft)
                Debug.LogWarning($"FalltuerSpawner: nur {gebaut}/{anzahlBurg + anzahlLandschaft} Teleport-Hütten platziert " +
                                  "(zu wenig freier Platz?).");
        }

        // Burghof ausmessen: Zentrum/Radius aus dem Mauerring des "Burg"-Objekts
        void SucheBurgGeometrie()
        {
            GameObject burgGo = GameObject.Find("Burg");
            if (burgGo != null)
            {
                Transform mauern = burgGo.transform.Find("Mauerweg");
                if (mauern == null) mauern = burgGo.transform.Find("Außenmauern");
                Bounds b = BoundsVonRenderern(mauern != null ? mauern : burgGo.transform);

                burgZentrum = new Vector3(burgGo.transform.position.x, 0f, burgGo.transform.position.z);
                burgRadius  = Mathf.Max(3f, Mathf.Min(b.extents.x, b.extents.z) * 0.7f);
            }
            else
            {
                burgZentrum = zentrum;
                burgRadius  = 8f;
            }

            burgBodenY = BurggrabenMittelalter.BurghofBodenHoehe(burgZentrum, burgRadius, zentrum.y);
        }

        static Bounds BoundsVonRenderern(Transform t)
        {
            Renderer[] rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            foreach (Renderer r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        // Liegt der Punkt auf dem echten Haupt-Terrain (Map 1)? Ohne diese
        // Prüfung landeten Hütten auf Nachbar-Terrains außerhalb der Map.
        static bool AufHauptTerrain(Vector3 pos)
        {
            Terrain terrain = BurggrabenMittelalter.AktivesTerrain;
            if (terrain == null) return true;
            Vector3 lokal = pos - terrain.transform.position;
            Vector3 groesse = terrain.terrainData.size;
            return lokal.x >= 6f && lokal.z >= 6f &&
                   lokal.x <= groesse.x - 6f && lokal.z <= groesse.z - 6f;
        }

        // Passt die Hütte hier hin, ohne in einem anderen Haus, einer Mauer
        // oder einem Baum zu stecken? Großzügiger Rand (0.6 m), damit die
        // Hütte auf einer wirklich FREIEN Fläche steht, nicht direkt neben
        // anderen Häusern.
        bool PlatzFrei(Vector3 pos)
        {
            Vector3 halb = new Vector3(huettenBreite * 0.5f + 0.6f, wandHoehe * 0.5f,
                                       huettenTiefe * 0.5f + 0.6f);
            Vector3 mitte = pos + Vector3.up * (wandHoehe * 0.5f + 0.25f);
            return !Physics.CheckBox(mitte, halb, Quaternion.identity,
                                     ~(1 << 4), QueryTriggerInteraction.Ignore);
        }

        Vector3? SuchePlatzImBurghof(System.Collections.Generic.List<Vector3> belegt, float bevorzugterWinkelGrad)
        {
            LayerMask maske = ~(1 << 4);

            for (int versuch = 0; versuch < 80; versuch++)
            {
                float streuung = Mathf.Min(versuch * 4f, 175f);
                float winkel = (bevorzugterWinkelGrad + Random.Range(-streuung, streuung)) * Mathf.Deg2Rad;
                float radius = Random.Range(2f, Mathf.Max(2.5f, burgRadius - 1f));
                Vector3 testXZ = burgZentrum + new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)) * radius;

                Vector3 start = new Vector3(testXZ.x, burgBodenY + 20f, testXZ.z);
                if (!Physics.Raycast(start, Vector3.down, out RaycastHit hit,
                        40f, maske, QueryTriggerInteraction.Ignore))
                    continue;

                if (hit.point.y > burgBodenY + 1.2f) continue;   // Mauer/Turm/Dach

                Vector3 pos = hit.point;
                if (BurgWege.IstAufWeg(pos)) continue;
                if (!PlatzFrei(pos)) continue;                    // nicht IN andere Häuser bauen
                if (ZuNahAnAnderen(belegt, pos, mindestAbstandBurg)) continue;

                return pos;
            }
            return null;
        }

        Vector3? SuchePlatzInLandschaft(System.Collections.Generic.List<Vector3> belegt, float bevorzugterWinkelGrad)
        {
            LayerMask maske = ~(1 << 4);

            for (int versuch = 0; versuch < 80; versuch++)
            {
                float streuung = Mathf.Min(versuch * 3f, 150f);
                float winkel = (bevorzugterWinkelGrad + Random.Range(-streuung, streuung)) * Mathf.Deg2Rad;
                float radius = Random.Range(radiusInnen, radiusAussen);
                Vector3 testXZ = zentrum + new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)) * radius;

                if (!AufHauptTerrain(testXZ)) continue;          // nur auf Map 1!

                if (!Physics.Raycast(testXZ + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                        100f, maske, QueryTriggerInteraction.Ignore))
                    continue;

                Vector3 pos = hit.point;
                if (BurggrabenMittelalter.IstGesperrt(pos)) continue;
                if (!PlatzFrei(pos)) continue;                    // nicht in Bauernhäuser bauen
                if (ZuNahAnAnderen(belegt, pos, mindestAbstandLandschaft)) continue;

                // KEINE Hütte am Hang/Map-Rand: die Stelle selbst muss eben sein
                // (Boden-Normale fast senkrecht) UND die Umgebung ringsum darf
                // nicht stark ansteigen (der steile Rand-Hügel faellt sonst
                // durch, wenn die Map-Grenzen beim Hütten-Bau noch nicht
                // gesetzt sind)
                if (Vector3.Angle(hit.normal, Vector3.up) > 10f) continue;
                if (!UmgebungIstFlach(pos, maske)) continue;

                return pos;
            }
            return null;
        }

        // Prueft 4 Punkte rings um die Position: weicht die Bodenhoehe dort
        // deutlich ab, steht man am Hang oder an der Rand-Steigung -> kein
        // Platz fuer eine Hütte.
        static bool UmgebungIstFlach(Vector3 pos, LayerMask maske)
        {
            for (int i = 0; i < 4; i++)
            {
                float w = i * 90f * Mathf.Deg2Rad;
                Vector3 p = pos + new Vector3(Mathf.Cos(w), 0f, Mathf.Sin(w)) * 2f;
                if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                        100f, maske, QueryTriggerInteraction.Ignore))
                    return false;
                if (Mathf.Abs(hit.point.y - pos.y) > 0.5f)
                    return false;
            }
            return true;
        }

        static bool ZuNahAnAnderen(System.Collections.Generic.List<Vector3> belegt, Vector3 pos, float mindestAbstand)
        {
            foreach (Vector3 anderer in belegt)
            {
                Vector3 abstand = anderer - pos;
                abstand.y = 0f;
                if (abstand.sqrMagnitude < mindestAbstand * mindestAbstand) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Eine Teleport-Hütte bauen – bevorzugt aus den Burg-Bauteilen
        // (Bauteil_Wand/Tuer/Dach/Boden), sonst aus einfachen Quadern.
        // Innen: schwarze Nebel-Auskleidung, kein Licht. Außen: Fackeln,
        // Wappen über der Tür und Banner-Fahnen.
        // ------------------------------------------------------------------
        void BaueHuette(Vector3 pos, int nummer, Transform eltern, Vector3 gebietsZentrum)
        {
            var huette = new GameObject("Teleport_Huette_" + nummer);
            huette.transform.SetParent(eltern, true);

            Vector3 zurMitte = gebietsZentrum - pos;
            zurMitte.y = 0f;
            float yaw = zurMitte.sqrMagnitude > 0.01f
                ? Mathf.Atan2(zurMitte.x, zurMitte.z) * Mathf.Rad2Deg
                : Random.Range(0f, 360f);
            huette.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            float b = huettenBreite, t = huettenTiefe, h = wandHoehe;

            GameObject wandSrc  = Resources.Load<GameObject>("KI/Bauteil_Wand");
            GameObject tuerSrc  = Resources.Load<GameObject>("KI/Bauteil_Tuer");
            GameObject dachSrc  = Resources.Load<GameObject>("KI/Bauteil_Dach");
            GameObject bodenSrc = Resources.Load<GameObject>("KI/Bauteil_Boden");

            if (wandSrc != null && dachSrc != null)
            {
                // ---- Kit-Bauweise: wie die Burg-Häuser, nur 1 Kästchen groß ----
                // Wände: hinten, links, rechts – vorn das offene Tür-Stück
                BaueKitTeil(wandSrc, huette, new Vector3(0f, 0f, -t * 0.5f), 180f, b, h, false, "Wand_Hinten");
                BaueKitTeil(wandSrc, huette, new Vector3(-b * 0.5f, 0f, 0f), -90f, t, h, false, "Wand_Links");
                BaueKitTeil(wandSrc, huette, new Vector3(b * 0.5f, 0f, 0f), 90f, t, h, false, "Wand_Rechts");
                BaueKitTeil(tuerSrc != null ? tuerSrc : wandSrc, huette,
                            new Vector3(0f, 0f, t * 0.5f), 0f, b, h, true, "Wand_Tuer");

                // NUR die Türöffnung ist begehbar: unsichtbare Blocker links/
                // rechts/über der Tür (das Kit-Türstück selbst hat keine
                // Kollision mehr – ohne Blocker wäre die GANZE Vorderwand offen)
                float segB = (b - tuerBreite) * 0.5f;
                UnsichtbarerBlock(huette, "Tuer_Blocker_L",
                    new Vector3(-(tuerBreite + segB) * 0.5f, h * 0.5f, t * 0.5f), new Vector3(segB, h, 0.08f));
                UnsichtbarerBlock(huette, "Tuer_Blocker_R",
                    new Vector3((tuerBreite + segB) * 0.5f, h * 0.5f, t * 0.5f), new Vector3(segB, h, 0.08f));
                float sturz = h - tuerHoehe;
                if (sturz > 0.02f)
                    UnsichtbarerBlock(huette, "Tuer_Blocker_Oben",
                        new Vector3(0f, tuerHoehe + sturz * 0.5f, t * 0.5f), new Vector3(tuerBreite, sturz, 0.08f));

                // Dach über dem ganzen Häuschen, mit Überhang (wie PlaceRoof)
                BaueKitDach(dachSrc, huette, b + 0.3f, t + 0.3f, h);

                // Boden innen (Kit-Fliese)
                if (bodenSrc != null)
                    BaueKitBoden(bodenSrc, huette, b, t);
            }
            else
            {
                BauePrimitivHuette(huette, b, t, h);
            }

            // ---- SCHWARZE Nebel-Auskleidung: innen erkennt man NICHTS ----
            // WICHTIG: alles OHNE Collider – sonst blockiert die Nebelwand
            // den Spieler, bevor er den Teleport-Auslöser erreicht (genau das
            // machte die Hütten funktionslos: "man kommt nirgends raus")
            const float rand = 0.1f;
            Quader(huette, "Innen_Hinten", new Vector3(0f, h * 0.5f, -t * 0.5f + rand),
                   new Vector3(b - rand * 2f, h, 0.01f), Quaternion.identity, InnenMat(), false);
            Quader(huette, "Innen_Links", new Vector3(-b * 0.5f + rand, h * 0.5f, 0f),
                   new Vector3(0.01f, h, t - rand * 2f), Quaternion.identity, InnenMat(), false);
            Quader(huette, "Innen_Rechts", new Vector3(b * 0.5f - rand, h * 0.5f, 0f),
                   new Vector3(0.01f, h, t - rand * 2f), Quaternion.identity, InnenMat(), false);
            Quader(huette, "Innen_Decke", new Vector3(0f, h - 0.02f, 0f),
                   new Vector3(b - rand, 0.01f, t - rand), Quaternion.identity, InnenMat(), false);
            // Dunkler Boden innen – kein Wiesen-Gras sichtbar
            Quader(huette, "Innen_Boden", new Vector3(0f, 0.06f, 0f),
                   new Vector3(b - rand, 0.02f, t - rand), Quaternion.identity, InnenMat(), false);
            // "Nebelwand" knapp hinter der Tür: von außen nur Schwärze
            Quader(huette, "Innen_Nebelwand", new Vector3(0f, h * 0.5f, t * 0.5f - 0.18f),
                   new Vector3(b - rand * 2f, h, 0.01f), Quaternion.identity, InnenMat(), false);

            // ---- Schmuck außen ----
            SchmueckeHuette(huette, b, t, h, nummer);

            // ---- Teleport-Auslöser: reicht bis DIREKT hinter die Türöffnung,
            // ein Schritt hinein genügt ----
            var ausloeser = huette.AddComponent<BoxCollider>();
            ausloeser.isTrigger = true;
            ausloeser.size   = new Vector3(b * 0.7f, tuerHoehe * 0.95f, t * 0.8f);
            ausloeser.center = new Vector3(0f, tuerHoehe * 0.5f, 0.05f);

            // ---- Ausgang: vor der Tür ----
            var ausgang = new GameObject("Teleport_Huette_" + nummer + "_Ausgang").transform;
            ausgang.SetParent(eltern, true);
            Vector3 ausgangPos = pos + huette.transform.forward * (t * 0.5f + 0.9f);
            ausgangPos.y = BurggrabenMittelalter.BodenHoehe(ausgangPos) + 0.1f;
            ausgang.position = ausgangPos;

            var logik = huette.AddComponent<AutoFalltuer>();
            logik.ausgang   = ausgang;
            logik.reinSound = reinSound;
            logik.rausSound = rausSound;
            logik.sperrZeit = sperrZeit;
        }

        // Ein Kit-Wandstück auf Zielmaß bringen und an eine Hütten-Seite setzen
        static void BaueKitTeil(GameObject prefab, GameObject huette, Vector3 seitePos, float yawGrad,
                                float zielBreite, float zielHoehe, bool collidersEntfernen, string name)
        {
            GameObject teil = Instantiate(prefab);
            Bounds mb = BoundsVonRenderern(teil.transform);
            float sx = zielBreite / Mathf.Max(mb.size.x, 0.001f);
            float sy = zielHoehe  / Mathf.Max(mb.size.y, 0.001f);
            teil.transform.localScale = new Vector3(sx, sy, sx);

            var halter = new GameObject(name);
            halter.transform.SetParent(huette.transform, false);
            halter.transform.localPosition = seitePos;
            halter.transform.localRotation = Quaternion.Euler(0f, yawGrad, 0f);

            teil.transform.SetParent(halter.transform, false);
            teil.transform.localPosition = new Vector3(-mb.center.x * sx,
                zielHoehe * 0.5f - mb.center.y * sy, -mb.center.z * sx);
            teil.transform.localRotation = Quaternion.identity;
            teil.name = name + "_Teil";

            if (collidersEntfernen)
                foreach (Collider c in teil.GetComponentsInChildren<Collider>()) Destroy(c);
            else
                SorgeFuerKollision(teil);   // FBX-Bauteile bringen von sich aus KEINEN Collider mit
        }

        // FBX-Bauteile (Bauteil_Wand etc.) haben von Haus aus keinen Collider –
        // ohne diese Ergänzung könnte man an den Seiten-/Hinterwänden der Hütte
        // einfach hindurchlaufen, statt zwingend durch die Tür zu müssen
        // (genau das wurde als "durch Wände gehen" gemeldet).
        static void SorgeFuerKollision(GameObject go)
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

        static void BaueKitDach(GameObject prefab, GameObject huette, float breite, float tiefe, float wandOben)
        {
            GameObject dach = Instantiate(prefab);
            Bounds mb = BoundsVonRenderern(dach.transform);
            float sx = breite / Mathf.Max(mb.size.x, 0.001f);
            float sz = tiefe  / Mathf.Max(mb.size.z, 0.001f);
            float sy = (sx + sz) * 0.5f;   // Dachhöhe im gleichen Maß mitschrumpfen

            dach.transform.SetParent(huette.transform, false);
            dach.transform.localScale = new Vector3(sx, sy, sz);
            dach.transform.localRotation = Quaternion.identity;
            dach.transform.localPosition = new Vector3(-mb.center.x * sx,
                wandOben - mb.min.y * sy, -mb.center.z * sz);
            dach.name = "Dach";
        }

        static void BaueKitBoden(GameObject prefab, GameObject huette, float breite, float tiefe)
        {
            GameObject boden = Instantiate(prefab);
            Bounds mb = BoundsVonRenderern(boden.transform);
            float sx = breite / Mathf.Max(mb.size.x, 0.001f);
            float sz = tiefe  / Mathf.Max(mb.size.z, 0.001f);

            boden.transform.SetParent(huette.transform, false);
            boden.transform.localScale = new Vector3(sx, 1f, sz);
            boden.transform.localRotation = Quaternion.identity;
            boden.transform.localPosition = new Vector3(-mb.center.x * sx,
                0.02f - mb.min.y, -mb.center.z * sz);
            boden.name = "Boden";
        }

        // Notfall-Bauweise aus Quadern (falls die Bauteile fehlen)
        void BauePrimitivHuette(GameObject huette, float b, float t, float h)
        {
            const float dicke = 0.06f;
            const float ueberhang = 0.15f;

            Quader(huette, "Wand_Hinten", new Vector3(0f, h * 0.5f, -t * 0.5f + dicke * 0.5f),
                   new Vector3(b, h, dicke), Quaternion.identity, WandMat());
            Quader(huette, "Wand_Links", new Vector3(-b * 0.5f + dicke * 0.5f, h * 0.5f, 0f),
                   new Vector3(dicke, h, t), Quaternion.identity, WandMat());
            Quader(huette, "Wand_Rechts", new Vector3(b * 0.5f - dicke * 0.5f, h * 0.5f, 0f),
                   new Vector3(dicke, h, t), Quaternion.identity, WandMat());

            float segBreite = (b - tuerBreite) * 0.5f;
            float vz = t * 0.5f - dicke * 0.5f;
            Quader(huette, "Wand_Vorn_L", new Vector3(-(tuerBreite * 0.5f + segBreite * 0.5f), h * 0.5f, vz),
                   new Vector3(segBreite, h, dicke), Quaternion.identity, WandMat());
            Quader(huette, "Wand_Vorn_R", new Vector3(tuerBreite * 0.5f + segBreite * 0.5f, h * 0.5f, vz),
                   new Vector3(segBreite, h, dicke), Quaternion.identity, WandMat());
            float sturzHoehe = h - tuerHoehe;
            if (sturzHoehe > 0.02f)
                Quader(huette, "Tuer_Sturz", new Vector3(0f, tuerHoehe + sturzHoehe * 0.5f, vz),
                       new Vector3(tuerBreite, sturzHoehe, dicke), Quaternion.identity, WandMat());

            Vector3 first = new Vector3(0f, h + dachHoehe, 0f);
            foreach (float seite in new[] { 1f, -1f })
            {
                Vector3 traufe = new Vector3(0f, h - 0.03f, seite * (t * 0.5f + ueberhang));
                Vector3 mitte = (first + traufe) * 0.5f;
                Vector3 richtung = traufe - first;
                Quader(huette, seite > 0f ? "Dach_Vorn" : "Dach_Hinten", mitte,
                       new Vector3(b + ueberhang * 2f, 0.05f, richtung.magnitude),
                       Quaternion.LookRotation(richtung.normalized, Vector3.up), DachMat());
            }
        }

        // Fackeln neben der Tür, Wappen darüber, 2 Banner an den Vorderecken
        void SchmueckeHuette(GameObject huette, float b, float t, float h, int nummer)
        {
            GameObject fackelSrc = Resources.Load<GameObject>("KI/Fackel");
            if (fackelSrc != null)
            {
                DekoTeil(fackelSrc, huette, new Vector3(-(tuerBreite * 0.5f + 0.1f), tuerHoehe * 0.7f, t * 0.5f + 0.04f), 0.22f, "Fackel_L");
                DekoTeil(fackelSrc, huette, new Vector3(tuerBreite * 0.5f + 0.1f, tuerHoehe * 0.7f, t * 0.5f + 0.04f), 0.22f, "Fackel_R");
            }

            GameObject wappenSrc = Resources.Load<GameObject>("KI/Wappen");
            if (wappenSrc != null)
                DekoTeil(wappenSrc, huette, new Vector3(0f, h - 0.08f, t * 0.5f + 0.04f), 0.2f, "Wappen");

            // Banner: Stange + Stoffbahn an beiden Vorderecken, Farbe pro Hütte
            Color farbe = bannerFarben[(nummer - 1) % bannerFarben.Length];
            var stoff = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Banner_" + nummer };
            stoff.SetColor("_BaseColor", farbe);
            stoff.SetFloat("_Smoothness", 0.1f);

            foreach (float seite in new[] { -1f, 1f })
            {
                float x = seite * (b * 0.5f - 0.04f);
                // Stange (dünner Zylinder, ragt über das Dach)
                var stange = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(stange.GetComponent<Collider>());
                stange.name = "Banner_Stange";
                stange.transform.SetParent(huette.transform, false);
                stange.transform.localPosition = new Vector3(x, h + dachHoehe * 0.5f, t * 0.5f + 0.06f);
                stange.transform.localScale = new Vector3(0.025f, (h + dachHoehe) * 0.5f, 0.025f);
                stange.GetComponent<MeshRenderer>().sharedMaterial = WandMat();

                // Stoffbahn (hängt an der Stange herunter)
                var bahn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(bahn.GetComponent<Collider>());
                bahn.name = "Banner_Stoff";
                bahn.transform.SetParent(huette.transform, false);
                bahn.transform.localPosition = new Vector3(x, h + dachHoehe * 0.55f, t * 0.5f + 0.09f);
                bahn.transform.localScale = new Vector3(0.14f, 0.34f, 0.015f);
                bahn.GetComponent<MeshRenderer>().sharedMaterial = stoff;
            }
        }

        // Ein Deko-Prefab (Fackel/Wappen) auf Zielgröße bringen und außen an
        // die Vorderwand hängen – ohne Collider, rein optisch
        static void DekoTeil(GameObject prefab, GameObject huette, Vector3 lokalePos, float zielGroesse, string name)
        {
            GameObject deko = Instantiate(prefab);
            foreach (Collider c in deko.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (Rigidbody rb in deko.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

            Bounds mb = BoundsVonRenderern(deko.transform);
            float groesste = Mathf.Max(mb.size.x, mb.size.y, mb.size.z);
            if (groesste > 0.001f)
                deko.transform.localScale *= zielGroesse / groesste;

            deko.transform.SetParent(huette.transform, false);
            deko.transform.localPosition = lokalePos;
            deko.transform.localRotation = Quaternion.identity;
            deko.name = name;
        }

        // Reiner Kollisions-Block ohne sichtbares Mesh (für die Tür-Blocker)
        static void UnsichtbarerBlock(GameObject eltern, string name, Vector3 lokalePos, Vector3 groesse)
        {
            var go = new GameObject(name);
            go.transform.SetParent(eltern.transform, false);
            go.transform.localPosition = lokalePos;
            var box = go.AddComponent<BoxCollider>();
            box.size = groesse;
        }

        static void Quader(GameObject eltern, string name, Vector3 lokalePos, Vector3 groesse,
                           Quaternion lokaleRot, Material mat, bool mitCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(eltern.transform, false);
            go.transform.localPosition = lokalePos;
            go.transform.localRotation = lokaleRot;
            go.transform.localScale = groesse;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!mitCollider) Destroy(go.GetComponent<Collider>());
        }

        static Material WandMat()
        {
            if (wandMaterial == null) wandMaterial = ErzeugeMat("Huette_Wand", new Color(0.45f, 0.33f, 0.20f));
            return wandMaterial;
        }

        static Material DachMat()
        {
            if (dachMaterial == null) dachMaterial = ErzeugeMat("Huette_Dach", new Color(0.42f, 0.18f, 0.12f));
            return dachMaterial;
        }

        // Fast schwarz und völlig matt: die Nebel-Dunkelheit im Inneren
        static Material InnenMat()
        {
            if (innenMaterial == null)
            {
                innenMaterial = ErzeugeMat("Huette_Nebel", new Color(0.02f, 0.02f, 0.03f));
                innenMaterial.SetFloat("_Smoothness", 0f);
            }
            return innenMaterial;
        }

        static Material ErzeugeMat(string name, Color farbe)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", farbe);
            mat.SetFloat("_Smoothness", 0.15f);
            return mat;
        }
    }
}
