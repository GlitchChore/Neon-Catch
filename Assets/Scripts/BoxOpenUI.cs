using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NeonCatch
{
    // ==================================================================
    // BOX-OPEN-UI - zeigt beim Oeffnen einer Box die passende 2D-Truhe
    // aus dem Paket "Modern 2D Animated Chests":
    //   SkinBox = Royal | MapBox = Crystal | PetBox = Energy
    //
    // Die Truhe ist ein SpriteRenderer-Prefab (Welt-2D). Damit sie sauber
    // ins UI passt, wird sie auf einer weit entfernten "Buehne" von einer
    // eigenen Kamera auf eine RenderTexture gerendert und als RawImage
    // gezeigt (gleiches Prinzip wie die Charakter-Schau). Nach der Oeffnen-
    // Animation fliegt das Item in der Farbe der Seltenheit heraus.
    //
    // Die Truhen-Prefabs liegen in Resources/Truhen (dorthin kopiert der
    // TruhenResourcenBauer sie im Editor). Fehlt eine Truhe, gibt es als
    // Rueckfall ein einfaches farbiges Item-Popup.
    // ==================================================================
    public class BoxOpenUI : MonoBehaviour
    {
        public static BoxOpenUI Instance { get; private set; }

        Canvas canvas;
        RawImage truhenBild;
        Text infoText;
        Transform buehne;
        Camera buehnenKamera;
        RenderTexture rt;
        GameObject aktuelleTruhe;
        bool laeuft;

        static readonly Vector2 M = new Vector2(0.5f, 0.5f);

        // -------------------- Auto-Aufbau --------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<BoxOpenUI>() == null)
            {
                var go = new GameObject("BoxOpenUI");
                Instance = go.AddComponent<BoxOpenUI>();
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()  { BoxOpener.OnBoxGeoeffnet += BeiBoxGeoeffnet; }
        void OnDisable() { BoxOpener.OnBoxGeoeffnet -= BeiBoxGeoeffnet; }

        void OnDestroy()
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
        }

        void BeiBoxGeoeffnet(string boxTyp, string seltenheit, Color farbe)
        {
            if (laeuft) return;   // eine Oeffnung nach der anderen
            StartCoroutine(Oeffne(boxTyp, seltenheit, farbe));
        }

        // Welche Truhe gehoert zu welcher Box?
        static string TruheFuer(string boxTyp)
        {
            switch (boxTyp)
            {
                case "SkinBox": return "Royal";
                case "MapBox":  return "Crystal";
                default:        return "Energy";   // PetBox
            }
        }

        IEnumerator Oeffne(string boxTyp, string seltenheit, Color farbe)
        {
            laeuft = true;
            BaueUiFallsNoetig();

            canvas.gameObject.SetActive(true);
            buehnenKamera.enabled = true;
            truhenBild.enabled = false;
            infoText.text = "";

            // Truhe laden und auf die Buehne stellen
            var prefab = Resources.Load<GameObject>("Truhen/PF_Chest_" + TruheFuer(boxTyp));
            if (prefab != null)
            {
                aktuelleTruhe = Instantiate(prefab);
                aktuelleTruhe.transform.SetParent(buehne, false);
                aktuelleTruhe.transform.localPosition = Vector3.zero;
                aktuelleTruhe.transform.localRotation = Quaternion.identity;
                Zentriere2D(aktuelleTruhe);
                truhenBild.enabled = true;

                var anim = aktuelleTruhe.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    // Laeuft auch bei Time.timeScale = 0 und immer (nicht wegcullen)
                    anim.updateMode = AnimatorUpdateMode.UnscaledTime;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                yield return Warte(0.7f);   // Intro/Idle kurz zeigen

                // Oeffnen ausloesen: egal wie der Controller es nennt -
                // alle Trigger feuern und alle Bools auf true setzen.
                if (anim != null)
                    foreach (var p in anim.parameters)
                    {
                        if (p.type == AnimatorControllerParameterType.Trigger) anim.SetTrigger(p.name);
                        else if (p.type == AnimatorControllerParameterType.Bool) anim.SetBool(p.name, true);
                    }

                yield return Warte(1.1f);   // Oeffnen-Animation laufen lassen
            }

            // Item fliegt in der Seltenheits-Farbe heraus
            yield return ItemReveal(seltenheit + "!", farbe);

            // Warten oder bis der Spieler tippt, dann schliessen
            infoText.text = "Zum Schließen tippen";
            float t = 0f;
            while (t < 2.5f && !Getippt())
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (aktuelleTruhe != null) Destroy(aktuelleTruhe);
            buehnenKamera.enabled = false;
            canvas.gameObject.SetActive(false);
            laeuft = false;
        }

        // -------------------- Item-Reveal --------------------

        IEnumerator ItemReveal(string text, Color farbe)
        {
            var go = new GameObject("Item", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(canvas.transform, false);
            var rt2 = go.GetComponent<RectTransform>();
            var cg  = go.GetComponent<CanvasGroup>();
            Setze(rt2, M, M, M, new Vector2(0, 60), new Vector2(260, 260));

            var glow = NeuesBild(go.transform, "Glow", farbe);
            Fuelle(glow.rectTransform);

            var lbl = NeuerText(go.transform, "Label", text, 48, Color.white);
            Setze(lbl.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 1),
                  new Vector2(0, -14), new Vector2(520, 80));

            float t = 0f, dauer = 1.2f;
            while (t < dauer)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dauer);
                rt2.localScale = Vector3.one * Mathf.SmoothStep(0.2f, 1.15f, Mathf.Min(1f, p * 3f));
                rt2.anchoredPosition = new Vector2(0f, Mathf.Lerp(60f, 260f, p));
                cg.alpha = p < 0.75f ? 1f : Mathf.Lerp(1f, 0f, (p - 0.75f) / 0.25f);
                yield return null;
            }
            Destroy(go);
        }

        // -------------------- Aufbau (einmalig) --------------------

        void BaueUiFallsNoetig()
        {
            if (canvas != null) return;

            var canvasGO = new GameObject("BoxOpenCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;   // ueber dem TopUI
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // Abdunkeln
            var dunkel = NeuesBild(canvas.transform, "Dunkel", new Color(0f, 0f, 0f, 0.85f));
            Fuelle(dunkel.rectTransform);

            // RenderTexture + Truhen-Bild
            rt = new RenderTexture(600, 600, 16) { name = "TruhenRT" };
            rt.Create();
            var riGO = new GameObject("TruhenBild", typeof(RectTransform));
            riGO.transform.SetParent(canvas.transform, false);
            truhenBild = riGO.AddComponent<RawImage>();
            truhenBild.texture = rt;
            Setze(truhenBild.rectTransform, M, M, M, new Vector2(0, 60), new Vector2(760, 760));

            // Info-Text unten
            infoText = NeuerText(canvas.transform, "Info", "", 40, new Color(1f, 1f, 1f, 0.9f));
            Setze(infoText.rectTransform, M, M, M, new Vector2(0, -520), new Vector2(900, 90));

            canvas.gameObject.SetActive(false);

            // Buehne + Kamera (weit weg von der Spielwelt)
            var buehneGO = new GameObject("TruhenBuehne");
            buehneGO.transform.SetParent(transform, false);
            buehneGO.transform.position = new Vector3(3000f, -1000f, 3000f);
            buehne = buehneGO.transform;

            var kamGO = new GameObject("TruhenKamera");
            kamGO.transform.SetParent(buehne, false);
            kamGO.transform.localPosition = new Vector3(0f, 0f, -10f);
            buehnenKamera = kamGO.AddComponent<Camera>();
            buehnenKamera.orthographic = true;
            buehnenKamera.orthographicSize = 1.5f;         // Sichthoehe = 3 Einheiten
            buehnenKamera.targetTexture = rt;              // rendert nur ins Bild
            buehnenKamera.clearFlags = CameraClearFlags.SolidColor;
            buehnenKamera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent
            buehnenKamera.nearClipPlane = 0.01f;
            buehnenKamera.farClipPlane = 50f;
            buehnenKamera.enabled = false;   // nur waehrend des Oeffnens rendern
        }

        // Truhe auf angenehme Groesse skalieren und mittig vor die Kamera setzen.
        void Zentriere2D(GameObject go)
        {
            var rends = go.GetComponentsInChildren<SpriteRenderer>();
            if (rends.Length == 0) return;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            float zielHoehe = 2.2f;   // etwas kleiner als die Sichthoehe (3)
            if (b.size.y > 0.01f) go.transform.localScale *= zielHoehe / b.size.y;

            // Nach dem Skalieren neu messen und auf den Buehnen-Ursprung schieben
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            go.transform.position += new Vector3(buehne.position.x - b.center.x,
                                                 buehne.position.y - b.center.y, 0f);
            var lp = go.transform.localPosition;
            go.transform.localPosition = new Vector3(lp.x, lp.y, 0f);
        }

        // -------------------- kleine Helfer --------------------

        static bool Getippt()
        {
            var maus = UnityEngine.InputSystem.Mouse.current;
            if (maus != null && maus.leftButton.wasPressedThisFrame) return true;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
        }

        static IEnumerator Warte(float sek)
        {
            float t = 0f;
            while (t < sek) { t += Time.unscaledDeltaTime; yield return null; }
        }

        Image NeuesBild(Transform parent, string name, Color farbe)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = farbe;
            return img;
        }

        Text NeuerText(Transform parent, string name, string inhalt, int font, Color farbe)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = inhalt;
            t.fontSize = font;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = farbe;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void Setze(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static void Fuelle(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
