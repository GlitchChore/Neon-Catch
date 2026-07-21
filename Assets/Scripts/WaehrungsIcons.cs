using UnityEngine;

namespace NeonCatch
{
    // ==================================================================
    // WÄHRUNGS-ICONS - kleine, per Code gemalte Symbole als Emoji-Ersatz.
    // Die eingebaute Unity-Schrift kann keine echten Emojis (🏆/💠/🪙),
    // darum werden hier saubere Symbole als Texturen erzeugt:
    //   Trophäe = goldener Stern | Juwel = tuerkiser Diamant | Muenze = Kreis
    // Einmal erzeugt und gecacht - nutzbar in IMGUI (GUI.DrawTexture) und uGUI.
    // ==================================================================
    public static class WaehrungsIcons
    {
        const int G = 64;
        static readonly Color Rand = new Color(0.08f, 0.07f, 0.05f, 1f);   // dunkle Kontur
        static readonly Color Leer = new Color(0, 0, 0, 0);

        static Texture2D _trophaee, _juwel, _muenze;

        public static Texture2D Trophaee => _trophaee != null ? _trophaee : (_trophaee = BaueStern(new Color(1f, 0.84f, 0.12f)));
        public static Texture2D Juwel    => _juwel    != null ? _juwel    : (_juwel    = BaueDiamant(new Color(0.15f, 0.9f, 0.95f)));
        public static Texture2D Muenze   => _muenze   != null ? _muenze   : (_muenze   = BaueKreis(new Color(1f, 0.72f, 0.15f)));

        static Texture2D Neu()
        {
            var t = new Texture2D(G, G, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[G * G];
            for (int i = 0; i < px.Length; i++) px[i] = Leer;
            t.SetPixels(px);
            return t;
        }

        static Texture2D BaueKreis(Color farbe)
        {
            var t = Neu();
            float c = (G - 1) * 0.5f, rIn = G * 0.40f, rOut = G * 0.46f;
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d <= rIn) t.SetPixel(x, y, farbe);
                    else if (d <= rOut) t.SetPixel(x, y, Rand);
                }
            t.Apply();
            return t;
        }

        static Texture2D BaueDiamant(Color farbe)
        {
            var t = Neu();
            float c = (G - 1) * 0.5f;
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                {
                    float d = Mathf.Abs(x - c) / (G * 0.44f) + Mathf.Abs(y - c) / (G * 0.48f);
                    if (d <= 0.86f) t.SetPixel(x, y, farbe);
                    else if (d <= 1.0f) t.SetPixel(x, y, Rand);
                }
            t.Apply();
            return t;
        }

        static Texture2D BaueStern(Color farbe)
        {
            var t = Neu();
            var innen  = SternEcken(0.40f, 0.15f);   // fuer die Fuellung
            var aussen = SternEcken(0.47f, 0.19f);   // fuer die Kontur
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                {
                    var p = new Vector2((x + 0.5f) / G, (y + 0.5f) / G);
                    if (ImPolygon(p, innen)) t.SetPixel(x, y, farbe);
                    else if (ImPolygon(p, aussen)) t.SetPixel(x, y, Rand);
                }
            t.Apply();
            return t;
        }

        // 5-zackiger Stern: 10 Eckpunkte (aussen/innen abwechselnd), Spitze oben.
        static Vector2[] SternEcken(float outer, float inner)
        {
            var v = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float ang = -Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0) ? outer : inner;
                v[i] = new Vector2(0.5f + r * Mathf.Cos(ang), 0.5f + r * Mathf.Sin(ang));
            }
            return v;
        }

        // Punkt-in-Polygon (Strahl-Methode).
        static bool ImPolygon(Vector2 p, Vector2[] v)
        {
            bool innen = false;
            int n = v.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((v[i].y > p.y) != (v[j].y > p.y)) &&
                    (p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x))
                    innen = !innen;
            }
            return innen;
        }
    }
}
