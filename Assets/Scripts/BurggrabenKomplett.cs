using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;   // eindeutig halten (Suimono hat eine eigene Random-Klasse)

namespace NeonCatch
{
    // Baut beim Start automatisch einen kompletten Burggraben rund um die Position
    // dieses Objekts. Das Objekt auf Gelände-Höhe in die Mitte der Burg setzen –
    // alle Höhen (Wasserspiegel, Schlamm, Grabensohle) werden relativ dazu berechnet.
    //
    //  1. Ring-Graben als generiertes Mesh; Schlamm-Material endet 0.2 m unter Wasser
    //  2. Natürlich verteilte Steine (Prefabs aus dem Polyeler-Ordner zuweisen)
    //  3. Halbtransparentes URP-Wasser im Graben
    //  4. Wasserpflanzen, die im Wind wackeln
    //  5. Fische, die ovale Runden im Graben schwimmen
    //  6. Grasbüschel, die wackeln, wenn der Player (Tag "Player") nahe kommt
    //  7. Schwimm-Steuerung für den Player, sobald er ins Wasser geht
    public class BurggrabenKomplett : MonoBehaviour
    {
        [Header("Graben-Form")]
        public float innenRadius   = 12f;   // Abstand Burginsel -> innere Uferkante
        public float grabenBreite  = 6f;    // Breite des Wassergrabens
        public float grabenTiefe   = 2f;    // Wasserspiegel -> Grabensohle
        public float wasserAbsenkung = 0.4f; // Wasserspiegel liegt so weit unter Geländeoberkante
        public float schlammRandUnterWasser = 0.2f; // Schlamm endet 0.2 unter Wasser
        [Range(16, 128)] public int ringSegmente = 72;

        [Header("Materialien (leer = automatische URP-Materialien)")]
        public Material schlammMaterial;
        public Material uferMaterial;

        [Header("Boden-Bausteine (einzeln abschaltbar)")]
        // Aus, weil der Graben (Mauern links/rechts/unten + Schlamm-Boden) inzwischen
        // direkt im Terrain gebaut wird – der kreisförmige Ring aus diesem Script
        // würde sich sichtbar mit dem viereckigen Terrain-Graben überschneiden.
        public bool baueGrabenBoden = false;   // Ring-Mesh des Grabens (Schlamm + Ufer)
        public bool baueInselBoden = false;    // grüne Scheibe unter der Burg (aus, wenn eigener Boden da ist)
        public bool baueWiesenBoden = false;   // grüner Wiesen-Kreis außen um den Graben
        public float umgebungsRadius = 60f;    // wie weit die Wiese um den Graben reicht
        public Material wiesenMaterial;        // leer = automatisches Grün

        [Header("Steine (Prefabs aus 'Polyeler' zuweisen)")]
        public GameObject[] steinPrefabs;
        public int steinAnzahl = 18;
        public float steinMindestAbstand = 6f;   // Abstand Mitte-zu-Mitte, damit Durchgänge frei bleiben
        public float steinGroesseMin = 0.8f;     // Steine werden auf diese Größe (in Metern)
        public float steinGroesseMax = 1.8f;     // gebracht – egal wie groß das Modell ist

        [Header("Wasser (einfaches URP-Wasser, leer = automatisches Blau)")]
        public Material wasserMaterial;

        [Header("Wasserpflanzen")]
        public GameObject[] pflanzenPrefabs;
        public int pflanzenAnzahl = 12;

        [Header("Fische")]
        public GameObject[] fischPrefabs;
        public int fischAnzahl = 8;
        public float fischGroesse = 1f;   // größer stellen, falls die Fisch-Modelle winzig sind

        [Header("Gras")]
        public GameObject[] grasPrefabs;
        public int grasAnzahl = 40;
        public float grasReaktionsRadius = 2.5f;

        [Header("Schwimmen")]
        public float schwimmGeschwindigkeit = 2f;

        [Header("Zufall (gleicher Seed = gleiche Verteilung bei jedem Start)")]
        public int zufallsSeed = 12345;

        // Berechnete Radien/Höhen des Querschnitts (Welt-Y)
        float wasserY, grenzeY, sohleY, bodenY;
        float sohleInnenR, sohleAussenR, aussenRadius;
        float wasserlinieInnenR, wasserlinieAussenR;   // wo der Wasserspiegel das Ufer trifft
        float grenzeInnenR, grenzeAussenR;             // wo der Schlamm endet (0.2 unter Wasser)

        // Vermessener ECHTER Graben-Ring (viereckig, ins Terrain gegraben):
        // halbe Kantenlänge innen/außen in "Schachbrett-Metrik" um die Mitte
        bool  ringGemessen;
        float messHalbInnen, messHalbAussen;

        // Zentrum aller Messungen/Aufbauten: die ECHTE Burg (das System-
        // Objekt selbst kann irgendwo stehen, z.B. am Weltursprung – dann
        // zielten alle Ring-Messungen, das Wasser und das Schwimmen daneben)
        Vector3 messZentrum;

        Transform elternObjekt;

        void Start()
        {
            // Suimono ist nicht URP-kompatibel (rendert pink) und lieferte
            // falsche Wasserhöhen – deshalb baut dieses Script sein EIGENES
            // Wasser weiter unten (BaueWasser). Liegt trotzdem noch ein altes
            // Suimono-Wasserobjekt in der Szene, entsteht eine zweite,
            // überlappende Wasserebene ("2 Wasserschichten") – daher hier weg damit.
            EntferneSuimonoWasser();

            if (zufallsSeed != 0) Random.InitState(zufallsSeed);

            GameObject burgGo = GameObject.Find("Burg");
            messZentrum = burgGo != null
                ? new Vector3(burgGo.transform.position.x, transform.position.y, burgGo.transform.position.z)
                : transform.position;

            bodenY  = transform.position.y;
            wasserY = bodenY - wasserAbsenkung;
            grenzeY = wasserY - schlammRandUnterWasser;
            sohleY  = wasserY - grabenTiefe;

            aussenRadius = innenRadius + grabenBreite;
            sohleInnenR  = innenRadius + grabenBreite * 0.3f;
            sohleAussenR = innenRadius + grabenBreite * 0.7f;

            // Höhen am ECHTEN (ins Terrain gegrabenen) Graben nachmessen –
            // die reine Formel-Rechnung oben passt sonst nicht zur Terrain-
            // Höhe, das Wasser läge unsichtbar UNTER dem Grabenboden und
            // Schwimmen würde nie ausgelöst
            KalibriereHoehenAmEchtenGraben();

            // Schnittpunkte von Wasserlinie und Schlammgrenze mit den Böschungen
            wasserlinieInnenR  = RadiusAufBoeschung(innenRadius,  sohleInnenR,  wasserY);
            grenzeInnenR       = RadiusAufBoeschung(innenRadius,  sohleInnenR,  grenzeY);
            wasserlinieAussenR = RadiusAufBoeschung(aussenRadius, sohleAussenR, wasserY);
            grenzeAussenR      = RadiusAufBoeschung(aussenRadius, sohleAussenR, grenzeY);

            elternObjekt = new GameObject("Burggraben_Generiert").transform;
            elternObjekt.position = messZentrum;

            if (baueGrabenBoden) BaueGrabenMesh();
            if (baueInselBoden || baueWiesenBoden) BaueUmgebungsBoden();

            // Nur EINE Wasserflaeche: liegt schon eine echte (von Hand /
            // Terrain-Builder platzierte) "Wasser"-Flaeche in der Szene, NICHT
            // zusaetzlich die eigene (tiefer liegende) Platte bauen - das
            // ergab bisher "2 Wasserflaechen" uebereinander.
            if (!EchtesWasserVorhanden())
                BaueWasser();
            else
                Debug.Log("BurggrabenKomplett: echtes Wasser-Objekt gefunden - " +
                          "automatisch gebaute (untere) Wasserflaeche uebersprungen.");

            PlatziereSteine();
            PlatzierePflanzen();
            PlatziereFische();
            PlatziereGras();
            AktiviereSchwimmen();
        }

