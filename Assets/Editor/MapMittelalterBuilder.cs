using UnityEngine;
using UnityEditor;

namespace NeonCatch.Editor
{
    // Ein Klick statt vier: baut Burg, Landschaft, Burggraben und Deko
    // hintereinander in der richtigen Reihenfolge (jede Stufe braucht die
    // vorherige – siehe Fehlermeldungen der einzelnen Builder):
    //   1. Burg Builder       – baut "Burg" (Mauern, Türme, Innengebäude)
    //   2. Landschaft Builder – braucht "Burg", baut Terrain/Wald/Höfe/Höhlen
    //      (gräbt dabei auch die Grube für den Burggraben)
    //   3. Burggraben Builder – braucht "Burg" + "Landschaft", füllt die
    //      Grube mit Wasser, Schlamm, Steinen und Pflanzen
    //   4. Burg Deko          – braucht "Burg", stellt Brunnen/Markt/Fackeln/
    //      Fahnen/Innendeko dazu
    //
    // Jeder Builder bleibt einzeln nutzbar (z.B. um nur die Deko neu zu
    // würfeln) – dieser Menüpunkt ruft einfach alle vier mit ihren
    // Standard-Einstellungen nacheinander auf, ganz ohne dass sich ein
    // Fenster öffnet.
    public static class MapMittelalterBuilder
    {
        [MenuItem("NeonCatch/Map Mittelalter %#m")]
        static void BaueAlles()
        {
            if (!EditorUtility.DisplayDialog("Map Mittelalter bauen",
                    "Baut Burg, Landschaft, Burggraben und Deko nacheinander mit den " +
                    "Standard-Einstellungen. Vorhandene Objekte mit denselben Namen " +
                    "werden dabei ersetzt.\n\nFortfahren?",
                    "Bauen", "Abbrechen"))
                return;

            RufeBuild(ScriptableObject.CreateInstance<BurgBuilder>(), "Burg Builder", b => b.Build());
            RufeBuild(ScriptableObject.CreateInstance<LandschaftBuilder>(), "Landschaft Builder", b => b.Build());
            RufeBuild(ScriptableObject.CreateInstance<BurggrabenBuilder>(), "Burggraben Builder", b => b.Build());
            RufeBuild(ScriptableObject.CreateInstance<BurgDekoBuilder>(), "Burg Deko", b => b.Build());

            Debug.Log("Map Mittelalter: Burg, Landschaft, Burggraben und Deko komplett gebaut.");
        }

        // Erzeugt/ruft auf einer unsichtbaren Fenster-Instanz auf (kein
        // Fenster öffnet sich) – genau das, was ein Klick auf den jeweiligen
        // "…bauen"-Knopf mit Standardeinstellungen auslösen würde. Bricht bei
        // einem Fehler NICHT die restliche Kette ab, meldet ihn nur klar.
        static void RufeBuild<T>(T instanz, string anzeigeName, System.Action<T> build) where T : EditorWindow
        {
            try
            {
                build(instanz);
            }
            catch (System.Exception e)
            {
                Debug.LogError("Map Mittelalter: " + anzeigeName + " ist fehlgeschlagen – " + e.Message);
            }
            finally
            {
                Object.DestroyImmediate(instanz);
            }
        }
    }
}
