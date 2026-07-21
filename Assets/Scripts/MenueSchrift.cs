using UnityEngine;

namespace NeonCatch
{
    // ======================================================================
    // Einheitlicher Menue-Look fuer die alten (IMGUI-)Menues:
    // - Schriftart wie die TextMeshPro-UI (LiberationSans aus dem TMP-Pack,
    //   liegt als Resources/MenueSchrift)
    // - Knoepfe/Eingabefelder: HELLE Flaechen mit IMMER SCHWARZER Schrift -
    //   auch beim Drueberfahren und Klicken (vorher wurde die Schrift auf
    //   hellem Hover-Hintergrund weiss und war kaum lesbar)
    // Ein Aufruf am Anfang von OnGUI genuegt; Layout und Ablauf bleiben gleich.
    // ======================================================================
    public static class MenueSchrift
    {
        static Font schrift;
        static bool gesucht;
        static Texture2D texNormal, texHover, texAktiv;

        static Texture2D Farbtex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public static void Anwenden()
        {
            if (!gesucht)
            {
                gesucht = true;
                schrift = Resources.Load<Font>("MenueSchrift");
                if (schrift == null)
                    Debug.LogWarning("MenueSchrift: Resources/MenueSchrift.ttf nicht gefunden - Standardschrift bleibt.");
            }
            if (schrift != null)
                GUI.skin.font = schrift;

            if (texNormal == null)
            {
                texNormal = Farbtex(new Color(0.85f, 0.85f, 0.88f));
                texHover  = Farbtex(new Color(0.76f, 0.79f, 0.81f));
                texAktiv  = Farbtex(new Color(0.62f, 0.68f, 0.70f));
            }

            Color schwarz = new Color(0.06f, 0.06f, 0.08f);

            var b = GUI.skin.button;
            b.normal.background = texNormal;
            b.hover.background = texHover;
            b.active.background = texAktiv;
            b.focused.background = texNormal;
            b.normal.textColor = schwarz;
            b.hover.textColor = schwarz;    // Schrift bleibt beim Drueberfahren SCHWARZ
            b.active.textColor = schwarz;
            b.focused.textColor = schwarz;

            var tf = GUI.skin.textField;
            tf.normal.background = texNormal;
            tf.hover.background = texNormal;
            tf.focused.background = texHover;
            tf.active.background = texHover;
            // Text vertikal mittig mit Luft nach unten - sonst werden
            // Unterlaengen (y, g, p) am Feldrand abgeschnitten
            tf.alignment = TextAnchor.MiddleLeft;
            tf.padding = new RectOffset(10, 10, 4, 6);
            tf.clipping = TextClipping.Overflow;
            tf.normal.textColor = schwarz;
            tf.hover.textColor = schwarz;
            tf.focused.textColor = schwarz;
            tf.active.textColor = schwarz;
        }
    }
}
