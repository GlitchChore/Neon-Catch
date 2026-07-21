using System;
using UnityEngine;

namespace NeonCatch
{
    // ==================================================================
    // PROGRESSION MANAGER (Singleton) - zentrale Drehscheibe
    //
    // Verwaltet ALLE 6 Waehrungen in PlayerPrefs und meldet jede Aenderung
    // ueber OnCurrencyChanged. Die Pfad-Manager (Trophaeen/Juwelen), der
    // BoxOpener und das TopUI haengen sich hier ein - so gibt es EINE Quelle
    // der Wahrheit fuer alle Werte.
    //
    // Erzeugt sich per RuntimeInitializeOnLoadMethod selbst und ueberlebt
    // Szenenwechsel (DontDestroyOnLoad). Kein manuelles Setup noetig.
    // ==================================================================
    public class ProgressionManager : MonoBehaviour
    {
        public static ProgressionManager Instance { get; private set; }

        // Die 6 Waehrungs-Schluessel (= PlayerPrefs-Keys)
        public const string TROPHIES   = "TotalTrophies";
        public const string JEWELS     = "TotalJewels";
        public const string MAPCOINS   = "MapCoins";
        public const string SKINSHARDS = "SkinShards";
        public const string MAPSHARDS  = "MapShards";
        public const string PETSHARDS  = "PetShards";

        // Wird bei JEDER Waehrungsaenderung gefeuert.
        // typ  = einer der Keys oben, wert = NEUER Gesamtstand dieser Waehrung.
        public static event Action<string, int> OnCurrencyChanged;

        // Meldet freigeschaltete Charaktere/Items (kategorie, name) - z.B. fuers UI.
        public static event Action<string, string> OnUnlocked;

        // -------------------- Auto-Erzeugung + Singleton --------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoErzeugen()
        {
            // Reihenfolge: erst der Manager, dann die Systeme, die ihn brauchen
            EnsureInstance();
            TrophyPathManager.EnsureInstance();
            JewelPathManager.EnsureInstance();
            BoxOpener.EnsureInstance();
        }

        public static ProgressionManager EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("ProgressionManager");
                Instance = go.AddComponent<ProgressionManager>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // -------------------- Waehrungen lesen/aendern --------------------

        // Aktueller Stand einer Waehrung.
        public int Get(string typ) => PlayerPrefs.GetInt(typ, 0);

        // Waehrung um 'menge' erhoehen (oder mit negativem Wert verringern).
        // Nie unter 0. Feuert OnCurrencyChanged mit dem neuen Gesamtstand.
        public void Add(string typ, int menge)
        {
            if (menge == 0) return;
            int neu = Mathf.Max(0, Get(typ) + menge);
            PlayerPrefs.SetInt(typ, neu);
            PlayerPrefs.Save();
            OnCurrencyChanged?.Invoke(typ, neu);
        }

        // Kosten abbuchen, wenn genug da ist (fuer den BoxOpener).
        // Gibt false zurueck, wenn das Guthaben nicht reicht.
        public bool Spend(string typ, int kosten)
        {
            if (kosten <= 0) return true;
            if (Get(typ) < kosten) return false;
            Add(typ, -kosten);
            return true;
        }

        // Bequeme, klar benannte Kurzformen
        public void AddTrophies(int menge)  => Add(TROPHIES, menge);
        public void AddJewels(int menge)    => Add(JEWELS, menge);
        public void AddMapCoins(int menge)  => Add(MAPCOINS, menge);
        public void AddSkinShards(int menge)=> Add(SKINSHARDS, menge);
        public void AddMapShards(int menge) => Add(MAPSHARDS, menge);
        public void AddPetShards(int menge) => Add(PETSHARDS, menge);

        // -------------------- Freischaltungen (Charaktere/Items) --------------------

        public void UnlockCharacter(string name)
        {
            if (IstFreigeschaltet(name)) return;
            PlayerPrefs.SetInt("Unlocked_" + name, 1);
            PlayerPrefs.Save();
            OnUnlocked?.Invoke("Charakter", name);
        }

        public bool IstFreigeschaltet(string name) => PlayerPrefs.GetInt("Unlocked_" + name, 0) == 1;

        // -------------------- Test --------------------

        // Schnell Trophaeen + Juwelen dazugeben (loest Meilensteine mit aus).
        public void DebugAdd(int trophies, int jewels)
        {
            if (trophies != 0) AddTrophies(trophies);
            if (jewels != 0)   AddJewels(jewels);
        }
    }
}
