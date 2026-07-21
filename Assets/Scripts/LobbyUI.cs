using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Spielerprofil: einfacher Name, gespeichert in PlayerPrefs.
/// Wird in beiden Modi in Lobby und Spielerliste angezeigt.
/// </summary>
public static class SpielerProfil
{
    const string Schluessel = "NeonCatch_SpielerName";

    public static string Name
    {
        get
        {
            string n = PlayerPrefs.GetString(Schluessel, "").Trim();
            return n == "" ? "Spieler" : n;
        }
        set
        {
            string n = (value ?? "").Trim();
            if (n.Length > 14) n = n.Substring(0, 14);
            PlayerPrefs.SetString(Schluessel, n);
        }
    }
}

/// <summary>
/// Eingebaute Hilfe-Texte für BEIDE Modi - getrennt nach Rolle. Dank Photon
/// braucht es keine IP, keine Portfreigabe und keine Fritzbox mehr - der
/// 4-stellige Code reicht.
/// </summary>
public static class NetzwerkHilfe
{
    public const string HostAnleitung =
        "DU BIST DER HOST (RUNDE ERSTELLEN)\n" +
        "\n" +
        "1. 'Runde erstellen' klicken - du bekommst einen 4-stelligen Code\n" +
        "2. Schick deinen Freunden diesen Code (Kopieren-Knopf in der Lobby)\n" +
        "3. Sobald sie den Code eingegeben haben, seht ihr euch in der Lobby\n" +
        "4. Wenn alle da sind: SPIEL STARTEN klicken\n" +
        "\n" +
        "Das war's! Photon läuft über einen Cloud-Server - keine IP nötig,\n" +
        "keine Portfreigabe, keine Fritzbox-Einstellungen. Funktioniert\n" +
        "einfach übers Internet, egal wo ihr seid.";

    public const string BeitretenAnleitung =
        "DU TRITTST EINER RUNDE BEI\n" +
        "\n" +
        "1. Frag den Host nach dem 4-stelligen Code\n" +
        "2. Code eintragen (z.B. X7K9)\n" +
        "3. 'Beitreten' klicken - fertig!\n" +
        "\n" +
        "Du musst nichts weiter einstellen - keine IP, kein Router.\n" +
        "\n" +
        "KOMMST DU NICHT REIN?\n" +
        "- Code Buchstabe für Buchstabe prüfen\n" +
        "- Der Host muss die Runde schon erstellt haben\n" +
        "- Internetverbindung prüfen (Photon braucht eine Verbindung\n" +
        "    zum Server, auch im gleichen WLAN)";
}