        // ------------------------------------------------------------------
        // Höhen-Kalibrierung: Der echte Graben ist inzwischen INS TERRAIN
        // gegraben. Hier werden Grabensohle (tiefster Punkt im Ring) und
        // Wiesenhöhe (außerhalb des Grabens) per Raycast gemessen und alle
        // Höhen (Wasserspiegel, Schlammgrenze, Sohle) daran ausgerichtet.
        // Findet die Messung keinen echten Graben (Sohle nicht deutlich
        // unter der Wiese), bleiben die Formel-Werte aus Start() bestehen.
        // ------------------------------------------------------------------
        void KalibriereHoehenAmEchtenGraben()
        {
            // Zuerst herausfinden, WO der Graben-Ring wirklich liegt – die
            // eingestellten Radien (innenRadius/grabenBreite) sind nur eine
            // Annahme; liegt der echte Terrain-Graben woanders, würde ohne
            // diese Messung weder Wasser noch Schwimmen dort landen
            MesseGrabenRing();

            float ringRadius = ringGemessen
                ? (messHalbInnen + messHalbAussen) * 0.5f
                : innenRadius + grabenBreite * 0.5f;

            // Sohle: tiefster Punkt an 16 Stellen im Grabenring. Der echte
            // Graben ist VIERECKIG – Punkte auf den Quadrat-Ring projizieren
            // (gleiche Technik wie die Fisch-Bahn), sonst verfehlen die
            // Diagonal-Richtungen den Graben
            float sohleEcht = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float w = i / 16f * Mathf.PI * 2f;
                float c = Mathf.Cos(w), s = Mathf.Sin(w);
                float skala = ringRadius / Mathf.Max(Mathf.Abs(c), Mathf.Abs(s));
                Vector3 p = messZentrum + new Vector3(c * skala, 0f, s * skala);
                if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                        100f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    sohleEcht = Mathf.Min(sohleEcht, hit.point.y);
            }

            // Wiese: an 16 Stellen außerhalb des Grabens messen und den
            // TIEFSTEN Punkt nehmen – beim Durchschnitt lief das Wasser an
            // allen Stellen über, die unter dem Mittelwert lagen ("das
            // Wasser rinnt hinaus")
            float wieseTiefste = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float w = i / 16f * Mathf.PI * 2f;
                Vector3 p = messZentrum
                          + new Vector3(Mathf.Cos(w), 0f, Mathf.Sin(w)) * (aussenRadius + 3f);
                if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                        100f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    wieseTiefste = Mathf.Min(wieseTiefste, hit.point.y);
            }
            if (wieseTiefste == float.MaxValue || sohleEcht == float.MaxValue) return;   // nichts messbar

            // Nur übernehmen, wenn wirklich ein Graben da ist (deutlich tiefer
            // als die Wiese) – sonst würde eine flache Szene alles verstellen
            if (sohleEcht > wieseTiefste - 0.5f) return;

