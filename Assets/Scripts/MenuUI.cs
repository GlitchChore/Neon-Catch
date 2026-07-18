using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Baut das Verbindungs-Menue komplett selbst auf (kein manuelles Canvas noetig):
/// - "Host starten"-Button  -> NetworkManager.StartHost()
/// - "Beitreten"-Button     -> zeigt IP-Eingabefeld + "Verbinden" -> StartClient()
/// - Laufende Verbindung    -> Statusanzeige + "Trennen"-Button
/// Gehoert auf ein leeres GameObject in der Szene (z.B. "Netzwerk").
/// </summary>
public class MenuUI : MonoBehaviour
{
    Font schrift;
    GameObject menuePanel;       // Host/Beitreten
    GameObject clientPanel;      // IP-Feld + Verbinden + Zurueck
    GameObject laufendPanel;     // Status + Trennen
    InputField ipFeld;
    Text statusText;

    static readonly Color Neon = new Color(0.1f, 0.9f, 0.9f);
    static readonly Color PanelFarbe = new Color(0f, 0f, 0f, 0.75f);

    void Start()
    {
        schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BaueUI();
    }

    void BaueUI()
    {
        var canvasGO = new GameObject("MenuUI_Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGO.AddComponent<GraphicRaycaster>();

        // EventSystem sicherstellen, sonst reagieren Buttons nicht
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ---------- Panel 1: Hauptmenue ----------
        menuePanel = ErzeugePanel(canvas.transform, "MenuePanel");
        ErzeugeText(menuePanel.transform, "NEON CATCH", new Vector2(0, 110), 40, Neon);
        ErzeugeButton(menuePanel.transform, "Host starten", new Vector2(0, 30), () =>
        {
            NetworkManager.singleton.StartHost();
        });
        ErzeugeButton(menuePanel.transform, "Beitreten", new Vector2(0, -40), () =>
        {
            menuePanel.SetActive(false);
            clientPanel.SetActive(true);
        });

        // ---------- Panel 2: Client (IP eingeben) ----------
        clientPanel = ErzeugePanel(canvas.transform, "ClientPanel");
        ErzeugeText(clientPanel.transform, "IP-Adresse des Hosts:", new Vector2(0, 110), 24, Color.white);
        ipFeld = ErzeugeInputField(clientPanel.transform, new Vector2(0, 55), "z.B. 192.168.1.20");
        ErzeugeButton(clientPanel.transform, "Verbinden", new Vector2(0, -15), () =>
        {
            string ip = ipFeld.text.Trim();
            if (ip == "")
                ip = "localhost";
            NetworkManager.singleton.networkAddress = ip;
            NetworkManager.singleton.StartClient();
        });
        ErzeugeButton(clientPanel.transform, "Zurueck", new Vector2(0, -85), () =>
        {
            clientPanel.SetActive(false);
            menuePanel.SetActive(true);
        });
        clientPanel.SetActive(false);

        // ---------- Panel 3: Verbindung laeuft ----------
        laufendPanel = new GameObject("LaufendPanel");
        laufendPanel.transform.SetParent(canvas.transform, false);
        var rect = laufendPanel.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0, 20);
        statusText = ErzeugeText(laufendPanel.transform, "", new Vector2(0, 70), 20, Neon);
        ErzeugeButton(laufendPanel.transform, "Trennen", new Vector2(0, 25), () =>
        {
            if (NetworkServer.active && NetworkClient.isConnected)
                NetworkManager.singleton.StopHost();
            else if (NetworkClient.active)
                NetworkManager.singleton.StopClient();
        });
        laufendPanel.SetActive(false);
    }

    void Update()
    {
        bool aktiv = NetworkServer.active || NetworkClient.active;

        laufendPanel.SetActive(aktiv);
        if (aktiv)
        {
            menuePanel.SetActive(false);
            clientPanel.SetActive(false);

            if (NetworkServer.active && NetworkClient.isConnected)
                statusText.text = "Host laeuft - Spieler: " + NetworkServer.connections.Count;
            else if (NetworkClient.isConnected)
                statusText.text = "Verbunden mit " + NetworkManager.singleton.networkAddress;
            else
                statusText.text = "Verbinde mit " + NetworkManager.singleton.networkAddress + "...";
        }
        else if (!menuePanel.activeSelf && !clientPanel.activeSelf)
        {
            // Verbindung wurde beendet -> zurueck ins Hauptmenue
            menuePanel.SetActive(true);
        }
    }

    // ---------- UI-Bausteine ----------

    GameObject ErzeugePanel(Transform eltern, string name)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(eltern, false);
        var bild = panel.AddComponent<Image>();
        bild.color = PanelFarbe;
        var rect = bild.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420, 320);
        return panel;
    }

    Text ErzeugeText(Transform eltern, string inhalt, Vector2 position, int groesse, Color farbe)
    {
        var go = new GameObject("Text_" + inhalt);
        go.transform.SetParent(eltern, false);
        var text = go.AddComponent<Text>();
        text.font = schrift;
        text.text = inhalt;
        text.fontSize = groesse;
        text.color = farbe;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = text.rectTransform;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(380, 40);
        return text;
    }

    Button ErzeugeButton(Transform eltern, string beschriftung, Vector2 position, UnityEngine.Events.UnityAction aktion)
    {
        var go = new GameObject("Button_" + beschriftung);
        go.transform.SetParent(eltern, false);
        var bild = go.AddComponent<Image>();
        bild.color = new Color(0.12f, 0.12f, 0.18f, 1f);
        var rect = bild.rectTransform;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(260, 55);

        var button = go.AddComponent<Button>();
        button.targetGraphic = bild;
        var farben = button.colors;
        farben.highlightedColor = Neon;
        farben.pressedColor = new Color(0.05f, 0.5f, 0.5f);
        button.colors = farben;
        button.onClick.AddListener(aktion);

        var text = ErzeugeText(go.transform, beschriftung, Vector2.zero, 24, Color.white);
        text.rectTransform.sizeDelta = rect.sizeDelta;
        return button;
    }

    InputField ErzeugeInputField(Transform eltern, Vector2 position, string platzhalter)
    {
        var go = new GameObject("IPFeld");
        go.transform.SetParent(eltern, false);
        var bild = go.AddComponent<Image>();
        bild.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        var rect = bild.rectTransform;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(300, 44);

        var feld = go.AddComponent<InputField>();

        var platzhalterText = ErzeugeText(go.transform, platzhalter, Vector2.zero, 20, new Color(0.4f, 0.4f, 0.4f));
        platzhalterText.rectTransform.sizeDelta = new Vector2(280, 40);
        platzhalterText.fontStyle = FontStyle.Italic;

        var eingabeText = ErzeugeText(go.transform, "", Vector2.zero, 20, Color.black);
        eingabeText.rectTransform.sizeDelta = new Vector2(280, 40);

        feld.targetGraphic = bild;
        feld.textComponent = eingabeText;
        feld.placeholder = platzhalterText;
        return feld;
    }
}
