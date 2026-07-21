using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NeonCatch
{
    // ==================================================================
    // TOP-UI - immer sichtbare Leiste oben mit den 3 Hauptwerten:
    //   🏆 Trophaeen-Fortschritt (x/100)
    //   💠 Juwelen-Fortschritt   (x/100)
    //   🪙 MapCoins
    //
    // - Blinkt kurz GOLD, wenn ein 100er voll wird.
    // - Zeigt beim Box-Oeffnen ein Item-Popup in der Farbe der Seltenheit.
    // - Baut sich komplett per Code (CreateFullUI) und haengt sich selbst an.
    //
    // Hinweis: die eingebaute Unity-Schrift kann keine Emojis - 🏆/💠/🪙
    // erscheinen als Kaestchen. Fuer echte Symbole TMP + Emoji-Font nutzen
    // oder kleine Sprites vor die Texte setzen.
    // ==================================================================
    public class TopUI : MonoBehaviour
    {
        public Text trophyText, jewelText, coinText;

        Font schrift;
        int letzteTrophies, letzteJewels;
        Coroutine blinkT, blinkJ;

        static readonly Color Gold     = new Color(1f, 0.85f, 0.1f);
        static readonly Color HellGrau = new Color(0.85f, 0.85f, 0.88f);

        // -------------------- Auto-Aufbau --------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            ProgressionManager.EnsureInstance();   // Manager + Pfad-Systeme sicherstellen
            if (FindAnyObjectByType<TopUI>() == null)
                CreateFullUI();
        }

        // Baut Canvas + Leiste + 3 Texte + Popup-Wurzel + Test-Knoepfe.
        public static TopUI CreateFullUI()
        {
            var canvasGO = new GameObject("TopUI");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;                     // ueber dem restlichen UI
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);   // Hochformat, mobil
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // EventSystem (NEUES Input System!) nur, falls keins existiert
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            var ui = canvasGO.AddComponent<TopUI>();
            ui.BaueInhalt(canvasGO.transform);
            return ui;
        }

        void BaueInhalt(Transform canvas)
        {
            schrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Dunkle Leiste ganz oben (volle Breite)
            var leiste = MacheBild(canvas, "Leiste", new Color(0.05f, 0.06f, 0.1f, 0.9f));
            Setze(leiste.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                  new Vector2(0, -55), new Vector2(0, 110));

            // Die 3 Werte gleichmaessig verteilt (Anker bei 22% / 50% / 78%)
            trophyText = MacheWert(canvas, "trophyText", "🏆 0/100", 0.22f);
            jewelText  = MacheWert(canvas, "jewelText",  "💠 0/100", 0.50f);
            coinText   = MacheWert(canvas, "coinText",   "🪙 0",     0.78f);

            // ---- Test-Knoepfe unten rechts (koennen geloescht werden) ----
            var t1 = MacheButton(canvas, "Test50Trophies", "+50 🏆", HellGrau, 34);
            Setze(t1.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                  new Vector2(-30, 150), new Vector2(220, 90));
            t1.onClick.AddListener(() => ProgressionManager.EnsureInstance().DebugAdd(50, 0));

            var t2 = MacheButton(canvas, "Test50Jewels", "+50 💠", HellGrau, 34);
            Setze(t2.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                  new Vector2(-30, 50), new Vector2(220, 90));
            t2.onClick.AddListener(() => ProgressionManager.EnsureInstance().DebugAdd(0, 50));
        }

        // -------------------- Aktualisieren --------------------

        void OnEnable()
        {
            ProgressionManager.OnCurrencyChanged += BeiWaehrung;
        }

        void OnDisable()
        {
            ProgressionManager.OnCurrencyChanged -= BeiWaehrung;
        }

        void Start()
        {
            var pm = ProgressionManager.EnsureInstance();
            letzteTrophies = pm.Get(ProgressionManager.TROPHIES);
            letzteJewels   = pm.Get(ProgressionManager.JEWELS);
            AktualisiereAlles();
        }

        void BeiWaehrung(string typ, int neu)
        {
            // Blinken, wenn ein neuer 100er-Block voll wurde
            if (typ == ProgressionManager.TROPHIES)
            {
                if (neu / 100 > letzteTrophies / 100) StarteBlink(ref blinkT, trophyText);
                letzteTrophies = neu;
            }
            else if (typ == ProgressionManager.JEWELS)
            {
                if (neu / 100 > letzteJewels / 100) StarteBlink(ref blinkJ, jewelText);
                letzteJewels = neu;
            }
            AktualisiereAlles();
        }

        void AktualisiereAlles()
        {
            if (trophyText == null) return;
            var pm = ProgressionManager.EnsureInstance();
            int tr = pm.Get(ProgressionManager.TROPHIES);
            int jw = pm.Get(ProgressionManager.JEWELS);
            int co = pm.Get(ProgressionManager.MAPCOINS);

            trophyText.text = "🏆 " + (tr % 100) + "/100";
            jewelText.text  = "💠 " + (jw % 100) + "/100";
            coinText.text   = "🪙 " + co;
        }

        // -------------------- Blink-Effekt (gold) --------------------

        void StarteBlink(ref Coroutine feld, Text ziel)
        {
            if (feld != null) StopCoroutine(feld);
            feld = StartCoroutine(BlinkGold(ziel));
        }

        IEnumerator BlinkGold(Text ziel)
        {
            float t = 0f;
            while (t < 1.6f)   // ~1,6 s blinken
            {
                t += Time.unscaledDeltaTime;
                // zwischen Gold und Weiss hin und her (Sinus)
                float k = (Mathf.Sin(t * 18f) + 1f) * 0.5f;
                ziel.color = Color.Lerp(Color.white, Gold, k);
                yield return null;
            }
            ziel.color = Color.white;
        }

        // -------------------- Kleine UI-Bausteine --------------------

        Text MacheWert(Transform parent, string name, string inhalt, float ankerX)
        {
            var t = MacheText(parent, name, inhalt, 44, TextAnchor.MiddleCenter, Color.white);
            Setze(t.rectTransform, new Vector2(ankerX, 1), new Vector2(ankerX, 1), new Vector2(0.5f, 1),
                  new Vector2(0, -55), new Vector2(340, 80));
            return t;
        }

        Text MacheText(Transform parent, string name, string inhalt, int font, TextAnchor anker, Color farbe)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = schrift;
            t.text = inhalt;
            t.fontSize = font;
            t.alignment = anker;
            t.color = farbe;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        Image MacheBild(Transform parent, string name, Color farbe)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = farbe;
            return img;
        }

        Button MacheButton(Transform parent, string name, string label, Color farbe, int font)
        {
            var img = MacheBild(parent, name, farbe);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var t = MacheText(img.transform, "Label", label, font, TextAnchor.MiddleCenter, new Color(0.06f, 0.06f, 0.08f));
            Fuelle(t.rectTransform);
            return btn;
        }

        static void Setze(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Fuelle(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
