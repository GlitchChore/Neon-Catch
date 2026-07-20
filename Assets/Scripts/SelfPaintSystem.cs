using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// PHASE 1 (Malen): Taste E oeffnet die Farb-UI (animiert mit LeanTween).
/// Auf dem Farbverlauf mit gedrueckter Maustaste wischen -> Farbe mischt sich.
/// Nach 3 Wischen wird die Farbe fest gesetzt und per Photon-RPC an alle verteilt.
/// Gehoert auf das Spieler-Prefab (zusammen mit PhotonView + PhotonTransformView).
///
/// Laeuft fuer ECHTE Spieler UND fuer Bots (die GamePhaseManager als
/// Raum-Objekt erzeugt und per RpcSetzeBotDaten einmalig befuellt) - Bots
/// erkennt man am Feld istBot, sie bekommen nie eigene Eingaben.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class SelfPaintSystem : MonoBehaviourPun
{
    public string spielerName = "";
    public Color spielerFarbe = Color.white;
    public bool farbeGesetzt;

    /// <summary>Server-gesteuerter Fuell-Bot statt echter Spieler.</summary>
    public bool istBot;

    /// <summary>Vom Sucher gefunden? Bestimmt zusammen mit platz die Endplatzierung.</summary>
    public bool gefunden;

    /// <summary>Endplatzierung dieser Runde (0 = noch nicht ausgewertet, 1 = bester Platz).</summary>
    public int platz;

    const int WischeNoetig = 3;
    const float MindestWischWeite = 60f;   // Pixel

    Renderer[] eigeneRenderer;
    Color botFarbeAusDaten;

    // UI (existiert nur beim lokalen Spieler)
    GameObject farbPanel;
    Image vorschau;
    Text hinweisText;
    bool uiOffen;
    int wische;
    float hue = 0.5f;
    float hueBeimDragStart;
    float dragStartX;
    bool draggt;

    void Awake()
    {
        eigeneRenderer = GetComponentsInChildren<Renderer>();

        // Bot-Daten (Name + Farbe) werden beim Erzeugen als Instantiation-
        // Data mitgegeben - die sind SOFORT verfuegbar (anders als RPCs, die
        // erst 1 Frame spaeter ankommen und in der Zwischenzeit faelschlich
        // "kein Bot" zeigen wuerden).
        object[] daten = photonView.InstantiationData;
        if (daten != null && daten.Length >= 4)
        {
            istBot = true;
            spielerName = (string)daten[0];
            farbeGesetzt = true;
            botFarbeAusDaten = new Color((float)daten[1], (float)daten[2], (float)daten[3]);
        }
    }

    void Start()
    {
        if (istBot)
        {
            SetzeSichtbareFarbe(botFarbeAusDaten);
            return;
        }

        // Eigenen Namen einmalig verteilen (AllBuffered: auch spaeter
        // beitretende Clients bekommen den Namen automatisch nachgeliefert)
        if (photonView.IsMine)
            photonView.RPC(nameof(RpcSetzeName), RpcTarget.AllBuffered, SpielerProfil.Name);
    }

    [PunRPC]
    void RpcSetzeName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length > 14) name = name.Substring(0, 14);
        spielerName = name;
    }

    void SetzeSichtbareFarbe(Color neu)
    {
        spielerFarbe = neu;
        foreach (var r in eigeneRenderer)
            foreach (var m in r.materials)
                m.color = neu;
    }

    /// <summary>Vom MasterClient aufgerufen, wenn eine neue Runde in der Lobby beginnt.</summary>
    [PunRPC]
    public void RpcResetFuerLobby()
    {
        farbeGesetzt = false;
        SetzeSichtbareFarbe(Color.white);
        gefunden = false;
        platz = 0;
    }

    /// <summary>Vom Sucher/MasterClient aufgerufen, wenn dieser Spieler gefunden wurde.</summary>
    [PunRPC]
    public void RpcSetzeGefunden()
    {
        gefunden = true;
    }

    [PunRPC]
    public void RpcSetzePlatz(int neuerPlatz)
    {
        platz = neuerPlatz;
    }

    void Update()
    {
        if (!photonView.IsMine || istBot)
            return;

        SpielPhase phase = GamePhaseManager.Instance != null
            ? GamePhaseManager.Instance.phase
            : SpielPhase.Lobby;

        var tastatur = Keyboard.current;
        if (tastatur != null && tastatur.eKey.wasPressedThisFrame &&
            phase == SpielPhase.Malen && !farbeGesetzt)
        {
            if (uiOffen) SchliesseUI();
            else OeffneUI();
        }

        // UI zwangsweise zu, wenn die Malphase endet
        if (uiOffen && phase != SpielPhase.Malen)
            SchliesseUI();

        if (uiOffen)
            VerarbeiteWischen();
    }

    // ---------- Wisch-Logik ----------

    void VerarbeiteWischen()
    {
        var maus = Mouse.current;
        if (maus == null)
            return;

        if (maus.leftButton.wasPressedThisFrame)
        {
            draggt = true;
            dragStartX = maus.position.ReadValue().x;
            hueBeimDragStart = hue;
        }

        if (draggt && maus.leftButton.isPressed)
        {
            float dx = maus.position.ReadValue().x - dragStartX;
            hue = Mathf.Repeat(hueBeimDragStart + dx / 600f, 1f);
            vorschau.color = Color.HSVToRGB(hue, 0.9f, 1f);
        }

        if (draggt && maus.leftButton.wasReleasedThisFrame)
        {
            draggt = false;
            float weite = Mathf.Abs(maus.position.ReadValue().x - dragStartX);
            if (weite < MindestWischWeite)
                return;

            wische++;
            LeanTween.scale(vorschau.gameObject, Vector3.one * 1.35f, 0.12f).setLoopPingPong(1);

            if (wische >= WischeNoetig)
            {
                if (GamePhaseManager.Instance != null &&
                    GamePhaseManager.Instance.phase == SpielPhase.Malen && !farbeGesetzt)
                {
                    photonView.RPC(nameof(RpcSetzeFarbe), RpcTarget.AllBuffered, hue);
                }
                SchliesseUI();
            }
            else
            {
                AktualisiereHinweis();
            }
        }
    }

    [PunRPC]
    void RpcSetzeFarbe(float gewaehlterHue)
    {
        if (farbeGesetzt) return;   // schon gesetzt -> doppelten Aufruf ignorieren
        farbeGesetzt = true;
        SetzeSichtbareFarbe(Color.HSVToRGB(gewaehlterHue, 0.9f, 1f));
    }

    // ---------- UI ----------

    void OeffneUI()
    {
        if (farbPanel == null)
            BaueUI();

        uiOffen = true;
        wische = 0;
        draggt = false;
        AktualisiereHinweis();

        farbPanel.SetActive(true);
        farbPanel.transform.localScale = Vector3.zero;
        LeanTween.scale(farbPanel, Vector3.one, 0.35f).setEaseOutBack();
    }

    void SchliesseUI()
    {
        uiOffen = false;
        draggt = false;
        if (farbPanel != null && farbPanel.activeSelf)
            LeanTween.scale(farbPanel, Vector3.zero, 0.25f).setEaseInBack()
                     .setOnComplete(() => farbPanel.SetActive(false));
    }

    void AktualisiereHinweis()
    {
        hinweisText.text = "Wischen zum Mischen  -  noch " + (WischeNoetig - wische) + "x wischen";
    }

    void BaueUI()
    {
        Font schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGO = new GameObject("FarbUI_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGO.AddComponent<GraphicRaycaster>();

        farbPanel = new GameObject("FarbPanel");
        farbPanel.transform.SetParent(canvas.transform, false);
        var panelBild = farbPanel.AddComponent<Image>();
        panelBild.color = new Color(0f, 0f, 0f, 0.8f);
        var panelRect = panelBild.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0, 40);
        panelRect.sizeDelta = new Vector2(460, 190);

        // Farbverlauf (Gradient als Textur erzeugt)
        var gradientTex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; x++)
            gradientTex.SetPixel(x, 0, Color.HSVToRGB(x / 255f, 0.9f, 1f));
        gradientTex.Apply();

        var gradientGO = new GameObject("Gradient");
        gradientGO.transform.SetParent(farbPanel.transform, false);
        var gradientBild = gradientGO.AddComponent<Image>();
        gradientBild.sprite = Sprite.Create(gradientTex, new Rect(0, 0, 256, 1), new Vector2(0.5f, 0.5f));
        var gradRect = gradientBild.rectTransform;
        gradRect.anchoredPosition = new Vector2(0, 20);
        gradRect.sizeDelta = new Vector2(400, 45);

        // Farb-Vorschau
        var vorschauGO = new GameObject("Vorschau");
        vorschauGO.transform.SetParent(farbPanel.transform, false);
        vorschau = vorschauGO.AddComponent<Image>();
        vorschau.color = Color.HSVToRGB(hue, 0.9f, 1f);
        var vorRect = vorschau.rectTransform;
        vorRect.anchoredPosition = new Vector2(0, 70);
        vorRect.sizeDelta = new Vector2(55, 55);

        // Hinweistext
        var textGO = new GameObject("Hinweis");
        textGO.transform.SetParent(farbPanel.transform, false);
        hinweisText = textGO.AddComponent<Text>();
        hinweisText.font = schrift;
        hinweisText.fontSize = 20;
        hinweisText.color = Color.white;
        hinweisText.alignment = TextAnchor.MiddleCenter;
        hinweisText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var textRect = hinweisText.rectTransform;
        textRect.anchoredPosition = new Vector2(0, -55);
        textRect.sizeDelta = new Vector2(440, 30);

        farbPanel.SetActive(false);
    }
}
