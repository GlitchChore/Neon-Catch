using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sorgt dafuer, dass die FARBMIMIK-Szene existiert und in den Build Settings
/// steht. Die Szene ist eine KOPIE der Hauptszene (SampleScene) - so hat
/// FARBMIMIK dieselbe Burg-Map mit Versteckmoeglichkeiten. Die FARBMIMIK-UI
/// (LobbyUI) wird zur Laufzeit automatisch erzeugt (siehe LobbyUI.Bootstrap),
/// die Szene selbst muss also nichts weiter enthalten.
///
/// Laeuft automatisch nach jedem Editor-Start/Kompilieren. Kann auch manuell
/// ueber Tools > FARBMIMIK > FARBMIMIK-Szene erstellen ausgeloest werden.
/// </summary>
public static class FarbmimikSzeneBauer
{
    const string Quelle = "Assets/Scenes/SampleScene.unity";
    const string Ziel = "Assets/Scenes/Farbmimik.unity";

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            Sicherstellen(false);
        };
    }

    [MenuItem("Tools/FARBMIMIK/FARBMIMIK-Szene erstellen")]
    static void Menue() => Sicherstellen(true);

    static void Sicherstellen(bool dialog)
    {
        // 1. Szene anlegen (Kopie der Hauptszene), falls sie fehlt
        bool neu = false;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Ziel) == null)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Quelle) == null)
            {
                if (dialog)
                    EditorUtility.DisplayDialog("FARBMIMIK",
                        "Hauptszene nicht gefunden:\n" + Quelle, "OK");
                return;
            }
            AssetDatabase.CopyAsset(Quelle, Ziel);
            AssetDatabase.SaveAssets();
            neu = true;
        }

        // 2. Beide Szenen in die Build Settings (aktiviert)
        var liste = new List<EditorBuildSettingsScene>();
        bool hatQuelle = false, hatZiel = false;
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == Quelle) { hatQuelle = true; liste.Add(new EditorBuildSettingsScene(Quelle, true)); }
            else if (s.path == Ziel) { hatZiel = true; liste.Add(new EditorBuildSettingsScene(Ziel, true)); }
            else liste.Add(s);
        }
        if (!hatQuelle) liste.Insert(0, new EditorBuildSettingsScene(Quelle, true));
        if (!hatZiel) liste.Add(new EditorBuildSettingsScene(Ziel, true));
        EditorBuildSettings.scenes = liste.ToArray();

        if (neu)
            Debug.Log("FarbmimikSzeneBauer: 'Farbmimik'-Szene erstellt und in Build Settings eingetragen.");
        if (dialog)
            EditorUtility.DisplayDialog("FARBMIMIK",
                (neu ? "Farbmimik-Szene erstellt" : "Farbmimik-Szene war schon da") +
                " und in den Build Settings eingetragen.\n\nJetzt kannst du FARBMIMIK spielen.", "OK");
    }
}