/// <summary>
/// Komplette FARBMIMIK-UI, wird zur Laufzeit selbst aufgebaut:
/// - Hauptmenue: "Runde erstellen" / "Runde beitreten" (Code eingeben)
/// - Lobby: Code gross, Spielerliste (max 7), Start-Button NUR fuer den Host
/// - Spiel-HUD: Phase + Timer + Sucher-Anzeige
/// Gehoert auf dasselbe GameObject "Netzwerk" wie der PhotonRoomManager.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    // Erzeugt die FARBMIMIK-UI automatisch, sobald die "Farbmimik"-Szene
    // geladen ist (beim Start UND bei jedem Szenenwechsel) - so muss die
    // Szene selbst kein UI-Objekt enthalten.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, modus) => ErzeugeWennFarbmimik(scene);
        ErzeugeWennFarbmimik(SceneManager.GetActiveScene());
    }

    static void ErzeugeWennFarbmimik(Scene scene)
    {
        if (scene.name != "Farbmimik") return;
        if (FindAnyObjectByType<LobbyUI>() != null) return;
        new GameObject("FarbmimikUI").AddComponent<LobbyUI>();
    }

    Font schrift;
    GameObject hauptPanel, beitretenPanel, lobbyPanel, hudPanel, endePanel, hilfePanel;
    Text hilfeInhaltText, beitretenStatusText;
    GameObject sucherWartePanel, skipButton;
    Text sucherWarteText;
    RawImage hintergrundBild;
    Image weisserSchleier;
    GameObject verbindePanel;
    bool verbindetGerade;
    string zuletztGezeigterFehler = "";
    InputField codeFeld;
    Text lobbyCodeText, spielerListeText, hudText, sucherText, endeText, platzierungenText;
    GameObject startButton, nochmalButton, kopierButton, sucherWahlButton;

    static readonly Color Neon = new Color(0.1f, 0.95f, 0.95f);

    float kopiertAnzeige;

    void Start()
    {
        schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // FARBMIMIK braucht einen GamePhaseManager - falls keiner in der Szene
        // liegt, hier automatisch erzeugen (spart einen manuellen Einbau-Schritt)
        if (GamePhaseManager.Instance == null)
            new GameObject("GamePhaseManager").AddComponent<GamePhaseManager>();

        BaueUI();

        // Kam der Spieler aus dem anderen Modus (NEON-BLASTER-Menue)?
        // AutoSolo = Solo gegen Bots (offline), AutoHost = Online-Runde.
        if (PlayerPrefs.GetInt("NeonCatch_AutoSolo", 0) == 1)
        {
            PlayerPrefs.SetInt("NeonCatch_AutoSolo", 0);
            StarteSolo();
        }
        else if (PlayerPrefs.GetInt("NeonCatch_AutoHost", 0) == 1)
        {
            PlayerPrefs.SetInt("NeonCatch_AutoHost", 0);
            if (Resources.Load<GameObject>("Spieler") != null)
                PhotonRoomManager.Instanz.ErstelleRaum("farbmimik", "Spieler", 7);
        }
    }

    void StarteSolo()
    {
        if (Resources.Load<GameObject>("Spieler") == null)
        {
            ZeigeHilfe("SPIELER-PREFAB FEHLT!\n\nIn Unity einmal neu kompilieren lassen\n" +
                       "(das Prefab wird automatisch erstellt) oder im Menue\n" +
                       "Tools > FARBMIMIK > Netzwerk-Prefabs erstellen klicken.");
            return;
        }
        PhotonRoomManager.Instanz.StarteSolo("farbmimik", "Spieler");
    }

    void Update()
    {
        bool verbunden = PhotonNetwork.InRoom;
        SpielPhase phase = GamePhaseManager.Instance != null
            ? GamePhaseManager.Instance.phase
            : SpielPhase.Lobby;

        // Taste Z = zurueck (auch wenn der Mauszeiger im Spiel gesperrt ist).
        // Y mit dabei, weil auf deutscher Tastatur die Z-Taste physisch dort
        // liegt, wo das Input System "Y" erwartet.
        var kb = Keyboard.current;
        if (kb != null && (kb.zKey.wasPressedThisFrame || kb.yKey.wasPressedThisFrame))
            GlobalerZurueck();

        // Hintergrundbild ueberall AUSSER im laufenden Spiel (Verstecken/Suchen)
        bool bildSichtbar = !(verbunden && (phase == SpielPhase.Verstecken || phase == SpielPhase.Suchen));
        if (hintergrundBild != null) hintergrundBild.enabled = bildSichtbar;
        if (weisserSchleier != null) weisserSchleier.enabled = bildSichtbar;

        // Fehlgeschlagener Beitritts-/Erstellungs-Versuch? (z.B. falscher Code)
        if (!verbunden && PhotonRoomManager.FehlerText != "" &&
            PhotonRoomManager.FehlerText != zuletztGezeigterFehler)
        {
            zuletztGezeigterFehler = PhotonRoomManager.FehlerText;
            hauptPanel.SetActive(false);
            hilfePanel.SetActive(false);
            beitretenPanel.SetActive(true);
            beitretenStatusText.text = PhotonRoomManager.FehlerText;
        }
        if (verbunden) zuletztGezeigterFehler = "";

        // Verbinde-Zustand beenden, sobald man im Raum ist oder ein Fehler kam
        if (verbindetGerade && (verbunden || PhotonRoomManager.FehlerText != ""))
            verbindetGerade = false;
        verbindePanel.SetActive(verbindetGerade);

        hauptPanel.SetActive(!verbunden && !verbindetGerade && !beitretenPanel.activeSelf && !hilfePanel.activeSelf);
        if (verbunden && beitretenPanel.activeSelf)
            beitretenPanel.SetActive(false);

        lobbyPanel.SetActive(verbunden && phase == SpielPhase.Lobby);
        hudPanel.SetActive(verbunden && (phase == SpielPhase.Verstecken || phase == SpielPhase.Suchen));
        endePanel.SetActive(verbunden && phase == SpielPhase.Ende);

        if (lobbyPanel.activeSelf)
            AktualisiereLobby();
        if (hudPanel.activeSelf)
            AktualisiereHud(phase);
        if (endePanel.activeSelf)
        {
            nochmalButton.SetActive(PhotonNetwork.IsMasterClient);
            AktualisiereEnde();
        }

        // Schwarzer Sucher-Bildschirm: waehrend VERSTECKEN sieht der Sucher nur
        // "DU BIST SUCHER" + Countdown - die anderen verstecken sich derweil.
        bool binSucherUndWartet = false;
        if (verbunden && phase == SpielPhase.Verstecken && GamePhaseManager.Instance != null)
        {
            var lokal = LokalerSpieler();
            if (lokal != null && GamePhaseManager.Instance.IstSucher(lokal.photonView.ViewID))
                binSucherUndWartet = true;
        }
        sucherWartePanel.SetActive(binSucherUndWartet);
        if (binSucherUndWartet)
        {
            sucherWarteText.text = "DU BIST SUCHER\n\nDie anderen verstecken sich...\nNoch " +
                                   GamePhaseManager.Instance.restSekunden + "s";
            skipButton.SetActive(PhotonNetwork.IsMasterClient);   // nur Host/Solo darf ueberspringen
        }
    }

    static SelfPaintSystem LokalerSpieler()
    {
        return FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
               .FirstOrDefault(s => s.photonView.IsMine && !s.istBot);
    }

    // ---------- Anzeigen aktualisieren ----------

    void AktualisiereLobby()
    {
        if (kopiertAnzeige > 0f) kopiertAnzeige -= Time.deltaTime;

        // Solo (offline): kein Code, kein Kopieren-Knopf
        if (PhotonRoomManager.IstSolo)
        {
            lobbyCodeText.text = "SOLO gegen Bots";
            kopierButton.SetActive(false);
        }
        else
        {
            lobbyCodeText.text = "BEITRITTS-CODE: " + PhotonRoomManager.RoomCode;
            kopierButton.SetActive(true);
            var knopfText = kopierButton.GetComponentInChildren<Text>();
            if (knopfText != null)
                knopfText.text = kopiertAnzeige > 0f ? "Kopiert! An Freunde schicken" : "CODE KOPIEREN";
        }

        var spieler = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                      .OrderBy(s => s.istBot ? int.MaxValue : (s.photonView.Owner != null ? s.photonView.Owner.ActorNumber : 0))
                      .ToArray();

        string liste = "Spieler (" + spieler.Length + "/7):\n";
        foreach (var s in spieler)
        {
            bool istHost = !s.istBot && s.photonView.Owner != null && s.photonView.Owner.IsMasterClient;
            liste += (s.spielerName != "" ? s.spielerName : "Spieler");
            if (istHost) liste += " (Host)";
            if (s.photonView.IsMine && !s.istBot) liste += "  <- DU";
            liste += "\n";
        }
        spielerListeText.text = liste;

        startButton.SetActive(PhotonNetwork.IsMasterClient);

        // Sucher-Auswahl nur beim Host anzeigen + Text aktualisieren
        sucherWahlButton.SetActive(PhotonNetwork.IsMasterClient);
        if (PhotonNetwork.IsMasterClient && GamePhaseManager.Instance != null)
        {
            int gw = GamePhaseManager.Instance.gewaehlterSucher;
            string name = "Zufällig";
            if (gw != 0)
            {
                var s = spieler.FirstOrDefault(x => x.photonView.ViewID == gw);
                name = s != null && s.spielerName != "" ? s.spielerName : "Zufällig";
                if (s == null) GamePhaseManager.Instance.gewaehlterSucher = 0;   // Spieler weg -> zurueck auf Zufaellig
            }
            var t = sucherWahlButton.GetComponentInChildren<Text>();
            if (t != null) t.text = "Sucher: " + name + "  (ändern)";
        }
    }

    // Host waehlt den Sucher durch: Zufaellig -> Spieler1 -> Spieler2 -> ... -> Zufaellig
    void SucherWahlWechseln()
    {
        if (!PhotonNetwork.IsMasterClient || GamePhaseManager.Instance == null) return;

        var menschen = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                       .Where(s => !s.istBot)
                       .OrderBy(s => s.photonView.Owner != null ? s.photonView.Owner.ActorNumber : 0)
                       .ToList();

        var ids = new List<int> { 0 };   // 0 = Zufaellig
        ids.AddRange(menschen.Select(m => m.photonView.ViewID));

        int akt = ids.IndexOf(GamePhaseManager.Instance.gewaehlterSucher);
        if (akt < 0) akt = 0;
        GamePhaseManager.Instance.gewaehlterSucher = ids[(akt + 1) % ids.Count];
    }

    void AktualisiereHud(SpielPhase phase)
    {
        int sek = GamePhaseManager.Instance.restSekunden;
        string zeit = (sek / 60) + ":" + (sek % 60).ToString("00");

        var lokal = LokalerSpieler();
        bool binSucher = lokal != null && GamePhaseManager.Instance.IstSucher(lokal.photonView.ViewID);

        if (phase == SpielPhase.Verstecken)
        {
            // Sucher sieht hier den schwarzen Bildschirm - dieser Text ist fuer
            // die Versteckten: verstecken + anmalen
            hudText.text = "VERSTECK DICH!   " + zeit + "\n[E] = anmalen (3x wischen)";
            sucherText.text = "";
        }
        else   // Suchen
        {
            if (binSucher)
            {
                hudText.text = "FINDE ALLE!   " + zeit;
                sucherText.text = "Berühre die Versteckten (2 m)";
                sucherText.color = Color.yellow;
            }
            else
            {
                hudText.text = "EINGEFROREN!   " + zeit;
                sucherText.text = "Du kannst dich nicht bewegen - aber weiter malen [E]";
                sucherText.color = Neon;
            }
        }
    }

    void AktualisiereEnde()
    {
        var alle = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                   .Where(s => s.platz > 0)
                   .OrderBy(s => s.platz)
                   .ToArray();

        if (alle.Length == 0)
        {
            platzierungenText.text = "";
            return;
        }

        bool sucherHatGewonnen = alle[0].photonView.ViewID == GamePhaseManager.Instance.sucherViewId;
        endeText.text = sucherHatGewonnen ? "SUCHER HAT GEWONNEN!" : "NICHT ALLE GEFUNDEN!";

        string liste = "";
        foreach (var s in alle)
        {
            string name = s.spielerName != "" ? s.spielerName : "Bot";
            bool istSucher = s.photonView.ViewID == GamePhaseManager.Instance.sucherViewId;
            liste += s.platz + ". Platz: " + name + (istSucher ? " (Sucher)" : "") +
                     (s.photonView.IsMine && !s.istBot ? "  <- DU" : "") + "\n";
        }
        platzierungenText.text = liste;
    }

    // ---------- UI-Aufbau ----------

    void BaueUI()
    {
        var canvasGO = new GameObject("LobbyUI_Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        // Schrift SCHAERFER: dynamische Fonts in 3-facher Aufloesung rastern,
        // damit die Schrift beim Hochskalieren nicht verschwimmt
        scaler.dynamicPixelsPerUnit = 3f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Menue-Hintergrundbild (Assets/Resources/MenueHintergrund) - wird als
        // erstes Kind erzeugt und liegt damit HINTER allen Panels. Sichtbar in
        // allen Menues, aber nicht waehrend Malen/Suchen (siehe Update()).
        var hintergrundTex = Resources.Load<Texture2D>("MenueHintergrund");
        if (hintergrundTex != null)
        {
            var hgGO = new GameObject("MenueHintergrund");
            hgGO.transform.SetParent(canvas.transform, false);
            hintergrundBild = hgGO.AddComponent<RawImage>();
            hintergrundBild.texture = hintergrundTex;
            // Bild fuellt den Bildschirm ohne Verzerrung (schneidet ggf. Raender ab)
            var fitter = hgGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)hintergrundTex.width / hintergrundTex.height;

            // Leicht weisse Schicht ueber dem Bild - man sieht es noch,
            // aber Schrift und Buttons sind viel besser lesbar
            var schleierGO = new GameObject("WeisserSchleier");
            schleierGO.transform.SetParent(canvas.transform, false);
            weisserSchleier = schleierGO.AddComponent<Image>();
            weisserSchleier.color = new Color(1f, 1f, 1f, 0.45f);
            weisserSchleier.raycastTarget = false;
            var schleierRect = weisserSchleier.rectTransform;
            schleierRect.anchorMin = Vector2.zero;
            schleierRect.anchorMax = Vector2.one;
            schleierRect.sizeDelta = Vector2.zero;
        }

        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ---------- Hauptmenue ----------
        hauptPanel = Panel(canvas.transform, "HauptPanel", 420, 560);
        Text(hauptPanel.transform, "FARBMIMIK", new Vector2(0, 245), 42, Neon);
        Text(hauptPanel.transform, "Verstecken + Anmalen", new Vector2(0, 205), 18, Color.white);

        // Profil: einfacher Name, wird in der Lobby und im Spiel angezeigt
        Text(hauptPanel.transform, "Dein Name:", new Vector2(0, 170), 16, Color.white);
        var nameFeld = Eingabefeld(hauptPanel.transform, new Vector2(0, 140), "z.B. Robi");
        nameFeld.text = PlayerPrefs.GetString("NeonCatch_SpielerName", "");
        nameFeld.onValueChanged.AddListener(wert => SpielerProfil.Name = wert);

        // Solo gegen Bots - OHNE Code, laeuft offline
        Knopf(hauptPanel.transform, "Solo gegen Bots", new Vector2(0, 82), StarteSolo);

        Knopf(hauptPanel.transform, "Online: Runde erstellen", new Vector2(0, 24), () =>
        {
            // Ohne Spieler-Prefab wuerde die Runde leer starten - klarer Hinweis statt Chaos
            if (Resources.Load<GameObject>("Spieler") == null)
            {
                ZeigeHilfe("SPIELER-PREFAB FEHLT!\n\nIn Unity einmal neu kompilieren lassen\n" +
                           "(das Prefab wird automatisch erstellt) oder im Menue\n" +
                           "Tools > FARBMIMIK > Netzwerk-Prefabs erstellen klicken.\n\n" +
                           "Danach hier nochmal auf 'Runde erstellen' klicken.");
                return;
            }
            // Direkt in den "Verbinde..."-Zustand - kein Startseiten-Flackern
            // und kein Hilfe-Popup, man landet gleich auf dem Code-Bildschirm
            verbindetGerade = true;
            hauptPanel.SetActive(false);
            PhotonRoomManager.Instanz.ErstelleRaum("farbmimik", "Spieler", 7);
        });
        Knopf(hauptPanel.transform, "Online: Runde beitreten", new Vector2(0, -34), () =>
        {
            hauptPanel.SetActive(false);
            beitretenPanel.SetActive(true);
            beitretenStatusText.text = "";
            ZeigeHilfe(NetzwerkHilfe.BeitretenAnleitung);   // zeigt sofort, was der Beitretende machen muss
        });
        Knopf(hauptPanel.transform, "Anderer Modus: NEON BLASTER", new Vector2(0, -92), () =>
        {
            string ziel = PlayerPrefs.GetString("NeonCatch_HauptSzene", "SampleScene");
            if (Application.CanStreamedLevelBeLoaded(ziel))
                UnityEngine.SceneManagement.SceneManager.LoadScene(ziel);
            else
                ZeigeHilfe("Szene '" + ziel + "' wurde nicht gefunden!\n\n" +
                           "In Unity: File > Build Settings öffnen und beide\n" +
                           "Szenen in die Liste ziehen. Dann klappt der Wechsel.");
        });

        // ---------- Beitreten ----------
        beitretenPanel = Panel(canvas.transform, "BeitretenPanel", 420, 360);
        Text(beitretenPanel.transform, "RUNDE BEITRETEN", new Vector2(0, 145), 30, Neon);
        beitretenStatusText = Text(beitretenPanel.transform, "", new Vector2(0, 100), 16, new Color(1f, 0.35f, 0.3f));
        beitretenStatusText.rectTransform.sizeDelta = new Vector2(400, 45);

        Text(beitretenPanel.transform, "Beitritts-Code:", new Vector2(0, 45), 18, Color.white);
        codeFeld = Eingabefeld(beitretenPanel.transform, new Vector2(0, 10), "z.B. X7K9");

        Knopf(beitretenPanel.transform, "Beitreten", new Vector2(0, -55), () =>
        {
            verbindetGerade = true;
            beitretenPanel.SetActive(false);
            PhotonRoomManager.Instanz.TretRaumBei(codeFeld.text, "farbmimik", "Spieler");
        });
        var beitretenHilfe = Knopf(beitretenPanel.transform, "Hilfe: Was muss ich machen?",
            new Vector2(0, -110), () => ZeigeHilfe(NetzwerkHilfe.BeitretenAnleitung));
        beitretenHilfe.GetComponentInChildren<Text>().color = new Color(1f, 0.7f, 0.2f);
        Knopf(beitretenPanel.transform, "Zurück", new Vector2(0, -165), () =>
        {
            beitretenPanel.SetActive(false);
            beitretenStatusText.text = "";
            hauptPanel.SetActive(true);
        });
        beitretenPanel.SetActive(false);

        // ---------- Lobby ----------
        lobbyPanel = Panel(canvas.transform, "LobbyPanel", 520, 560);
        lobbyCodeText = Text(lobbyPanel.transform, "", new Vector2(0, 230), 28, Neon);
        lobbyCodeText.rectTransform.sizeDelta = new Vector2(480, 45);

        kopierButton = Knopf(lobbyPanel.transform, "CODE KOPIEREN", new Vector2(0, 175), () =>
        {
            GUIUtility.systemCopyBuffer =
                "Beitritts-Code: " + PhotonRoomManager.RoomCode +
                " - Spiel FARBMIMIK starten, 'Runde beitreten' klicken und den Code eingeben!";
            kopiertAnzeige = 2f;
        }).gameObject;

        spielerListeText = Text(lobbyPanel.transform, "", new Vector2(0, 90), 19, Color.white);
        spielerListeText.alignment = TextAnchor.UpperCenter;
        spielerListeText.rectTransform.sizeDelta = new Vector2(460, 110);

        // Sucher-Auswahl (nur der Host): Zufaellig oder ein bestimmter Spieler.
        // Klick auf den Knopf wechselt durch die Moeglichkeiten.
        sucherWahlButton = Knopf(lobbyPanel.transform, "Sucher: Zufällig", new Vector2(0, -25),
            SucherWahlWechseln).gameObject;

        // Kurze Spielerklaerung
        var erklaerung = Text(lobbyPanel.transform, "", new Vector2(0, -85), 15, new Color(0.85f, 0.85f, 0.85f));
        erklaerung.text = "Der Sucher wartet (schwarzer Bildschirm), die anderen verstecken sich\n" +
                          "und malen sich an (E). Dann sucht der Sucher - Berühren = gefangen.";
        erklaerung.rectTransform.sizeDelta = new Vector2(490, 55);

        startButton = Knopf(lobbyPanel.transform, "SPIEL STARTEN (nur Host)", new Vector2(0, -155), () =>
        {
            if (PhotonNetwork.IsMasterClient)
                GamePhaseManager.Instance.StarteSpiel();
        }).gameObject;
        Knopf(lobbyPanel.transform, "Verlassen", new Vector2(0, -215), TrenneVerbindung);
        lobbyPanel.SetActive(false);

        // ---------- Spiel-HUD ----------
        hudPanel = new GameObject("HudPanel");
        hudPanel.transform.SetParent(canvas.transform, false);
        hudPanel.AddComponent<RectTransform>();
        hudText = Text(hudPanel.transform, "", Vector2.zero, 26, Neon);
        var hudRect = hudText.rectTransform;
        hudRect.anchorMin = hudRect.anchorMax = hudRect.pivot = new Vector2(0.5f, 1f);
        hudRect.anchoredPosition = new Vector2(0, -20);
        sucherText = Text(hudPanel.transform, "", Vector2.zero, 22, Neon);
        var sucherRect = sucherText.rectTransform;
        sucherRect.anchorMin = sucherRect.anchorMax = sucherRect.pivot = new Vector2(0.5f, 1f);
        sucherRect.anchoredPosition = new Vector2(0, -85);
        hudPanel.SetActive(false);

        // ---------- Ende ----------
        endePanel = Panel(canvas.transform, "EndePanel", 460, 460);
        endeText = Text(endePanel.transform, "SPIEL VORBEI!", new Vector2(0, 195), 32, Neon);
        platzierungenText = Text(endePanel.transform, "", new Vector2(0, 60), 20, Color.white);
        platzierungenText.alignment = TextAnchor.UpperCenter;
        platzierungenText.rectTransform.sizeDelta = new Vector2(400, 220);
        nochmalButton = Knopf(endePanel.transform, "Zurück zur Lobby", new Vector2(0, -160), () =>
        {
            if (PhotonNetwork.IsMasterClient)
                GamePhaseManager.Instance.ZurueckZurLobby();
        }).gameObject;
        Knopf(endePanel.transform, "Verlassen", new Vector2(0, -220), TrenneVerbindung);
        endePanel.SetActive(false);

        // ---------- Hilfe: Beschreibungen auf leicht weissem Grund ----------
        hilfePanel = Panel(canvas.transform, "HilfePanel", 560, 420);
        hilfePanel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.88f);
        hilfeInhaltText = Text(hilfePanel.transform, "", new Vector2(0, 10), 17, new Color(0.1f, 0.1f, 0.12f));
        hilfeInhaltText.alignment = TextAnchor.UpperCenter;
        hilfeInhaltText.rectTransform.sizeDelta = new Vector2(520, 320);
        Knopf(hilfePanel.transform, "Zurück", new Vector2(0, -180), () => hilfePanel.SetActive(false));
        hilfePanel.SetActive(false);

        // ---------- Sucher-Wartebildschirm (voller schwarzer Bildschirm) ----------
        sucherWartePanel = new GameObject("SucherWartePanel");
        sucherWartePanel.transform.SetParent(canvas.transform, false);
        var wartenBild = sucherWartePanel.AddComponent<Image>();
        wartenBild.color = Color.black;
        var wartenRect = wartenBild.rectTransform;
        wartenRect.anchorMin = Vector2.zero;
        wartenRect.anchorMax = Vector2.one;
        wartenRect.sizeDelta = Vector2.zero;
        sucherWarteText = Text(sucherWartePanel.transform, "", new Vector2(0, 40), 34, Neon);
        sucherWarteText.rectTransform.sizeDelta = new Vector2(600, 200);
        // Nur Host/Solo: Wartezeit ueberspringen (bei Bots muss man nicht warten)
        skipButton = Knopf(sucherWartePanel.transform, "Verstecken überspringen", new Vector2(0, -110), () =>
        {
            if (GamePhaseManager.Instance != null) GamePhaseManager.Instance.UeberspringeVerstecken();
        }).gameObject;
        sucherWartePanel.SetActive(false);

        // ---------- Verbinde-Panel (kein Startseiten-Flackern beim Erstellen/Beitreten) ----------
        verbindePanel = Panel(canvas.transform, "VerbindePanel", 420, 160);
        Text(verbindePanel.transform, "Verbinde mit dem Server ...", new Vector2(0, 20), 24, Neon);
        Text(verbindePanel.transform, "einen Moment bitte", new Vector2(0, -25), 16, Color.white);
        verbindePanel.SetActive(false);

        // ---------- Immer sichtbarer Zurueck-Button oben links ----------
        var zurueck = Knopf(canvas.transform, "< Zurück (Z)", Vector2.zero, GlobalerZurueck);
        var zr = zurueck.GetComponent<Image>().rectTransform;
        zr.anchorMin = zr.anchorMax = zr.pivot = new Vector2(0f, 1f);   // oben links
        zr.anchoredPosition = new Vector2(100f, -34f);
        zr.sizeDelta = new Vector2(170f, 46f);
        zurueck.transform.SetAsLastSibling();   // ueber allem anderen
    }

    // Kontext-abhaengiges "Zurueck": schliesst Hilfe, geht von Beitreten
    // zurueck ins Hauptmenue, verlaesst einen Raum, oder wechselt vom
    // Hauptmenue zurueck zu NEON BLASTER.
    void GlobalerZurueck()
    {
        if (hilfePanel.activeSelf) { hilfePanel.SetActive(false); return; }
        if (beitretenPanel.activeSelf)
        {
            beitretenPanel.SetActive(false);
            beitretenStatusText.text = "";
            hauptPanel.SetActive(true);
            return;
        }
        if (PhotonNetwork.InRoom) { TrenneVerbindung(); return; }   // Lobby/Ende -> Raum verlassen

        // Hauptmenue: zurueck in die NEON-BLASTER-Szene
        string ziel = PlayerPrefs.GetString("NeonCatch_HauptSzene", "SampleScene");
        if (Application.CanStreamedLevelBeLoaded(ziel))
            SceneManager.LoadScene(ziel);
    }

    void ZeigeHilfe(string inhalt)
    {
        hilfeInhaltText.text = inhalt;
        hilfePanel.SetActive(true);
    }

    void TrenneVerbindung()
    {
        PhotonRoomManager.Instanz.VerlasseRaum();
    }

    // ---------- Bausteine ----------

    GameObject Panel(Transform eltern, string name, float breite, float hoehe)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(eltern, false);
        var bild = panel.AddComponent<Image>();
        bild.color = new Color(0f, 0f, 0f, 0.8f);
        bild.rectTransform.sizeDelta = new Vector2(breite, hoehe);
        return panel;
    }

    Text Text(Transform eltern, string inhalt, Vector2 position, int groesse, Color farbe)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(eltern, false);
        var text = go.AddComponent<Text>();
        text.font = schrift;
        text.text = inhalt;
        text.fontSize = groesse;
        text.color = farbe;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = new Vector2(400, 40);

        // Dunkler Umriss (duenn = scharf): Schrift bleibt auf JEDEM Hintergrund
        // lesbar (auch wenn ein Button beim Drueberfahren hell wird)
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    Button Knopf(Transform eltern, string beschriftung, Vector2 position, UnityEngine.Events.UnityAction aktion)
    {
        var go = new GameObject("Button_" + beschriftung);
        go.transform.SetParent(eltern, false);
        var bild = go.AddComponent<Image>();
        bild.color = new Color(0.12f, 0.12f, 0.18f, 1f);
        bild.rectTransform.anchoredPosition = position;
        bild.rectTransform.sizeDelta = new Vector2(280, 52);

        var button = go.AddComponent<Button>();
        button.targetGraphic = bild;
        var farben = button.colors;
        farben.highlightedColor = Neon;
        farben.pressedColor = new Color(0.05f, 0.5f, 0.5f);
        button.colors = farben;
        button.onClick.AddListener(aktion);

        // Text passt sich automatisch der Button-Groesse an (schrumpft bei
        // langen Beschriftungen, statt ueber den Rand zu laufen oder
        // abgeschnitten zu werden)
        var text = Text(go.transform, beschriftung, Vector2.zero, 22, Color.white);
        text.rectTransform.sizeDelta = bild.rectTransform.sizeDelta - new Vector2(16, 6);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = true;
        text.resizeTextMaxSize = 20;
        text.resizeTextMinSize = 9;
        return button;
    }

    InputField Eingabefeld(Transform eltern, Vector2 position, string platzhalter)
    {
        var go = new GameObject("Feld");
        go.transform.SetParent(eltern, false);
        var bild = go.AddComponent<Image>();
        bild.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        bild.rectTransform.anchoredPosition = position;
        bild.rectTransform.sizeDelta = new Vector2(300, 42);

        var feld = go.AddComponent<InputField>();

        var platzhalterText = Text(go.transform, platzhalter, Vector2.zero, 18, new Color(0.45f, 0.45f, 0.45f));
        platzhalterText.fontStyle = FontStyle.Italic;
        platzhalterText.rectTransform.sizeDelta = new Vector2(280, 38);

        var eingabeText = Text(go.transform, "", Vector2.zero, 18, Color.black);
        eingabeText.rectTransform.sizeDelta = new Vector2(280, 38);

        feld.targetGraphic = bild;
        feld.textComponent = eingabeText;
        feld.placeholder = platzhalterText;
        return feld;
    }
}
