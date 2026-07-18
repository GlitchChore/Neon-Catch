using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonCatch
{
    // ======================================================================
    // ONLINE-KAMPFMODUS: derselbe Abschiess-Modus, aber mit Freunden UND
    // Bots zusammen. Der Server steuert die Bots und rechnet alle Treffer,
    // die Clients sehen synchronisierte Figuren, Schuesse und Farbkleckse.
    // Room-Code-Schutz und kcp2k kommen vom LobbyManager (wie FARBMIMIK).
    // ======================================================================

    /// <summary>Startet/beendet Online-Kampfrunden (vom KampfModus-Menue aufgerufen).</summary>
    public static class KampfOnline
    {
        public static int BotAnzahl = 4;

        public static void Hoste(int bots)
        {
            BotAnzahl = bots;
            HoleManager().StartHost();
        }

        public static void Trete(string ip, string code)
        {
            NetworkManager manager = HoleManager();
            LobbyManager.EingegebenerCode = (code ?? "").Trim().ToUpper();
            manager.networkAddress = string.IsNullOrWhiteSpace(ip) ? "localhost" : ip.Trim();
            manager.StartClient();
        }

        public static void Verlasse()
        {
            if (NetworkServer.active && NetworkClient.isConnected)
                NetworkManager.singleton.StopHost();
            else if (NetworkClient.active)
                NetworkManager.singleton.StopClient();
        }

        static NetworkManager HoleManager()
        {
            if (NetworkManager.singleton is KampfLobbyManager fertig)
                return fertig;

            if (NetworkManager.singleton != null)
            {
                Debug.LogError("KampfOnline: In dieser Szene liegt schon ein anderer NetworkManager " +
                               "(altes MenuUI/NetworkManagerSetup?). Bitte aus der Szene entfernen!");
                return NetworkManager.singleton;
            }

            var go = new GameObject("Kampf_NetzwerkManager");
            return go.AddComponent<KampfLobbyManager>();
        }
    }

    /// <summary>
    /// NetworkManager fuer den Online-Kampf: nutzt das KampfSpieler-Prefab,
    /// spawnt nach dem ersten Spieler die Server-Bots.
    /// </summary>
    public class KampfLobbyManager : LobbyManager
    {
        bool botsGespawnt;
        Vector3 spawnBasis;
        bool spawnBasisGesetzt;

        public override void Awake()
        {
            playerPrefab = Resources.Load<GameObject>("KampfSpieler");
            if (playerPrefab == null)
                Debug.LogError("KampfLobbyManager: Prefab 'KampfSpieler' fehlt! Im Unity-Menue " +
                               "Tools > FARBMIMIK > Netzwerk-Prefabs erstellen ausfuehren.");

            GameObject botPrefab = Resources.Load<GameObject>("KampfBotNetz");
            if (botPrefab != null && !spawnPrefabs.Contains(botPrefab))
                spawnPrefabs.Add(botPrefab);

            base.Awake();
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (!spawnBasisGesetzt)
            {
                GameObject solo = GameObject.FindGameObjectWithTag("Player");
                spawnBasis = solo != null ? solo.transform.position : Vector3.zero;
                spawnBasisGesetzt = true;
            }

            // Spieler leicht versetzt nebeneinander spawnen
            Vector3 pos = spawnBasis + Quaternion.Euler(0f, numPlayers * 51f, 0f) *
                          Vector3.forward * (numPlayers > 0 ? 2f : 0f);
            GameObject spieler = Instantiate(playerPrefab, pos, Quaternion.identity);
            NetworkServer.AddPlayerForConnection(conn, spieler);

            if (!botsGespawnt)
            {
                botsGespawnt = true;
                SpawneBots();
            }
        }

        public override void OnStopServer()
        {
            botsGespawnt = false;
            spawnBasisGesetzt = false;
            base.OnStopServer();
        }

        void SpawneBots()
        {
            GameObject botPrefab = Resources.Load<GameObject>("KampfBotNetz");
            if (botPrefab == null) return;

            for (int i = 0; i < KampfOnline.BotAnzahl; i++)
            {
                GameObject bot = Instantiate(botPrefab, SucheBotPlatz(i, KampfOnline.BotAnzahl),
                                             Quaternion.identity);
                bot.name = "NetzBot_" + (i + 1);
                var netz = bot.GetComponent<KampfNetzwerk>();
                netz.istBot = true;
                netz.botNummer = i % 4 + 1;
                NetworkServer.Spawn(bot);
            }
        }

        // Gleiche Idee wie beim Solo-Kampf: jeder Bot im eigenen Sektor rund um die Map
        Vector3 SucheBotPlatz(int index, int gesamt)
        {
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
    }

    /// <summary>
    /// Ein Kaempfer in der Online-Runde - Mensch ODER Bot (istBot).
    /// Menschen: Ego-Steuerung (Maus + WASD, Linksklick schiesst).
    /// Bots: Server-KI (wandern, Ziel suchen, mit Streuung schiessen).
    /// Gehoert auf die Prefabs KampfSpieler und KampfBotNetz.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class KampfNetzwerk : NetworkBehaviour
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

        [SyncVar] public bool istBot;
        [SyncVar] public int botNummer = 1;      // welches KI/Bot_N-Modell
        [SyncVar] public int spielerNummer;      // 1-7 fuer Menschen
        [SyncVar(hook = nameof(BeiLebenAenderung))] public int leben;
        [SyncVar(hook = nameof(BeiTod))] public bool tot;

        static string endText = "";
        static GameObject soloSpieler;   // deaktivierter Einzelspieler waehrend der Online-Runde

        CharacterController cc;
        GameObject visual;
        BotAnimation visualAnim;
        WeltHerzen herzen;
        Camera eigeneKamera;
        GUIStyle hudStil;
        float yaw, pitch, vertikal;
        int munition;
        float nachladeRest;
        Vector3 letztePos;
        bool rundeVorbeiGemeldet;
        float naechstePruefung;

        // Bot-KI (nur Server)
        Vector3 wanderRichtung;
        float richtungsWechsel;
        float naechsterSchuss;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
        }

        // ---------- Start / Ende ----------

        public override void OnStartServer()
        {
            leben = maxLeben;
            if (!istBot)
            {
                bool[] belegt = new bool[8];
                foreach (var s in FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None))
                    if (s != this && !s.istBot && s.spielerNummer >= 1 && s.spielerNummer <= 7)
                        belegt[s.spielerNummer] = true;
                for (int i = 1; i <= 7; i++)
                    if (!belegt[i]) { spielerNummer = i; break; }
            }
            NeueWanderrichtung();
        }

        public override void OnStartClient()
        {
            yaw = transform.eulerAngles.y;
            letztePos = transform.position;
            BaueVisual();

            if (!isLocalPlayer)
            {
                herzen = gameObject.AddComponent<WeltHerzen>();
                herzen.maxLeben = maxLeben;
                herzen.SetzeLeben(leben);

                // Herzen sitzen fuer 0.75-m-Bots richtig - bei 1.55-m-Menschen hochschieben
                if (!istBot)
                    foreach (Transform kind in transform)
                        if (kind.name == "Herzen_Anzeige")
                            kind.localPosition = new Vector3(kind.localPosition.x, 1.35f, kind.localPosition.z);
            }
        }

        public override void OnStartLocalPlayer()
        {
            // Einzelspieler-Figur (samt ihrer Kamera) schlafen legen
            GameObject solo = GameObject.FindGameObjectWithTag("Player");
            if (solo != null) { soloSpieler = solo; solo.SetActive(false); }

            var kamGO = new GameObject("KampfNetz_Kamera") { tag = "MainCamera" };
            kamGO.transform.SetParent(transform, false);
            kamGO.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            eigeneKamera = kamGO.AddComponent<Camera>();
            kamGO.AddComponent<AudioListener>();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            munition = maxMunition;
            endText = "";
        }

        public override void OnStopLocalPlayer()
        {
            if (soloSpieler != null) soloSpieler.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Sichtbare Figur (Synty-Modell) fuer Bots und MITSPIELER -
        // die eigene Figur bleibt unsichtbar (Ego-Perspektive)
        void BaueVisual()
        {
            if (isLocalPlayer) return;

            float zielHoehe = istBot ? 0.75f : 1.55f;
            int nr = istBot ? botNummer : (Mathf.Max(1, spielerNummer) - 1) % 4 + 1;
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
            if (isServer && istBot && !tot)
                BotKI();

            if (isLocalPlayer)
            {
                if (!tot) LokaleSteuerung();

                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                    KampfOnline.Verlasse();
            }

            // Rundenende prueft genau EIN Objekt auf dem Server (Spieler 1)
            if (isServer && !istBot && spielerNummer == 1)
                PruefeRundenEnde();

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

            // Laufen
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector3 richtung = (transform.right * x + transform.forward * z).normalized * tempo;
            vertikal = cc.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
            cc.Move((richtung + Vector3.up * vertikal) * Time.deltaTime);

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
                CmdSchiesse(eigeneKamera.transform.position + schussRichtung * 0.3f, schussRichtung);
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

        // ---------- Schiessen (Server rechnet, alle sehen) ----------

        [Command]
        void CmdSchiesse(Vector3 start, Vector3 richtung)
        {
            if (tot) return;
            FuehreSchussAus(start, richtung);
        }

        [Server]
        void FuehreSchussAus(Vector3 start, Vector3 richtung)
        {
            Vector3 ende = start + richtung * reichweite;
            Vector3 normal = -richtung;
            uint getroffenId = 0;

            if (Physics.Raycast(start, richtung, out RaycastHit hit, reichweite,
                    ~(1 << 4), QueryTriggerInteraction.Ignore))
            {
                ende = hit.point;
                normal = hit.normal;
                var ziel = hit.collider.GetComponentInParent<KampfNetzwerk>();
                if (ziel != null && ziel != this && !ziel.tot)
                {
                    getroffenId = ziel.netId;
                    ziel.NimmSchaden();
                }
            }
            RpcSchussEffekt(start, ende, normal, getroffenId);
        }

        [ClientRpc]
        void RpcSchussEffekt(Vector3 start, Vector3 ende, Vector3 normal, uint getroffenId)
        {
            Transform anheften = null;
            if (getroffenId != 0 && NetworkClient.spawned.TryGetValue(getroffenId, out NetworkIdentity id))
                anheften = id.transform;
            Farbschuss.Abfeuern(start, ende, normal, anheften);
        }

        [Server]
        public void NimmSchaden()
        {
            if (tot) return;
            leben--;
            if (leben <= 0)
            {
                tot = true;
                if (istBot)
                    Invoke(nameof(EntferneBot), 4f);   // nach dem Umfallen liegen lassen
            }
        }

        [Server]
        void EntferneBot()
        {
            if (gameObject != null)
                NetworkServer.Destroy(gameObject);
        }

        // ---------- SyncVar-Hooks ----------

        void BeiLebenAenderung(int alt, int neu)
        {
            if (herzen != null) herzen.SetzeLeben(neu);
        }

        void BeiTod(bool alt, bool neu)
        {
            if (!neu) return;

            if (visualAnim != null) visualAnim.SpieleTod();
            else if (visual != null) visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            if (herzen != null) herzen.SetzeLeben(0);

            if (isLocalPlayer)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // ---------- Rundenende ----------

        [Server]
        void PruefeRundenEnde()
        {
            if (rundeVorbeiGemeldet || Time.time < naechstePruefung) return;
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
                    : "Spieler " + letzter.spielerNummer;
                RpcRundenEnde(sieger);
            }
        }

        [ClientRpc]
        void RpcRundenEnde(string sieger)
        {
            endText = "RUNDE VORBEI - Sieger: " + sieger + "   (ESC = Verlassen)";
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ---------- Bot-KI (Server) ----------

        void BotKI()
        {
            // Naechstes lebendes, sichtbares Ziel (Mensch ODER anderer Bot)
            Transform ziel = null;
            float beste = botSichtweite;
            foreach (var k in FindObjectsByType<KampfNetzwerk>(FindObjectsSortMode.None))
            {
                if (k == this || k.tot) continue;
                float d = Vector3.Distance(transform.position, k.transform.position);
                if (d >= beste) continue;

                Vector3 augen = transform.position + Vector3.up * 0.45f;
                Vector3 punkt = k.transform.position + Vector3.up * 0.8f;
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
                    Vector3 start = transform.position + Vector3.up * 0.45f;
                    Vector3 richtung = (ziel.position + Vector3.up * 0.5f - start).normalized;
                    // absichtliche Streuung - Bots treffen nicht immer
                    richtung = Quaternion.Euler(Random.Range(-botStreuung, botStreuung),
                                                Random.Range(-botStreuung, botStreuung), 0f) * richtung;
                    FuehreSchussAus(start + richtung * 0.2f, richtung);
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

        void OnGUI()
        {
            if (!isLocalPlayer) return;

            if (hudStil == null)
                hudStil = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            float sw = Screen.width, sh = Screen.height;

            // Room-Code oben links (der Host gibt Code + IP an seine Freunde)
            hudStil.alignment = TextAnchor.UpperLeft;
            hudStil.fontSize = Mathf.RoundToInt(sh * 0.024f);
            hudStil.normal.textColor = Color.white;
            string info = "Room-Code: " + LobbyManager.RoomCode;
            if (NetworkServer.active)
                info += "   (Freunde brauchen deine IP + diesen Code)";
            GUI.Label(new Rect(14f, sh * 0.095f, sw * 0.9f, sh * 0.05f), info, hudStil);

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

            // Fadenkreuz
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
