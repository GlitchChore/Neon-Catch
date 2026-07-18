using System.Linq;
using System.Net;
using System.Net.Sockets;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

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
    GameObject hauptPanel, beitretenPanel, lobbyPanel, hudPanel, endePanel;
    InputField ipFeld, codeFeld;
    Text lobbyCodeText, spielerListeText, hudText, sucherText, endeText;
    GameObject startButton, nochmalButton;

    static readonly Color Neon = new Color(0.1f, 0.95f, 0.95f);

    void Start()
    {
        schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BaueUI();
    }

    void Update()
    {
        bool verbunden = NetworkClient.active;
        SpielPhase phase = GamePhaseManager.Instance != null
            ? GamePhaseManager.Instance.phase
            : SpielPhase.Lobby;

        hauptPanel.SetActive(!verbunden && !beitretenPanel.activeSelf);
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
    }

    // ---------- Anzeigen aktualisieren ----------

    void AktualisiereLobby()
    {
        lobbyCodeText.text = "ROOM-CODE: " + LobbyManager.RoomCode +
                             (NetworkServer.active ? "\nDeine IP: " + LokaleIP() : "");

        var spieler = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                      .OrderBy(s => s.spielerNummer).ToArray();

        string liste = "Spieler (" + spieler.Length + "/7):\n";
        foreach (var s in spieler)
        {
            liste += "Spieler " + s.spielerNummer;
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

        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ---------- Hauptmenue ----------
        hauptPanel = Panel(canvas.transform, "HauptPanel", 420, 340);
        Text(hauptPanel.transform, "FARBMIMIK", new Vector2(0, 120), 42, Neon);
        Knopf(hauptPanel.transform, "Server starten", new Vector2(0, 35), () =>
        {
            NetworkManager.singleton.StartHost();
        });
        Knopf(hauptPanel.transform, "Code eingeben", new Vector2(0, -35), () =>
        {
            hauptPanel.SetActive(false);
            beitretenPanel.SetActive(true);
        });

        // ---------- Beitreten ----------
        beitretenPanel = Panel(canvas.transform, "BeitretenPanel", 420, 380);
        Text(beitretenPanel.transform, "SPIEL BEITRETEN", new Vector2(0, 140), 30, Neon);
        Text(beitretenPanel.transform, "IP des Hosts:", new Vector2(0, 100), 18, Color.white);
        ipFeld = Eingabefeld(beitretenPanel.transform, new Vector2(0, 65), "z.B. 192.168.1.20");
        Text(beitretenPanel.transform, "Room-Code:", new Vector2(0, 25), 18, Color.white);
        codeFeld = Eingabefeld(beitretenPanel.transform, new Vector2(0, -10), "z.B. X7K9");
        Knopf(beitretenPanel.transform, "Beitreten", new Vector2(0, -70), () =>
        {
            string ip = ipFeld.text.Trim();
            if (ip == "") ip = "localhost";
            LobbyManager.EingegebenerCode = codeFeld.text.Trim().ToUpper();
            NetworkManager.singleton.networkAddress = ip;
            NetworkManager.singleton.StartClient();
        });
        Knopf(beitretenPanel.transform, "Zurueck", new Vector2(0, -140), () =>
        {
            beitretenPanel.SetActive(false);
            hauptPanel.SetActive(true);
        });
        beitretenPanel.SetActive(false);

        // ---------- Lobby ----------
        lobbyPanel = Panel(canvas.transform, "LobbyPanel", 460, 420);
        lobbyCodeText = Text(lobbyPanel.transform, "", new Vector2(0, 160), 30, Neon);
        spielerListeText = Text(lobbyPanel.transform, "", new Vector2(0, 30), 20, Color.white);
        spielerListeText.alignment = TextAnchor.UpperCenter;
        spielerListeText.rectTransform.sizeDelta = new Vector2(400, 200);
        startButton = Knopf(lobbyPanel.transform, "START (nur Host)", new Vector2(0, -120), () =>
        {
            if (NetworkServer.active)
                GamePhaseManager.Instance.StarteSpiel();
        }).gameObject;
        Knopf(lobbyPanel.transform, "Verlassen", new Vector2(0, -180), TrenneVerbindung);
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
        nochmalButton = Knopf(endePanel.transform, "Zurueck zur Lobby", new Vector2(0, 0), () =>
        {
            if (NetworkServer.active)
                GamePhaseManager.Instance.ZurueckZurLobby();
        }).gameObject;
        Knopf(endePanel.transform, "Verlassen", new Vector2(0, -70), TrenneVerbindung);
        endePanel.SetActive(false);
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

        var text = Text(go.transform, beschriftung, Vector2.zero, 22, Color.white);
        text.rectTransform.sizeDelta = bild.rectTransform.sizeDelta;
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
