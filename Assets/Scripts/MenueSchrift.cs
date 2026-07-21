using UnityEngine;

namespace NeonCatch
{
    // ======================================================================
    // Gibt den alten (IMGUI-)Menues dieselbe Schriftart wie der TextMeshPro-
    // UI: LiberationSans aus dem TMP-Pack (liegt als Resources/MenueSchrift).
    // Ein Aufruf am Anfang von OnGUI genuegt - NUR die Schrift aendert sich,
    // Layout, Knoepfe und Ablauf bleiben exakt gleich. Die gecachten
    // GUIStyles erben die Schrift automatisch, weil ihr font-Feld leer ist
    // und dann immer GUI.skin.font gilt.
    // ======================================================================
    public static class MenueSchrift
    {
        static Font schrift;
        static bool gesucht;

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
        }
    }
}
