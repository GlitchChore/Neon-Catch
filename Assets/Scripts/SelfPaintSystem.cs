using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// FARBMIMIK-Malen: Taste E oeffnet das Mal-Studio (LeanTween-Animation).
/// - Oben: Farbverlauf - anklicken/ziehen waehlt die Farbe
/// - Mitte: Zeichenflaeche = deine Figur-Haut, mit der Maus zeichnen
/// - Rechts: 3 STIFTE (Duenn / Mittel / Dick) + "Alles fuellen"
/// Das Gemalte erscheint auf deiner Figur (Mitspieler sehen es - die eigene
/// Figur ist in der Ego-Ansicht ausgeblendet) und wird beim Schliessen per
/// Photon an alle verteilt. Erlaubt in VERSTECKEN und SUCHEN (auch
/// eingefroren darf man weiter malen), nur nicht als Sucher.
/// Gehoert auf das Spieler-Prefab (mit PhotonView + PhotonTransformView).
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

    /// <summary>Ist die Mal-UI gerade offen? (FreezePenalty gibt dann den Cursor frei)</summary>
    public static bool MalUiOffen;

    const int TexturGroesse = 128;                          // Pixel der Figur-Haut
    static readonly int[] PinselRadien = { 2, 5, 10 };      // Duenn / Mittel / Dick
    static readonly string[] PinselNamen = { "Dünn", "Mittel", "Dick" };

    Renderer[] eigeneRenderer;
    Texture2D malTextur;      // die Haut der Figur (lokal gemalt, per RPC verteilt)
    bool texturGeaendert;

    // UI (existiert nur beim lokalen Spieler)
    GameObject farbPanel;
    RawImage canvasBild;
    Image vorschau;
    TMP_Text hinweisText;
    Image[] stiftKnoepfe;
    bool uiOffen;
    int pinsel = 1;           // Start: Mittel
    float hue = 0.5f;
    Vector2 letzterMalPunkt;
    bool malt;

    void Awake()
    {
        eigeneRenderer = GetComponentsInChildren<Renderer>();

        // Bot-Daten aus InstantiationData (sofort verfuegbar, kein RPC-Verzug)
        object[] daten = photonView.InstantiationData;
        if (daten != null && daten.Length >= 4)
        {
            istBot = true;
            spielerName = (string)daten[0];
            farbeGesetzt = true;
            BaueBotFigur();   // Synty-Figur wie im Abschiess-Modus statt Kapsel
            SetzeSichtbareFarbe(new Color((float)daten[1], (float)daten[2], (float)daten[3]));
        }
    }

    // Bots tragen dieselben Figuren wie im Abschiess-Modus (KI/Bot_1..4)
    // statt der einfachen Kapsel - inklusive Idle-Animationen.
    void BaueBotFigur()
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;   // Kapsel ausblenden

        // Bot-Nummer aus dem Namen ("Bot 3" -> 3), sonst 1
        int nr = 1;
        for (int i = spielerName.Length - 1; i >= 0; i--)
            if (char.IsDigit(spielerName[i])) { nr = spielerName[i] - '0'; break; }
        nr = Mathf.Clamp((nr - 1) % 4 + 1, 1, 4);

        GameObject prefab = Resources.Load<GameObject>("KI/Bot_" + nr);
        if (prefab == null) return;

        var figur = Instantiate(prefab, transform);
        figur.transform.localPosition = Vector3.zero;
        figur.transform.localRotation = Quaternion.identity;
        float hoehe = 0f;
        foreach (Renderer r in figur.GetComponentsInChildren<Renderer>())
            hoehe = Mathf.Max(hoehe, r.bounds.size.y);
        if (hoehe > 0.01f)
            figur.transform.localScale *= 0.55f / hoehe;   // passend zur kleinen Spielwelt
        figur.AddComponent<NeonCatch.BotAnimation>();

        // Anmal-Farbe soll auf die Synty-Figur wirken, nicht auf die Kapsel
        eigeneRenderer = figur.GetComponentsInChildren<Renderer>();
    }

    void Start()
    {
        if (photonView.IsMine && !istBot)
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
            {
                m.mainTexture = null;
                m.color = neu;
            }
    }

    void SetzeSichtbareTextur(Texture2D tex)
    {
        foreach (var r in eigeneRenderer)
            foreach (var m in r.materials)
            {
                m.color = Color.white;    // Textur unverfaelscht zeigen
                m.mainTexture = tex;
            }
    }

    /// <summary>Vom MasterClient aufgerufen, wenn eine neue Runde in der Lobby beginnt.</summary>
    [PunRPC]
    public void RpcResetFuerLobby()
    {
        farbeGesetzt = false;
        malTextur = null;
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

    // Ganze Figur einfaerben ("Alles fuellen" / Bots)
    [PunRPC]
    void RpcSetzeFarbe(float gewaehlterHue)
    {
        farbeGesetzt = true;
        malTextur = null;
        SetzeSichtbareFarbe(Color.HSVToRGB(gewaehlterHue, 0.9f, 1f));
    }

    // Gemalte Haut an alle verteilen (PNG-Bytes, 128x128 = wenige KB)
    [PunRPC]
    void RpcSetzeTextur(byte[] png)
    {
        if (png == null || png.Length == 0) return;
        farbeGesetzt = true;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (tex.LoadImage(png))
        {
            tex.filterMode = FilterMode.Point;
            SetzeSichtbareTextur(tex);
        }
    }

    void Update()
    {
        if (!photonView.IsMine || istBot)
            return;

        var pm = GamePhaseManager.Instance;
        SpielPhase phase = pm != null ? pm.phase : SpielPhase.Lobby;
        bool binSucher = pm != null && pm.IstSucher(photonView.ViewID);

        // Malen erlaubt: nur wer NICHT Sucher ist, in Verstecken UND Suchen
        bool darfMalen = !binSucher &&
            (phase == SpielPhase.Verstecken || phase == SpielPhase.Suchen);

        var tastatur = Keyboard.current;
        if (tastatur != null && tastatur.eKey.wasPressedThisFrame && darfMalen)
        {
            if (uiOffen) SchliesseUI();
            else OeffneUI();
        }

        if (uiOffen && !darfMalen)
            SchliesseUI();

        if (uiOffen)
            VerarbeiteMalen();
    }

    // ---------- Zeichnen ----------

    void VerarbeiteMalen()
    {
        var maus = Mouse.current;
        if (maus == null) return;
        Vector2 mausPos = maus.position.ReadValue();

        // Farbe waehlen: Klick/ziehen auf dem Farbverlauf
        if (maus.leftButton.isPressed && PunktInBild(vorschauGradient, mausPos, out Vector2 gradUv))
        {
            hue = Mathf.Clamp01(gradUv.x);
            vorschau.color = Color.HSVToRGB(hue, 0.9f, 1f);
        }

        // Zeichnen auf der Figur-Haut
        if (maus.leftButton.isPressed && PunktInBild(canvasBild, mausPos, out Vector2 uv))
        {
            Vector2 pixel = new Vector2(uv.x * TexturGroesse, uv.y * TexturGroesse);
            if (!malt)
                MalePunkt(pixel);
            else
                MaleLinie(letzterMalPunkt, pixel);   // durchgezogener Strich
            letzterMalPunkt = pixel;
            malt = true;
        }
        else if (!maus.leftButton.isPressed)
        {
            malt = false;
        }

        if (texturGeaendert)
        {
            texturGeaendert = false;
            malTextur.Apply();
            canvasBild.texture = malTextur;
            SetzeSichtbareTextur(malTextur);
        }
    }

    RawImage vorschauGradient;

    static bool PunktInBild(Graphic bild, Vector2 schirmPunkt, out Vector2 uv)
    {
        uv = Vector2.zero;
        if (bild == null) return false;
        RectTransform rt = bild.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, schirmPunkt, null, out Vector2 lokal))
            return false;
        Rect r = rt.rect;
        uv = new Vector2((lokal.x - r.xMin) / r.width, (lokal.y - r.yMin) / r.height);
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }

    void MaleLinie(Vector2 von, Vector2 nach)
    {
        float laenge = Vector2.Distance(von, nach);
        int schritte = Mathf.Max(1, Mathf.CeilToInt(laenge));
        for (int i = 0; i <= schritte; i++)
            MalePunkt(Vector2.Lerp(von, nach, i / (float)schritte));
    }

    void MalePunkt(Vector2 pixel)
    {
        int r = PinselRadien[pinsel];
        Color32 farbe = Color.HSVToRGB(hue, 0.9f, 1f);
        int cx = Mathf.RoundToInt(pixel.x), cy = Mathf.RoundToInt(pixel.y);

        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r) continue;
                int px = cx + x, py = cy + y;
                if (px < 0 || px >= TexturGroesse || py < 0 || py >= TexturGroesse) continue;
                malTextur.SetPixel(px, py, farbe);
            }
        texturGeaendert = true;
        farbeGesetzt = true;
    }

    void FuelleAlles()
    {
        Color32 farbe = Color.HSVToRGB(hue, 0.9f, 1f);
        var pixels = new Color32[TexturGroesse * TexturGroesse];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = farbe;
        malTextur.SetPixels32(pixels);
        texturGeaendert = true;
        farbeGesetzt = true;
    }

    // ---------- UI ----------

    void OeffneUI()
    {
        if (farbPanel == null)
            BaueUI();

        // Haut anlegen (weiss), falls noch keine existiert
        if (malTextur == null)
        {
            malTextur = new Texture2D(TexturGroesse, TexturGroesse, TextureFormat.RGBA32, false);
            malTextur.filterMode = FilterMode.Point;
            var pixels = new Color32[TexturGroesse * TexturGroesse];
            var start = (Color32)Color.white;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = start;
            malTextur.SetPixels32(pixels);
            malTextur.Apply();
            canvasBild.texture = malTextur;
        }

        uiOffen = true;
        MalUiOffen = true;
        malt = false;
        AktualisiereStiftKnoepfe();

        farbPanel.SetActive(true);
        farbPanel.transform.localScale = Vector3.zero;
        LeanTween.scale(farbPanel, Vector3.one, 0.35f).setEaseOutBack();
    }

    void SchliesseUI()
    {
        uiOffen = false;
        MalUiOffen = false;
        malt = false;
        if (farbPanel != null && farbPanel.activeSelf)
            LeanTween.scale(farbPanel, Vector3.zero, 0.25f).setEaseInBack()
                     .setOnComplete(() => farbPanel.SetActive(false));

        // Gemalte Haut an alle Mitspieler schicken
        if (malTextur != null && farbeGesetzt)
            photonView.RPC(nameof(RpcSetzeTextur), RpcTarget.All, malTextur.EncodeToPNG());
    }

    void AktualisiereStiftKnoepfe()
    {
        for (int i = 0; i < stiftKnoepfe.Length; i++)
            stiftKnoepfe[i].color = i == pinsel
                ? new Color(0.1f, 0.9f, 0.9f)          // gewaehlter Stift: Neon
                : new Color(0.85f, 0.85f, 0.88f);   // helle Flaeche wie alle Knoepfe
        hinweisText.text = "Stift: " + PinselNamen[pinsel] + "  -  [E] schließt und übernimmt";
    }

    void BaueUI()
    {
        var canvasGO = new GameObject("MalUI_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.dynamicPixelsPerUnit = 3f;
        canvasGO.AddComponent<GraphicRaycaster>();

        farbPanel = new GameObject("MalPanel");
        farbPanel.transform.SetParent(canvas.transform, false);
        var panelBild = farbPanel.AddComponent<Image>();
        panelBild.color = new Color(0f, 0f, 0f, 0.85f);
        var panelRect = panelBild.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(520, 400);

        // Farbverlauf oben (anklicken = Farbe waehlen)
        var gradientTex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; x++)
            gradientTex.SetPixel(x, 0, Color.HSVToRGB(x / 255f, 0.9f, 1f));
        gradientTex.Apply();

        var gradGO = new GameObject("Gradient");
        gradGO.transform.SetParent(farbPanel.transform, false);
        vorschauGradient = gradGO.AddComponent<RawImage>();
        vorschauGradient.texture = gradientTex;
        var gradRect = vorschauGradient.rectTransform;
        gradRect.anchoredPosition = new Vector2(-30, 160);
        gradRect.sizeDelta = new Vector2(360, 36);

        // Farb-Vorschau
        var vorschauGO = new GameObject("Vorschau");
        vorschauGO.transform.SetParent(farbPanel.transform, false);
        vorschau = vorschauGO.AddComponent<Image>();
        vorschau.color = Color.HSVToRGB(hue, 0.9f, 1f);
        var vorRect = vorschau.rectTransform;
        vorRect.anchoredPosition = new Vector2(200, 160);
        vorRect.sizeDelta = new Vector2(40, 40);

        // Zeichenflaeche (deine Figur-Haut)
        var cGO = new GameObject("Zeichenflaeche");
        cGO.transform.SetParent(farbPanel.transform, false);
        canvasBild = cGO.AddComponent<RawImage>();
        canvasBild.color = Color.white;
        var cRect = canvasBild.rectTransform;
        cRect.anchoredPosition = new Vector2(-90, -25);
        cRect.sizeDelta = new Vector2(260, 260);

        // 3 Stifte: Duenn / Mittel / Dick
        stiftKnoepfe = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var kGO = new GameObject("Stift_" + PinselNamen[i]);
            kGO.transform.SetParent(farbPanel.transform, false);
            var kBild = kGO.AddComponent<Image>();
            stiftKnoepfe[i] = kBild;
            var kRect = kBild.rectTransform;
            kRect.anchoredPosition = new Vector2(155, 60 - i * 60);
            kRect.sizeDelta = new Vector2(150, 48);

            var knopf = kGO.AddComponent<Button>();
            knopf.targetGraphic = kBild;
            knopf.onClick.AddListener(() => { pinsel = index; AktualisiereStiftKnoepfe(); });

            var tGO = new GameObject("Text");
            tGO.transform.SetParent(kGO.transform, false);
            var t = tGO.AddComponent<TextMeshProUGUI>();
            t.text = PinselNamen[i];
            t.fontSize = 20;
            t.color = new Color(0.06f, 0.06f, 0.08f);   // immer schwarze Schrift
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = kRect.sizeDelta;

            // Punkt-Symbol in Stift-Staerke daneben
            var pGO = new GameObject("Punkt");
            pGO.transform.SetParent(kGO.transform, false);
            var p = pGO.AddComponent<Image>();
            p.color = Color.white;
            float d = 6f + PinselRadien[i] * 2.2f;
            p.rectTransform.anchoredPosition = new Vector2(-58, 0);
            p.rectTransform.sizeDelta = new Vector2(d, d);
        }

        // Alles fuellen
        var fGO = new GameObject("Fuellen");
        fGO.transform.SetParent(farbPanel.transform, false);
        var fBild = fGO.AddComponent<Image>();
        fBild.color = new Color(0.85f, 0.85f, 0.88f);
        fBild.rectTransform.anchoredPosition = new Vector2(155, -125);
        fBild.rectTransform.sizeDelta = new Vector2(150, 44);
        var fKnopf = fGO.AddComponent<Button>();
        fKnopf.targetGraphic = fBild;
        fKnopf.onClick.AddListener(FuelleAlles);
        var fTGO = new GameObject("Text");
        fTGO.transform.SetParent(fGO.transform, false);
        var fT = fTGO.AddComponent<TextMeshProUGUI>();
        fT.text = "Alles füllen";
        fT.fontSize = 18;
        fT.color = new Color(0.06f, 0.06f, 0.08f);
        fT.alignment = TextAlignmentOptions.Center;
        fT.rectTransform.sizeDelta = fBild.rectTransform.sizeDelta;

        // Hinweistext unten
        var textGO = new GameObject("Hinweis");
        textGO.transform.SetParent(farbPanel.transform, false);
        hinweisText = textGO.AddComponent<TextMeshProUGUI>();
        hinweisText.fontSize = 18;
        hinweisText.color = Color.white;
        hinweisText.alignment = TextAlignmentOptions.Center;
        var textRect = hinweisText.rectTransform;
        textRect.anchoredPosition = new Vector2(0, -178);
        textRect.sizeDelta = new Vector2(500, 30);

        farbPanel.SetActive(false);
    }
}
