using UnityEngine;

namespace NeonCatch
{
    // ==================================================================
    // TROPHÄENPFAD - ENDLOS
    //
    // Reagiert auf Trophaeen-Aenderungen des ProgressionManagers und
    // vergibt AUTOMATISCH (kein manuelles Abholen):
    //   alle 100  -> 25..100 Scherben  (70% Skin / 20% Pet / 10% Map)
    //   alle 1000 -> 1 gratis Box      (60% Pet  / 30% Skin / 10% Map)
    //   0 / 500 / 1000 -> Charakter freischalten
    //
    // Merkt sich in PlayerPrefs, wie viele 100er/1000er schon vergeben wurden
    // (TrophyStep100 / TrophyStep1000) - dadurch endlos ohne wachsende Liste.
    // ==================================================================
    public class TrophyPathManager : MonoBehaviour
    {
        public static TrophyPathManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoErzeugen() { EnsureInstance(); }

        public static TrophyPathManager EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("TrophyPathManager");
                Instance = go.AddComponent<TrophyPathManager>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()  { ProgressionManager.OnCurrencyChanged += BeiWaehrung; }
        void OnDisable() { ProgressionManager.OnCurrencyChanged -= BeiWaehrung; }

        void Start()
        {
            // Einmal beim Start pruefen: schaltet Burgkommandant (0 Trophaeen)
            // frei und holt bei geladenem Spielstand offene Meilensteine nach.
            PruefeMeilensteine(ProgressionManager.EnsureInstance().Get(ProgressionManager.TROPHIES));
        }

        void BeiWaehrung(string typ, int neu)
        {
            if (typ == ProgressionManager.TROPHIES) PruefeMeilensteine(neu);
        }

        void PruefeMeilensteine(int total)
        {
            var pm = ProgressionManager.EnsureInstance();

            // --- Charaktere (einmalig) bei 0 / 500 / 1000 ---
            if (!pm.IstFreigeschaltet("Burgkommandant"))                    pm.UnlockCharacter("Burgkommandant");
            if (total >= 500  && !pm.IstFreigeschaltet("Schattenwächter"))  pm.UnlockCharacter("Schattenwächter");
            if (total >= 1000 && !pm.IstFreigeschaltet("FuchsNinja"))       pm.UnlockCharacter("FuchsNinja");

            // --- Alle 100 Trophaeen: Scherben ---
            int gegeben100 = PlayerPrefs.GetInt("TrophyStep100", 0);
            while (total / 100 > gegeben100) { gegeben100++; GibScherben(); }
            PlayerPrefs.SetInt("TrophyStep100", gegeben100);

            // --- Alle 1000 Trophaeen: gratis Box ---
            int gegeben1000 = PlayerPrefs.GetInt("TrophyStep1000", 0);
            while (total / 1000 > gegeben1000) { gegeben1000++; GibBox(); }
            PlayerPrefs.SetInt("TrophyStep1000", gegeben1000);

            PlayerPrefs.Save();
        }

        // 25..100 Scherben - Drop 70% Skin / 20% Pet / 10% Map
        void GibScherben()
        {
            var pm = ProgressionManager.EnsureInstance();
            int menge = Random.Range(25, 101);   // 25..100 (obere Grenze exklusiv)
            float r = Random.value;
            if (r < 0.70f)      pm.AddSkinShards(menge);
            else if (r < 0.90f) pm.AddPetShards(menge);
            else                pm.AddMapShards(menge);
        }

        // gratis Box - Drop 60% Pet / 30% Skin / 10% Map
        void GibBox()
        {
            float r = Random.value;
            string box = r < 0.60f ? "PetBox" : (r < 0.90f ? "SkinBox" : "MapBox");
            BoxOpener.EnsureInstance().OpenBoxKostenlos(box);
        }
    }
}
