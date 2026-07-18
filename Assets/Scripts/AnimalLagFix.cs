using UnityEngine;
using System.Collections;

namespace NeonCatch
{
    // Lag-freies Tier-Verhalten komplett über eine Coroutine, KEIN Update().
    // Die Tiere sind DAUERHAFT in Bewegung (kein eingefrorenes Herumstehen):
    // nach jedem Laufabschnitt schließt nahtlos der nächste an, nur beim
    // Eingekesselt-Sein gibt es eine kurze Steh-Pause. Kein NavMeshAgent,
    // nur transform.position. Raycasts nur beim Richtungswechsel (alle paar
    // Sekunden) plus ein günstiger Voraus-Check alle 0.5 s, nie pro Frame.
    public class AnimalLagFix : MonoBehaviour
    {
        [Header("Zeiten")]
        public float walkTimeMin = 5f;   // läuft 5-8 Sekunden in eine zufällige Richtung
        public float walkTimeMax = 8f;

        [Header("Bewegung")]
        public float walkSpeed = 2f;
        // Bei 2 m/s und bis zu 8 Sekunden Laufzeit reichen 5 m nicht aus, um die
        // volle Zeit auszunutzen – hier hoch genug, damit die Gehzeit (nicht die
        // Strecke) der eigentliche Taktgeber ist. Eine Wand im Weg verkürzt die
        // Strecke ohnehin automatisch (siehe WaehleRichtung).
        public float maxStrecke = 14f;

        [Header("Flucht (rennt weg, wenn der Spieler zu nahe kommt)")]
        public float fluchtRadius = 4f;
        public float fluchtTempoFaktor = 2.5f;

        [Header("Heimat (Tier bleibt in der Nähe seines Spawn-Platzes)")]
        public float maxHeimAbstand = 15f;

        [Header("Boden")]
        // An: Bodenhöhe per Physik-Raycast statt Terrain-Heightmap. Nötig für
        // Tiere, die auf einem Mesh ÜBER dem Terrain leben (z.B. Hunde/Katzen
        // auf dem Burghof-Boden) – die Heightmap kennt nur das Terrain darunter
        // und würde sie durch den Hof-Boden hindurch absinken lassen.
        public bool bodenPerRaycast = false;

        // Alle aktiven Tiere: Tiere haben (aus Performance-Gründen) keine
        // Collider, der Hindernis-SphereCast sieht sie also nicht – über
        // diese Liste weichen sie einander trotzdem aus
        static readonly System.Collections.Generic.List<AnimalLagFix> alleTiere =
            new System.Collections.Generic.List<AnimalLagFix>();

        Animator animator;
        bool hatVert;           // ithappy-Controller: Vert 0 = stehen, 1 = laufen
        bool hatState;          // ithappy-Controller: State 0 = gehen, 1 = rennen
        string ersatzParameter; // Fallback für andere Controller (Speed etc.)
        float vertWert;         // aktueller Blend-Wert, wird weich überblendet
        Vector3 richtung;
        float strecke;          // freie Weglänge aus dem Wand-Raycast
        float bodenVersatz;     // Ankerpunkt-Korrektur: Füße exakt auf den Boden
        bool versatzGemessen;
        Vector3 heim;           // Spawn-Platz: das Tier entfernt sich nie weit davon
        Transform player;

        void Awake()
        {

            // Schatten aus – Skinned Meshes mit Schatten sind ein großer Lag-Faktor
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Eigener Layer "Animal", falls im Projekt angelegt (Project Settings >
            // Tags and Layers) – dann kollidieren Tiere nicht mit allem
            int layer = LayerMask.NameToLayer("Animal");
            if (layer >= 0)
                foreach (Transform t in GetComponentsInChildren<Transform>())
                    t.gameObject.layer = layer;

            // Funktioniert mit und ohne Animator
            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                // Statt den Animator hart abzuschalten: Unity kümmert sich selbst
                // darum, unsichtbare Tiere nicht zu animieren (spart genauso Zeit,
                // aber die Idle-Animation läuft, wenn man das Tier ansieht)
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

                foreach (AnimatorControllerParameter param in animator.parameters)
                {
                    if (param.type != AnimatorControllerParameterType.Float) continue;
                    if (param.name == "Vert")  hatVert = true;
                    else if (param.name == "State") hatState = true;
                    else if (param.name == "Speed" || param.name == "MoveSpeed")
                        ersatzParameter = param.name;
                }

                // ithappy-Tiere: State 0 = Geh-Animation (1 wäre Rennen)
                if (hatState) animator.SetFloat("State", 0f);
                SetzeBlend(0f);
            }
        }

