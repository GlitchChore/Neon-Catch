using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;
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
/// Gespeicherte Freunde (Name + IP) - die IP eines Freundes bleibt meist
/// gleich, also einmal speichern und danach nur noch anklicken.
/// Format in PlayerPrefs: "Name|IP;Name|IP" (maximal 8 Freunde).
/// </summary>
public static class FreundeListe
{
    const string Schluessel = "NeonCatch_Freunde";

    /// <summary>Alle gespeicherten Freunde, neueste zuerst. [0]=Name, [1]=IP.</summary>
    public static List<string[]> Alle()
    {
        var ergebnis = new List<string[]>();
        string roh = PlayerPrefs.GetString(Schluessel, "");
        foreach (string eintrag in roh.Split(';'))
        {
            var teile = eintrag.Split('|');
            if (teile.Length == 2 && teile[0].Trim() != "" && teile[1].Trim() != "")
                ergebnis.Add(new[] { teile[0].Trim(), teile[1].Trim() });
        }
        return ergebnis;
    }

    public static void Speichere(string name, string ip)
    {
        name = (name ?? "").Trim().Replace("|", "").Replace(";", "");
        ip = (ip ?? "").Trim().Replace("|", "").Replace(";", "");
        if (name == "" || ip == "")
            return;
        if (name.Length > 14) name = name.Substring(0, 14);

        var alle = Alle();
        alle.RemoveAll(f => f[0].ToLower() == name.ToLower());
        alle.Insert(0, new[] { name, ip });
        while (alle.Count > 8)
            alle.RemoveAt(alle.Count - 1);

        var teile = new List<string>();
        foreach (var f in alle)
            teile.Add(f[0] + "|" + f[1]);
        PlayerPrefs.SetString(Schluessel, string.Join(";", teile));
    }
}

/// <summary>
/// Eingebaute Hilfe-Texte für BEIDE Modi - getrennt nach Rolle:
/// HostAnleitung = was der Ersteller der Runde machen muss (inkl. Portfreigabe)
/// BeitretenAnleitung = was ein beitretender Freund machen muss
/// Angezeigt in der FARBMIMIK-UI UND im Kampfmodus-Startmenü.
/// </summary>
public static class NetzwerkHilfe
{
    public const string HostAnleitung =
        "DU BIST DER HOST (RUNDE ERSTELLEN)\n" +
        "\n" +
        "1. 'Runde erstellen' klicken - du bekommst einen ROOM-CODE\n" +
        "2. Schick deinen Freunden den Code und deine IP\n" +
        "    (der Kopieren-Knopf in der Lobby macht das für dich)\n" +
        "    - Freunde im GLEICHEN WLAN brauchen deine WLAN-IP\n" +
        "    - Freunde ÜBERS INTERNET brauchen deine Internet-IP\n" +
        "3. Nur für Internet-Freunde: EINMAL Port 7777 (UDP)\n" +
        "    im Router freigeben - so geht das:\n" +
        "\n" +
        "FRITZBOX - 4 SCHRITTE:\n" +
        "1. Im Browser  fritz.box  öffnen und anmelden\n" +
        "    (Passwort steht unten auf dem Router-Aufkleber)\n" +
        "2. Internet > Freigaben > Reiter 'Portfreigaben'\n" +
        "3. 'Gerät für Freigaben hinzufügen' > deinen PC wählen\n" +
        "    > 'Neue Freigabe' > 'Portfreigabe'\n" +
        "4. Protokoll UDP, Port 7777 bis 7777 eintragen,\n" +
        "    dann OK und Übernehmen. Fertig!\n" +
        "\n" +
        "ANDERE ROUTER - 4 SCHRITTE:\n" +
        "1. Router-Adresse im Browser öffnen (steht auf dem\n" +
        "    Aufkleber, oft 192.168.1.1) und anmelden\n" +
        "2. Menüpunkt 'Portfreigabe' oder 'Port Forwarding'\n" +
        "    suchen (oft unter 'Erweitert')\n" +
        "3. Neue Regel: dein PC, Protokoll UDP, Port 7777\n" +
        "4. Speichern - fertig!\n" +
        "\n" +
        "Klappt es trotzdem nicht? Alle installieren das\n" +
        "kostenlose 'Tailscale' - dann geht es wie im WLAN.";

