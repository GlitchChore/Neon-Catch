using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace NeonCatch
{
    // ======================================================================
    // KAMPFMODUS (jeder gegen jeden):
    //  - START-Knopf mitten am Bildschirm – Klick, und es geht los
    //  - Du bekommst eine Waffe (Blaster) und 3 Leben (rote Herzen)
    //  - 4 Bots (Synty-Sidekick-Figuren) spawnen verteilt, ebenfalls je
    //    3 Leben und dieselbe Waffe; sie kämpfen gegen DICH und GEGENEINANDER
    //  - Bots treffen absichtlich nicht immer (Streuung), laufen umher,
    //    weichen Wänden aus und suchen sich das nächste sichtbare Ziel
    //  - Geschossen wird mit Farbkugeln, die dauerhafte Farbkleckse hinterlassen;
    //    3 Schuss Munition, die wie in Brawl Stars einzeln nachladen
    //  - Wer alle Leben verliert, ist raus; Letzter gewinnt
    // Startet sich in jeder Szene selbst.
    // ======================================================================
    public static class KampfStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            // In der FARBMIMIK-Szene (erkennbar am LobbyManager) NICHT starten -
            // das schwarze Startmenue wuerde dort das komplette Spiel verdecken
            if (Object.FindAnyObjectByType<LobbyManager>() != null) return;

            var go = new GameObject("Kampf_Modus");
            go.AddComponent<KampfModus>();
        }
    }

    public class KampfModus : MonoBehaviour
    {
        public static KampfModus Instanz { get; private set; }

        [Header("Regeln")]
        public int botAnzahl = 4;
        public int lebenProKaempfer = 3;   // 3 Leben = 3 rote Herzen

        bool laeuft;
        int spielerLeben;
        string endText = "";
        float trefferBlitz;   // roter Bildschirm-Blitz, wenn DU getroffen wirst

        readonly List<KampfBot> bots = new List<KampfBot>();
        int botsGesamt;   // tatsächlich gespawnte Bots dieser Runde – für die Platzierungs-Anzeige
        Transform spieler;
        PistolenSchuetze waffe;
        GUIStyle knopfStil, textStil, herzStil, steuerungStil;
        bool zeigeSteuerung;
        bool zeigeOnlineBeitritt;
        bool zeigeHilfe;
        bool zeigeModusWahl;
        bool zeigeSoloWahl;
        string hilfeInhalt = "";
        bool hilfeZurueckZuBeitritt;
        Texture2D menueHintergrund;
        bool hintergrundGesucht;
        string nameEingabe;
        bool onlineLiefGerade, onlineWarVerbunden;
        static Texture2D kartenTex;
        GUIStyle kartenStil, kartenTitelStil;
        string onlineIp = "";
        string onlineCode = "";
        string onlineFreundName = "";

        // Spawn-Position bei der Burg (wie am Anfang) – für den Reset-Knopf
        Vector3 spielerStartPos;
        Quaternion spielerStartRot;
        bool startPosGemerkt;

        public bool KampfLaeuft => laeuft;
        public int SpielerLeben => spielerLeben;
        public Transform Spieler => spieler;
        public List<KampfBot> Bots => bots;

        void Awake()
        {
            Instanz = this;
            nameEingabe = SpielerProfil.Name;
        }

        // Spawn-Position merken, sobald der Spieler auffindbar ist
        void MerkeStartPosition()
        {
            if (startPosGemerkt) return;
            GameObject spielerObj = GameObject.FindGameObjectWithTag("Player");
            if (spielerObj == null) return;
            spielerStartPos = spielerObj.transform.position;
            spielerStartRot = spielerObj.transform.rotation;
            startPosGemerkt = true;
        }

        // Spieler exakt auf die ursprüngliche Position setzen (Burg-Spawn wie
        // am Anfang) – für RESET (R-Taste) UND für einen neuen Kampf über
        // START, damit jede Runde am selben Fleck beginnt statt dort, wo die
        // vorige Runde zufällig endete/verlassen wurde.
        void SetzeSpielerAufStartPosition(GameObject spielerObj)
        {
            if (spielerObj == null || !startPosGemerkt) return;
            var cc = spielerObj.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            spielerObj.transform.SetPositionAndRotation(spielerStartPos, spielerStartRot);
            if (cc != null) cc.enabled = true;
        }

        // RESET: Kampf beenden, Spieler zurück zum Burg-Spawn wie am Anfang,
        // Hütten-Sperre aufheben – alles wieder wie bei Spielbeginn
        void MacheReset()
        {
            if (laeuft) KampfEnde(false);
            endText = "";
            spielerLeben = lebenProKaempfer;
            FalltuerTimer.Zuruecksetzen();

            SetzeSpielerAufStartPosition(GameObject.FindGameObjectWithTag("Player"));
        }

        void StarteKampf()
        {
            GameObject spielerObj = GameObject.FindGameObjectWithTag("Player");
            if (spielerObj == null)
            {
                endText = "Kein Spieler gefunden!";
                return;
            }
            spieler = spielerObj.transform;

            endText = "";
            spielerLeben = lebenProKaempfer;
            laeuft = true;

            // Jede neue Runde beginnt exakt an der ursprünglichen Spawn-
            // Position – egal, wo die letzte Runde beendet/verlassen wurde
            SetzeSpielerAufStartPosition(spielerObj);
            FalltuerTimer.Zuruecksetzen();
            Farbschuss.AlleEntfernen();   // sauberes Schlachtfeld, keine Kleckse der vorigen Runde

            // Pistole für den Spieler + Kampf-Steuerung (freier Blick,
            // Linksklick = Schuss), Cursor sperren
            PlayerController.kampfModus = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            waffe = spielerObj.GetComponent<PistolenSchuetze>();
            if (waffe == null) waffe = spielerObj.AddComponent<PistolenSchuetze>();
            waffe.Aktiviere();

            // 4 Bots verteilt spawnen
            for (int i = 0; i < botAnzahl; i++)
            {
                Vector3? platz = SucheSpawnPlatz(i, botAnzahl);
                if (!platz.HasValue) continue;

                GameObject prefab = Resources.Load<GameObject>("KI/Bot_" + (i % 4 + 1));
                GameObject koerper;
                if (prefab != null)
                {
                    koerper = Instantiate(prefab, platz.Value, Quaternion.identity);
                    // Sidekick-Figuren sind ~1.8 m groß – auf Weltmaß bringen
                    // (auf Wunsch größer als die alten 0.55 m)
                    SkaliereAufHoehe(koerper, 0.75f);
                }
                else
                {
                    koerper = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    koerper.transform.position = platz.Value + Vector3.up * 0.375f;
                    koerper.transform.localScale = new Vector3(0.27f, 0.375f, 0.27f);
                    Destroy(koerper.GetComponent<Collider>());
                }
                koerper.name = "Bot_" + (i + 1);

                var bot = koerper.AddComponent<KampfBot>();
                bot.leben = lebenProKaempfer;
                bots.Add(bot);
            }
            botsGesamt = bots.Count;
        }

        static void SkaliereAufHoehe(GameObject go, float zielHoehe)
        {
            float hoehe = 0f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                hoehe = Mathf.Max(hoehe, r.bounds.size.y);
            if (hoehe > 0.01f)
                go.transform.localScale *= zielHoehe / hoehe;
        }

        // Spawn-Plätze rund um die Map verteilt (jeder Bot im eigenen Sektor)
        Vector3? SucheSpawnPlatz(int index, int gesamt)
        {
            Vector3 mitte = spieler != null ? spieler.position : Vector3.zero;
            Terrain terrain = BurggrabenMittelalter.AktivesTerrain;
            if (terrain != null)
                mitte = terrain.transform.position + terrain.terrainData.size * 0.5f;

            float sektor = 360f / gesamt;
            for (int versuch = 0; versuch < 40; versuch++)
            {
                float winkel = (index * sektor + Random.Range(-sektor * 0.4f, sektor * 0.4f)) * Mathf.Deg2Rad;
                float radius = Random.Range(10f, 25f);
                Vector3 kandidat = mitte + new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)) * radius;

                if (BurggrabenMittelalter.IstGesperrt(kandidat)) continue;
                kandidat.y = BurggrabenMittelalter.BodenHoehe(kandidat);
                return kandidat;
            }
            return null;
        }

        // ---- Treffer-Meldungen ----

        // Gibt true zurück, wenn dieser Treffer tödlich war (für den Schützen-
        // Bot: Sieges-Animation auslösen)
        public bool SpielerGetroffen()
        {
            if (!laeuft) return false;
            if (waffe != null && waffe.IstUnverwundbar) return false;   // gerade mitten im Ausweich-Dash
            spielerLeben--;
            trefferBlitz = 1f;
            if (spielerLeben <= 0)
            {
                KampfEnde(false);   // einmal tot ist man tot – die Runde endet sofort
                return true;
            }
            return false;
        }

        public void BotEliminiert(KampfBot bot)
        {
            bots.Remove(bot);
            if (laeuft && bots.Count == 0)
                KampfEnde(true);
        }

        void KampfEnde(bool gewonnen)
        {
            // Platzierung VOR dem Einsammeln merken: noch lebende Bots waren
            // beim eigenen Tod alle noch im Rennen, also besser platziert
            int platz = bots.Count + 1;
            laeuft = false;

            if (gewonnen)
                endText = "GEWONNEN! Du bist der Letzte!";
            else if (spielerLeben <= 0)
                endText = "GESTORBEN – Platz " + platz + " von " + (botsGesamt + 1) +
                           ". Drücke START für eine neue Runde.";
            else
                endText = "Drücke START für eine neue Runde.";

            // Restliche Bots einsammeln
            foreach (KampfBot bot in bots)
                if (bot != null) Destroy(bot.gameObject);
            bots.Clear();

            if (waffe != null) waffe.Deaktiviere();
            PlayerController.kampfModus = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Meldung im Startmenue anzeigen (z.B. Fehlerhinweise).</summary>
        public void ZeigeMeldung(string text)
        {
            endText = text;
        }

        void Update()
        {
            // Waehrend einer Online-Runde uebernimmt KampfNetzwerk komplett -
            // aber ESC funktioniert IMMER als Notausgang zurueck ins Menue
            if (NetworkClient.active)
            {
                onlineLiefGerade = true;
                if (NetworkClient.isConnected && NetworkClient.localPlayer != null)
                    onlineWarVerbunden = true;

                var tastatur = Keyboard.current;
                if (tastatur != null && tastatur.escapeKey.wasPressedThisFrame)
                    KampfOnline.Verlasse();
                return;
            }

            // Gerade aus einem Online-Versuch zurueckgekommen?
            if (onlineLiefGerade)
            {
                onlineLiefGerade = false;
                endText = onlineWarVerbunden
                    ? "Drücke START für eine neue Runde."
                    : "Das ist nicht korrekt! IP oder Room-Code prüfen und nochmal versuchen.";
                onlineWarVerbunden = false;
            }

            MerkeStartPosition();

            if (trefferBlitz > 0f) trefferBlitz -= Time.deltaTime * 2.5f;

            var kb = Keyboard.current;
            if (kb == null) return;

            // Escape beendet den Kampf (Aufgeben)
            if (laeuft && kb.escapeKey.wasPressedThisFrame)
                KampfEnde(false);

            // R = Reset, funktioniert IMMER – auch mitten im Kampf, wenn der
            // Mauszeiger gesperrt ist und der Knopf nicht klickbar wäre
            if (kb.rKey.wasPressedThisFrame)
                MacheReset();
        }

        // Text OHNE Kästchen, mit Schatten – passt bei jeder Länge
        void TextObenAmRand(string text, float yAnteil, float groesseAnteil, TextAnchor ausrichtung)
        {
            if (textStil == null)
            {
                textStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }
            textStil.alignment = ausrichtung;
            textStil.fontSize = Mathf.RoundToInt(Screen.height * groesseAnteil);

            var rect = new Rect(10f, Screen.height * yAnteil, Screen.width - 20f, Screen.height * 0.08f);
            // Heller Schatten + dunkle Schrift - gut lesbar auf dem hellen Menue
            textStil.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, textStil);
            textStil.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
            GUI.Label(rect, text, textStil);
        }

        const string steuerungText =
            "W A S D — Laufen\n" +
            "Maus — Umschauen\n" +
            "Leertaste — Springen / nach oben schwimmen\n" +
            "Strg links — nach unten schwimmen\n" +
            "Leiter: mit W reingehen, mit S runter\n" +
            "\n" +
            "Linksklick — Schießen: Farbkugel, 3 Schuss, einzeln nachladen\n" +
            "Shift links — Ausweichrolle, alle 10 Sekunden, macht kurz unverwundbar\n" +
            "R — Reset, jederzeit\n" +
            "ESC — Aufgeben";

        // Online beitreten: IP + Room-Code eintippen (IMGUI, passend zum Startmenü)
        void ZeichneOnlineBeitritt(float sw, float sh)
        {
            if (textStil == null)
                textStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            textStil.alignment = TextAnchor.MiddleLeft;
            textStil.fontSize = Mathf.RoundToInt(sh * 0.03f);
            textStil.normal.textColor = new Color(0.08f, 0.08f, 0.1f);

            float feldBreite = sw * 0.3f;
            float x = sw * 0.5f - feldBreite * 0.5f;

            // Gespeicherte Freunde: Klick auf den Namen fuellt die IP aus
            var freunde = FreundeListe.Alle();
            if (freunde.Count > 0)
            {
                textStil.alignment = TextAnchor.MiddleCenter;
                textStil.fontSize = Mathf.RoundToInt(sh * 0.022f);
                GUI.Label(new Rect(x, sh * 0.12f, feldBreite, sh * 0.04f),
                          "Mit wem spielen? (Klick = IP wird eingefügt)", textStil);
                textStil.alignment = TextAnchor.MiddleLeft;

                knopfStil.fontSize = Mathf.RoundToInt(sh * 0.02f);
                int anzahl = Mathf.Min(freunde.Count, 4);
                float knopfBreite = sw * 0.075f;
                for (int i = 0; i < anzahl; i++)
                {
                    float kx = sw * 0.5f + (i - (anzahl - 1) * 0.5f) * (knopfBreite + sw * 0.008f) - knopfBreite * 0.5f;
                    if (GUI.Button(new Rect(kx, sh * 0.165f, knopfBreite, sh * 0.045f), freunde[i][0], knopfStil))
                    {
                        onlineIp = freunde[i][1];
                        onlineFreundName = "";
                    }
                }
            }

            textStil.fontSize = Mathf.RoundToInt(sh * 0.03f);
            GUI.Label(new Rect(x, sh * 0.23f, feldBreite, sh * 0.05f), "IP des Hosts:", textStil);
            onlineIp = GUI.TextField(new Rect(x, sh * 0.285f, feldBreite, sh * 0.05f), onlineIp);

            GUI.Label(new Rect(x, sh * 0.35f, feldBreite, sh * 0.05f), "Beitritts-Code:", textStil);
            onlineCode = GUI.TextField(new Rect(x, sh * 0.405f, feldBreite, sh * 0.05f), onlineCode);

            textStil.fontSize = Mathf.RoundToInt(sh * 0.022f);
            GUI.Label(new Rect(x, sh * 0.47f, feldBreite, sh * 0.04f),
                      "Zum Merken - Name des Freundes:", textStil);
            onlineFreundName = GUI.TextField(new Rect(x, sh * 0.515f, feldBreite, sh * 0.045f), onlineFreundName);

            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.035f);
            if (GUI.Button(new Rect(sw * 0.5f - sw * 0.11f, sh * 0.585f, sw * 0.22f, sh * 0.07f),
                    "VERBINDEN", knopfStil))
            {
                // Freund merken: beim naechsten Mal reicht ein Klick auf den Namen
                if (onlineFreundName.Trim() != "")
                    FreundeListe.Speichere(onlineFreundName, onlineIp);

                zeigeOnlineBeitritt = false;
                KampfOnline.Trete(onlineIp, onlineCode);
            }
            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.028f);
            if (GUI.Button(new Rect(sw * 0.5f - sw * 0.11f, sh * 0.755f, sw * 0.22f, sh * 0.05f),
                    "HILFE", knopfStil))
            {
                hilfeInhalt = NetzwerkHilfe.BeitretenAnleitung;
                hilfeZurueckZuBeitritt = true;
                zeigeOnlineBeitritt = false;
                zeigeHilfe = true;
            }

            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.032f);
            if (GUI.Button(new Rect(sw * 0.5f - sw * 0.11f, sh * 0.675f, sw * 0.22f, sh * 0.06f),
                    "ZURÜCK", knopfStil))
                zeigeOnlineBeitritt = false;
        }

        // Modus auswählen - EIN Bildschirm für beide Einstiege:
        // solo == true  -> vom START-Knopf (offline, sofort spielen)
        // solo == false -> von RUNDE ERSTELLEN (online, mit Room-Code)
        // Zwei Karten mit je 1-2 Sätzen Beschreibung auf leicht weissem
        // Grund, darunter der Start-Knopf. Das Hintergrundbild bleibt sichtbar.
        void ZeichneModusKarten(float sw, float sh, bool solo)
        {
            if (kartenTex == null)
            {
                kartenTex = new Texture2D(1, 1);
                kartenTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.85f));
                kartenTex.Apply();
            }
            if (kartenStil == null)
                kartenStil = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            kartenStil.fontSize = Mathf.RoundToInt(sh * 0.023f);
            kartenStil.normal.textColor = new Color(0.1f, 0.1f, 0.12f);
            if (kartenTitelStil == null)
                kartenTitelStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            kartenTitelStil.fontSize = Mathf.RoundToInt(sh * 0.04f);
            kartenTitelStil.normal.textColor = new Color(0.05f, 0.05f, 0.08f);
            GUI.Label(new Rect(0f, sh * 0.06f, sw, sh * 0.07f),
                      (solo ? "SOLO SPIELEN" : "RUNDE ERSTELLEN") + " - Modus auswählen", kartenTitelStil);

            kartenTitelStil.fontSize = Mathf.RoundToInt(sh * 0.034f);
            float kartenBreite = sw * 0.27f, kartenHoehe = sh * 0.42f;
            var links  = new Rect(sw * 0.5f - kartenBreite - sw * 0.015f, sh * 0.17f, kartenBreite, kartenHoehe);
            var rechts = new Rect(sw * 0.5f + sw * 0.015f, sh * 0.17f, kartenBreite, kartenHoehe);

            GUI.DrawTexture(links, kartenTex);
            GUI.DrawTexture(rechts, kartenTex);

            GUI.Label(new Rect(links.x, links.y + sh * 0.015f, links.width, sh * 0.05f),
                      "NEON BLASTER", kartenTitelStil);
            GUI.Label(new Rect(links.x + sw * 0.012f, links.y + sh * 0.08f,
                               links.width - sw * 0.024f, links.height - sh * 0.15f),
                      solo
                        ? "Abschießen gegen Bots! Wer als Letzter übrig ist, gewinnt."
                        : "Abschießen - jeder gegen jeden mit Farb-Blastern! " +
                          "Wer als Letzter übrig ist, gewinnt. Freie Plätze füllen Bots auf.", kartenStil);

            GUI.Label(new Rect(rechts.x, rechts.y + sh * 0.015f, rechts.width, sh * 0.05f),
                      "FARBMIMIK", kartenTitelStil);
            GUI.Label(new Rect(rechts.x + sw * 0.012f, rechts.y + sh * 0.08f,
                               rechts.width - sw * 0.024f, rechts.height - sh * 0.15f),
                      "Anmalen und verstecken! Danach sucht ein Sucher - " +
                      "wer sich bewegt, blinkt neon auf.", kartenStil);

            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.024f);
            string neonKnopfText = solo ? "NEON BLASTER SPIELEN" : "NEON BLASTER HOSTEN";
            if (GUI.Button(new Rect(links.x + links.width * 0.12f, links.y + links.height - sh * 0.075f,
                                    links.width * 0.76f, sh * 0.055f), neonKnopfText, knopfStil))
            {
                zeigeModusWahl = false;
                zeigeSoloWahl = false;
                if (solo) StarteKampf();
                else KampfOnline.Hoste(botAnzahl);
            }

            string farbKnopfText = solo ? "FARBMIMIK SPIELEN" : "FARBMIMIK HOSTEN";
            if (GUI.Button(new Rect(rechts.x + rechts.width * 0.12f, rechts.y + rechts.height - sh * 0.075f,
                                    rechts.width * 0.76f, sh * 0.055f), farbKnopfText, knopfStil))
            {
                PlayerPrefs.SetString("NeonCatch_HauptSzene",
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                if (Application.CanStreamedLevelBeLoaded("Farbmimik"))
                {
                    // Nach dem Szenenwechsel startet die FARBMIMIK-Lobby von selbst
                    // (solo: man spielt einfach allein weiter, ohne auf Freunde zu warten)
                    PlayerPrefs.SetInt("NeonCatch_AutoHost", 1);
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Farbmimik");
                }
                else
                {
                    zeigeModusWahl = false;
                    zeigeSoloWahl = false;
                    endText = "Szene 'Farbmimik' fehlt in den Build Settings! " +
                              "(File > Build Settings > beide Szenen hinzufügen)";
                }
            }

            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.028f);
            if (GUI.Button(new Rect(sw * 0.5f - sw * 0.08f, sh * 0.66f, sw * 0.16f, sh * 0.06f),
                    "ZURÜCK", knopfStil))
            {
                zeigeModusWahl = false;
                zeigeSoloWahl = false;
            }
        }

        // Eingebaute Online-Hilfe: gleiche Texte wie in der FARBMIMIK-Lobby
        // (NetzwerkHilfe.HostAnleitung bzw. .BeitretenAnleitung, je nachdem
        // woher die Hilfe geoeffnet wurde)
        void ZeichneHilfe(float sw, float sh)
        {
            if (steuerungStil == null)
                steuerungStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            steuerungStil.fontSize = Mathf.RoundToInt(sh * 0.021f);
            steuerungStil.normal.textColor = new Color(0.08f, 0.08f, 0.1f);

            GUI.Label(new Rect(sw * 0.5f - sw * 0.28f, sh * 0.04f, sw * 0.56f, sh * 0.76f),
                       hilfeInhalt, steuerungStil);

            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.035f);
            if (GUI.Button(new Rect(sw * 0.5f - sw * 0.11f, sh * 0.84f, sw * 0.22f, sh * 0.07f),
                    "ZURÜCK", knopfStil))
            {
                zeigeHilfe = false;
                if (hilfeZurueckZuBeitritt)
                {
                    hilfeZurueckZuBeitritt = false;
                    zeigeOnlineBeitritt = true;
                }
            }
        }

        // Steuerungs-Übersicht auf schwarzem Grund im Startmenü
        void ZeichneSteuerung(float sw, float sh)
        {
            if (steuerungStil == null)
                steuerungStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            steuerungStil.fontSize = Mathf.RoundToInt(sh * 0.026f);
            steuerungStil.normal.textColor = new Color(0.08f, 0.08f, 0.1f);

            GUI.Label(new Rect(sw * 0.5f - sw * 0.28f, sh * 0.14f, sw * 0.56f, sh * 0.6f),
                       steuerungText, steuerungStil);

            knopfStil.fontSize = Mathf.RoundToInt(sh * 0.035f);
            if (GUI.Button(new Rect(sw * 0.5f - sw * 0.11f, sh * 0.82f, sw * 0.22f, sh * 0.08f),
                    "ZURÜCK", knopfStil))
                zeigeSteuerung = false;
        }

        void OnGUI()
        {
            // Online-HUD zeichnet KampfNetzwerk - aber solange KEIN eigener
            // Netzwerk-Spieler existiert (z.B. Verbindung klappt nicht),
            // gibt es hier einen sichtbaren Abbrechen-Knopf als Notausgang
            if (NetworkClient.active)
            {
                if (NetworkClient.localPlayer == null)
                {
                    if (knopfStil == null)
                        knopfStil = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, wordWrap = true };
                    knopfStil.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);
                    GUI.Box(new Rect(Screen.width * 0.5f - Screen.width * 0.16f, Screen.height * 0.3f,
                                     Screen.width * 0.32f, Screen.height * 0.08f), "Verbinde / warte auf Spieler...");
                    if (GUI.Button(new Rect(Screen.width * 0.5f - Screen.width * 0.11f, Screen.height * 0.42f,
                                            Screen.width * 0.22f, Screen.height * 0.07f), "ABBRECHEN (ESC)", knopfStil))
                        KampfOnline.Verlasse();
                }
                return;
            }

            if (knopfStil == null)
                knopfStil = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, wordWrap = true };

            float sh = Screen.height, sw = Screen.width;

            if (!laeuft)
            {
                // Hintergrund fürs Startmenü: das Bild aus Assets/Resources/
                // MenueHintergrund (leicht abgedunkelt, damit Text lesbar
                // bleibt), Fallback schwarz. Verschwindet komplett zusammen
                // mit allen Knöpfen, sobald der Kampf läuft.
                if (!hintergrundGesucht)
                {
                    hintergrundGesucht = true;
                    menueHintergrund = Resources.Load<Texture2D>("MenueHintergrund");
                }
                Color altBg = GUI.color;
                if (menueHintergrund != null)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(0f, 0f, sw, sh), menueHintergrund, ScaleMode.ScaleAndCrop);
                    // Leicht weisse Schicht: Bild bleibt sichtbar, Schrift gut lesbar
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
                }
                else
                {
                    GUI.color = new Color(0.88f, 0.88f, 0.9f);
                    GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
                }
                GUI.color = altBg;

                // Kein RESET-Knopf mehr – die R-Taste übernimmt das (siehe Update()).

                if (zeigeSteuerung)
                {
                    ZeichneSteuerung(sw, sh);
                    return;
                }

                if (zeigeOnlineBeitritt)
                {
                    ZeichneOnlineBeitritt(sw, sh);
                    return;
                }

                if (zeigeHilfe)
                {
                    ZeichneHilfe(sw, sh);
                    return;
                }

                if (zeigeSoloWahl)
                {
                    ZeichneModusKarten(sw, sh, true);
                    return;
                }

                if (zeigeModusWahl)
                {
                    ZeichneModusKarten(sw, sh, false);
                    return;
                }

                // Titel
                if (kartenTitelStil == null)
                    kartenTitelStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                kartenTitelStil.fontSize = Mathf.RoundToInt(sh * 0.07f);
                kartenTitelStil.normal.textColor = new Color(0.05f, 0.05f, 0.08f);
                GUI.Label(new Rect(0f, sh * 0.12f, sw, sh * 0.1f), "NEON CATCH", kartenTitelStil);
                kartenTitelStil.fontSize = Mathf.RoundToInt(sh * 0.026f);
                GUI.Label(new Rect(0f, sh * 0.22f, sw, sh * 0.05f),
                          "Online mit Freunden - oder solo gegen Bots", kartenTitelStil);

                // Profil: einfacher Name, wird online in der Lobby angezeigt
                if (textStil == null)
                    textStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                textStil.alignment = TextAnchor.MiddleRight;
                textStil.fontSize = Mathf.RoundToInt(sh * 0.024f);
                textStil.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
                GUI.Label(new Rect(sw * 0.5f - sw * 0.24f, sh * 0.635f, sw * 0.13f, sh * 0.045f),
                          "Dein Name:", textStil);
                string neuerName = GUI.TextField(new Rect(sw * 0.5f - sw * 0.10f, sh * 0.635f,
                                                          sw * 0.22f, sh * 0.045f), nameEingabe ?? "");
                if (neuerName != nameEingabe)
                {
                    nameEingabe = neuerName;
                    SpielerProfil.Name = neuerName;
                }

                // START-Knopf (verschwindet, sobald der Kampf läuft) - oeffnet
                // die Modus-Auswahl, damit man auch solo zwischen NEON BLASTER
                // und FARBMIMIK waehlen kann
                knopfStil.fontSize = Mathf.RoundToInt(sh * 0.032f);
                if (GUI.Button(new Rect(sw * 0.5f - sw * 0.13f, sh * 0.70f, sw * 0.26f, sh * 0.08f),
                        "START (Solo, Modus wählen)", knopfStil))
                    zeigeSoloWahl = true;

                knopfStil.fontSize = Mathf.RoundToInt(sh * 0.022f);
                if (GUI.Button(new Rect(sw * 0.5f - sw * 0.13f, sh * 0.79f, sw * 0.26f, sh * 0.055f),
                        "RUNDE ERSTELLEN (Online, Modus wählen)", knopfStil))
                    zeigeModusWahl = true;

                if (GUI.Button(new Rect(sw * 0.5f - sw * 0.13f, sh * 0.85f, sw * 0.26f, sh * 0.055f),
                        "RUNDE BEITRETEN (Code eingeben)", knopfStil))
                    zeigeOnlineBeitritt = true;

                // kleinere Schrift fuer die schmalen Buttons unten, damit der
                // Text sicher hineinpasst
                knopfStil.fontSize = Mathf.RoundToInt(sh * 0.022f);
                if (GUI.Button(new Rect(sw * 0.5f - sw * 0.13f, sh * 0.91f, sw * 0.125f, sh * 0.05f),
                        "STEUERUNG", knopfStil))
                    zeigeSteuerung = true;

                if (GUI.Button(new Rect(sw * 0.5f + sw * 0.005f, sh * 0.91f, sw * 0.125f, sh * 0.05f),
                        "HILFE: ONLINE", knopfStil))
                {
                    hilfeInhalt = NetzwerkHilfe.HostAnleitung;
                    hilfeZurueckZuBeitritt = false;
                    zeigeHilfe = true;
                }

                // End-Nachricht: ganz oben am Rand, ohne Kästchen
                if (endText != "")
                    TextObenAmRand(endText, 0.005f, 0.045f, TextAnchor.UpperCenter);
                return;
            }

            // HUD: rote Herzen links oben (leere Herzen = schon verlorene Leben)
            if (herzStil == null)
                herzStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            herzStil.fontSize = Mathf.RoundToInt(sh * 0.05f);
            herzStil.normal.textColor = new Color(0.95f, 0.15f, 0.15f);
            string herzText = "";
            for (int i = 0; i < lebenProKaempfer; i++) herzText += i < spielerLeben ? "♥ " : "♡ ";
            GUI.Label(new Rect(14f, sh * 0.01f, sw * 0.3f, sh * 0.08f), herzText, herzStil);

            TextObenAmRand("Gegner übrig: " + bots.Count, 0.005f, 0.028f, TextAnchor.UpperRight);

            // Munitions-Anzeige unten mittig – 3 Schuss, laden nacheinander
            // nach (genau wie in Brawl Stars): voll = Cyan, leer = dunkel,
            // der gerade ladende Schuss füllt sich von unten nach oben
            if (waffe != null)
            {
                float pipGroesse = sh * 0.035f;
                float abstand = pipGroesse * 0.35f;
                int max = waffe.MaxMunition;
                float breiteGesamt = max * pipGroesse + (max - 1) * abstand;
                float startX = sw * 0.5f - breiteGesamt * 0.5f;
                float y = sh * 0.88f;

                Color alt = GUI.color;
                for (int i = 0; i < max; i++)
                {
                    var pip = new Rect(startX + i * (pipGroesse + abstand), y, pipGroesse, pipGroesse);
                    bool gefuellt = i < waffe.Munition;

                    GUI.color = gefuellt ? new Color(0.2f, 0.85f, 1f) : new Color(0.12f, 0.12f, 0.12f, 0.8f);
                    GUI.DrawTexture(pip, Texture2D.whiteTexture);

                    if (!gefuellt && i == waffe.Munition)
                    {
                        float fortschritt = waffe.NachladeFortschritt;
                        var balken = new Rect(pip.x, pip.y + pip.height * (1f - fortschritt),
                                               pip.width, pip.height * fortschritt);
                        GUI.color = new Color(0.2f, 0.85f, 1f, 0.9f);
                        GUI.DrawTexture(balken, Texture2D.whiteTexture);
                    }
                }

                // Ausweichen-Anzeige direkt neben den Munitions-Pips, in
                // Violett statt Cyan – füllt sich von unten, bis der Dash
                // (Corkscrew Evade, Linke Umschalttaste) wieder bereit ist
                var ausweichPip = new Rect(startX + max * (pipGroesse + abstand) + pipGroesse * 0.4f,
                                            y, pipGroesse, pipGroesse);
                float ausweichFortschritt = waffe.AusweichFortschritt;
                GUI.color = new Color(0.35f, 0.1f, 0.15f, 0.8f);
                GUI.DrawTexture(ausweichPip, Texture2D.whiteTexture);
                GUI.color = new Color(0.7f, 0.25f, 0.95f);
                var ausweichBalken = new Rect(ausweichPip.x, ausweichPip.y + ausweichPip.height * (1f - ausweichFortschritt),
                                               ausweichPip.width, ausweichPip.height * ausweichFortschritt);
                GUI.DrawTexture(ausweichBalken, Texture2D.whiteTexture);

                GUI.color = alt;
            }

            // Fadenkreuz in der Mitte
            GUI.Box(new Rect(sw * 0.5f - 2f, sh * 0.5f - 2f, 4f, 4f), GUIContent.none);

            // Roter Blitz bei eigenem Treffer
            if (trefferBlitz > 0f)
            {
                Color alt = GUI.color;
                GUI.color = new Color(1f, 0f, 0f, trefferBlitz * 0.4f);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                GUI.color = alt;
            }
        }
    }

    // ======================================================================
    // Die Waffe des Spielers: Linksklick schießt (Hitscan aus der Kamera-
    // mitte mit kleiner Streuung). Sichtbares Blaster-Modell (Cosmic Retro
    // Blaster – Platzhalter, bis das "Spooky"-Paket importiert ist) unten
    // rechts im Bild. Jeder Treffer feuert eine Farbkugel ab, die am
    // Einschlagsort einen dauerhaften Farbklecks hinterlässt.
    //
    // Munition wie in Brawl Stars: 3 Schuss, danach lädt EIN Schuss nach dem
    // anderen automatisch nach (kein "alles auf einmal").
    // ======================================================================
    public class PistolenSchuetze : MonoBehaviour
    {
        public float reichweite = 45f;
        public float streuungGrad = 0.8f;   // Spieler zielt fast exakt

        [Header("Munition (Brawl-Stars-Art)")]
        public int maxMunition = 3;
        public float nachladeZeit = 2.2f;   // Sekunden pro einzelnem Schuss

        [Header("Ausweichen (Corkscrew Evade)")]
        public float ausweichCooldown = 10f;
        public float ausweichDauer = 0.35f;
        public float ausweichStrecke = 2.2f;

        [Header("Letztes-Leben-Boost")]
        public float boostFaktor = 1.15f;   // 15 % schneller schießen + rennen

        int munition;
        float nachladeRest;
        float ausweichRest;    // Sekunden bis Ausweichen wieder verfügbar ist
        float ausweichZeit;    // >0, solange der Ausweich-Dash gerade läuft
        Vector3 ausweichRichtung;
        bool geboostet;
        BlitzUmkreisung blitzEffekt;
        float basisWalkSpeed;

        // Für die HUD-Anzeige (Ammo-Pips, Ausweich-Cooldown) in KampfModus.OnGUI()
        public int Munition => munition;
        public int MaxMunition => maxMunition;
        public float NachladeFortschritt => munition >= maxMunition ? 1f : 1f - (nachladeRest / EffektiveNachladeZeit);
        public float AusweichFortschritt => ausweichRest <= 0f ? 1f : 1f - (ausweichRest / ausweichCooldown);
        public bool KannAusweichen => ausweichRest <= 0f && ausweichZeit <= 0f;
        // Während des Dashs unverwundbar – das ist der Sinn eines Ausweich-Manövers
        public bool IstUnverwundbar => ausweichZeit > 0f;

        float EffektiveNachladeZeit => geboostet ? nachladeZeit / boostFaktor : nachladeZeit;

        Camera cam;
        CharacterController cc;
        PlayerController spielerController;
        GameObject modell;

        public void Aktiviere()
        {
            enabled = true;
            munition = maxMunition;
            nachladeRest = 0f;
            ausweichRest = 0f;
            ausweichZeit = 0f;
            geboostet = false;
            cam = GetComponentInChildren<Camera>();
            cc = GetComponent<CharacterController>();
            spielerController = GetComponent<PlayerController>();
            if (spielerController != null) basisWalkSpeed = spielerController.walkSpeed;
            if (modell == null && cam != null) BaueModell();
            if (modell != null) modell.SetActive(true);
        }

        public void Deaktiviere()
        {
            if (modell != null) modell.SetActive(false);
            if (spielerController != null)
            {
                spielerController.walkSpeed = basisWalkSpeed;
                spielerController.extraRoll = 0f;
            }
            if (blitzEffekt != null) { Destroy(blitzEffekt.gameObject); blitzEffekt = null; }
            geboostet = false;
            enabled = false;
        }

        void BaueModell()
        {
            modell = new GameObject("Waffe");
            modell.transform.SetParent(cam.transform, false);
            modell.transform.localPosition = new Vector3(0.09f, -0.07f, 0.16f);
            // Falls der Blaster visuell rückwärts zeigt: hier auf
            // Quaternion.Euler(0f, 180f, 0f) ändern (Orientierung des
            // Cosmic-Retro-Blaster-Modells ist von hier aus nicht einsehbar)
            modell.transform.localRotation = Quaternion.identity;

            GameObject prefab = Resources.Load<GameObject>("KI/Blaster");
            if (prefab != null)
            {
                GameObject waffe = Instantiate(prefab, modell.transform);
                foreach (Collider c in waffe.GetComponentsInChildren<Collider>()) Destroy(c);
                waffe.transform.localPosition = Vector3.zero;
                waffe.transform.localRotation = Quaternion.identity;
                // Waffe auf handliche Ego-Perspektive-Größe bringen (war zu groß)
                float hoehe = 0f;
                foreach (Renderer r in waffe.GetComponentsInChildren<Renderer>())
                    hoehe = Mathf.Max(hoehe, r.bounds.size.y);
                if (hoehe > 0.01f) waffe.transform.localScale *= 0.085f / hoehe;
            }
            else
            {
                // Fallback: einfache Mini-Pistole aus zwei Quadern
                var lauf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(lauf.GetComponent<Collider>());
                lauf.transform.SetParent(modell.transform, false);
                lauf.transform.localPosition = new Vector3(0f, 0.012f, 0.03f);
                lauf.transform.localScale = new Vector3(0.014f, 0.014f, 0.07f);
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Pistole_Metall" };
                mat.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.17f));
                lauf.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        void Update()
        {
            if (KampfModus.Instanz == null || !KampfModus.Instanz.KampfLaeuft) return;

            // Munition einzeln nachladen, wie in Brawl Stars
            if (munition < maxMunition)
            {
                nachladeRest -= Time.deltaTime;
                if (nachladeRest <= 0f)
                {
                    munition++;
                    nachladeRest = munition < maxMunition ? EffektiveNachladeZeit : 0f;
                }
            }

            if (ausweichRest > 0f) ausweichRest -= Time.deltaTime;
            AktualisiereBoost();
            AktualisiereAusweichen();

            var kb = Keyboard.current;
            if (kb != null && kb.leftShiftKey.wasPressedThisFrame && KannAusweichen)
                StarteAusweichen();

            var mouse = Mouse.current;
            if (mouse == null || cam == null) return;
            if (mouse.leftButton.wasPressedThisFrame && munition > 0)
                Schiesse();
        }

        // Letztes Leben (Spieler bei 1 von lebenProKaempfer Leben): 15 %
        // schnelleres Schießen + Rennen, dazu kreisen Blitz-Symbole um die Figur
        void AktualisiereBoost()
        {
            bool sollBoosten = KampfModus.Instanz.SpielerLeben == 1;
            if (sollBoosten == geboostet) return;
            geboostet = sollBoosten;

            if (spielerController != null)
                spielerController.walkSpeed = geboostet ? basisWalkSpeed * boostFaktor : basisWalkSpeed;

            if (geboostet)
            {
                if (blitzEffekt == null) blitzEffekt = BlitzUmkreisung.Erzeuge(transform);
            }
            else if (blitzEffekt != null)
            {
                Destroy(blitzEffekt.gameObject);
                blitzEffekt = null;
            }
        }

        // Corkscrew-Ausweichrolle: kurzer Dash in Blickrichtung, während dem
        // man unverwundbar ist – alle 8 Sekunden verfügbar (siehe HUD-Anzeige
        // neben den Munitions-Pips in KampfModus.OnGUI())
        void StarteAusweichen()
        {
            ausweichRest = ausweichCooldown;
            ausweichZeit = ausweichDauer;

            Vector3 vorwaerts = cam != null ? cam.transform.forward : transform.forward;
            vorwaerts.y = 0f;
            ausweichRichtung = vorwaerts.sqrMagnitude > 0.01f ? vorwaerts.normalized : transform.forward;
        }

        void AktualisiereAusweichen()
        {
            if (ausweichZeit <= 0f) return;
            ausweichZeit -= Time.deltaTime;

            if (cc != null)
            {
                float tempo = ausweichStrecke / ausweichDauer;
                cc.Move(ausweichRichtung * tempo * Time.deltaTime);
            }

            // Corkscrew-Gefühl in der Ego-Perspektive: die Kamera rollt einmal
            // komplett um die Blickachse, da die Figur selbst unsichtbar ist
            if (spielerController != null)
            {
                float t = 1f - Mathf.Clamp01(ausweichZeit / ausweichDauer);
                spielerController.extraRoll = t * 360f;
                if (ausweichZeit <= 0f) spielerController.extraRoll = 0f;
            }
        }

        void Schiesse()
        {
            // Timer erst starten, wenn er nicht schon läuft (sonst würde ein
            // Schuss mitten in der Aufladung sie wieder verlängern)
            if (munition == maxMunition) nachladeRest = EffektiveNachladeZeit;
            munition--;

            // Richtung: Bildmitte plus minimale Streuung
            Vector3 richtung = cam.transform.forward;
            richtung = Quaternion.Euler(Random.Range(-streuungGrad, streuungGrad),
                                        Random.Range(-streuungGrad, streuungGrad), 0f) * richtung;

            Vector3 start = cam.transform.position + richtung * 0.2f;
            Vector3 ende = start + richtung * reichweite;
            Vector3 normal = -richtung;
            Transform getroffen = null;

            // Naechsten Treffer suchen, der NICHT der eigene Koerper ist -
            // sonst blockt der eigene CharacterController den Schuss
            RaycastHit[] alleTreffer = Physics.RaycastAll(start, richtung, reichweite,
                ~(1 << 4), QueryTriggerInteraction.Ignore);
            float naechste = float.MaxValue;
            RaycastHit bester = default;
            bool gefunden = false;
            foreach (RaycastHit h in alleTreffer)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;   // eigener Koerper
                if (h.distance >= naechste) continue;
                naechste = h.distance;
                bester = h;
                gefunden = true;
            }

            if (gefunden)
            {
                ende = bester.point;
                normal = bester.normal;
                KampfBot bot = bester.collider.GetComponentInParent<KampfBot>();
                if (bot != null)
                {
                    getroffen = bot.transform;
                    bot.Treffer();
                }
                else
                {
                    AnimalLagFix tier = bester.collider.GetComponentInParent<AnimalLagFix>();
                    if (tier != null) getroffen = tier.transform;
                }
            }

            Farbschuss.Abfeuern(modell.transform.position + modell.transform.forward * 0.1f, ende, normal, getroffen);
        }
    }

    // ======================================================================
    // Kampf-Bot: läuft umher (mit Wand-Ausweichen), sucht das nächste
    // sichtbare Ziel (Spieler ODER anderer Bot – jeder gegen jeden) und
    // schießt mit ABSICHTLICHER Streuung (trifft nicht immer). 2 Leben.
    // ======================================================================
    public class KampfBot : MonoBehaviour
    {
        public int leben = 3;   // wird von KampfModus.StarteKampf() auf lebenProKaempfer gesetzt
        public float tempo = 1.1f;   // war 1.6 – langsamer, damit man leichter trifft
        public float sichtweite = 22f;
        public float schussPause = 1.4f;
        public float streuungGrad = 7f;     // absichtlich ungenau (~50-60 % Treffer)
        public float wunschAbstand = 6f;    // versucht, auf dieser Distanz zu kämpfen

        CharacterController cc;
        BotAnimation anim;
        WeltHerzen herzen;
        BlitzUmkreisung blitzEffekt;
        float naechsterSchuss;
        Vector3 wanderRichtung;
        float richtungsWechsel;
        float vertikal;
        Renderer[] koerperTeile;
        Color[] originalFarben;
        float blitzZeit;
        bool stirbt;

        void Start()
        {
            // Eigener kleiner CharacterController: läuft Hänge hoch, bleibt
            // an Wänden hängen statt hindurchzugehen. WICHTIG: das ist auch
            // der einzige Collider des Bots und damit seine Trefferbox – muss
            // zur sichtbaren Figurengröße (0.75 m, siehe SkaliereAufHoehe in
            // KampfModus.StarteKampf) passen, sonst gehen Schüsse einfach
            // durch die (zu große) Figur hindurch, weil die Box zu klein ist.
            //
            // CharacterController.height/radius/center sind LOKALE Werte und
            // werden von Unity automatisch mit transform.localScale multipliziert.
            // SkaliereAufHoehe() hat die Figur VOR diesem Start() schon auf
            // 0.75 m herunterskaliert (localScale ist also << 1) – ohne die
            // Skala hier herauszurechnen wäre die Trefferbox viel zu klein UND
            // knapp über dem Boden statt auf Brusthöhe. Das war der eigentliche
            // Grund, warum Bots kaum zu treffen waren.
            float skala = Mathf.Max(0.0001f, transform.localScale.y);
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = 0.75f / skala;
            cc.radius = 0.19f / Mathf.Max(0.0001f, transform.localScale.x);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.08f / skala;

            // Mixamo-Animationen (Idle/Gehen/Rennen/Treppen/Sterben) –
            // Zustandswahl passiert automatisch anhand der Bewegung
            anim = gameObject.AddComponent<BotAnimation>();

            // Sichtbare Waffe in die rechte Hand
            BauePistoleInHand();

            // Rote Herzen über dem Kopf, sichtbar aus der Ego-Perspektive
            // des Spielers (der Spieler sieht ja seine EIGENEN nicht, siehe
            // KampfModus.OnGUI() für die HUD-Variante)
            herzen = gameObject.AddComponent<WeltHerzen>();
            herzen.maxLeben = leben;
            herzen.SetzeLeben(leben);   // Awake() lief schon mit dem alten maxLeben-Default

            koerperTeile = GetComponentsInChildren<Renderer>();
            originalFarben = new Color[koerperTeile.Length];
            for (int i = 0; i < koerperTeile.Length; i++)
                originalFarben[i] = koerperTeile[i].material.HasProperty("_BaseColor")
                    ? koerperTeile[i].material.GetColor("_BaseColor") : Color.white;

            NeueWanderrichtung();
        }

        // Sichtbare Waffe am rechten Hand-Knochen der Figur (Cosmic Retro
        // Blaster – Platzhalter, bis "Spooky" importiert ist)
        void BauePistoleInHand()
        {
            Animator a = GetComponentInChildren<Animator>();
            if (a == null || a.avatar == null || !a.avatar.isHuman) return;
            Transform hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) return;

            // Gegen die Figuren-Skalierung rechnen, damit die Waffe in
            // Weltmaßen ~8 cm groß ist
            float gegenSkala = 1f / Mathf.Max(hand.lossyScale.x, 0.0001f);

            GameObject prefab = Resources.Load<GameObject>("KI/Blaster");
            if (prefab != null)
            {
                GameObject waffe = Instantiate(prefab, hand);
                foreach (Collider c in waffe.GetComponentsInChildren<Collider>()) Destroy(c);
                waffe.name = "Waffe";
                waffe.transform.localPosition = new Vector3(0.03f, 0.02f, 0f) * gegenSkala * 0.35f;
                waffe.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                float hoehe = 0f;
                foreach (Renderer r in waffe.GetComponentsInChildren<Renderer>())
                    hoehe = Mathf.Max(hoehe, r.bounds.size.y);
                if (hoehe > 0.01f)
                    waffe.transform.localScale = Vector3.one * (0.045f * gegenSkala / hoehe);
                return;
            }

            // Fallback: einfacher Quader, falls das Prefab fehlt
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Bot_Pistole" };
            mat.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.17f));
            mat.SetFloat("_Smoothness", 0.6f);

            var lauf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lauf.GetComponent<Collider>());
            lauf.name = "Pistole";
            lauf.transform.SetParent(hand, false);
            lauf.transform.localPosition = new Vector3(0.03f, 0.02f, 0f) * gegenSkala * 0.35f;
            lauf.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            lauf.transform.localScale = new Vector3(0.012f, 0.012f, 0.05f) * gegenSkala * 0.35f;
            lauf.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void Update()
        {
            if (stirbt) return;
            if (KampfModus.Instanz == null || !KampfModus.Instanz.KampfLaeuft) return;

            if (blitzZeit > 0f)
            {
                blitzZeit -= Time.deltaTime;
                if (blitzZeit <= 0f) SetzeFarbe(false);
            }

            Transform ziel = SucheZiel();

            Vector3 bewegung;
            if (ziel != null)
            {
                // Zum Ziel ausrichten und den Wunschabstand halten
                Vector3 zumZiel = ziel.position - transform.position;
                zumZiel.y = 0f;
                float abstand = zumZiel.magnitude;
                Vector3 richtungZiel = abstand > 0.01f ? zumZiel / abstand : transform.forward;

                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(richtungZiel), Time.deltaTime * 6f);

                if (abstand > wunschAbstand + 1f)      bewegung = richtungZiel;
                else if (abstand < wunschAbstand - 1f) bewegung = -richtungZiel;
                else if (abstand > wunschAbstand + 0.3f || abstand < wunschAbstand - 0.3f)
                {
                    bewegung = Quaternion.Euler(0f, 90f, 0f) * richtungZiel * 0.35f;   // seitlich umkreisen, langsamer als vorher
                    // Gelegentlich eine Ausweichrolle beim Umkreisen (Corkscrew Evade)
                    if (anim != null && Random.value < 0.004f)
                        anim.SpieleEinmalig(BotAnimation.CLIP_AUSWEICHEN, 0.6f);
                }
                else
                {
                    // Nah genug am Wunschabstand dran: kurz stehen bleiben und
                    // zielen statt dauerhaft zu laufen – sonst läuft die
                    // Renn-Animation ständig, auch wenn sich die Figur kaum
                    // vom Fleck bewegt ("rennt immer auch am Platz")
                    bewegung = Vector3.zero;
                }

                if (Time.time >= naechsterSchuss)
                    SchiesseAuf(ziel);
            }
            else
            {
                // Kein Ziel in Sicht: umherstreifen
                richtungsWechsel -= Time.deltaTime;
                if (richtungsWechsel <= 0f) NeueWanderrichtung();
                bewegung = wanderRichtung;
                if (bewegung.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(bewegung), Time.deltaTime * 4f);
            }

            // Nicht in gesperrtes Gelände (Steilhang, Map-Rand, Hütten) laufen
            if (BurggrabenMittelalter.IstGesperrt(transform.position + bewegung * 1.2f))
            {
                NeueWanderrichtung();
                bewegung = Vector3.zero;

                // Kurze Leerlauf-Auflockerung beim Innehalten außerhalb des Kampfes
                if (anim != null && ziel == null && Random.value < 0.25f)
                    anim.SpieleEinmalig(Random.value < 0.5f ? BotAnimation.CLIP_TANZ : BotAnimation.CLIP_SPRUNG, 1.0f);
            }

            // Schwerkraft + Bewegung über den CharacterController
            vertikal = cc.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
            cc.Move((bewegung * tempo + Vector3.up * vertikal) * Time.deltaTime);

            // Animation automatisch passend zur Bewegung wählen:
            // stehen/gehen/rennen, bergauf = Treppe-rauf, bergab = Treppe-runter
            if (anim != null)
                anim.MeldeBewegung(bewegung.sqrMagnitude > 0.01f, ziel != null,
                                   cc.isGrounded, cc.velocity.y);
        }

        void NeueWanderrichtung()
        {
            float winkel = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            wanderRichtung = new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel));
            richtungsWechsel = Random.Range(2.5f, 5f);
        }

        // Nächstes SICHTBARES Ziel: Spieler oder ein anderer Bot
        Transform SucheZiel()
        {
            Transform bestes = null;
            float besteDistanz = sichtweite;

            var kandidaten = new List<Transform>();
            if (KampfModus.Instanz.Spieler != null) kandidaten.Add(KampfModus.Instanz.Spieler);
            foreach (KampfBot anderer in KampfModus.Instanz.Bots)
                if (anderer != null && anderer != this) kandidaten.Add(anderer.transform);

            foreach (Transform kandidat in kandidaten)
            {
                Vector3 zumZiel = kandidat.position - transform.position;
                float distanz = zumZiel.magnitude;
                if (distanz > besteDistanz) continue;

                // Sichtlinie: nichts Dickes dazwischen?
                Vector3 augen = transform.position + Vector3.up * 0.45f;
                Vector3 zielPunkt = kandidat.position + Vector3.up * 0.3f;
                if (Physics.Linecast(augen, zielPunkt, out RaycastHit hit,
                        ~(1 << 4), QueryTriggerInteraction.Ignore))
                {
                    // Treffen wir dabei das Ziel selbst? Dann ist die Sicht frei
                    if (hit.collider.transform != kandidat &&
                        hit.collider.GetComponentInParent<KampfBot>()?.transform != kandidat &&
                        !hit.collider.CompareTag("Player"))
                        continue;
                }

                bestes = kandidat;
                besteDistanz = distanz;
            }
            return bestes;
        }

        void SchiesseAuf(Transform ziel)
        {
            naechsterSchuss = Time.time + schussPause * Random.Range(0.8f, 1.3f);

            Vector3 start = transform.position + Vector3.up * 0.45f;
            Vector3 richtung = (ziel.position + Vector3.up * 0.3f - start).normalized;
            // ABSICHTLICHE Streuung: keine 100 % Trefferquote
            richtung = Quaternion.Euler(Random.Range(-streuungGrad, streuungGrad),
                                        Random.Range(-streuungGrad, streuungGrad), 0f) * richtung;

            Vector3 ende = start + richtung * sichtweite;
            Vector3 normal = -richtung;
            Transform getroffen = null;

            // Naechsten Treffer suchen, der nicht der eigene Koerper ist
            RaycastHit[] alleTreffer = Physics.RaycastAll(start, richtung, sichtweite,
                ~(1 << 4), QueryTriggerInteraction.Ignore);
            float naechsteDistanz = float.MaxValue;
            RaycastHit hit = default;
            bool getroffenEtwas = false;
            foreach (RaycastHit h in alleTreffer)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance >= naechsteDistanz) continue;
                naechsteDistanz = h.distance;
                hit = h;
                getroffenEtwas = true;
            }

            if (getroffenEtwas)
            {
                ende = hit.point;
                normal = hit.normal;

                if (hit.collider.CompareTag("Player"))
                {
                    getroffen = KampfModus.Instanz.Spieler;
                    // Tödlicher Treffer gegen den Spieler beendet die Runde sofort –
                    // dieser Bot ist damit der letzte Überlebende, nur DANN die
                    // Sieges-Animation (nicht bei jedem einzelnen Kill mittendrin)
                    if (KampfModus.Instanz.SpielerGetroffen())
                        anim?.SpieleEinmalig(BotAnimation.CLIP_SIEG, 0.8f);
                }
                else
                {
                    KampfBot anderer = hit.collider.GetComponentInParent<KampfBot>();
                    if (anderer != null && anderer != this)
                    {
                        getroffen = anderer.transform;
                        anderer.Treffer();   // ein Kill mittendrin – keine Sieges-Animation
                    }
                    else
                    {
                        AnimalLagFix tier = hit.collider.GetComponentInParent<AnimalLagFix>();
                        if (tier != null) getroffen = tier.transform;
                    }
                }
            }

            anim?.SpieleEinmalig(BotAnimation.CLIP_KNIEFALL, 0.5f);
            Farbschuss.Abfeuern(start + richtung * 0.2f, ende, normal, getroffen);
        }

        // Gibt true zurück, wenn dieser Treffer tödlich war (für den Schützen:
        // Sieges-Animation auslösen)
        public bool Treffer()
        {
            if (stirbt) return false;   // liegt schon im Sterben
            leben--;
            blitzZeit = 0.15f;
            SetzeFarbe(true);
            if (herzen != null) herzen.SetzeLeben(leben);

            // Letztes Leben: Blitze kreisen um den Bot - wie beim Spieler
            if (leben == 1 && blitzEffekt == null)
                blitzEffekt = BlitzUmkreisung.Erzeuge(transform);

            if (leben <= 0)
            {
                StirbMitAnimation();
                return true;
            }

            // Kurze Reaktion auf einen nicht-tödlichen Treffer
            anim?.SpieleEinmalig(Random.value < 0.5f ? BotAnimation.CLIP_TREFFER_SAD : BotAnimation.CLIP_TREFFER_DIZZY, 1f);
            return false;
        }

        // Leben weg → Sterbe-Animation läuft AUTOMATISCH, die Figur bleibt
        // kurz liegen und verschwindet dann
        void StirbMitAnimation()
        {
            stirbt = true;
            SetzeFarbe(false);
            if (blitzEffekt != null) { Destroy(blitzEffekt.gameObject); blitzEffekt = null; }
            KampfModus.Instanz.BotEliminiert(this);

            if (cc != null) cc.enabled = false;

            if (anim != null)
            {
                anim.SpieleTod();
                // Nur bei der "wackelnden" Sterbe-Animation (Zombie Dying) –
                // die anderen (Hände vors Gesicht, Schulterzucken) bleiben ohne Effekt
                if (anim.LetzterTodWarWackelClip)
                    TodesSterneEffekt.Erzeuge(transform, Vector3.up * 0.85f);
            }
            else CannonStation.SpawneRauchwolke(transform.position + Vector3.up * 0.3f);

            Destroy(gameObject, 4f);   // nach dem Umfallen noch liegen lassen
        }

        // Kurz rot aufblitzen, wenn getroffen
        void SetzeFarbe(bool rot)
        {
            for (int i = 0; i < koerperTeile.Length; i++)
            {
                if (koerperTeile[i] == null) continue;
                if (koerperTeile[i].material.HasProperty("_BaseColor"))
                    koerperTeile[i].material.SetColor("_BaseColor",
                        rot ? new Color(1f, 0.25f, 0.25f) : originalFarben[i]);
            }
        }
    }

    // ======================================================================
    // Farbschuss: eine kurz sichtbare fliegende Kugel vom Lauf zum Ziel, die
    // dort einen FARBKLECKS hinterlässt. Der Klecks bleibt für immer liegen
    // (kein Destroy) – wie eine Paintball-Markierung.
    // ======================================================================
    public static class Farbschuss
    {
        static readonly Color[] farben =
        {
            new Color(1f, 0.15f, 0.55f),   // Pink
            new Color(0.15f, 0.65f, 1f),   // Blau
            new Color(0.25f, 0.9f, 0.35f), // Grün
            new Color(1f, 0.65f, 0.1f),    // Orange
        };

        // Alle jemals erzeugten Farbkleckse – damit eine neue Runde (START)
        // mit einem sauberen Schlachtfeld beginnen kann, statt die Kleckse
        // der vorigen Runde(n) für immer mitzuschleppen.
        static readonly List<GameObject> alleKleckse = new List<GameObject>();

        public static void Registriere(GameObject kleck) => alleKleckse.Add(kleck);

        public static void AlleEntfernen()
        {
            foreach (GameObject k in alleKleckse)
                if (k != null) Object.Destroy(k);
            alleKleckse.Clear();
        }

        // anheften: Transform eines getroffenen Bots/Tiers/Spielers – falls
        // gesetzt, wandert der spätere Farbkleck MIT diesem Ziel mit, statt
        // an der (dann leeren) Trefferstelle in der Luft hängen zu bleiben,
        // sobald sich das Ziel wegbewegt.
        public static void Abfeuern(Vector3 start, Vector3 ziel, Vector3 trefferNormal, Transform anheften = null)
        {
            Color farbe = farben[Random.Range(0, farben.Length)];

            var kugel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(kugel.GetComponent<Collider>());
            kugel.name = "Farbkugel";
            kugel.transform.position = start;
            kugel.transform.localScale = Vector3.one * 0.04f;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Farbkugel_Mat" };
            mat.SetColor("_BaseColor", farbe);
            kugel.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var flug = kugel.AddComponent<FliegendeFarbkugel>();
            flug.Starte(start, ziel, trefferNormal, farbe, anheften);
        }
    }

    // Bewegt die Farbkugel über eine kurze Flugzeit zum Ziel, dann Farbkleck
    public class FliegendeFarbkugel : MonoBehaviour
    {
        Vector3 start, ziel, normal;
        Color farbe;
        Transform anheften;
        float flugDauer = 0.1f, zeit;

        Vector3 zielLokal;

        public void Starte(Vector3 von, Vector3 nach, Vector3 trefferNormal, Color f, Transform anheftenAn)
        {
            start = von; ziel = nach; normal = trefferNormal; farbe = f; anheften = anheftenAn;
            // Lokalen Trefferpunkt auf dem Ziel merken – bewegt/dreht es sich
            // während der kurzen Flugzeit weiter, landet der Klecks trotzdem
            // an der richtigen Körperstelle statt am alten Weltraum-Punkt
            if (anheften != null) zielLokal = anheften.InverseTransformPoint(nach);
            // Flugzeit an die Distanz koppeln, damit nahe Treffer nicht "ruckeln"
            flugDauer = Mathf.Clamp(Vector3.Distance(von, nach) / 40f, 0.03f, 0.15f);
        }

        void Update()
        {
            zeit += Time.deltaTime;
            float t = zeit / flugDauer;
            if (t >= 1f)
            {
                // Bewegt sich das getroffene Ziel während der kurzen Flugzeit
                // weiter, an der ZIELPOSITION zum Aufprallzeitpunkt kleben,
                // nicht am längst veralteten ursprünglichen Trefferpunkt
                Vector3 einschlagPos = anheften != null ? anheften.TransformPoint(zielLokal) : ziel;
                ErzeugeFarbkleck(einschlagPos, normal, farbe, anheften);
                Destroy(gameObject);
                return;
            }
            transform.position = Vector3.Lerp(start, ziel, t);
        }

        // Permanent liegenbleibender Farbfleck an der Trefferstelle – aus dem
        // Schleim-Modell (Spooky-Paket), eingefärbt in der Schuss-Farbe.
        // Ersetzt den alten flachen, eckigen Quad-Fleck.
        static void ErzeugeFarbkleck(Vector3 pos, Vector3 normal, Color farbe, Transform anheften)
        {
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;

            GameObject prefab = Resources.Load<GameObject>("KI/Schleim");
            if (prefab != null)
            {
                GameObject kleck = Object.Instantiate(prefab);
                foreach (Collider c in kleck.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                kleck.name = "Farbkleck";
                kleck.transform.position = pos + normal * 0.01f;   // knapp über der Oberfläche, kein Z-Fighting
                kleck.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                float breite = 0f;
                foreach (Renderer r in kleck.GetComponentsInChildren<Renderer>())
                    breite = Mathf.Max(breite, r.bounds.size.x, r.bounds.size.z);
                float zielGroesse = Random.Range(0.14f, 0.24f);
                if (breite > 0.01f) kleck.transform.localScale *= zielGroesse / breite;

                // Schleim-Shader nutzt eigene Shadergraph-Farb-Properties statt
                // _BaseColor – beide Grundfarben auf die Schuss-Farbe einfärben
                foreach (Renderer r in kleck.GetComponentsInChildren<Renderer>())
                {
                    var mat = new Material(r.sharedMaterial) { name = "Farbkleck_Mat" };
                    if (mat.HasProperty("Color_785622C2")) mat.SetColor("Color_785622C2", farbe);
                    if (mat.HasProperty("Color_D187F352")) mat.SetColor("Color_D187F352", farbe * 0.6f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", farbe);
                    r.sharedMaterial = mat;
                }
                // An bewegliche Ziele (Bot/Tier/Spieler) anheften, damit der
                // Klecks mitwandert statt in der Luft schweben zu bleiben;
                // ohne Ziel (Wand/Boden) bleibt er unverändert für immer liegen
                if (anheften != null) kleck.transform.SetParent(anheften, true);
                Farbschuss.Registriere(kleck);
                return;
            }

            // Fallback, falls das Schleim-Prefab fehlt: einfacher flacher Fleck
            var kleckFallback = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(kleckFallback.GetComponent<Collider>());
            kleckFallback.name = "Farbkleck";
            kleckFallback.transform.position = pos + normal * 0.01f;
            kleckFallback.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            kleckFallback.transform.localScale = Vector3.one * Random.Range(0.14f, 0.24f);

            var matFallback = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Farbkleck_Mat" };
            matFallback.SetColor("_BaseColor", farbe);
            kleckFallback.GetComponent<MeshRenderer>().sharedMaterial = matFallback;
            if (anheften != null) kleckFallback.transform.SetParent(anheften, true);
            Farbschuss.Registriere(kleckFallback);
        }
    }

    // ======================================================================
    // Herzen-Anzeige an einer Figur: IMMER sichtbar und IMMER zum Betrachter
    // gedreht (Billboard). Die Herzen schweben leicht VOR dem Körper - je
    // nachdem, von wo man die Figur anschaut, also vor Bauch, Rücken oder
    // Seite. Für den Spieler selbst unsichtbar (Ego-Perspektive) - dafür
    // gibt es zusätzlich die HUD-Herzen in KampfModus.OnGUI().
    // ======================================================================
    public class WeltHerzen : MonoBehaviour
    {
        public int maxLeben = 3;
        int aktuelleLeben;
        TextMesh text;
        Transform anzeige;
        float herzHoehe = 0.45f;
        float abstandVor = 0.25f;

        void Awake()
        {
            aktuelleLeben = maxLeben;

            // Figurgröße messen -> Herzen auf Brusthöhe, Schrift passend groß.
            // Die Anzeige wird NICHT an die Figur geparentet, damit skalierte
            // Figuren die Schrift nicht verzerren - sie folgt in LateUpdate().
            float figurHoehe = 0f;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                figurHoehe = Mathf.Max(figurHoehe, r.bounds.max.y - transform.position.y);
            if (figurHoehe < 0.2f) figurHoehe = 0.8f;
            herzHoehe = figurHoehe * 0.6f;
            abstandVor = Mathf.Max(0.18f, figurHoehe * 0.3f);

            var go = new GameObject("Herzen_Anzeige");
            anzeige = go.transform;
            text = go.AddComponent<TextMesh>();
            text.color = new Color(0.95f, 0.15f, 0.15f);   // rot
            text.fontSize = 96;
            text.characterSize = 0.014f * Mathf.Max(0.5f, figurHoehe / 0.75f);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;

            AktualisiereText();
        }

        void LateUpdate()
        {
            if (anzeige == null) return;
            Camera kamera = Camera.main;
            if (kamera == null) return;

            Vector3 zurKamera = kamera.transform.position - transform.position;
            zurKamera.y = 0f;
            zurKamera = zurKamera.sqrMagnitude > 0.001f
                ? zurKamera.normalized
                : -kamera.transform.forward;

            // leicht VOR dem Körper in Richtung Betrachter, immer lesbar gedreht
            anzeige.position = transform.position + Vector3.up * herzHoehe + zurKamera * abstandVor;
            anzeige.rotation = kamera.transform.rotation;
        }

        void OnDestroy()
        {
            if (anzeige != null)
                Destroy(anzeige.gameObject);
        }

        public void SetzeLeben(int leben)
        {
            aktuelleLeben = Mathf.Clamp(leben, 0, maxLeben);
            AktualisiereText();
        }

        void AktualisiereText()
        {
            if (text == null) return;
            string s = "";
            for (int i = 0; i < maxLeben; i++) s += i < aktuelleLeben ? "♥" : "♡";
            text.text = s;
        }
    }

    // ======================================================================
    // 8 kleine Sterne kreisen kurz um den Kopf – NUR beim "wackelnden" Tod
    // (Zombie Dying). Die anderen, freundlicheren Sterbe-Animationen (Hände
    // vors Gesicht, Schulterzucken) bekommen bewusst KEINEN Effekt.
    // ======================================================================
    public class TodesSterneEffekt : MonoBehaviour
    {
        const int anzahl = 8;
        const float radius = 0.12f;
        const float lebensdauer = 2f;
        const float kreiselTempo = 220f;

        float zeit;
        Transform[] sterne;

        public static void Erzeuge(Transform traeger, Vector3 lokalerVersatz)
        {
            var go = new GameObject("Todes_Sterne");
            go.transform.SetParent(traeger, false);
            go.transform.localPosition = lokalerVersatz;
            go.AddComponent<TodesSterneEffekt>().BaueSterne();
        }

        void BaueSterne()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Stern_Mat" };
            mat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.15f));   // Gold-Gelb

            sterne = new Transform[anzahl];
            for (int i = 0; i < anzahl; i++)
            {
                var stern = new GameObject("Stern_" + i).transform;
                stern.SetParent(transform, false);

                // Zwei gekreuzte, flache Quader ergeben eine einfache Stern-/Funkel-Form
                for (int q = 0; q < 2; q++)
                {
                    GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(arm.GetComponent<Collider>());
                    arm.transform.SetParent(stern, false);
                    arm.transform.localRotation = Quaternion.Euler(0f, 0f, q * 90f);
                    arm.transform.localScale = new Vector3(0.05f, 0.012f, 0.012f);
                    arm.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
                sterne[i] = stern;
            }
        }

        void Update()
        {
            zeit += Time.deltaTime;
            if (zeit >= lebensdauer) { Destroy(gameObject); return; }

            transform.Rotate(Vector3.up, kreiselTempo * Time.deltaTime, Space.Self);
            for (int i = 0; i < sterne.Length; i++)
            {
                float winkel = (i / (float)sterne.Length) * Mathf.PI * 2f;
                sterne[i].localPosition = new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)) * radius;
                sterne[i].Rotate(Vector3.up, 300f * Time.deltaTime, Space.World);
            }
        }
    }

    // ======================================================================
    // Blitz-Symbole kreisen dauerhaft um den Spieler, solange der Letztes-
    // Leben-Boost aktiv ist (PistolenSchuetze.AktualisiereBoost) – kein
    // eigener Timer, wird von außen hinzugefügt und wieder entfernt.
    // ======================================================================
    public class BlitzUmkreisung : MonoBehaviour
    {
        const int anzahl = 6;
        const float radius = 0.35f;
        const float hoehe = 0.55f;
        const float kreiselTempo = 160f;

        Transform[] blitze;

        public static BlitzUmkreisung Erzeuge(Transform traeger)
        {
            var go = new GameObject("Blitz_Umkreisung");
            go.transform.SetParent(traeger, false);
            var effekt = go.AddComponent<BlitzUmkreisung>();
            effekt.BaueBlitze();
            return effekt;
        }

        void BaueBlitze()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Blitz_Mat" };
            mat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.2f));   // Gelb

            blitze = new Transform[anzahl];
            for (int i = 0; i < anzahl; i++)
            {
                var blitz = new GameObject("Blitz_" + i).transform;
                blitz.SetParent(transform, false);

                // Zickzack-Blitzform aus 3 schräg versetzten, flachen Quadern
                for (int s = 0; s < 3; s++)
                {
                    GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(seg.GetComponent<Collider>());
                    seg.transform.SetParent(blitz, false);
                    seg.transform.localPosition = new Vector3(s % 2 == 0 ? 0.01f : -0.01f, 0.02f - s * 0.02f, 0f);
                    seg.transform.localRotation = Quaternion.Euler(0f, 0f, s % 2 == 0 ? 20f : -20f);
                    seg.transform.localScale = new Vector3(0.02f, 0.035f, 0.008f);
                    seg.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
                blitze[i] = blitz;
            }
        }

        void Update()
        {
            transform.Rotate(Vector3.up, kreiselTempo * Time.deltaTime, Space.Self);
            for (int i = 0; i < blitze.Length; i++)
            {
                float winkel = (i / (float)blitze.Length) * Mathf.PI * 2f + Time.time * 0.5f;
                blitze[i].localPosition = new Vector3(Mathf.Cos(winkel) * radius, hoehe, Mathf.Sin(winkel) * radius);
                blitze[i].localRotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)));
            }
        }
    }
}
