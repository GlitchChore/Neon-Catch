using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeonCatch
{
    // ==================================================================
    // Kopiert die 3 Truhen-Prefabs (Crystal / Energy / Royal) aus dem
    // Paket "Modern 2D Animated Chests" nach Resources/Truhen, damit der
    // BoxOpenUI sie zur Laufzeit per Resources.Load laden kann.
    //
    // Die Kopien behalten ihre Verweise auf Controller/Sprites (per GUID),
    // laufen also im Editor UND im fertigen Build. Passiert automatisch
    // beim Kompilieren (InitializeOnLoad) - nichts von Hand noetig.
    // ==================================================================
    [InitializeOnLoad]
    public static class TruhenResourcenBauer
    {
        const string PaketWurzel = "Assets/Modern 2D Animated Chests Pack_FREE Demo/Chests/";
        const string ZielOrdner  = "Assets/Resources/Truhen";

        static TruhenResourcenBauer()
        {
            Kopiere("Crystal");
            Kopiere("Energy");
            Kopiere("Royal");
        }

        static void Kopiere(string name)
        {
            string ziel = ZielOrdner + "/PF_Chest_" + name + ".prefab";
            if (File.Exists(ziel)) return;   // schon vorhanden

            string quelle = PaketWurzel + name + "/PF_Chest_" + name + ".prefab";
            if (!File.Exists(quelle))
            {
                Debug.LogWarning("TruhenResourcenBauer: Quelle fehlt - " + quelle);
                return;
            }

            if (!Directory.Exists(ZielOrdner))
                Directory.CreateDirectory(ZielOrdner);

            if (AssetDatabase.CopyAsset(quelle, ziel))
                Debug.Log("TruhenResourcenBauer: Truhe nach Resources kopiert -> " + ziel);
            AssetDatabase.Refresh();
        }
    }
}