    public const string BeitretenAnleitung =
        "DU TRITTST EINER RUNDE BEI\n" +
        "\n" +
        "1. Frag den Host nach seiner IP und dem ROOM-CODE\n" +
        "    (er kann dir beides mit einem Klick schicken)\n" +
        "2. IP eintragen:\n" +
        "    - Gleiches WLAN wie der Host: seine WLAN-IP\n" +
        "    - Übers Internet: seine INTERNET-IP\n" +
        "3. Room-Code eintragen (z.B. X7K9)\n" +
        "4. 'Beitreten' klicken - fertig!\n" +
        "\n" +
        "Du musst NICHTS am Router einstellen -\n" +
        "die Portfreigabe braucht nur der Host.\n" +
        "\n" +
        "KOMMST DU NICHT REIN?\n" +
        "- IP und Code Buchstabe für Buchstabe prüfen\n" +
        "- Der Host muss die Runde schon erstellt haben\n" +
        "- Übers Internet: Der Host muss Port 7777 (UDP)\n" +
        "    freigegeben haben (die Anleitung hat er im Spiel)\n" +
        "- Notlösung: Alle installieren das kostenlose\n" +
        "    'Tailscale' - dann geht es wie im gleichen WLAN.";
}

/// <summary>
/// Komplette FARBMIMIK-UI, wird zur Laufzeit selbst aufgebaut:
/// - Hauptmenue: "Server starten" / "Code eingeben"
/// - Beitreten: IP + Room-Code eingeben
/// - Lobby: Room-Code gross, Spielerliste (max 7), Start-Button NUR fuer den Host
/// - Spiel-HUD: Phase + Timer + Sucher-Anzeige
/// Gehoert auf dasselbe GameObject "Netzwerk" wie der LobbyManager.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    Font schrift;
    GameObject hauptPanel, beitretenPanel, lobbyPanel, hudPanel, endePanel, hilfePanel;
    Text hilfeInhaltText, beitretenStatusText, ipHinweisText;
    InputField freundNameFeld;
    GameObject freundeReihe;
    GameObject sucherWartePanel;
    Text sucherWarteText;
    RawImage hintergrundBild;
    Image weisserSchleier;
    bool verbindungLief, warVollVerbunden;
    InputField ipFeld, codeFeld;
    Text lobbyCodeText, spielerListeText, hudText, sucherText, endeText;
    GameObject startButton, nochmalButton, kopierButton;

    static readonly Color Neon = new Color(0.1f, 0.95f, 0.95f);

    string oeffentlicheIP = "wird geladen...";
    float kopiertAnzeige;

    void Start()
    {
        schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BaueUI();
        StartCoroutine(HoleOeffentlicheIP());

        // Kam der Spieler ueber "FARBMIMIK HOSTEN" aus dem anderen Modus?
        // Dann die Runde automatisch erstellen - er landet direkt in der Lobby.
        if (PlayerPrefs.GetInt("NeonCatch_AutoHost", 0) == 1)
        {
            PlayerPrefs.SetInt("NeonCatch_AutoHost", 0);
            if (Resources.Load<GameObject>("Spieler") != null)
                NetworkManager.singleton.StartHost();
        }
    }

    // Oeffentliche IP von api.ipify.org holen (fuer Freunde uebers Internet)
    IEnumerator HoleOeffentlicheIP()
    {
        using (UnityWebRequest anfrage = UnityWebRequest.Get("https://api.ipify.org"))
        {
            anfrage.timeout = 5;
            yield return anfrage.SendWebRequest();
            oeffentlicheIP = anfrage.result == UnityWebRequest.Result.Success
                ? anfrage.downloadHandler.text.Trim()
                : "nicht ermittelbar";
        }
    }

    void Update()
    {
        bool verbunden = NetworkClient.active;
        SpielPhase phase = GamePhaseManager.Instance != null
            ? GamePhaseManager.Instance.phase
            : SpielPhase.Lobby;

        // Hintergrundbild ueberall AUSSER im laufenden Spiel (Malen/Suchen)
        bool bildSichtbar = !(verbunden && (phase == SpielPhase.Malen || phase == SpielPhase.Suchen));
        if (hintergrundBild != null) hintergrundBild.enabled = bildSichtbar;
        if (weisserSchleier != null) weisserSchleier.enabled = bildSichtbar;

        // Fehlgeschlagener Beitritts-Versuch? (falsche IP oder falscher Code)
        if (NetworkClient.active)
        {
            verbindungLief = true;
            if (NetworkClient.isConnected && NetworkClient.localPlayer != null)
                warVollVerbunden = true;
        }
        else if (verbindungLief)
        {
            verbindungLief = false;
            if (!warVollVerbunden)
            {
                hauptPanel.SetActive(false);
                hilfePanel.SetActive(false);
                beitretenPanel.SetActive(true);
                beitretenStatusText.text = "Das ist nicht korrekt!\nIP oder Room-Code prüfen und nochmal versuchen.";
            }
            warVollVerbunden = false;
        }

        hauptPanel.SetActive(!verbunden && !beitretenPanel.activeSelf && !hilfePanel.activeSelf);
        if (verbunden && beitretenPanel.activeSelf)
            beitretenPanel.SetActive(false);

        lobbyPanel.SetActive(verbunden && phase == SpielPhase.Lobby);
        hudPanel.SetActive(verbunden && (phase == SpielPhase.Malen || phase == SpielPhase.Suchen));
        endePanel.SetActive(verbunden && phase == SpielPhase.Ende);

        if (lobbyPanel.activeSelf)
            AktualisiereLobby();
        if (hudPanel.activeSelf)
            AktualisiereHud(phase);
        if (endePanel.activeSelf)
            nochmalButton.SetActive(NetworkServer.active);

        // Schwarzer Wartebildschirm: nur fuer den Sucher selbst, waehrend
        // die Vorbereitungszeit laeuft (Sucher noch nicht auf der Map)
        bool binSucherUndWartet = false;
        if (verbunden && phase == SpielPhase.Suchen && GamePhaseManager.Instance != null && !GamePhaseManager.Instance.sucherAktiv)
        {
            var lokal = NetworkClient.localPlayer;
            if (lokal != null && GamePhaseManager.Instance.IstSucher(lokal))
                binSucherUndWartet = true;
        }
        sucherWartePanel.SetActive(binSucherUndWartet);
        if (binSucherUndWartet)
            sucherWarteText.text = "DU BIST DER SUCHER\n\nWarte...\nSpawn in " + GamePhaseManager.Instance.sucherSpawnRest + "s";
    }

    // ---------- Anzeigen aktualisieren ----------

    void AktualisiereLobby()
    {
        if (kopiertAnzeige > 0f) kopiertAnzeige -= Time.deltaTime;

        if (NetworkServer.active)
        {
            lobbyCodeText.text = "BEITRITTS-CODE: " + LobbyManager.RoomCode;
            ipHinweisText.text = "Beim ERSTEN Mal auch deine IP mitschicken (danach haben Freunde sie gespeichert):\n" +
                                 "WLAN: " + LokaleIP() + "   |   Internet: " + oeffentlicheIP;
            kopierButton.SetActive(true);
            var knopfText = kopierButton.GetComponentInChildren<Text>();
            if (knopfText != null)
                knopfText.text = kopiertAnzeige > 0f ? "Kopiert! An Freunde schicken" : "CODE KOPIEREN";
        }
        else
        {
            lobbyCodeText.text = "Verbunden - warte auf den Host...";
            ipHinweisText.text = "";
            kopierButton.SetActive(false);
        }

        var spieler = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                      .OrderBy(s => s.spielerNummer).ToArray();

        string liste = "Spieler (" + spieler.Length + "/7):\n";
        foreach (var s in spieler)
        {
            liste += s.spielerName != "" ? s.spielerName : "Spieler " + s.spielerNummer;
            if (s.spielerNummer == 1) liste += " (Host)";
            if (s.isLocalPlayer) liste += "  <- DU";
            liste += "\n";
        }
        spielerListeText.text = liste;

        startButton.SetActive(NetworkServer.active);
    }

    void AktualisiereHud(SpielPhase phase)
    {
        int sek = GamePhaseManager.Instance.restSekunden;
        string zeit = (sek / 60) + ":" + (sek % 60).ToString("00");

        if (phase == SpielPhase.Malen)
        {
            hudText.text = "MALEN & VERSTECKEN   " + zeit + "\n[E] = Farbe waehlen (3x wischen)";
            sucherText.text = "";
        }
        else
        {
            hudText.text = "SUCHEN & FREEZE   " + zeit;

            var lokal = NetworkClient.localPlayer;
            if (lokal != null && GamePhaseManager.Instance.IstSucher(lokal))
            {
                sucherText.text = "DU BIST DER SUCHER! Finde die anderen!";
                sucherText.color = Color.yellow;
            }
            else
            {
                var sucher = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                    .FirstOrDefault(s => s.netId == GamePhaseManager.Instance.sucherNetId);
                string name = sucher != null ? "Spieler " + sucher.spielerNummer : "?";
                sucherText.text = "Sucher: " + name + "  -  NICHT BEWEGEN!";
                sucherText.color = Neon;
            }
        }
    }

    static string LokaleIP()
    {
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "unbekannt";
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
        hauptPanel = Panel(canvas.transform, "HauptPanel", 420, 470);
        Text(hauptPanel.transform, "FARBMIMIK", new Vector2(0, 190), 42, Neon);
        Text(hauptPanel.transform, "Verstecken + Anmalen", new Vector2(0, 150), 18, Color.white);

        // Profil: einfacher Name, wird in der Lobby und im Spiel angezeigt
        Text(hauptPanel.transform, "Dein Name:", new Vector2(0, 115), 16, Color.white);
        var nameFeld = Eingabefeld(hauptPanel.transform, new Vector2(0, 85), "z.B. Robi");
        nameFeld.text = PlayerPrefs.GetString("NeonCatch_SpielerName", "");
        nameFeld.onValueChanged.AddListener(wert => SpielerProfil.Name = wert);

        Knopf(hauptPanel.transform, "Runde erstellen", new Vector2(0, 20), () =>
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
            NetworkManager.singleton.StartHost();
            ZeigeHilfe(NetzwerkHilfe.HostAnleitung);   // zeigt sofort, was der Host machen muss
        });
        Knopf(hauptPanel.transform, "Runde beitreten", new Vector2(0, -45), () =>
        {
            hauptPanel.SetActive(false);
            beitretenPanel.SetActive(true);
            beitretenStatusText.text = "";
            BaueFreundeKnoepfe();
            ZeigeHilfe(NetzwerkHilfe.BeitretenAnleitung);   // zeigt sofort, was der Beitretende machen muss
        });
        Knopf(hauptPanel.transform, "Anderer Modus: NEON BLASTER", new Vector2(0, -110), () =>
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
        beitretenPanel = Panel(canvas.transform, "BeitretenPanel", 420, 580);
        Text(beitretenPanel.transform, "RUNDE BEITRETEN", new Vector2(0, 250), 30, Neon);
        beitretenStatusText = Text(beitretenPanel.transform, "", new Vector2(0, 210), 16, new Color(1f, 0.35f, 0.3f));
        beitretenStatusText.rectTransform.sizeDelta = new Vector2(400, 45);

        // Gespeicherte Freunde: anklicken fuellt die IP automatisch aus
        Text(beitretenPanel.transform, "Mit wem spielen? (Klick = IP wird eingefügt)", new Vector2(0, 172), 15, Color.white);
        freundeReihe = new GameObject("FreundeReihe");
        freundeReihe.transform.SetParent(beitretenPanel.transform, false);
        freundeReihe.AddComponent<RectTransform>().anchoredPosition = new Vector2(0, 135);

        Text(beitretenPanel.transform, "IP des Hosts:", new Vector2(0, 95), 18, Color.white);
        ipFeld = Eingabefeld(beitretenPanel.transform, new Vector2(0, 62), "z.B. 192.168.1.20");
        Text(beitretenPanel.transform, "Beitritts-Code:", new Vector2(0, 22), 18, Color.white);
        codeFeld = Eingabefeld(beitretenPanel.transform, new Vector2(0, -12), "z.B. X7K9");

        Text(beitretenPanel.transform, "Zum Merken - Name des Freundes:", new Vector2(0, -52), 15, Color.white);
        freundNameFeld = Eingabefeld(beitretenPanel.transform, new Vector2(0, -85), "z.B. Daniel");

        Knopf(beitretenPanel.transform, "Beitreten", new Vector2(0, -140), () =>
        {
            string ip = ipFeld.text.Trim();
            if (ip == "") ip = "localhost";

            // Freund merken: beim naechsten Mal reicht ein Klick auf den Namen
            if (freundNameFeld.text.Trim() != "")
            {
                FreundeListe.Speichere(freundNameFeld.text, ip);
                BaueFreundeKnoepfe();
            }

            LobbyManager.EingegebenerCode = codeFeld.text.Trim().ToUpper();
            NetworkManager.singleton.networkAddress = ip;
            NetworkManager.singleton.StartClient();
        });
        var beitretenHilfe = Knopf(beitretenPanel.transform, "Hilfe: Was muss ich machen?",
            new Vector2(0, -196), () => ZeigeHilfe(NetzwerkHilfe.BeitretenAnleitung));
        beitretenHilfe.GetComponentInChildren<Text>().color = new Color(1f, 0.7f, 0.2f);
        Knopf(beitretenPanel.transform, "Zurück", new Vector2(0, -250), () =>
        {
            beitretenPanel.SetActive(false);
            beitretenStatusText.text = "";
            hauptPanel.SetActive(true);
        });
        beitretenPanel.SetActive(false);

        // ---------- Lobby ----------
        lobbyPanel = Panel(canvas.transform, "LobbyPanel", 520, 600);
        lobbyCodeText = Text(lobbyPanel.transform, "", new Vector2(0, 250), 28, Neon);
        lobbyCodeText.rectTransform.sizeDelta = new Vector2(480, 45);

        kopierButton = Knopf(lobbyPanel.transform, "CODE KOPIEREN", new Vector2(0, 195), () =>
        {
            GUIUtility.systemCopyBuffer =
                "Beitritts-Code: " + LobbyManager.RoomCode +
                " - Spiel FARBMIMIK starten, 'Runde beitreten' klicken und den Code eingeben!";
            kopiertAnzeige = 2f;
        }).gameObject;

        ipHinweisText = Text(lobbyPanel.transform, "", new Vector2(0, 140), 14, new Color(0.85f, 0.85f, 0.85f));
        ipHinweisText.rectTransform.sizeDelta = new Vector2(490, 45);

        spielerListeText = Text(lobbyPanel.transform, "", new Vector2(0, 30), 20, Color.white);
        spielerListeText.alignment = TextAnchor.UpperCenter;
        spielerListeText.rectTransform.sizeDelta = new Vector2(460, 145);

        // Kurze Spielerklaerung (2 Saetze)
        var erklaerung = Text(lobbyPanel.transform, "", new Vector2(0, -115), 15, new Color(0.85f, 0.85f, 0.85f));
        erklaerung.text = "So geht's: 90 Sekunden Farbe mischen (Taste E, 3x wischen) und verstecken.\n" +
                          "Danach sucht ein Sucher 30 Sekunden - wer sich bewegt, blinkt neon!";
        erklaerung.rectTransform.sizeDelta = new Vector2(490, 55);

        var portKnopf = Knopf(lobbyPanel.transform, "Anleitung: So laden Freunde ein (Host)",
            new Vector2(0, -160), () => ZeigeHilfe(NetzwerkHilfe.HostAnleitung));
        var portKnopfText = portKnopf.GetComponentInChildren<Text>();
        portKnopfText.color = new Color(1f, 0.7f, 0.2f);
        portKnopfText.fontSize = 16;

        startButton = Knopf(lobbyPanel.transform, "SPIEL STARTEN (nur Host)", new Vector2(0, -215), () =>
        {
            if (NetworkServer.active)
                GamePhaseManager.Instance.StarteSpiel();
        }).gameObject;
        Knopf(lobbyPanel.transform, "Verlassen", new Vector2(0, -275), TrenneVerbindung);
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
        endePanel = Panel(canvas.transform, "EndePanel", 420, 300);
        endeText = Text(endePanel.transform, "SPIEL VORBEI!", new Vector2(0, 90), 36, Neon);
        nochmalButton = Knopf(endePanel.transform, "Zurück zur Lobby", new Vector2(0, 0), () =>
        {
            if (NetworkServer.active)
                GamePhaseManager.Instance.ZurueckZurLobby();
        }).gameObject;
        Knopf(endePanel.transform, "Verlassen", new Vector2(0, -70), TrenneVerbindung);
        endePanel.SetActive(false);

        // ---------- Hilfe: Beschreibungen auf leicht weissem Grund ----------
        hilfePanel = Panel(canvas.transform, "HilfePanel", 620, 680);
        hilfePanel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.88f);
        hilfeInhaltText = Text(hilfePanel.transform, "", new Vector2(0, 20), 17, new Color(0.1f, 0.1f, 0.12f));
        hilfeInhaltText.alignment = TextAnchor.UpperCenter;
        hilfeInhaltText.rectTransform.sizeDelta = new Vector2(580, 580);
        Knopf(hilfePanel.transform, "Zurück", new Vector2(0, -300), () => hilfePanel.SetActive(false));
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
        sucherWarteText = Text(sucherWartePanel.transform, "", Vector2.zero, 34, Neon);
        sucherWarteText.rectTransform.sizeDelta = new Vector2(600, 200);
        sucherWartePanel.SetActive(false);
    }

    void ZeigeHilfe(string inhalt)
    {
        hilfeInhaltText.text = inhalt;
        hilfePanel.SetActive(true);
    }

    // Baut die Reihe der gespeicherten Freunde neu auf (max. 4 sichtbar).
    // Klick auf einen Namen fuellt die IP automatisch aus.
    void BaueFreundeKnoepfe()
    {
        foreach (Transform kind in freundeReihe.transform)
            Destroy(kind.gameObject);

        var freunde = FreundeListe.Alle();
        int anzahl = Mathf.Min(freunde.Count, 4);
        for (int i = 0; i < anzahl; i++)
        {
            string name = freunde[i][0];
            string ip = freunde[i][1];
            float x = (i - (anzahl - 1) * 0.5f) * 96f;
            var knopf = Knopf(freundeReihe.transform, name, new Vector2(x, 0), () =>
            {
                ipFeld.text = ip;
                if (freundNameFeld != null) freundNameFeld.text = "";
            });
            knopf.GetComponent<Image>().rectTransform.sizeDelta = new Vector2(90, 36);
            var text = knopf.GetComponentInChildren<Text>();
            text.rectTransform.sizeDelta = new Vector2(82, 32);
            text.color = Neon;
        }
    }

    void TrenneVerbindung()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
            NetworkManager.singleton.StopHost();
        else if (NetworkClient.active)
            NetworkManager.singleton.StopClient();
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
