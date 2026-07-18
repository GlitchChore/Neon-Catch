using UnityEngine;

/// <summary>
/// Verwaltet die Mal-Leinwand: erzeugt die Textur, malt Farbkleckse
/// und zaehlt, wie viel Flaeche jede Farbe bedeckt.
/// Gehoert auf das Leinwand-Quad (braucht einen MeshCollider fuer die UV-Treffer).
/// </summary>
public class FarbManager : MonoBehaviour
{
    public static FarbManager Instance { get; private set; }

    [Header("Leinwand")]
    public int texturBreite = 512;
    public int texturHoehe = 512;
    public int pinselRadius = 10;

    // Reihenfolge: 0 = Rot, 1 = Blau, 2 = Gelb
    public static readonly Color32[] Farben =
    {
        new Color32(230, 40, 40, 255),   // Rot
        new Color32(40, 90, 230, 255),   // Blau
        new Color32(250, 210, 30, 255)   // Gelb
    };
    public static readonly string[] FarbNamen = { "Rot", "Blau", "Gelb" };

    Texture2D leinwand;
    Color32[] pixel;
    byte[] pixelBesitzer;            // 255 = unbemalt, sonst Farbindex 0-2
    readonly int[] pixelProFarbe = new int[3];
    bool texturGeaendert;

    void Awake()
    {
        Instance = this;

        leinwand = new Texture2D(texturBreite, texturHoehe, TextureFormat.RGBA32, false);
        leinwand.wrapMode = TextureWrapMode.Clamp;

        pixel = new Color32[texturBreite * texturHoehe];
        pixelBesitzer = new byte[texturBreite * texturHoehe];
        var weiss = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixel.Length; i++)
        {
            pixel[i] = weiss;
            pixelBesitzer[i] = 255;
        }
        leinwand.SetPixels32(pixel);
        leinwand.Apply();

        var meshRenderer = GetComponent<Renderer>();
        if (meshRenderer != null)
            meshRenderer.material.mainTexture = leinwand;
    }

    /// <summary>Malt einen runden Klecks an der UV-Position (0-1) in der angegebenen Farbe.</summary>
    public void Male(Vector2 uv, int farbIndex)
    {
        if (farbIndex < 0 || farbIndex >= Farben.Length)
            return;

        int cx = Mathf.Clamp((int)(uv.x * texturBreite), 0, texturBreite - 1);
        int cy = Mathf.Clamp((int)(uv.y * texturHoehe), 0, texturHoehe - 1);
        Color32 farbe = Farben[farbIndex];
        int r = pinselRadius;

        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r)
                    continue;

                int px = cx + x;
                int py = cy + y;
                if (px < 0 || px >= texturBreite || py < 0 || py >= texturHoehe)
                    continue;

                int i = py * texturBreite + px;
                byte alterBesitzer = pixelBesitzer[i];
                if (alterBesitzer == farbIndex)
                    continue;

                if (alterBesitzer != 255)
                    pixelProFarbe[alterBesitzer]--;
                pixelProFarbe[farbIndex]++;

                pixelBesitzer[i] = (byte)farbIndex;
                pixel[i] = farbe;
            }
        }
        texturGeaendert = true;
    }

    void LateUpdate()
    {
        // Textur nur einmal pro Frame hochladen, egal wie viele Kleckse kamen
        if (texturGeaendert)
        {
            leinwand.SetPixels32(pixel);
            leinwand.Apply();
            texturGeaendert = false;
        }
    }

    /// <summary>Anteil der Leinwand (0-1), der von dieser Farbe bedeckt ist.</summary>
    public float Anteil(int farbIndex)
    {
        return pixelProFarbe[farbIndex] / (float)(texturBreite * texturHoehe);
    }
}
