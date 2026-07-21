using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace NeonCatch
{
    // ======================================================================
    // ONLINE-KAMPFMODUS (NEON BLASTER) auf Photon PUN 2:
    // Derselbe Abschiess-Modus, aber mit Freunden UND Bots zusammen. Der
    // MasterClient (Photons Entsprechung zum "Host") steuert die Bots und
    // rechnet Treffer/Leben, alle sehen synchronisierte Figuren, Schuesse
    // und Farbkleckse. Beitritt ueber den 4-stelligen Code - keine IP, keine
    // Portfreigabe, keine Fritzbox mehr (Photon laeuft ueber die Cloud).
    // ======================================================================

    /// <summary>Startet/beendet Online-Kampfrunden (vom KampfModus-Menue aufgerufen).</summary>
    public static class KampfOnline
    {
        public static int BotAnzahl = 4;

        /// <summary>So viele Kaempfer insgesamt pro Runde - fehlende
        /// Plaetze werden beim Rundenstart mit Bots aufgefuellt.</summary>
        public static int ZielKaempfer = 5;

        /// <summary>Verbindet gerade (fuer "Verbinde..."-Anzeige statt Menue-Flackern).</summary>
        public static bool Verbindet;

        public static void Hoste(int bots)
        {
            if (!PruefePrefabs()) return;
            BotAnzahl = bots;
            Verbindet = true;
            KampfManager.Sicherstellen();
            PhotonRoomManager.Instanz.ErstelleRaum("neonblaster", "KampfSpieler", 7);
        }

        public static void Trete(string code)
        {
            if (!PruefePrefabs()) return;
            Verbindet = true;
            KampfManager.Sicherstellen();
            PhotonRoomManager.Instanz.TretRaumBei(code, "neonblaster", "KampfSpieler");
        }

        public static void Verlasse()
        {
            if (PhotonRoomManager.Instanz != null)
                PhotonRoomManager.Instanz.VerlasseRaum();
        }

        // Ohne die Netzwerk-Prefabs wuerde Online ins Leere starten (Menue weg,
        // kein Spieler). Dann lieber gar nicht starten und es klar sagen.
        static bool PruefePrefabs()
        {
            if (Resources.Load<GameObject>("KampfSpieler") != null &&
                Resources.Load<GameObject>("KampfBotNetz") != null)
                return true;

            Debug.LogError("KampfOnline: Netzwerk-Prefabs fehlen (KampfSpieler/KampfBotNetz in Resources).");
            if (KampfModus.Instanz != null)
                KampfModus.Instanz.ZeigeMeldung(
                    "Netzwerk-Prefabs fehlen! In Unity einmal neu kompilieren lassen " +
                    "(sie werden automatisch erstellt) oder Tools > FARBMIMIK > Netzwerk-Prefabs erstellen.");
            return false;
        }
    }

    // ======================================================================
    // KampfManager: haelt den Rundenzustand (laeuft eine Runde?) als Photon-
    // Room-Property und uebernimmt auf dem MasterClient das Bot-Spawnen und
    // die Rundenende-Pruefung. Existiert auf JEDEM Client (jeder liest den
    // Zustand mit) - wird von KampfOnline einmalig erzeugt.
    // ======================================================================
    public class KampfManager : MonoBehaviourPunCallbacks
    {
        public static KampfManager Instanz { get; private set; }

        /// <summary>Sicht aller Clients: laeuft gerade eine Runde? (false = Lobby)</summary>
        public static bool RundeLaeuft { get; private set; }

        const string K_RUNDE = "kampfRunde";

        Vector3 spawnBasis;
        bool spawnBasisGesetzt;
        bool rundeVorbeiGemeldet;
        float naechstePruefung;
        float raeumenBei;
        bool raeumenGeplant;

        public static void Sicherstellen()
        {
            if (Instanz != null) return;
            var go = new GameObject("KampfManager");
            DontDestroyOnLoad(go);
            Instanz = go.AddComponent<KampfManager>();
        }

        void Awake()
        {
            if (Instanz != null && Instanz != this) { Destroy(gameObject); return; }
            Instanz = this;
        }

        public override void OnJoinedRoom()
        {
            LiesRunde(PhotonNetwork.CurrentRoom.CustomProperties);
        }

        public override void OnLeftRoom()
        {
            RundeLaeuft = false;
            rundeVorbeiGemeldet = false;
            raeumenGeplant = false;
            spawnBasisGesetzt = false;
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            LiesRunde(propertiesThatChanged);
        }

        void LiesRunde(Hashtable props)
        {
            if (props != null && props.TryGetValue(K_RUNDE, out object r))
                RundeLaeuft = (bool)r;
        }

        static void SetzeRunde(bool laeuft)
        {
            RundeLaeuft = laeuft;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { K_RUNDE, laeuft } });
        }

        /// <summary>Host klickt in der Lobby auf RUNDE STARTEN.</summary>
        public void StarteRunde()
        {
            if (!PhotonNetwork.IsMasterClient || RundeLaeuft) return;

            int menschen = 0;
            foreach (var k in FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None))
            {
                if (k.istBot) { PhotonNetwork.Destroy(k.gameObject); continue; }
                menschen++;
                k.photonView.RPC(nameof(KampfNetzwerk.RpcReset), RpcTarget.All);
            }

            int botAnzahl = Mathf.Max(0, KampfOnline.ZielKaempfer - menschen);
            SpawneBots(botAnzahl);
            rundeVorbeiGemeldet = false;
            SetzeRunde(true);
        }

        void SpawneBots(int anzahl)
        {
            for (int i = 0; i < anzahl; i++)
            {
                // Bot-Nummer als InstantiationData: sofort verfuegbar in Awake,
                // ohne den RPC-Frame-Verzug (siehe KampfNetzwerk.Awake)
                object[] daten = { i % 4 + 1 };
                PhotonNetwork.InstantiateRoomObject("KampfBotNetz", SucheBotPlatz(i, anzahl),
                    Quaternion.identity, 0, daten);
            }
        }

        // Jeder Bot in einem eigenen Sektor rund um die Map
        Vector3 SucheBotPlatz(int index, int gesamt)
        {
            if (!spawnBasisGesetzt)
            {
                GameObject solo = GameObject.FindGameObjectWithTag("Player");
                spawnBasis = solo != null ? solo.transform.position : Vector3.zero;
                spawnBasisGesetzt = true;
            }

            Vector3 mitte = spawnBasis;
            Terrain terrain = BurggrabenMittelalter.AktivesTerrain;
            if (terrain != null)
                mitte = terrain.transform.position + terrain.terrainData.size * 0.5f;

            float sektor = 360f / Mathf.Max(1, gesamt);
            for (int versuch = 0; versuch < 40; versuch++)
            {
                float winkel = (index * sektor + Random.Range(-sektor * 0.4f, sektor * 0.4f)) * Mathf.Deg2Rad;
                float radius = Random.Range(10f, 25f);
                Vector3 kandidat = mitte + new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel)) * radius;
                if (BurggrabenMittelalter.IstGesperrt(kandidat)) continue;
                kandidat.y = BurggrabenMittelalter.BodenHoehe(kandidat) + 0.5f;
                return kandidat;
            }
            return mitte + Vector3.up * 0.5f;
        }

        void Update()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            if (raeumenGeplant && Time.time >= raeumenBei)
            {
                raeumenGeplant = false;
                foreach (var k in FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None))
                    if (k.istBot) PhotonNetwork.Destroy(k.gameObject);
                SetzeRunde(false);
            }

            if (!RundeLaeuft || rundeVorbeiGemeldet) return;
            if (Time.time < naechstePruefung) return;
            naechstePruefung = Time.time + 1f;

            var alle = FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None);
            if (alle.Length < 2) return;

            int lebendig = 0;
            KampfNetzwerk letzter = null;
            foreach (var k in alle)
                if (!k.tot) { lebendig++; letzter = k; }

            if (lebendig <= 1)
            {
                rundeVorbeiGemeldet = true;
                string sieger = letzter == null ? "niemand"
                    : letzter.istBot ? "Bot " + letzter.botNummer
                    : letzter.spielerName != "" ? letzter.spielerName
                    : "Spieler";
                // KampfManager hat selbst keine PhotonView - die Meldung laeuft
                // ueber die PhotonView einer beliebigen Figur (Sieger, sonst erste)
                KampfNetzwerk traeger = letzter != null ? letzter : (alle.Length > 0 ? alle[0] : null);
                if (traeger != null)
                    traeger.photonView.RPC(nameof(KampfNetzwerk.RpcRundenEndeGlobal), RpcTarget.All, sieger);
                raeumenBei = Time.time + 5f;   // Sieger-Anzeige kurz stehen lassen
                raeumenGeplant = true;
            }
        }
    }

    /// <summary>
    /// Ein Kaempfer in der Online-Runde - Mensch ODER Bot (istBot).
    /// Menschen: Ego-Steuerung (Maus + WASD, Linksklick schiesst).
    /// Bots: MasterClient-KI (wandern, Ziel suchen, mit Streuung schiessen).
    /// Gehoert auf die Prefabs KampfSpieler und KampfBotNetz
    /// (mit PhotonView + PhotonTransformView).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PhotonView))]
    public class KampfNetzwerk : MonoBehaviourPun
    {
        [Header("Bewegung")]
        public float tempo = 4f;
        public float botTempo = 1.1f;
        public float mausEmpfindlichkeit = 0.12f;

        [Header("Kampf")]
        public float reichweite = 45f;
        public int maxMunition = 3;
        public float nachladeZeit = 2.2f;
        public int maxLeben = 3;

        [Header("Bot-KI")]
        public float botSichtweite = 22f;
        public float botSchussPause = 1.4f;
        public float botStreuung = 7f;
        public float botWunschAbstand = 6f;

        public bool istBot;
        public int botNummer = 1;      // welches KI/Bot_N-Modell
        public string spielerName = "";
        public int leben;
        public bool tot;

        static string endText = "";
        static GameObject soloSpieler;   // deaktivierter Einzelspieler waehrend der Online-Runde

        CharacterController cc;
        GameObject visual;
        BotAnimation visualAnim;
        WeltHerzen herzen;
        BlitzUmkreisung blitzEffekt;
        Camera eigeneKamera;
        GUIStyle hudStil;
        float yaw, pitch, vertikal;
        int munition;
        float nachladeRest;
        Vector3 letztePos;
        float kopiertAnzeige;

        // Bot-KI (nur MasterClient)
        Vector3 wanderRichtung;
        float richtungsWechsel;
        float naechsterSchuss;

        bool MasterSteuertMich => photonView.IsMine;   // Bots sind Raum-Objekte -> IsMine nur auf MasterClient

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            leben = maxLeben;

            // Bot-Nummer als InstantiationData (siehe KampfManager.SpawneBots) -
            // sofort verfuegbar, kein RPC-Frame-Verzug
            object[] daten = photonView.InstantiationData;
            if (daten != null && daten.Length >= 1)
            {
                istBot = true;
                botNummer = (int)daten[0];
            }
        }

        void Start()
        {
            yaw = transform.eulerAngles.y;
            letztePos = transform.position;
            NeueWanderrichtung();
            BaueVisual();

            if (photonView.IsMine && !istBot)
            {
                LokalStart();
                photonView.RPC(nameof(RpcSetzeName), RpcTarget.AllBuffered, SpielerProfil.Name);
            }

            if (!(photonView.IsMine && !istBot))
            {
                herzen = gameObject.AddComponent<WeltHerzen>();
                herzen.maxLeben = maxLeben;
                herzen.SetzeLeben(leben);
            }
        }

        void LokalStart()
        {
            // Einzelspieler-Figur (samt ihrer Kamera) schlafen legen
            GameObject solo = GameObject.FindGameObjectWithTag("Player");
            if (solo != null) { soloSpieler = solo; solo.SetActive(false); }

            var kamGO = new GameObject("KampfNetz_Kamera") { tag = "MainCamera" };
            kamGO.transform.SetParent(transform, false);
            kamGO.transform.localPosition = new Vector3(0f, 0.46f, 0f);   // Augenhoehe der kleinen Figur
            eigeneKamera = kamGO.AddComponent<Camera>();
            kamGO.AddComponent<AudioListener>();

            munition = maxMunition;
            endText = "";
        }

        void OnDestroy()
        {
            if (photonView.IsMine && !istBot && soloSpieler != null)
            {
                soloSpieler.SetActive(true);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        [PunRPC]
        void RpcSetzeName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 14) name = name.Substring(0, 14);
            spielerName = name;
        }

        // Sichtbare Figur (Synty-Modell) fuer Bots und MITSPIELER -
        // die eigene Figur bleibt unsichtbar (Ego-Perspektive)
        void BaueVisual()
        {
            if (photonView.IsMine && !istBot) return;

            // Mitspieler-Modell so klein wie die eigene Figur (passt durch Tueren)
            float zielHoehe = istBot ? 0.6f : 0.5f;
            int nr = istBot ? botNummer : (Mathf.Max(1, photonView.OwnerActorNr) - 1) % 4 + 1;
            GameObject prefab = Resources.Load<GameObject>("KI/Bot_" + Mathf.Clamp(nr, 1, 4));

            if (prefab != null)
            {
                visual = Instantiate(prefab, transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                float hoehe = 0f;
                foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
                    hoehe = Mathf.Max(hoehe, r.bounds.size.y);
                if (hoehe > 0.01f)
                    visual.transform.localScale *= zielHoehe / hoehe;
                visualAnim = visual.AddComponent<BotAnimation>();
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(visual.GetComponent<Collider>());
                visual.transform.SetParent(transform, false);
                visual.transform.localPosition = Vector3.up * (zielHoehe * 0.5f);
                visual.transform.localScale = new Vector3(zielHoehe * 0.35f, zielHoehe * 0.5f, zielHoehe * 0.35f);
            }
        }

        // ---------- Ablauf ----------

        void Update()
        {
            bool runde = KampfManager.RundeLaeuft;

            // Bot-KI laeuft nur auf dem MasterClient (dort ist der Bot "IsMine")
            if (istBot && MasterSteuertMich && !tot && runde)
                BotKI();

            if (photonView.IsMine && !istBot)
            {
                // Cursor: in der Lobby frei (zum Klicken), im Kampf gesperrt
                bool sperren = runde && !tot;
                if (sperren && Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else if (!sperren && Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                if (runde && !tot) LokaleSteuerung();

                if (kopiertAnzeige > 0f) kopiertAnzeige -= Time.deltaTime;

                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                    KampfOnline.Verlasse();
            }

            AktualisiereVisualAnimation();
        }

        void LokaleSteuerung()
        {
            var maus = Mouse.current;
            var kb = Keyboard.current;
            if (maus == null || kb == null) return;

            // Umschauen
            Vector2 delta = maus.delta.ReadValue();
            yaw += delta.x * mausEmpfindlichkeit;
            pitch = Mathf.Clamp(pitch - delta.y * mausEmpfindlichkeit, -80f, 80f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (eigeneKamera != null)
                eigeneKamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Laufen + Springen (Leertaste)
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector3 richtung = (transform.right * x + transform.forward * z).normalized * tempo;
            if (cc.isGrounded)
            {
                vertikal = -1f;
                if (kb.spaceKey.wasPressedThisFrame)
                    vertikal = Mathf.Sqrt(2f * 0.6f * 20f);   // Sprunghoehe ~0.6 m
            }
            else vertikal -= 20f * Time.deltaTime;
            cc.Move((richtung + Vector3.up * vertikal) * Time.deltaTime);

            // Tueren oeffnen/schliessen mit E (wie im Solo-Spiel per Klick)
            if (kb.eKey.wasPressedThisFrame && eigeneKamera != null &&
                Physics.Raycast(eigeneKamera.transform.position, eigeneKamera.transform.forward,
                    out RaycastHit tuerHit, 2.5f, ~(1 << 4), QueryTriggerInteraction.Ignore))
            {
                var tuer = tuerHit.collider.GetComponentInParent<Door>();
                if (tuer != null) tuer.Toggle();
            }

            // Munition einzeln nachladen (Brawl-Stars-Art)
            if (munition < maxMunition)
            {
                nachladeRest -= Time.deltaTime;
                if (nachladeRest <= 0f)
                {
                    munition++;
                    nachladeRest = munition < maxMunition ? nachladeZeit : 0f;
                }
            }

            // Schiessen
            if (maus.leftButton.wasPressedThisFrame && munition > 0 && eigeneKamera != null)
            {
                if (munition == maxMunition) nachladeRest = nachladeZeit;
                munition--;
                Vector3 schussRichtung = eigeneKamera.transform.forward;
                Schiesse(eigeneKamera.transform.position + schussRichtung * 0.3f, schussRichtung);
            }
        }

        void AktualisiereVisualAnimation()
        {
            if (visualAnim == null) return;
            Vector3 pos = transform.position;
            bool bewegt = (pos - letztePos).sqrMagnitude > 0.0001f;
            letztePos = pos;
            if (!tot)
                visualAnim.MeldeBewegung(bewegt, true, true, 0f);
        }

        // ---------- Schiessen (Schuetze rechnet lokal, verteilt Effekt + Schaden) ----------

        void Schiesse(Vector3 start, Vector3 richtung)
        {
            if (tot || !KampfManager.RundeLaeuft) return;

            Vector3 ende = start + richtung * reichweite;
            Vector3 normal = -richtung;
            KampfNetzwerk getroffener = null;

            // Naechsten Treffer nehmen, der NICHT der eigene Koerper ist -
            // sonst blockt der eigene CharacterController den Schuss direkt
            // vor der Kamera
            RaycastHit[] alleTreffer = Physics.RaycastAll(start, richtung, reichweite,
                ~(1 << 4), QueryTriggerInteraction.Ignore);
            float naechste = float.MaxValue;
            foreach (var hit in alleTreffer)
            {
                var wer = hit.collider.GetComponentInParent<KampfNetzwerk>();
                if (wer == this) continue;   // eigener Koerper
                if (hit.distance >= naechste) continue;

                naechste = hit.distance;
                ende = hit.point;
                normal = hit.normal;
                getroffener = (wer != null && !wer.tot) ? wer : null;
            }

            // Schuss-Effekt fuer ALLE (getroffene ViewID zum Anheften des Farbkleckses)
            int zielView = getroffener != null ? getroffener.photonView.ViewID : 0;
            photonView.RPC(nameof(RpcSchussEffekt), RpcTarget.All, start, ende, normal, zielView);

            // Schaden meldet der Getroffene an den MasterClient (der rechnet Leben)
            if (getroffener != null)
                getroffener.photonView.RPC(nameof(RpcTrefferAnfrage), RpcTarget.MasterClient);
        }

        [PunRPC]
        void RpcSchussEffekt(Vector3 start, Vector3 ende, Vector3 normal, int zielView)
        {
            Transform anheften = null;
            if (zielView != 0)
            {
                PhotonView pv = PhotonView.Find(zielView);
                if (pv != null) anheften = pv.transform;

                // Treffer gelandet: kurze FREUDE beim Schuetzen (fuer Mitspieler
                // sichtbar - die eigene Figur ist in der Ego-Ansicht unsichtbar)
                visualAnim?.SpieleEinmalig(BotAnimation.CLIP_SIEG, 0.7f);
            }
            Farbschuss.Abfeuern(start, ende, normal, anheften);
        }

        // Nur auf dem MasterClient ausgefuehrt (RpcTarget.MasterClient)
        [PunRPC]
        void RpcTrefferAnfrage()
        {
            if (!PhotonNetwork.IsMasterClient || tot || !KampfManager.RundeLaeuft) return;
            int neuesLeben = leben - 1;
            photonView.RPC(nameof(RpcSetzeLeben), RpcTarget.All, neuesLeben);
            if (neuesLeben <= 0)
            {
                photonView.RPC(nameof(RpcSetzeTot), RpcTarget.All);
                if (istBot)
                    Invoke(nameof(EntferneBot), 4f);   // nach dem Umfallen liegen lassen
            }
        }

        void EntferneBot()
        {
            if (PhotonNetwork.IsMasterClient && this != null && gameObject != null)
                PhotonNetwork.Destroy(gameObject);
        }

        [PunRPC]
        void RpcSetzeLeben(int neu)
        {
            bool getroffenWorden = neu < leben && neu > 0;
            leben = neu;
            if (herzen != null) herzen.SetzeLeben(Mathf.Max(0, neu));

            // 1x abgeschossen (nicht toedlich): WACKELN + Sterne kreisen 3 s
            if (getroffenWorden)
            {
                visualAnim?.SpieleEinmalig(BotAnimation.CLIP_TREFFER_DIZZY, 1.2f);
                TodesSterneEffekt.Erzeuge(transform, Vector3.up * 0.7f);
            }

            // Letztes Leben: Blitze kreisen um die Figur - fuer ALLE sichtbar
            if (neu == 1 && !tot && blitzEffekt == null)
                blitzEffekt = BlitzUmkreisung.Erzeuge(transform);
            else if (neu != 1 && blitzEffekt != null)
            {
                Destroy(blitzEffekt.gameObject);
                blitzEffekt = null;
            }
        }

        [PunRPC]
        void RpcSetzeTot()
        {
            tot = true;
            if (visualAnim != null) visualAnim.SpieleTod();
            else if (visual != null) visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            if (herzen != null) herzen.SetzeLeben(0);
            if (blitzEffekt != null) { Destroy(blitzEffekt.gameObject); blitzEffekt = null; }

            if (photonView.IsMine && !istBot)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>Neue Runde: wiederbeleben und Figur frisch aufbauen.</summary>
        [PunRPC]
        public void RpcReset()
        {
            leben = maxLeben;
            tot = false;
            munition = maxMunition;
            nachladeRest = 0f;
            endText = "";

            if (visual != null && (photonView.IsMine == false || istBot))
            {
                Destroy(visual); visual = null; visualAnim = null;
                BaueVisual();
            }
            if (herzen != null) herzen.SetzeLeben(maxLeben);
        }

        [PunRPC]
        public void RpcRundenEndeGlobal(string sieger)
        {
            endText = "RUNDE VORBEI - Sieger: " + sieger;

            // Platz 1: SIEGERTANZ (dieser RPC laeuft auf der Figur des Gewinners)
            if (!tot)
                visualAnim?.SpieleEinmalig(BotAnimation.CLIP_TANZ, 5f);

            if (photonView.IsMine && !istBot)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // ---------- Bot-KI (nur MasterClient) ----------

        void BotKI()
        {
            Transform ziel = null;
            float beste = botSichtweite;
            foreach (var k in FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None))
            {
                if (k == this || k.tot) continue;
                float d = Vector3.Distance(transform.position, k.transform.position);
                if (d >= beste) continue;

                Vector3 augen = transform.position + Vector3.up * 0.35f;
                Vector3 punkt = k.transform.position + Vector3.up * 0.35f;   // Mitte der kleinen Figur
                if (Physics.Linecast(augen, punkt, out RaycastHit hit,
                        ~(1 << 4), QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<KampfNetzwerk>() != k)
                    continue;

                beste = d;
                ziel = k.transform;
            }

            Vector3 bewegung;
            if (ziel != null)
            {
                Vector3 zumZiel = ziel.position - transform.position;
                zumZiel.y = 0f;
                float abstand = zumZiel.magnitude;
                Vector3 richtungZiel = abstand > 0.01f ? zumZiel / abstand : transform.forward;

                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(richtungZiel), Time.deltaTime * 6f);

                if (abstand > botWunschAbstand + 1f) bewegung = richtungZiel;
                else if (abstand < botWunschAbstand - 1f) bewegung = -richtungZiel;
                else bewegung = Vector3.zero;

                if (Time.time >= naechsterSchuss)
                {
                    naechsterSchuss = Time.time + botSchussPause * Random.Range(0.8f, 1.3f);
                    Vector3 start = transform.position + Vector3.up * 0.35f;
                    Vector3 richtung = (ziel.position + Vector3.up * 0.35f - start).normalized;
                    richtung = Quaternion.Euler(Random.Range(-botStreuung, botStreuung),
                                                Random.Range(-botStreuung, botStreuung), 0f) * richtung;
                    Schiesse(start + richtung * 0.2f, richtung);
                }
            }
            else
            {
                richtungsWechsel -= Time.deltaTime;
                if (richtungsWechsel <= 0f) NeueWanderrichtung();
                bewegung = wanderRichtung;
                if (bewegung.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(bewegung), Time.deltaTime * 4f);
            }

            if (BurggrabenMittelalter.IstGesperrt(transform.position + bewegung * 1.2f))
            {
                NeueWanderrichtung();
                bewegung = Vector3.zero;
            }

            vertikal = cc.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
            cc.Move((bewegung * botTempo + Vector3.up * vertikal) * Time.deltaTime);
        }

        void NeueWanderrichtung()
        {
            float winkel = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            wanderRichtung = new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel));
            richtungsWechsel = Random.Range(2.5f, 5f);
        }

        // ---------- HUD (nur lokaler Spieler) ----------

        static Texture2D lobbyKartenTex;
        GUIStyle lobbyTextStil, lobbyTitelStil;

        // Online-Lobby: Beitritts-Code, Spielerliste mit Namen, Host startet
        // die Runde - auf leicht weissem Grund, Hintergrund bleibt sichtbar
        void ZeichneLobby(float sw, float sh)
        {
            if (lobbyKartenTex == null)
            {
                lobbyKartenTex = new Texture2D(1, 1);
                lobbyKartenTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.85f));
                lobbyKartenTex.Apply();
            }
            if (lobbyTextStil == null)
                lobbyTextStil = new GUIStyle(GUI.skin.label)
                { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true };
            if (lobbyTitelStil == null)
                lobbyTitelStil = new GUIStyle(GUI.skin.label)
                { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            lobbyTextStil.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
            lobbyTitelStil.normal.textColor = new Color(0.05f, 0.05f, 0.08f);

            Color alt = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
            GUI.color = alt;

            var karte = new Rect(sw * 0.5f - sw * 0.22f, sh * 0.08f, sw * 0.44f, sh * 0.8f);
            GUI.DrawTexture(karte, lobbyKartenTex);

            float y = karte.y + sh * 0.02f;
            lobbyTitelStil.fontSize = Mathf.RoundToInt(sh * 0.04f);
            GUI.Label(new Rect(karte.x, y, karte.width, sh * 0.05f), "ONLINE-LOBBY", lobbyTitelStil);
            y += sh * 0.06f;

            lobbyTextStil.fontSize = Mathf.RoundToInt(sh * 0.032f);
            GUI.Label(new Rect(karte.x, y, karte.width, sh * 0.05f),
                      "Beitritts-Code: " + PhotonRoomManager.RoomCode, lobbyTextStil);
            y += sh * 0.055f;

            if (PhotonNetwork.IsMasterClient)
            {
                var kopierKnopf = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                kopierKnopf.fontSize = Mathf.RoundToInt(sh * 0.02f);
                string kopierText = kopiertAnzeige > 0f ? "Kopiert! Jetzt verschicken." : "CODE KOPIEREN";
                if (GUI.Button(new Rect(karte.x + karte.width * 0.2f, y, karte.width * 0.6f, sh * 0.05f),
                        kopierText, kopierKnopf))
                {
                    GUIUtility.systemCopyBuffer =
                        "Beitritts-Code: " + PhotonRoomManager.RoomCode +
                        " - Spiel NEON CATCH starten, RUNDE BEITRETEN klicken und den Code eintippen!";
                    kopiertAnzeige = 2f;
                }
                y += sh * 0.06f;

                lobbyTextStil.fontSize = Mathf.RoundToInt(sh * 0.018f);
                GUI.Label(new Rect(karte.x + karte.width * 0.05f, y, karte.width * 0.9f, sh * 0.06f),
                          "Schick deinen Freunden den Code (Knopf oben). Kein Router, keine IP - " +
                          "Photon verbindet euch automatisch übers Internet.", lobbyTextStil);
                y += sh * 0.07f;
            }

            // Wer tritt dem Spiel bei?
            int menschen = 0;
            string liste = "";
            foreach (var k in FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None))
            {
                if (k.istBot) continue;
                menschen++;
                liste += (k.spielerName != "" ? k.spielerName : "Spieler");
                if (k.photonView.IsMine) liste += "  <- DU";
                liste += "\n";
            }
            int botsZumAuffuellen = Mathf.Max(0, KampfOnline.ZielKaempfer - menschen);
            if (botsZumAuffuellen > 0)
                liste += "+ " + botsZumAuffuellen + " Bot(s) füllen die Runde auf\n";

            lobbyTextStil.fontSize = Mathf.RoundToInt(sh * 0.024f);
            GUI.Label(new Rect(karte.x, y, karte.width, sh * 0.22f),
                      "Wer tritt bei (" + menschen + "/7):\n" + liste, lobbyTextStil);
            y += sh * 0.2f;

            lobbyTextStil.fontSize = Mathf.RoundToInt(sh * 0.019f);
            GUI.Label(new Rect(karte.x + karte.width * 0.05f, y, karte.width * 0.9f, sh * 0.07f),
                      "So geht's: Jeder gegen jeden mit Farb-Blastern - 3 Leben, 3 Schuss, die einzeln nachladen. " +
                      "Wer als Letzter übrig ist, gewinnt die Runde!", lobbyTextStil);
            y += sh * 0.075f;

            if (endText != "")
            {
                lobbyTextStil.fontSize = Mathf.RoundToInt(sh * 0.026f);
                GUI.Label(new Rect(karte.x, y, karte.width, sh * 0.05f), endText, lobbyTextStil);
            }
            y += sh * 0.055f;

            var knopf = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, wordWrap = true };
            knopf.fontSize = Mathf.RoundToInt(sh * 0.028f);
            if (PhotonNetwork.IsMasterClient)
            {
                if (GUI.Button(new Rect(karte.x + karte.width * 0.15f, y, karte.width * 0.7f, sh * 0.07f),
                        "RUNDE STARTEN", knopf))
                    if (KampfManager.Instanz != null) KampfManager.Instanz.StarteRunde();
            }
            else
            {
                lobbyTextStil.fontSize = Mathf.RoundToInt(sh * 0.024f);
                GUI.Label(new Rect(karte.x, y + sh * 0.01f, karte.width, sh * 0.06f),
                          "Warte, bis der Host die Runde startet ...", lobbyTextStil);
            }
            y += sh * 0.08f;

            knopf.fontSize = Mathf.RoundToInt(sh * 0.022f);
            if (GUI.Button(new Rect(karte.x + karte.width * 0.25f, y, karte.width * 0.5f, sh * 0.05f),
                    "VERLASSEN (ESC)", knopf))
                KampfOnline.Verlasse();
        }

        void OnGUI()
        {
            if (!(photonView.IsMine && !istBot)) return;

            if (hudStil == null)
                hudStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            float sw = Screen.width, sh = Screen.height;

            // Lobby statt Kampf-HUD, solange keine Runde laeuft
            if (!KampfManager.RundeLaeuft)
            {
                ZeichneLobby(sw, sh);
                return;
            }

            hudStil.alignment = TextAnchor.UpperLeft;
            hudStil.fontSize = Mathf.RoundToInt(sh * 0.024f);
            hudStil.normal.textColor = Color.white;
            GUI.Label(new Rect(14f, sh * 0.095f, sw * 0.9f, sh * 0.05f),
                      "Code: " + PhotonRoomManager.RoomCode, hudStil);

            // Herzen
            hudStil.fontSize = Mathf.RoundToInt(sh * 0.05f);
            hudStil.normal.textColor = new Color(0.95f, 0.15f, 0.15f);
            string herzText = "";
            for (int i = 0; i < maxLeben; i++) herzText += i < leben ? "♥ " : "♡ ";
            GUI.Label(new Rect(14f, sh * 0.01f, sw * 0.3f, sh * 0.08f), herzText, hudStil);

            // Munitions-Pips
            float pip = sh * 0.035f;
            float abstand = pip * 0.35f;
            float startX = sw * 0.5f - (maxMunition * pip + (maxMunition - 1) * abstand) * 0.5f;
            Color alt = GUI.color;
            for (int i = 0; i < maxMunition; i++)
            {
                var rect = new Rect(startX + i * (pip + abstand), sh * 0.88f, pip, pip);
                GUI.color = i < munition ? new Color(0.2f, 0.85f, 1f) : new Color(0.12f, 0.12f, 0.12f, 0.8f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
            }
            GUI.color = alt;

            if (!tot)
                GUI.Box(new Rect(sw * 0.5f - 2f, sh * 0.5f - 2f, 4f, 4f), GUIContent.none);

            hudStil.alignment = TextAnchor.UpperCenter;
            if (tot && endText == "")
            {
                hudStil.fontSize = Mathf.RoundToInt(sh * 0.038f);
                hudStil.normal.textColor = Color.white;
                GUI.Label(new Rect(0f, sh * 0.3f, sw, sh * 0.1f),
                          "GESTORBEN - schau den anderen zu   (ESC = Verlassen)", hudStil);
            }
            if (endText != "")
            {
                hudStil.fontSize = Mathf.RoundToInt(sh * 0.04f);
                hudStil.normal.textColor = Color.yellow;
                GUI.Label(new Rect(0f, sh * 0.04f, sw, sh * 0.1f), endText, hudStil);
            }
            hudStil.alignment = TextAnchor.UpperLeft;
        }
    }
}
