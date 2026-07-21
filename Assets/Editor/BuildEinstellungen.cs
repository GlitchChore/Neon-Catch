using UnityEditor;
using UnityEngine;

/// <summary>
/// Erzwingt die Fenster-Einstellungen fuer den Build - ueber die Unity-API,
/// damit der offene Editor sie nicht wieder ueberschreibt (direktes Editieren
/// von ProjectSettings.asset wird von Unity beim Speichern rueckgaengig gemacht):
/// - Fenster statt Vollbild (Alt+Enter schaltet weiter um)
/// - frei skalierbar -> laesst sich an die halbe Bildschirmseite heften
/// - laeuft im Hintergrund weiter (WICHTIG fuer Photon-Online, sonst reisst
///   die Verbindung ab, sobald das Fenster den Fokus verliert)
/// Laeuft automatisch nach jedem Kompilieren.
/// </summary>
public static class BuildEinstellungen
{
    [InitializeOnLoadMethod]
    static void Erzwingen()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool geaendert = false;

            if (PlayerSettings.fullScreenMode != FullScreenMode.Windowed)
            { PlayerSettings.fullScreenMode = FullScreenMode.Windowed; geaendert = true; }
            if (!PlayerSettings.resizableWindow)
            { PlayerSettings.resizableWindow = true; geaendert = true; }
            if (!PlayerSettings.runInBackground)
            { PlayerSettings.runInBackground = true; geaendert = true; }
            if (PlayerSettings.defaultScreenWidth != 1280 || PlayerSettings.defaultScreenHeight != 720)
            {
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                geaendert = true;
            }

            if (geaendert)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("BuildEinstellungen: Fenster-Modus, skalierbares Fenster und Run-in-Background gesetzt.");
            }
        };
    }
}
