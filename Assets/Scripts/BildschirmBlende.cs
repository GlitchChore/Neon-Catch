using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace NeonCatch
{
    // Einfache Bildschirm-Abblende (schwarzes Overlay) für Teleport-Effekte
    // wie die Falltüren. Einziges Objekt für das ganze Spiel (Singleton),
    // entsteht automatisch beim ersten Gebrauch.
    public class BildschirmBlende : MonoBehaviour
    {
        static BildschirmBlende instanz;
        Image bild;

        public static BildschirmBlende Hole()
        {
            if (instanz != null) return instanz;

            var go = new GameObject("Bildschirm_Blende");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;   // über allem anderen (HUD, Welt) zeichnen
            go.AddComponent<CanvasScaler>();

            var bildGo = new GameObject("Schwarz");
            bildGo.transform.SetParent(go.transform, false);
            var bild = bildGo.AddComponent<Image>();
            bild.color = new Color(0f, 0f, 0f, 0f);
            bild.raycastTarget = false;   // blockiert keine Klicks auf Kanonen/Falltüren

            RectTransform rect = bild.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            instanz = go.AddComponent<BildschirmBlende>();
            instanz.bild = bild;
            return instanz;
        }

        public IEnumerator Ausblenden(float dauer) { yield return Blende(bild.color.a, 1f, dauer); }
        public IEnumerator Einblenden(float dauer) { yield return Blende(bild.color.a, 0f, dauer); }

        IEnumerator Blende(float von, float nach, float dauer)
        {
            float zeit = 0f;
            while (zeit < dauer)
            {
                zeit += Time.deltaTime;
                float a = Mathf.Lerp(von, nach, Mathf.Clamp01(zeit / Mathf.Max(dauer, 0.0001f)));
                bild.color = new Color(0f, 0f, 0f, a);
                yield return null;
            }
            bild.color = new Color(0f, 0f, 0f, nach);
        }
    }
}