        // Boden-Höhe an einer XZ-Position, inklusive Fuß-Versatz. Nutzt
        // Terrain.SampleHeight statt Raycast, wenn ein Terrain vorhanden ist –
        // das ist ein billiger Heightmap-Lookup (keine Physik-Abfrage) und
        // darum auch JEDEN FRAME günstig genug. Dadurch folgt das Tier dem
        // Boden durchgehend statt zwischen zwei Punkten zu interpolieren –
        // genau das Interpolieren verursachte das "Teleportieren in den
        // Himmel" am Ende eines Laufabschnitts, wenn die Strecke über
        // unebenes Terrain führte.
        float BodenYMitVersatz(Vector3 xzPos)
        {
            if (bodenPerRaycast)
            {
                // Knapp über der aktuellen Tier-Höhe starten (nicht von weit
                // oben): läuft das Tier unter einem Torbogen durch, soll der
                // Strahl den Hof-Boden treffen, nicht die Bogen-Oberseite
                Vector3 start = new Vector3(xzPos.x, transform.position.y + 1f, xzPos.z);
                if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, 8f,
                        ~(1 << 4), QueryTriggerInteraction.Ignore))
                    return hit.point.y + bodenVersatz;
                return transform.position.y;   // kein Boden gefunden: Höhe halten
            }

