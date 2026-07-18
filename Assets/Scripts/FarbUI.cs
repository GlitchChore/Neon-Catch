using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Baut die komplette UI selbst auf (kein manuelles Canvas noetig):
/// zeigt oben links die eigene Farbe und rechts drei Balken mit dem
/// Flaechen-Anteil von Rot, Blau und Gelb.
/// Gehoert auf ein leeres GameObject in der Szene.
/// </summary>
public class FarbUI : MonoBehaviour
{
    Text meineFarbeText;
    readonly Text[] anteilTexte = new Text[3];
    readonly RectTransform[] balken = new RectTransform[3];
    Font schrift;

    const float BalkenMaxBreite = 200f;

    void Start()
    {
        schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BaueUI();
    }

    void BaueUI()
    {
        var canvasGO = new GameObject("FarbUI_Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Oben links: eigene Farbe
        meineFarbeText = ErzeugeText(canvas.transform, "MeineFarbe",
            new Vector2(0, 1), new Vector2(20, -20), 26, TextAnchor.UpperLeft);
        meineFarbeText.text = "Verbinde...";

        // Oben rechts: drei Farb-Balken untereinander
        for (int i = 0; i < 3; i++)
        {
            float y = -20 - i * 34;

            var balkenGO = new GameObject("Balken_" + FarbManager.FarbNamen[i]);
            balkenGO.transform.SetParent(canvas.transform, false);
            var bild = balkenGO.AddComponent<Image>();
            bild.color = FarbManager.Farben[i];
            var rect = bild.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-20, y);
            rect.sizeDelta = new Vector2(4, 26);
            balken[i] = rect;

            anteilTexte[i] = ErzeugeText(canvas.transform, "Anteil_" + FarbManager.FarbNamen[i],
                new Vector2(1, 1), new Vector2(-20 - BalkenMaxBreite - 10, y), 20, TextAnchor.UpperRight);
            anteilTexte[i].color = FarbManager.Farben[i];
        }
    }

    Text ErzeugeText(Transform eltern, string name, Vector2 anker, Vector2 position, int groesse, TextAnchor ausrichtung)
    {
        var go = new GameObject(name);
        go.transform.SetParent(eltern, false);
        var text = go.AddComponent<Text>();
        text.font = schrift;
        text.fontSize = groesse;
        text.alignment = ausrichtung;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anker;
        rect.pivot = anker;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(300, 30);
        return text;
    }

    void Update()
    {
        // Eigene Farbe anzeigen, sobald der lokale Spieler existiert
        var lokalerSpieler = NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<FarbNetzwerk>()
            : null;

        if (lokalerSpieler != null && lokalerSpieler.farbIndex >= 0)
        {
            meineFarbeText.text = "Deine Farbe: " + FarbManager.FarbNamen[lokalerSpieler.farbIndex].ToUpper();
            meineFarbeText.color = FarbManager.Farben[lokalerSpieler.farbIndex];
        }
        else if (NetworkClient.isConnected)
        {
            meineFarbeText.text = "Warte auf Farbe...";
            meineFarbeText.color = Color.white;
        }
        else
        {
            meineFarbeText.text = "Nicht verbunden";
            meineFarbeText.color = Color.gray;
        }

        // Farb-Anteile aktualisieren
        if (FarbManager.Instance == null)
            return;

        for (int i = 0; i < 3; i++)
        {
            float anteil = FarbManager.Instance.Anteil(i);
            anteilTexte[i].text = FarbManager.FarbNamen[i] + ": " + (anteil * 100f).ToString("0.0") + " %";
            balken[i].sizeDelta = new Vector2(Mathf.Max(4f, BalkenMaxBreite * anteil), 26);
        }
    }
}
