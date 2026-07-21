using System;
using UnityEngine;
using Random = UnityEngine.Random;   // sonst mehrdeutig mit System.Random (wegen using System)

namespace NeonCatch
{
    // ==================================================================
    // BOX OPENER
    //
    // Zieht die Seltenheit und vergibt ein Item. Zwei Wege:
    //   OpenBox(type)          -> kostet Scherben (manuelles Oeffnen)
    //   OpenBoxKostenlos(type) -> gratis (aus einem Meilenstein)
    //
    // Kosten:  SkinBox = 300 SkinShards | MapBox = 200 MapShards | PetBox = 100 PetShards
    //
    // Drop-Tabelle fuer ALLE Boxen:
    //   Mythisch   1%  (Rot)
    //   Legendär   4%  (Gelb)
    //   Episch    10%  (Lila)
    //   Selten    25%  (Blau)
    //   Gewöhnlich 60% (Grau)
    //
    // Feuert OnBoxGeoeffnet(boxTyp, seltenheit, farbe) - daran haengt die
    // Oeffnen-Animation im UI (Truhe + Item fliegt in der Seltenheits-Farbe raus).
    // ==================================================================
    public class BoxOpener : MonoBehaviour
    {
        public static BoxOpener Instance { get; private set; }

        // (boxTyp, seltenheitsName, farbe) fuer die Oeffnen-Animation.
        public static event Action<string, string, Color> OnBoxGeoeffnet;

        struct Seltenheit { public string name; public float prozent; public Color farbe; }

        static readonly Seltenheit[] Tabelle =
        {
            new Seltenheit { name = "Mythisch",   prozent =  1f, farbe = new Color(0.95f, 0.15f, 0.15f) },
            new Seltenheit { name = "Legendär",   prozent =  4f, farbe = new Color(1f,    0.84f, 0.10f) },
            new Seltenheit { name = "Episch",     prozent = 10f, farbe = new Color(0.70f, 0.25f, 0.95f) },
            new Seltenheit { name = "Selten",     prozent = 25f, farbe = new Color(0.20f, 0.55f, 1f)    },
            new Seltenheit { name = "Gewöhnlich", prozent = 60f, farbe = new Color(0.72f, 0.72f, 0.75f) },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoErzeugen() { EnsureInstance(); }

        public static BoxOpener EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("BoxOpener");
                Instance = go.AddComponent<BoxOpener>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // -------------------- Kosten je Box --------------------

        static int Kosten(string boxTyp)
        {
            switch (boxTyp)
            {
                case "SkinBox": return 300;
                case "MapBox":  return 200;
                default:        return 100;   // PetBox
            }
        }

        static string KostenWaehrung(string boxTyp)
        {
            switch (boxTyp)
            {
                case "SkinBox": return ProgressionManager.SKINSHARDS;
                case "MapBox":  return ProgressionManager.MAPSHARDS;
                default:        return ProgressionManager.PETSHARDS;   // PetBox
            }
        }

        // -------------------- Oeffnen --------------------

        // Manuelles Oeffnen: kostet Scherben. Gibt false zurueck, wenn zu wenig da ist.
        public bool OpenBox(string type)
        {
            if (!ProgressionManager.EnsureInstance().Spend(KostenWaehrung(type), Kosten(type)))
                return false;
            ZieheUndVergib(type);
            return true;
        }

        // Gratis-Oeffnen (aus einem Meilenstein) - ohne Kosten.
        public void OpenBoxKostenlos(string type)
        {
            ZieheUndVergib(type);
        }

        // Ein gewoehnlicher Skin (fuer den Juwelenpfad).
        public void GibCommonSkin()
        {
            Vergib("SkinBox", Tabelle[Tabelle.Length - 1]);   // "Gewöhnlich"
        }

        void ZieheUndVergib(string type)
        {
            Vergib(type, ZieheSeltenheit());
        }

        // Zieht eine Seltenheit gemaess der Prozent-Tabelle.
        Seltenheit ZieheSeltenheit()
        {
            float r = Random.value * 100f;
            float summe = 0f;
            foreach (var s in Tabelle)
            {
                summe += s.prozent;
                if (r < summe) return s;
            }
            return Tabelle[Tabelle.Length - 1];   // Sicherheitsnetz: Gewoehnlich
        }

        void Vergib(string boxTyp, Seltenheit s)
        {
            // Item merken (einfacher Zaehler je Box + Seltenheit)
            string key = "Item_" + boxTyp + "_" + s.name;
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
            PlayerPrefs.Save();

            // UI-Animation ausloesen (Truhe auf + Item in Seltenheits-Farbe)
            OnBoxGeoeffnet?.Invoke(boxTyp, s.name, s.farbe);
        }

        // -------------------- Abfragen fuers UI --------------------

        public int PreisVon(string boxTyp)      => Kosten(boxTyp);
        public string WaehrungVon(string boxTyp) => KostenWaehrung(boxTyp);
        public bool KannOeffnen(string boxTyp) =>
            ProgressionManager.EnsureInstance().Get(KostenWaehrung(boxTyp)) >= Kosten(boxTyp);
    }
}