            Terrain terrain = BurggrabenMittelalter.AktivesTerrain;
            float y = terrain != null
                ? terrain.SampleHeight(xzPos) + terrain.transform.position.y
                : BurggrabenMittelalter.BodenHoehe(xzPos);
            return y + bodenVersatz;
        }

        // Blend-Wert an den Controller geben: Vert (ithappy) oder Fallback-Parameter
        void SetzeBlend(float wert)
        {
            vertWert = wert;
            if (animator == null) return;
            // Wichtig: IsNullOrEmpty statt != null – nach einem Script-Reload
            // im Play-Modus macht Unity aus null-Strings leere Strings
            if (hatVert) animator.SetFloat("Vert", wert);
            else if (!string.IsNullOrEmpty(ersatzParameter)) animator.SetFloat(ersatzParameter, wert);
            else if (hatState) animator.SetFloat("State", wert);
        }

        // OnEnable/OnDisable: kompatibel mit dem Despawn-System (SetActive) –
        // beim Aufwachen startet der Zyklus automatisch neu
        void OnEnable()
        {
            alleTiere.Add(this);

            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;

            StartCoroutine(IdleWalkZyklus());
        }

        void OnDisable()
        {
            alleTiere.Remove(this);
        }

        // Steht ein anderes Tier dicht voraus? Tiere haben keine Collider,
        // der Hindernis-SphereCast sieht sie deshalb nicht – diese Prüfung
        // verhindert, dass Tiere durcheinander hindurchlaufen.
        bool TierVoraus(Vector3 laufRichtung, float abstand)
        {
            foreach (AnimalLagFix anderes in alleTiere)
            {
                if (anderes == this) continue;
                Vector3 zuAnderem = anderes.transform.position - transform.position;
                zuAnderem.y = 0f;
                if (zuAnderem.sqrMagnitude > abstand * abstand) continue;
                if (zuAnderem.sqrMagnitude < 0.0001f) return true;
                // Nur zählen, wenn das andere Tier VORAUS liegt – Tiere hinter
                // einem blockieren den Weg nicht
                if (Vector3.Dot(laufRichtung, zuAnderem.normalized) > 0.3f) return true;
            }
            return false;
        }

        IEnumerator IdleWalkZyklus()
        {
            if (!versatzGemessen)
            {
                versatzGemessen = true;

                // WICHTIGER FIX gegen Schweben/Einsinken: cullingMode steht auf
                // CullCompletely (Performance), d.h. der Animator läuft NICHT,
                // solange keine Kamera das Tier sieht. Spawnt ein Tier außerhalb
                // des Bildausschnitts, wurde die Fuß-Position bisher aus der
                // eingefrorenen T-Pose gemessen statt aus der echten Stehhaltung –
                // daher schwebten/versanken Tiere ohne erkennbares Muster.
                // Für die Messung kurz erzwingen, dass die Pose wirklich berechnet
                // wird, danach zurück auf die sparsame Einstellung.
                if (animator != null)
                {
                    var vorherigerModus = animator.cullingMode;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Update(0f);
                    yield return null;   // ein Frame, damit die Pose sicher greift
                    animator.Update(0f);
                    animator.cullingMode = vorherigerModus;
                }
                else
                {
                    yield return null;
                }

                float tiefsterPunkt = float.MaxValue;
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                    tiefsterPunkt = Mathf.Min(tiefsterPunkt, r.bounds.min.y);
                if (tiefsterPunkt < float.MaxValue)
                    bodenVersatz = Mathf.Clamp(transform.position.y - tiefsterPunkt, -2f, 2f);
                heim = transform.position;   // Spawn-Platz merken
            }

            // Sauber auf den Boden setzen (gegen schwebende/eingesunkene Tiere)
            Vector3 start = transform.position;
            start.y = BodenYMitVersatz(start);
            transform.position = start;

            var kurzWarten = new WaitForSeconds(0.5f);

            // Dauerhaft in Bewegung: kein Idle-Stehen mehr zwischen den
            // Laufabschnitten – ein stehendes, "eingefrorenes" Tier wirkt
            // unecht. Nach jedem Abschnitt wird nahtlos die nächste Richtung
            // gewählt; nur wenn gar kein freier Weg existiert (eingekesselt),
            // steht das Tier kurz und probiert es gleich wieder.
            while (true)
            {
                if (PlayerZuNah())
                {
                    yield return Fliehe();
                    continue;
                }

                // ---- Richtungswechsel: hier passieren die EINZIGEN Raycasts ----
                // Zu weit vom Spawn-Platz entfernt? Dann Richtung Heimat laufen,
                // damit kein Tier quer über die Map wandert
                Vector3 zurHeim = heim - transform.position;
                zurHeim.y = 0f;
                float wunschWinkel = zurHeim.magnitude > maxHeimAbstand
                    ? Mathf.Atan2(zurHeim.x, zurHeim.z) * Mathf.Rad2Deg
                    : Random.Range(0f, 360f);

                if (!WaehleRichtung(wunschWinkel))
                {
                    // Eingekesselt: kurz stehen bleiben, dann neuer Versuch
                    SetzeBlend(0f);
                    yield return kurzWarten;
                    continue;
                }

                yield return Laufe(walkSpeed, Random.Range(walkTimeMin, walkTimeMax), 0f);
            }
        }

        // Läuft in die aktuelle Richtung; blendWert 0..1 wird weich angefahren
        // (bei ithappy: Vert 1 = Füße bewegen sich, State 0 = gehen / 1 = rennen)
        IEnumerator Laufe(float tempo, float dauer, float rennWert)
        {
            if (hatState && animator != null) animator.SetFloat("State", rennWert);

            // Weich in die neue Richtung eindrehen statt hart zu springen –
            // die Bewegung geht dabei nahtlos weiter (wirkt natürlicher)
            Quaternion zielRotation = Quaternion.LookRotation(richtung);

            float zeit = 0f, gelaufen = 0f, pruefTimer = 0f;
            while (zeit < dauer && gelaufen < strecke)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, zielRotation,
                    Time.deltaTime * 4f);

                float schritt = tempo * Time.deltaTime;
                Vector3 pos = transform.position + richtung * schritt;
                gelaufen += schritt;
                zeit += Time.deltaTime;
                // Jeden Frame die echte Boden-/Terrain-Höhe übernehmen –
                // günstig (Terrain.SampleHeight), verhindert Schweben/Springen
                pos.y = BodenYMitVersatz(pos);
                transform.position = pos;

                // Geh-Animation weich einblenden (Füße bewegen sich!)
                SetzeBlend(Mathf.MoveTowards(vertWert, 1f, Time.deltaTime * 4f));

                // Alle 0.5 s prüfen: Spieler zu nah (nur beim normalen Gehen),
                // anderes Tier direkt voraus oder gesperrtes Gelände (Steilhang,
                // Map-Rand, Sperrzone) voraus – dann Abschnitt sofort beenden
                // und eine neue Richtung wählen, statt hineinzulaufen
                pruefTimer += Time.deltaTime;
                if (pruefTimer >= 0.5f)
                {
                    pruefTimer = 0f;
                    if (rennWert < 0.5f && PlayerZuNah()) break;
                    if (TierVoraus(richtung, 1.4f)) break;
                    if (BurggrabenMittelalter.IstGesperrt(transform.position + richtung * 1.5f, !bodenPerRaycast)) break;
                }
                yield return null;
            }

            // Kein Abblenden auf "Stehen" mehr: der nächste Laufabschnitt
            // schließt nahtlos an, die Geh-Animation läuft einfach weiter
            if (hatState && animator != null) animator.SetFloat("State", 0f);
        }

        // ---- FLUCHT: rennt vom Spieler weg (Renn-Animation!) ----
        IEnumerator Fliehe()
        {
            if (player == null) yield break;

            Vector3 weg = transform.position - player.position;
            weg.y = 0f;
            if (weg.sqrMagnitude < 0.001f) weg = Vector3.forward;
            float basisWinkel = Mathf.Atan2(weg.x, weg.z) * Mathf.Rad2Deg;

            // Fluchtrichtung: direkt weg vom Spieler, sonst leicht seitlich versetzt
            float[] ausweichWinkel = { 0f, 35f, -35f, 70f, -70f };
            bool gefunden = false;
            foreach (float offset in ausweichWinkel)
            {
                if (WaehleRichtung(basisWinkel + offset)) { gefunden = true; break; }
            }
            if (!gefunden) yield break;   // eingekesselt: bleibt stehen

            yield return Laufe(walkSpeed * fluchtTempoFaktor, 3f, 1f);
        }

        bool PlayerZuNah()
        {
            if (player == null) return false;
            Vector3 abstand = player.position - transform.position;
            abstand.y = 0f;
            return abstand.sqrMagnitude < fluchtRadius * fluchtRadius;
        }

        // Freie Richtung um den Wunsch-Winkel suchen: nur beim Richtungswechsel,
        // nie pro Frame. Ein SphereCast (statt eines dünnen Strahls) erkennt
        // auch Objekte, die die Tier-Körperbreite streifen würden – "nicht
        // durch Objekte gehen". Zusätzlich wird die Mitte der Strecke auf
        // Steilhang/Terrain-Rand geprüft, nicht nur das Ziel selbst, damit
        // kein Bein der Strecke über einen Hügel führt, den beide Enden zwar
        // vermeiden, der aber dazwischen liegt.
        bool WaehleRichtung(float wunschWinkel)
        {
            const float koerperRadius = 0.6f;   // etwas breiter als ein Tierkörper, gegen Streifen an Bäumen/Häusern

            for (int versuch = 0; versuch < 6; versuch++)
            {
                float winkel = versuch == 0 ? wunschWinkel : wunschWinkel + Random.Range(-90f, 90f);
                Vector3 kandidat = Quaternion.Euler(0f, winkel, 0f) * Vector3.forward;

                // Objekt im Weg? Dann nur bis kurz davor laufen
                float frei = maxStrecke;
                if (Physics.SphereCast(transform.position + Vector3.up * 0.3f, koerperRadius, kandidat,
                        out RaycastHit hit, maxStrecke, ~(1 << 4), QueryTriggerInteraction.Ignore))
                    frei = hit.distance - 0.6f;
                if (frei < 1f) continue;   // zu eng, andere Richtung

                // Nicht aus der Map, auf Hügel/Steilhang (z.B. die steile
                // Rand-Wiese am Terrain-Ende) oder in Sperrzonen laufen – an
                // VIER Punkten entlang der Strecke prüfen, damit kein Teil-
                // stück über gesperrtes Gelände führt
                bool gesperrt = false;
                for (float anteil = 0.25f; anteil <= 1.001f; anteil += 0.25f)
                {
                    if (BurggrabenMittelalter.IstGesperrt(transform.position + kandidat * (frei * anteil), !bodenPerRaycast))
                    {
                        gesperrt = true;
                        break;
                    }
                }
                if (gesperrt) continue;

                // Kein Kurs direkt auf ein anderes Tier (die haben keine
                // Collider und wären für den SphereCast unsichtbar)
                if (TierVoraus(kandidat, 2f)) continue;

                richtung = kandidat;
                strecke = frei;
                return true;
            }
            return false;
        }
    }
}