            bodenY  = wieseTiefste;
            sohleY  = Mathf.Max(sohleEcht, wieseTiefste - 4f);   // Ausreißer begrenzen
            // Wasser sicher UNTER dem tiefsten Uferpunkt – nichts läuft mehr
            // über. Nur wenn der Graben insgesamt zu flach ist, wird minimal
            // angehoben, damit Schwimmen überhaupt auslösen kann.
            wasserY = bodenY - wasserAbsenkung;
            if (wasserY < sohleY + 0.45f)
                wasserY = Mathf.Min(sohleY + 0.45f, bodenY - 0.15f);
            grenzeY = wasserY - schlammRandUnterWasser;
        }

        // Findet den echten (viereckigen) Terrain-Graben: läuft von der Burg
        // aus in 4 Achsen-Richtungen nach außen und sucht in jedem Höhenprofil
        // die zusammenhängende TIEFE Zone (deutlich unter der normalen
        // Bodenhöhe) – deren Anfang/Ende ergibt innere und äußere Ringkante.
        void MesseGrabenRing()
        {
            Vector3[] richtungen = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            const int maxAbstand = 60;

            float startSumme = 0f, endeSumme = 0f;
            int gefunden = 0;

            foreach (Vector3 richtung in richtungen)
            {
                // Höhenprofil entlang dieser Achse abtasten
                var hoehen = new float[maxAbstand + 1];
                var getroffen = new bool[maxAbstand + 1];
                var werte = new System.Collections.Generic.List<float>();

                for (int r = 2; r <= maxAbstand; r++)
                {
                    Vector3 p = messZentrum + richtung * r;
                    if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit,
                            120f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    {
                        hoehen[r] = hit.point.y;
                        getroffen[r] = true;
                        werte.Add(hit.point.y);
                    }
                }
                if (werte.Count < 10) continue;

                // Median = normale Bodenhöhe (Burginsel + Wiese); Mauern und
                // Türme liegen darüber, der Graben deutlich darunter
                werte.Sort();
                float referenz = werte[werte.Count / 2];

                int rStart = -1, rEnde = -1;
                for (int r = 2; r <= maxAbstand; r++)
                {
                    if (!getroffen[r]) continue;
                    if (hoehen[r] < referenz - 0.5f)
                    {
                        if (rStart < 0) rStart = r;
                        rEnde = r;
                    }
                    else if (rStart >= 0 && r > rEnde + 3)
                    {
                        break;   // tiefe Zone ist vorbei – nicht weiter suchen
                    }
                }
                if (rStart < 0) continue;

                startSumme += rStart;
                endeSumme  += rEnde;
                gefunden++;
            }

            // Mindestens 2 der 4 Richtungen müssen den Graben gesehen haben
            if (gefunden >= 2)
            {
                messHalbInnen  = startSumme / gefunden - 0.5f;
                messHalbAussen = endeSumme  / gefunden + 0.5f;
                ringGemessen   = messHalbAussen > messHalbInnen + 1f;
                if (ringGemessen)
                    Debug.Log($"BurggrabenKomplett: echten Graben-Ring vermessen – " +
                              $"innen {messHalbInnen:F1} m, außen {messHalbAussen:F1} m.");
            }
        }

        // ------------------------------------------------------------------
        // 1. Graben-Mesh: Ring mit zwei Sub-Meshes (Ufer oben, Schlamm unten).
        //    Die Grenze zwischen beiden liegt exakt 0.2 m unter dem Wasserspiegel.
        // ------------------------------------------------------------------
        void BaueGrabenMesh()
        {
            // Querschnitts-Profil von innen nach außen: (Radius, Welt-Y)
            // Zwischen Index 1 und 4 liegt der Schlamm, außen herum das Ufer.
            Vector2[] profil =
            {
                new Vector2(innenRadius,  bodenY),
                new Vector2(grenzeInnenR, grenzeY),
                new Vector2(sohleInnenR,  sohleY),
                new Vector2(sohleAussenR, sohleY),
                new Vector2(grenzeAussenR, grenzeY),
                new Vector2(aussenRadius, bodenY),
            };
            bool[] streifenIstSchlamm = { false, true, true, true, false };

            int punkte = profil.Length;
            var vertices = new Vector3[(ringSegmente + 1) * punkte];
            var uvs      = new Vector2[vertices.Length];

            Vector3 zentrum = transform.position;
            for (int s = 0; s <= ringSegmente; s++)
            {
                float winkel = (float)s / ringSegmente * Mathf.PI * 2f;
                float cos = Mathf.Cos(winkel), sin = Mathf.Sin(winkel);
                for (int p = 0; p < punkte; p++)
                {
                    Vector3 welt = new Vector3(zentrum.x + cos * profil[p].x,
                                               profil[p].y,
                                               zentrum.z + sin * profil[p].x);
                    int i = s * punkte + p;
                    vertices[i] = welt - elternObjekt.position;
                    uvs[i] = new Vector2((float)s / ringSegmente * 24f, (float)p / (punkte - 1) * 2f);
                }
            }

            var uferTris    = new System.Collections.Generic.List<int>();
            var schlammTris = new System.Collections.Generic.List<int>();
            for (int s = 0; s < ringSegmente; s++)
            {
                for (int p = 0; p < punkte - 1; p++)
                {
                    int a = s * punkte + p;
                    int b = a + punkte;          // gleicher Profilpunkt, nächstes Segment
                    var tris = streifenIstSchlamm[p] ? schlammTris : uferTris;
                    // Wicklung so, dass die Flächen nach oben zeigen und der
                    // Graben von oben/innen sichtbar ist
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }

            var mesh = new Mesh { name = "Burggraben_Ring" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(uferTris, 0);
            mesh.SetTriangles(schlammTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Graben_Boden");
            go.transform.SetParent(elternObjekt, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                uferMaterial    != null ? uferMaterial    : ErzeugeMaterial("Ufer_Auto",    new Color(0.36f, 0.42f, 0.22f)),
                schlammMaterial != null ? schlammMaterial : ErzeugeMaterial("Schlamm_Auto", new Color(0.30f, 0.22f, 0.14f)),
            };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // ------------------------------------------------------------------
        // Umgebungsboden: flache Burginsel innen und Wiesen-Ring außen, mit
        // einem Ring-Loch genau dort, wo der Graben liegt. Nur nötig, wenn es
        // kein eigenes Boden-Objekt (oder Terrain) an dieser Stelle gibt.
        // ------------------------------------------------------------------
        void BaueUmgebungsBoden()
        {
            Material mat = wiesenMaterial != null
                ? wiesenMaterial
                : ErzeugeMaterial("Wiese_Auto", new Color(0.30f, 0.45f, 0.20f));

            if (baueInselBoden)
                BaueFlachenRing("Burginsel", 0f, innenRadius, bodenY, mat, mitCollider: true, layerName: null);
            if (baueWiesenBoden)
                // Layer "Grass": darauf spawnen und wandern die Tiere (siehe
                // BurggrabenMittelalter) – ohne diesen Layer gäbe es nichts,
                // worauf der Grass-Raycast beim Spawnen treffen könnte.
                BaueFlachenRing("Wiese", aussenRadius, umgebungsRadius, bodenY, mat, mitCollider: true, layerName: "Grass");
        }

        // Flacher Ring (bzw. Scheibe bei radiusInnen = 0) auf gegebener Höhe
        void BaueFlachenRing(string name, float radiusInnen, float radiusAussen, float hoehe, Material mat,
                             bool mitCollider, string layerName)
        {
            var vertices = new Vector3[(ringSegmente + 1) * 2];
            var uvs      = new Vector2[vertices.Length];
            var tris     = new int[ringSegmente * 6];

            Vector3 zentrum = transform.position;
            for (int s = 0; s <= ringSegmente; s++)
            {
                float winkel = (float)s / ringSegmente * Mathf.PI * 2f;
                float cos = Mathf.Cos(winkel), sin = Mathf.Sin(winkel);

                Vector3 innen  = new Vector3(zentrum.x + cos * radiusInnen, hoehe, zentrum.z + sin * radiusInnen);
                Vector3 aussen = new Vector3(zentrum.x + cos * radiusAussen, hoehe, zentrum.z + sin * radiusAussen);
                vertices[s * 2]     = innen - elternObjekt.position;
                vertices[s * 2 + 1] = aussen - elternObjekt.position;
                uvs[s * 2]     = new Vector2(innen.x, innen.z) * 0.2f;
                uvs[s * 2 + 1] = new Vector2(aussen.x, aussen.z) * 0.2f;
            }

            for (int s = 0; s < ringSegmente; s++)
            {
                int a = s * 2;          // innen, dieses Segment
                int b = (s + 1) * 2;    // innen, nächstes Segment
                int t = s * 6;
                // gleiche Wicklung wie beim Graben: Flächen zeigen nach oben
                tris[t]     = a;     tris[t + 1] = b; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = b; tris[t + 5] = b + 1;
            }

            var mesh = new Mesh { name = "Boden_" + name };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(elternObjekt, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (mitCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;

            if (!string.IsNullOrEmpty(layerName))
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0) go.layer = layer;
                else Debug.LogWarning("BurggrabenKomplett: Layer '" + layerName + "' existiert nicht (Project Settings > Tags and Layers).");
            }
        }

        Material ErzeugeMaterial(string name, Color farbe)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", farbe);
            mat.SetFloat("_Smoothness", 0.15f);
            return mat;
        }

        // Entfernt alte Suimono-Wasserobjekte aus der Szene (siehe Start()) –
        // verhindert eine zweite, überlappende Wasserebene.
        static void EntferneSuimonoWasser()
        {
            foreach (Suimono.Core.SuimonoModule modul in FindObjectsByType<Suimono.Core.SuimonoModule>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Destroy(modul.gameObject);
        }

        // ------------------------------------------------------------------
        // 2. Wasser: halbtransparenter blauer Ring genau über dem Graben.
        //    (Suimono wurde entfernt – es ist nicht URP-kompatibel und wurde
        //    rosa dargestellt; außerdem lieferte es falsche Wasserhöhen.)
        // ------------------------------------------------------------------
        void BaueWasser()
        {
            Material mat = wasserMaterial != null ? wasserMaterial : ErzeugeWasserMaterial();

            // QUADRATISCHE Wasserplatte statt Kreisring: der echte Graben ist
            // ein 4-eckiger Ring um die Burg – die Platte deckt auch dessen
            // Ecken ab. Sie reicht unter die Burginsel (dort verdeckt sie der
            // höher liegende Burgboden) und endet KNAPP INNERHALB der äußeren
            // Grabenkante, damit kein Wasser auf die Wiese hinausragt.
            // Bevorzugt die VERMESSENE Ringkante statt der eingestellten.
            float halb = (ringGemessen ? messHalbAussen : aussenRadius) - 0.5f;
            var vertices = new Vector3[]
            {
                new Vector3(-halb, wasserY - transform.position.y, -halb),
                new Vector3(-halb, wasserY - transform.position.y,  halb),
                new Vector3( halb, wasserY - transform.position.y,  halb),
                new Vector3( halb, wasserY - transform.position.y, -halb),
            };
            var uvs  = new Vector2[] { new Vector2(0, 0), new Vector2(0, 8), new Vector2(8, 8), new Vector2(8, 0) };
            var tris = new int[] { 0, 1, 2, 0, 2, 3 };

            var mesh = new Mesh { name = "Burggraben_Wasser" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Burggraben_Wasser");
            go.transform.SetParent(elternObjekt, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            int layer = LayerMask.NameToLayer("Water");
            if (layer >= 0) go.layer = layer;

            // Zusätzlich ein Trigger-Volumen über die GANZE Wassersäule: reine
            // Positions-Vergleiche pro Frame (siehe BurggrabenSchwimmen.Update)
            // verpassen einen schnellen Sprung von hoch oben ins Wasser – die
            // Fallbewegung überspringt das "gerade eben eingetaucht"-Fenster
            // in einem einzigen Frame, Schwimmen aktivierte sich bisher immer
            // erst beim Aufkommen am Grabenboden ("erst wenn man Boden
            // berührt kann man schwimmen"). Ein Trigger erkennt das Eintauchen
            // per Sweep-Kollision zuverlässig, unabhängig von der Fallgeschwindigkeit.
            float wasserYLokal = wasserY - transform.position.y;
            float sohleYLokal  = sohleY  - transform.position.y;
            var triggerGo = new GameObject("Burggraben_Wasser_Trigger");
            triggerGo.transform.SetParent(elternObjekt, false);
            var box = triggerGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, (wasserYLokal + sohleYLokal - 1f) * 0.5f, 0f);
            box.size   = new Vector3(halb * 2f, wasserYLokal - sohleYLokal + 2f, halb * 2f);
            triggerGo.AddComponent<WasserEintauchTrigger>();
        }

        // Sucht ein von Hand platziertes Wasser-Objekt ("Burggraben/Wasser"
        // oder ein eigenstaendiges "Wasser" in der Szene) - siehe SucheEchtenGraben.
        static bool EchtesWasserVorhanden()
        {
            GameObject grabenGo = GameObject.Find("Burggraben");
            if (grabenGo != null && grabenGo.transform.Find("Wasser") != null)
                return true;
            return GameObject.Find("Wasser") != null;
        }

        Material ErzeugeWasserMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Wasser_Auto" };
            // Auf transparent umstellen (URP-Lit)
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.15f, 0.42f, 0.60f, 0.65f));
            mat.SetFloat("_Smoothness", 0.9f);
            return mat;
        }

        // ------------------------------------------------------------------
        // 3. Steine natürlich im Schlammbereich verteilen
        // ------------------------------------------------------------------
        void PlatziereSteine()
        {
            if (steinPrefabs == null || steinPrefabs.Length == 0)
            {
                Debug.LogWarning("BurggrabenKomplett: Keine Stein-Prefabs (Polyeler) zugewiesen.");
                return;
            }

            // Bereits belegte Plätze merken, damit die Steine nicht zusammenklumpen
            // und die Durchgänge zwischen ihnen begehbar bleiben
            var belegtePlaetze = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < steinAnzahl; i++)
            {
                Vector3 pos = Vector3.zero;
                bool platzGefunden = false;
                for (int versuch = 0; versuch < 40 && !platzGefunden; versuch++)
                {
                    float radius = Random.Range(grenzeInnenR, grenzeAussenR);
                    pos = PunktImRing(radius);
                    pos.y = ProfilHoehe(radius) - 0.1f; // leicht eingegraben, wirkt natürlicher

                    platzGefunden = true;
                    foreach (Vector3 anderer in belegtePlaetze)
                    {
                        Vector3 abstand = anderer - pos;
                        abstand.y = 0f;
                        if (abstand.sqrMagnitude < steinMindestAbstand * steinMindestAbstand)
                        {
                            platzGefunden = false;
                            break;
                        }
                    }
                }
                belegtePlaetze.Add(pos);

                // Außerhalb der Map keine Steine ablegen
                if (BurggrabenMittelalter.IstGesperrt(pos)) continue;

                GameObject stein = Instantiate(ZufallsPrefab(steinPrefabs), pos,
                    Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f)),
                    elternObjekt);
                stein.name = "Graben_Stein_" + (i + 1);

                // Stein auf kontrollierte Größe bringen: manche Felsen-Modelle
                // sind mehrere Meter groß und würden die Durchgänge blockieren
                Renderer messung = stein.GetComponentInChildren<Renderer>();
                if (messung != null)
                {
                    Vector3 groesse = messung.bounds.size;
                    float groesste = Mathf.Max(groesse.x, groesse.y, groesse.z);
                    if (groesste > 0.01f)
                        stein.transform.localScale *= Random.Range(steinGroesseMin, steinGroesseMax) / groesste;
                }
            }
        }

        // ------------------------------------------------------------------
        // 4. Wasserpflanzen auf der Grabensohle, wackeln im Wind
        // ------------------------------------------------------------------
        void PlatzierePflanzen()
        {
            if (pflanzenPrefabs == null || pflanzenPrefabs.Length == 0)
            {
                Debug.LogWarning("BurggrabenKomplett: Keine Wasserpflanzen-Prefabs zugewiesen.");
                return;
            }

            for (int i = 0; i < pflanzenAnzahl; i++)
            {
                float radius = Random.Range(sohleInnenR + 0.3f, sohleAussenR - 0.3f);
                Vector3 pos = PunktImRing(radius);
                if (BurggrabenMittelalter.IstGesperrt(pos)) continue;   // außerhalb der Map
                pos.y = sohleY - 0.05f;

                GameObject pflanze = Instantiate(ZufallsPrefab(pflanzenPrefabs), pos,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), elternObjekt);
                pflanze.name = "Graben_Pflanze_" + (i + 1);
                pflanze.transform.localScale *= Random.Range(0.8f, 1.3f);
                pflanze.AddComponent<PflanzenWind>();
            }
        }

        // ------------------------------------------------------------------
        // 5. Fische, die quadratische Runden im Graben schwimmen
        // ------------------------------------------------------------------
        void PlatziereFische()
        {
            if (fischPrefabs == null || fischPrefabs.Length == 0)
            {
                Debug.LogWarning("BurggrabenKomplett: Keine Fisch-Prefabs zugewiesen.");
                return;
            }

            // Bevorzugt: die ECHTE Burg + der echte (Editor-gebaute) Burggraben
            // in der Szene ("Burg" und "Burggraben/Wasser") statt der eigenen
            // rechnerischen Geometrie dieses Scripts – der echte Graben ist ein
            // QUADRATISCHER Ring um die quadratische Burg (siehe LandschaftBuilder/
            // BurggrabenBuilder), und die Wasser-Fläche ist eine große Platte, die
            // auch unter der Burg liegt (dort nur vom Burgboden verdeckt). Ohne
            // echte Burg/Graben in der Szene fällt es auf die alte Berechnung
            // dieses Scripts zurück.
            Vector3 zentrumFisch = new Vector3(messZentrum.x, 0f, messZentrum.z);
            float innenHalb = sohleInnenR;
            float aussenHalb = sohleAussenR;
            float tiefeMin = sohleY + 0.35f;
            float tiefeMax = wasserY - 0.35f;

            // Wurde der echte Terrain-Graben vermessen, schwimmen die Fische
            // in DESSEN Ring (mit Sicherheitsabstand zu beiden Ufern)
            if (ringGemessen)
            {
                innenHalb  = messHalbInnen + 0.8f;
                aussenHalb = messHalbAussen - 0.8f;
            }

            if (SucheEchtenGraben(out Vector3 zEcht, out float echtInnen, out float echtAussen, out float wasserYEcht))
            {
                zentrumFisch = zEcht;
                // Sicherheitsabstand zu beiden Seiten: nicht direkt an der
                // Burgmauer (35 %) und nicht am äußersten Plattenrand,
                // der schon unter dem ansteigenden Gelände liegt (75 %)
                innenHalb  = Mathf.Lerp(echtInnen, echtAussen, 0.35f);
                aussenHalb = Mathf.Lerp(echtInnen, echtAussen, 0.75f);
                // Deutlich mehr Abstand zur Oberfläche: das Auf-und-Ab-Wackeln
                // der Bahn addiert bis zu +0.12 m, UND bei hochskalierten
                // Fischen (fischGroesse) reicht der Rücken sonst über die
                // gemessene Pivot-Tiefe hinaus bis über die Oberfläche –
                // deshalb zusätzlich mit fischGroesse mitwachsender Abstand
                float groessenVersatz = Mathf.Max(0f, fischGroesse - 1f) * 0.15f;
                tiefeMin = wasserYEcht - 1.1f - groessenVersatz;
                tiefeMax = wasserYEcht - 0.65f - groessenVersatz;
            }

            float mittelGroesse = (innenHalb + aussenHalb) * 0.5f;
            float halbeBreite   = (aussenHalb - innenHalb) * 0.5f;

            // Exakt die Hälfte schwimmt im Uhrzeigersinn, die andere Hälfte
            // dagegen – bei ungerader Anzahl bekommt Uhrzeigersinn den Rest
            int imUhrzeigersinn = (fischAnzahl + 1) / 2;

            for (int i = 0; i < fischAnzahl; i++)
            {
                GameObject fisch = Instantiate(ZufallsPrefab(fischPrefabs), elternObjekt);
                fisch.name = "Graben_Fisch_" + (i + 1);

                // Mitgelieferte Scripte der Fisch-Prefabs (z.B. Sardine-Demo/Boids)
                // abschalten, damit sie nicht gegen unsere Bahn schwimmen –
                // der Animator läuft weiter, FischSchwimmer steuert ihn direkt an
                foreach (MonoBehaviour mb in fisch.GetComponentsInChildren<MonoBehaviour>())
                    mb.enabled = false;

                fisch.transform.localScale *= fischGroesse;

                var schwimmer = fisch.AddComponent<FischSchwimmer>();
                schwimmer.zentrum        = zentrumFisch;
                schwimmer.halbGroesse    = Random.Range(mittelGroesse - halbeBreite * 0.7f, mittelGroesse + halbeBreite * 0.7f);
                schwimmer.tiefeY         = Random.Range(tiefeMin, tiefeMax);
                schwimmer.winkel         = Random.Range(0f, Mathf.PI * 2f);
                schwimmer.geschwindigkeit = Random.Range(0.12f, 0.25f) * (i < imUhrzeigersinn ? 1f : -1f);
            }
        }

        // Sucht die ECHTE Burg + den echten (Editor-gebauten) Burggraben in
        // der Szene ("Burg" und "Burggraben/Wasser") und liefert deren
        // Geometrie zurück. false, wenn keine gefunden wurde – dann sollte
        // der Aufrufer auf die eigene, rein rechnerische Geometrie zurück-
        // fallen. Wird von Fischen UND vom Schwimmen benutzt, damit beide
        // dieselbe echte Wasserhöhe/-fläche verwenden (nicht die interne,
        // von Weltursprung + Standardmaßen abgeleitete Annahme dieses
        // Scripts – DAS hat "man kann sich nicht mehr bewegen" verursacht:
        // das Schwimmen dachte anhand der falschen wasserY, der Spieler
        // stünde ab Frame 1 tief im Wasser, und schaltete die Steuerung
        // sofort dauerhaft ab).
        bool SucheEchtenGraben(out Vector3 zentrumEcht, out float innenHalb, out float aussenHalb, out float wasserYEcht)
        {
            zentrumEcht = Vector3.zero; innenHalb = 0f; aussenHalb = 0f; wasserYEcht = 0f;

            GameObject burgGo   = GameObject.Find("Burg");
            GameObject grabenGo = GameObject.Find("Burggraben");
            Transform   wasserT = grabenGo != null ? grabenGo.transform.Find("Wasser") : null;

            // Beim Terrain-Graben gibt es kein "Burggraben"-Elternobjekt mehr –
            // dort liegt die Wasserfläche als eigenständiges Objekt "Wasser"
            // direkt in der Szene.
            if (wasserT == null)
            {
                GameObject wasserGo = GameObject.Find("Wasser");
                if (wasserGo != null) wasserT = wasserGo.transform;
            }
            if (burgGo == null || wasserT == null) return false;

            Transform mauerBasis = burgGo.transform.Find("Mauerweg");
            if (mauerBasis == null) mauerBasis = burgGo.transform.Find("Außenmauern");
            Bounds burgB = BoundsVonRenderern(mauerBasis != null ? mauerBasis : burgGo.transform);
            Renderer wasserR = wasserT.GetComponent<Renderer>();
            if (wasserR == null || burgB.size.sqrMagnitude < 0.01f) return false;

            float echtInnen  = Mathf.Max(burgB.extents.x, burgB.extents.z);
            float echtAussen = Mathf.Min(wasserR.bounds.extents.x, wasserR.bounds.extents.z);
            // Nur übernehmen, wenn die Wasser-Platte spürbar größer ist als
            // die Burg – sonst lieber die alte Berechnung behalten
            if (echtAussen <= echtInnen + 0.5f) return false;

            zentrumEcht = new Vector3(burgGo.transform.position.x, 0f, burgGo.transform.position.z);
            innenHalb   = echtInnen;
            aussenHalb  = echtAussen;
            wasserYEcht = wasserT.position.y;
            return true;
        }

        // Gesamt-Bounds aller Renderer unter t – zum Vermessen echter
        // Szenen-Objekte (Burg-Mauerring) zur Laufzeit
        static Bounds BoundsVonRenderern(Transform t)
        {
            Renderer[] rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            foreach (Renderer r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------
        // 6. Grasbüschel an beiden Ufern (über der Wasserlinie)
        // ------------------------------------------------------------------
        void PlatziereGras()
        {
            if (grasPrefabs == null || grasPrefabs.Length == 0)
            {
                Debug.LogWarning("BurggrabenKomplett: Keine Gras-Prefabs zugewiesen.");
                return;
            }

            for (int i = 0; i < grasAnzahl; i++)
            {
                // Nur am äußeren Ufer (Wiesenseite) – nicht auf dem Burgboden;
                // mehrere Versuche, damit kein Gras auf den Wegen (BurgWege) steht
                Vector3 pos = Vector3.zero;
                float radius = 0f;
                for (int versuch = 0; versuch < 30; versuch++)
                {
                    radius = Random.Range(wasserlinieAussenR + 0.2f, aussenRadius + 1.0f);
                    pos = PunktImRing(radius);
                    if (!BurggrabenMittelalter.IstGesperrt(pos)) break;
                }
                // Kein gültiger Platz (z.B. außerhalb der Map): Gras weglassen
                if (BurggrabenMittelalter.IstGesperrt(pos)) continue;

                // Nicht auf Mauern oder Gebäuden wachsen lassen
                if (BurggrabenMittelalter.BodenHoehe(pos) > bodenY + 0.4f) continue;

                pos.y = ProfilHoehe(radius);

                GameObject gras = Instantiate(ZufallsPrefab(grasPrefabs), pos,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), elternObjekt);
                gras.name = "Graben_Gras_" + (i + 1);
                gras.transform.localScale *= Random.Range(0.8f, 1.4f);

                var wackeln = gras.AddComponent<GrasWackeln>();
                wackeln.reaktionsRadius = grasReaktionsRadius;
            }
        }

        // ------------------------------------------------------------------
        // 7. Schwimm-Steuerung an den Player hängen
        // ------------------------------------------------------------------
        void AktiviereSchwimmen()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("BurggrabenKomplett: Kein Objekt mit Tag 'Player' gefunden – Schwimmen deaktiviert.");
                return;
            }

            var schwimmen = player.GetComponent<BurggrabenSchwimmen>();
            if (schwimmen == null) schwimmen = player.AddComponent<BurggrabenSchwimmen>();

            // Bevorzugt die ECHTE Burg/Wasser-Geometrie – die interne,
            // rechnerische (Weltursprung + Standardmaße) passte nicht zur
            // tatsächlichen Terrain-Höhe und ließ das Schwimmen sofort beim
            // Start fälschlich auslösen (Spieler "stand" laut der falschen
            // Rechnung tief im Wasser → Steuerung dauerhaft deaktiviert).
            if (SucheEchtenGraben(out Vector3 zEcht, out float echtInnen, out float echtAussen, out float wasserYEcht))
            {
                schwimmen.wasserY           = wasserYEcht;
                schwimmen.grabenZentrum     = zEcht;
                schwimmen.wasserInnenRadius = Mathf.Lerp(echtInnen, echtAussen, 0.25f);
                schwimmen.wasserAussenRadius = Mathf.Lerp(echtInnen, echtAussen, 0.85f);
            }
            else
            {
                schwimmen.wasserY           = wasserY;
                schwimmen.grabenZentrum     = messZentrum;
                schwimmen.wasserInnenRadius = wasserlinieInnenR;
                schwimmen.wasserAussenRadius = wasserlinieAussenR;
                // Der echte Graben ist ein 4-ECKIGER Ring: Bereichs-Prüfung
                // quadratisch statt kreisförmig, sonst gäbe es in den Ecken
                // kein Schwimmen. Bevorzugt die VERMESSENEN Ringkanten.
                schwimmen.quadratischerBereich = true;
                schwimmen.halbInnen  = ringGemessen ? messHalbInnen  : innenRadius;
                schwimmen.halbAussen = ringGemessen ? messHalbAussen : aussenRadius;
            }
            schwimmen.geschwindigkeit  = schwimmGeschwindigkeit;

            // Diagnose: diese Zeile zeigt in der Console, WO das Schwimmen
            // aktiv ist – wenn Schwimmen nicht geht, diese Werte durchgeben!
            Debug.Log($"Schwimmen aktiv: Zentrum ({schwimmen.grabenZentrum.x:F0}, {schwimmen.grabenZentrum.z:F0}), " +
                      $"Wasserhöhe {schwimmen.wasserY:F2}, Ring " +
                      (schwimmen.quadratischerBereich
                          ? $"eckig {schwimmen.halbInnen:F1}-{schwimmen.halbAussen:F1} m"
                          : $"rund {schwimmen.wasserInnenRadius:F1}-{schwimmen.wasserAussenRadius:F1} m") +
                      $", Ring vermessen: {ringGemessen}");
        }

        // ------------------------------------------------------------------
        // Hilfsfunktionen
        // ------------------------------------------------------------------

        // Radius, bei dem eine Böschung (von Uferkante zur Sohlenkante) die Höhe y erreicht
        float RadiusAufBoeschung(float uferR, float sohleR, float y)
        {
            float t = Mathf.InverseLerp(bodenY, sohleY, y);
            return Mathf.Lerp(uferR, sohleR, t);
        }

        // Höhe des Graben-Querschnitts an einem Radius (außerhalb: Geländeoberkante)
        float ProfilHoehe(float radius)
        {
            if (radius <= innenRadius || radius >= aussenRadius) return bodenY;
            if (radius < sohleInnenR)  return Mathf.Lerp(bodenY, sohleY, Mathf.InverseLerp(innenRadius, sohleInnenR, radius));
            if (radius <= sohleAussenR) return sohleY;
            return Mathf.Lerp(sohleY, bodenY, Mathf.InverseLerp(sohleAussenR, aussenRadius, radius));
        }

        Vector3 PunktImRing(float radius)
        {
            float winkel = Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(messZentrum.x + Mathf.Cos(winkel) * radius,
                               0f,
                               messZentrum.z + Mathf.Sin(winkel) * radius);
        }

        GameObject ZufallsPrefab(GameObject[] prefabs)
        {
            return prefabs[Random.Range(0, prefabs.Length)];
        }
    }

    // ======================================================================
    // Wasserpflanze: wiegt sich dauerhaft sanft, als würde Wind/Strömung ziehen
    // ======================================================================
    public class PflanzenWind : MonoBehaviour
    {
        public float staerke = 4f;      // maximale Neigung in Grad
        public float tempo   = 1.2f;

        Quaternion startRotation;
        float phase;

        void Start()
        {
            startRotation = transform.localRotation;
            phase = Random.Range(0f, 100f);
        }

        void Update()
        {
            float t = Time.time * tempo + phase;
            float nickX = Mathf.Sin(t) * staerke;
            float nickZ = Mathf.Sin(t * 0.73f + 1.7f) * staerke * 0.7f;
            transform.localRotation = startRotation * Quaternion.Euler(nickX, 0f, nickZ);
        }
    }

    // ======================================================================
    // Fisch: schwimmt eine ovale Runde um das Grabenzentrum, Blick in Schwimmrichtung
    // ======================================================================
    // Schwimmt eine feste ovale Runde (Bahn liegt komplett innerhalb der
    // bekannten Grabengeometrie, siehe PlatziereFische) – KEINE Boden-
    // Erkennung mehr zur Laufzeit: die lief gegen das echte Terrain (das
    // keine Vertiefung für den Graben hat) und meldete ständig fälschlich
    // "Ufer voraus", wodurch die Fische ruckartig die Richtung wechselten.
    //
    // Steuert zusätzlich den Sardine-Animator direkt an (Forward/Turn/Up-
    // Blendtree), synchron zur tatsächlichen Bewegung – dadurch wirkt das
    // Schwimmen durchgehend statt wie ein bewegtes Standbild.
    public class FischSchwimmer : MonoBehaviour
    {
        public Vector3 zentrum;
        public float halbGroesse = 15f;   // halbe Kantenlänge des quadratischen Grabenrings
        public float tiefeY  = 0f;
        public float winkel;
        public float geschwindigkeit = 0.2f;   // Radiant pro Sekunde, negativ = andere Richtung

        Animator animator;
        bool hatForward, hatTurn, hatUp;
        float vorherigeRichtung;
        bool ersterFrame = true;

        void Start()
        {
            transform.position = Bahnpunkt(winkel);
            transform.rotation = Quaternion.LookRotation(TangentenRichtung(winkel));

            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.type != AnimatorControllerParameterType.Float) continue;
                    if (p.name == "Forward") hatForward = true;
                    else if (p.name == "Turn") hatTurn = true;
                    else if (p.name == "Up") hatUp = true;
                }
                // Dauerhaft "vorwärts schwimmen" im Blendtree – die Grund-
                // Schwimmbewegung läuft von hier an durchgehend mit
                if (hatForward) animator.SetFloat("Forward", 1f);
            }
        }

        void Update()
        {
            winkel += geschwindigkeit * Time.deltaTime;
            transform.position = Bahnpunkt(winkel);

            Vector3 tangente = TangentenRichtung(winkel);
            if (tangente.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(tangente.normalized), Time.deltaTime * 3f);

            // Turn/Up-Blend an die tatsächliche Kurvenschärfe bzw. das Auf-
            // und-Ab der Bahn koppeln, statt sie auf 0 einzufrieren – so
            // hängen alle Animationsphasen flüssig an der echten Bewegung
            if (animator != null)
            {
                if (hatTurn)
                {
                    float richtungsWinkel = Mathf.Atan2(tangente.x, tangente.z);
                    float drehRate = ersterFrame ? 0f
                        : Mathf.DeltaAngle(vorherigeRichtung * Mathf.Rad2Deg, richtungsWinkel * Mathf.Rad2Deg)
                          / Mathf.Max(Time.deltaTime, 0.0001f);
                    vorherigeRichtung = richtungsWinkel;
                    ersterFrame = false;
                    animator.SetFloat("Turn", Mathf.Clamp(drehRate / 90f, -1f, 1f));
                }
                if (hatUp)
                {
                    float bobTempo = 0.36f * Mathf.Cos(winkel * 3f) * geschwindigkeit;
                    animator.SetFloat("Up", Mathf.Clamp(bobTempo * 5f, -1f, 1f));
                }
            }
        }

        // Tangente per kleiner Winkeldifferenz (robust, unabhängig von der
        // genauen Ableitung der Quadrat-Projektion) – Vorzeichen von
        // geschwindigkeit bestimmt die tatsächliche Laufrichtung
        Vector3 TangentenRichtung(float w)
        {
            const float dw = 0.02f;
            Vector3 a = QuadratPunkt(w - dw);
            Vector3 b = QuadratPunkt(w + dw);
            Vector3 dir = (b - a) * Mathf.Sign(geschwindigkeit);
            return dir.sqrMagnitude > 0.000001f ? dir.normalized : transform.forward;
        }

        // Projiziert einen Kreispunkt auf den Rand eines Quadrats – die Bahn
        // folgt so exakt der quadratischen Form des echten Burggrabens statt
        // eine Ellipse durch die Ecken zu schneiden
        Vector3 QuadratPunkt(float w)
        {
            float c = Mathf.Cos(w), s = Mathf.Sin(w);
            float skala = halbGroesse / Mathf.Max(Mathf.Abs(c), Mathf.Abs(s));
            return new Vector3(zentrum.x + c * skala, 0f, zentrum.z + s * skala);
        }

        Vector3 Bahnpunkt(float w)
        {
            Vector3 p = QuadratPunkt(w);
            // Leichtes Auf und Ab, damit die Bahn lebendiger wirkt
            p.y = tiefeY + Mathf.Sin(w * 3f) * 0.12f;
            return p;
        }
    }

    // ======================================================================
    // Gras: ruhig in Grundstellung, wackelt wenn der Player nahe kommt
    // ======================================================================
    public class GrasWackeln : MonoBehaviour
    {
        public float reaktionsRadius = 2.5f;
        public float maxNeigung      = 14f;   // Grad bei direkter Berührung

        Transform player;
        Quaternion startRotation;
        float staerke;   // aktuelle Wackel-Stärke, weich ein-/ausgeblendet
        float phase;

        void Start()
        {
            startRotation = transform.localRotation;
            phase = Random.Range(0f, 100f);
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        void Update()
        {
            if (player == null) return;

            float distanz = Vector3.Distance(player.position, transform.position);
            float ziel = distanz < reaktionsRadius
                ? 1f - distanz / reaktionsRadius   // je näher, desto stärker
                : 0f;
            staerke = Mathf.MoveTowards(staerke, ziel, Time.deltaTime * 3f);

            if (staerke < 0.001f)
            {
                transform.localRotation = startRotation;
                return;
            }

            // Vom Player weg neigen und dabei zittern
            Vector3 weg = transform.position - player.position;
            weg.y = 0f;
            float zittern = Mathf.Sin(Time.time * 9f + phase) * 0.4f + 0.6f;
            Quaternion neigung = Quaternion.AngleAxis(
                maxNeigung * staerke * zittern,
                Vector3.Cross(Vector3.up, weg.sqrMagnitude > 0.0001f ? weg.normalized : Vector3.forward));
            transform.localRotation = neigung * startRotation;
        }
    }

    // Sitzt auf dem großen Trigger-Volumen der Wassersäule (siehe BaueWasser)
    // und meldet dem Spieler sofort per Sweep-Kollision, wenn er eintaucht –
    // zuverlässig auch bei einem schnellen Sprung von hoch oben, wo die
    // Positions-Prüfung in BurggrabenSchwimmen.Update() das kurze
    // "gerade eingetaucht"-Fenster sonst zwischen zwei Frames verpassen kann.
    public class WasserEintauchTrigger : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            BurggrabenSchwimmen schwimmen = other.GetComponentInParent<BurggrabenSchwimmen>();
            if (schwimmen != null) schwimmen.PruefeSofort();
        }
    }

    // ======================================================================
    // Schwimmen: übernimmt die Steuerung vom PlayerController, sobald der
    // Player tief genug im Wasser ist, und gibt sie beim Auftauchen zurück.
    // Steuerung: WASD in Blickrichtung (nach unten schauen + W = tauchen),
    // Leertaste = auftauchen, Strg = abtauchen, Maus wie an Land.
    // ======================================================================
    public class BurggrabenSchwimmen : MonoBehaviour
    {
        public float wasserY;              // Höhe des Wasserspiegels
        public Vector3 grabenZentrum;
        public float wasserInnenRadius = 12f;   // Wasser gibt es nur im Graben-Ring
        public float wasserAussenRadius = 18f;
        // Der Terrain-Graben ist ein 4-eckiger Ring: quadratische Prüfung
        // (sonst fehlt das Schwimmen in den Ecken des Rings)
        public bool quadratischerBereich;
        public float halbInnen  = 12f;     // halbe Kantenlänge Burginsel
        public float halbAussen = 18f;     // halbe Kantenlänge äußere Grabenkante
        public float geschwindigkeit = 2f;
        public float eintauchSchwelle = 0.04f;  // so tief muss der Player einsinken – SEHR klein, damit Schwimmen sofort beim Reinlaufen startet (0.12 war zu traege: Wasser kam erst nach einem Sprung/tiefem Einsinken)
        public float auftauchSchwelle = 0.15f;
        public float mouseSensitivity = 0.15f;

        [Header("Schwimm-Gefühl")]
        public float beschleunigung = 2.5f;   // wie schnell man im Wasser Fahrt aufnimmt/verliert
        public float auftrieb = 0.4f;         // treibt ohne Eingabe sanft Richtung Oberfläche
        public float wiegenStaerke = 1.2f;    // leichtes Rollen der Kamera beim Schwimmen (Grad)

        PlayerController controller;
        CharacterController cc;
        Transform camTransform;
        bool schwimmt;
        float pitch;
        Vector3 schwimmTempo;    // aktuelle Geschwindigkeit – Wasser ist träge
        bool unterwasserBildAn;
        bool fogVorher;
        Color fogFarbeVorher;
        FogMode fogModusVorher;
        float fogDichteVorher;

        // Für andere Scripts, die wissen müssen, ob gerade die
        // Schwimmsteuerung aktiv ist
        public bool Schwimmt { get { return schwimmt; } }

        void Start()
        {
            controller = GetComponent<PlayerController>();
            cc = GetComponent<CharacterController>();
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) camTransform = cam.transform;
        }

        void Update()
        {
            if (cc == null) return;

            // SICHERHEITSNETZ: Wenn wir NICHT schwimmen, muss die normale
            // Steuerung immer aktiv sein. Falls sie aus irgendeinem Grund
            // deaktiviert haengen blieb (z.B. Ausstieg nicht sauber erkannt),
            // hier wieder anschalten -> "nach dem Rausgehen kann man nichts tun"
            // kann so nicht mehr passieren.
            if (!schwimmt && controller != null && !controller.enabled)
            {
                controller.enabled = true;
                SetzeUnterwasserBild(false);
            }

            float wasserTiefe = wasserY - transform.position.y;

            if (!schwimmt)
            {
                // Plausibilitätsgrenze: eine ECHTE "gerade eingetaucht"-Situation
                // hat nie mehr als ein paar Meter Tiefe. Ist wasserTiefe riesig,
                // stimmt die erkannte Wasserhöhe nicht (z.B. Burg/Graben in der
                // Szene noch nicht gebaut) – dann lieber NICHT einsperren, statt
                // die Steuerung dauerhaft zu deaktivieren. (6 m statt 3 m: der
                // ins Terrain gegrabene Graben kann tiefer sein als der alte
                // berechnete – die Wasserhöhe ist inzwischen ECHT gemessen.)
                if (wasserTiefe > eintauchSchwelle && wasserTiefe < 6f && ImGrabenBereich())
                    StarteSchwimmen();
                return;
            }

            if (wasserTiefe < auftauchSchwelle || !ImGrabenBereich())
            {
                BeendeSchwimmen();
                return;
            }

            SchwimmBewegung(wasserTiefe);
        }

        // Wird vom Wasser-Trigger (WasserEintauchTrigger) sofort beim
        // Eintauchen aufgerufen – zuverlässig auch bei einem schnellen Sprung
        // von hoch oben, den die reine Positions-Prüfung in Update() sonst
        // zwischen zwei Frames verpassen kann (Schwimmen aktivierte sich
        // dann erst beim Aufkommen am Grabenboden). Niedrigere Schwelle als
        // in Update(), weil der Trigger das ECHTE Eintauchen markiert.
        public void PruefeSofort()
        {
            if (schwimmt || cc == null) return;
            float wasserTiefe = wasserY - transform.position.y;
            if (wasserTiefe > 0.02f && wasserTiefe < 6f && ImGrabenBereich())
                StarteSchwimmen();
        }

        // Wasser gibt es nur im Ring des Burggrabens
        bool ImGrabenBereich()
        {
            Vector3 abstand = transform.position - grabenZentrum;
            abstand.y = 0f;

            if (quadratischerBereich)
            {
                // Quadratischer Ring: Abstand in "Schachbrett-Metrik"
                // (größte Achsen-Entfernung) statt Luftlinie
                float kante = Mathf.Max(Mathf.Abs(abstand.x), Mathf.Abs(abstand.z));
                return kante >= halbInnen - 0.5f && kante <= halbAussen + 0.5f;
            }

            float entfernung = abstand.magnitude;
            return entfernung >= wasserInnenRadius - 0.5f && entfernung <= wasserAussenRadius + 0.5f;
        }

        void StarteSchwimmen()
        {
            schwimmt = true;
            schwimmTempo = Vector3.zero;   // sanft Fahrt aufnehmen statt lossausen
            if (controller != null) controller.enabled = false;
            if (camTransform != null)
            {
                // Aktuelle Kamera-Neigung übernehmen, damit nichts springt
                pitch = camTransform.localEulerAngles.x;
                if (pitch > 180f) pitch -= 360f;
            }
        }

        void BeendeSchwimmen()
        {
            schwimmt = false;
            SetzeUnterwasserBild(false);
            if (camTransform != null)
                camTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);   // Wiegen zurücksetzen
            if (controller != null)
            {
                // Internen Pitch des PlayerControllers angleichen, sonst springt
                // die Kamera beim ersten Maus-Schwenk nach dem Auftauchen
                var feld = typeof(PlayerController).GetField("pitch",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (feld != null) feld.SetValue(controller, pitch);
                controller.enabled = true;
            }
        }

        void OnDisable()
        {
            // Sicherheitsnetz: Nebel nie "hängen lassen"
            SetzeUnterwasserBild(false);
        }

        // Blauer Unterwasser-Nebel, sobald die Kamera unter der Oberfläche ist
        void SetzeUnterwasserBild(bool an)
        {
            if (an == unterwasserBildAn) return;
            unterwasserBildAn = an;

            if (an)
            {
                fogVorher       = RenderSettings.fog;
                fogFarbeVorher  = RenderSettings.fogColor;
                fogModusVorher  = RenderSettings.fogMode;
                fogDichteVorher = RenderSettings.fogDensity;

                RenderSettings.fog        = true;
                RenderSettings.fogMode    = FogMode.Exponential;
                RenderSettings.fogColor   = new Color(0.10f, 0.32f, 0.45f);
                RenderSettings.fogDensity = 0.12f;
            }
            else
            {
                RenderSettings.fog        = fogVorher;
                RenderSettings.fogColor   = fogFarbeVorher;
                RenderSettings.fogMode    = fogModusVorher;
                RenderSettings.fogDensity = fogDichteVorher;
            }
        }

        void SchwimmBewegung(float wasserTiefe)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // Maus-Blick wie im PlayerController: im Kampfmodus frei (Cursor ist
            // gesperrt, Linksklick gehört dann der Pistole), sonst nur bei
            // gehaltener linker Taste. Ohne diese Unterscheidung musste man beim
            // Schwimmen die linke Maustaste zum Umschauen HALTEN – dieselbe
            // Taste, die auch schießt – und kam im Wasser praktisch nicht zum
            // Schuss ("kann man im Wasser nicht schießen").
            if (camTransform != null && (PlayerController.kampfModus || mouse.leftButton.isPressed))
            {
                Vector2 look = mouse.delta.ReadValue();
                transform.Rotate(0f, look.x * mouseSensitivity, 0f);
                pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -80f, 80f);
            }

            float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

            // In Blickrichtung schwimmen (inkl. Neigung), Leertaste hoch, Strg runter
            Vector3 blick = camTransform != null ? camTransform.forward : transform.forward;
            Vector3 richtung = blick * v + transform.right * h;
            if (kb.spaceKey.isPressed)    richtung += Vector3.up;
            if (kb.leftCtrlKey.isPressed) richtung += Vector3.down;
            if (richtung.sqrMagnitude > 1f) richtung.Normalize();

            // Wasser ist träge: sanft beschleunigen und ausgleiten statt
            // sofort loszulaufen und stehenzubleiben
            Vector3 wunschTempo = richtung * geschwindigkeit;

            // Ohne Eingabe treibt man langsam Richtung Oberfläche (Auftrieb)
            if (richtung.sqrMagnitude < 0.01f && wasserTiefe > auftauchSchwelle + 0.2f)
                wunschTempo += Vector3.up * auftrieb;

            float angleich = 1f - Mathf.Exp(-beschleunigung * Time.deltaTime);
            schwimmTempo = Vector3.Lerp(schwimmTempo, wunschTempo, angleich);

            // Nicht über die Wasseroberfläche hinaus schwimmen – knapp darunter
            // bremsen, das Auftauchen übernimmt dann die Schwelle oben
            if (schwimmTempo.y > 0f && wasserTiefe < auftauchSchwelle + 0.1f)
                schwimmTempo.y = 0f;

            cc.Move(schwimmTempo * Time.deltaTime);

            // Kamera wiegt sich leicht mit der Schwimmbewegung, und unter der
            // Oberfläche gibt es blauen Unterwasser-Nebel
            if (camTransform != null)
            {
                float fahrt = Mathf.Clamp01(schwimmTempo.magnitude / Mathf.Max(geschwindigkeit, 0.01f));
                float rollen = Mathf.Sin(Time.time * 1.6f) * wiegenStaerke * (0.4f + 0.6f * fahrt);
                camTransform.localRotation = Quaternion.Euler(pitch, 0f, rollen);

                SetzeUnterwasserBild(camTransform.position.y < wasserY - 0.02f);
            }
        }
    }
}
