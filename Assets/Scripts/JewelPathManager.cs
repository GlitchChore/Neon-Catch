using UnityEngine;

namespace NeonCatch
{
    // ==================================================================
    // JUWELENPFAD - ENDLOS
    //
    // Alle 100 Juwelen gibt es (50/50) entweder 20..60 MapCoins ODER
    // 1 zufaelligen "Gewoehnlich"-Skin.
    //
    // Juwelen-Quellen (von der Spiellogik aufzurufen):
    //   +10 nach einem Match            -> JuwelenFuerMatch()
    //   +5  wenn jemand deine Map spielt -> JuwelenFuerMapBesuch()
    // ==================================================================
    public class JewelPathManager : MonoBehaviour
    {
        public static JewelPathManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoErzeugen() { EnsureInstance(); }

        public static JewelPathManager EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("JewelPathManager");
                Instance = go.AddComponent<JewelPathManager>();
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
            PruefeMeilensteine(ProgressionManager.EnsureInstance().Get(ProgressionManager.JEWELS));
        }

        void BeiWaehrung(string typ, int neu)
        {
            if (typ == ProgressionManager.JEWELS) PruefeMeilensteine(neu);
        }

        void PruefeMeilensteine(int total)
        {
            int gegeben = PlayerPrefs.GetInt("JewelStep100", 0);
            while (total / 100 > gegeben) { gegeben++; GibBelohnung(); }
            PlayerPrefs.SetInt("JewelStep100", gegeben);
            PlayerPrefs.Save();
        }

        // 20..60 MapCoins ODER 1 Gewoehnlich-Skin (50/50)
        void GibBelohnung()
        {
            var pm = ProgressionManager.EnsureInstance();
            if (Random.value < 0.5f)
                pm.AddMapCoins(Random.Range(20, 61));       // 20..60
            else
                BoxOpener.EnsureInstance().GibCommonSkin();  // 1 zufaelliger Gewoehnlich-Skin
        }

        // ---- Von der Spiellogik aufzurufen ----
        public void JuwelenFuerMatch()     => ProgressionManager.EnsureInstance().AddJewels(10);
        public void JuwelenFuerMapBesuch() => ProgressionManager.EnsureInstance().AddJewels(5);
    }
}
