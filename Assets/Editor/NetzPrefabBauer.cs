using Mirror;
using NeonCatch;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Baut alle Netzwerk-Prefabs automatisch:
/// Unity-Menue: Tools > FARBMIMIK > Netzwerk-Prefabs erstellen
/// - Spieler        (FARBMIMIK-Modus: Kapsel, SelfPaint + Freeze)
/// - KampfSpieler   (Online-Kampf: Mensch, Ego-Steuerung)
/// - KampfBotNetz   (Online-Kampf: Server-Bot)
/// Vorhandene Prefabs werden NICHT ueberschrieben.
/// </summary>
public static class NetzPrefabBauer
{
    const string Ordner = "Assets/Resources";

    [MenuItem("Tools/FARBMIMIK/Netzwerk-Prefabs erstellen")]
    public static void ErstelleAlle()
    {
        if (!AssetDatabase.IsValidFolder(Ordner))
            AssetDatabase.CreateFolder("Assets", "Resources");

        int neu = 0;
        if (ErstelleFarbmimikSpieler()) neu++;
        if (ErstelleKampfSpieler()) neu++;
        if (ErstelleKampfBot()) neu++;

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Netzwerk-Prefabs",
            neu + " Prefab(s) neu erstellt in " + Ordner + ".\n" +
            "Bereits vorhandene wurden nicht angefasst.", "OK");
    }

    static bool Existiert(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(Ordner + "/" + name + ".prefab") != null;
    }

    static void Speichere(GameObject go, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(go, Ordner + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
        Debug.Log("NetzPrefabBauer: " + name + ".prefab erstellt.");
    }

    // FARBMIMIK-Spieler: sichtbare Kapsel, malt sich selbst an, Freeze-Strafe
    static bool ErstelleFarbmimikSpieler()
    {
        if (Existiert("Spieler")) return false;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Spieler";
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());

        go.AddComponent<NetworkIdentity>();
        var nt = go.AddComponent<NetworkTransformReliable>();
        nt.syncDirection = SyncDirection.ClientToServer;

        var cc = go.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = Vector3.zero;   // Kapsel-Mesh hat den Pivot in der Mitte

        go.AddComponent<SelfPaintSystem>();
        go.AddComponent<FreezePenalty>();

        Speichere(go, "Spieler");
        return true;
    }

    // Online-Kampf-Mensch: unsichtbarer Wurzel-Koerper (Ego-Perspektive),
    // Mitspieler bekommen zur Laufzeit ein Synty-Modell angehaengt
    static bool ErstelleKampfSpieler()
    {
        if (Existiert("KampfSpieler")) return false;

        var go = new GameObject("KampfSpieler");
        go.AddComponent<NetworkIdentity>();
        var nt = go.AddComponent<NetworkTransformReliable>();
        nt.syncDirection = SyncDirection.ClientToServer;

        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.7f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.85f, 0f);

        go.AddComponent<KampfNetzwerk>();

        Speichere(go, "KampfSpieler");
        return true;
    }

    // Online-Kampf-Bot: wird vom SERVER gesteuert (SyncDirection bleibt
    // Server -> Client), Trefferbox passend zur 0.75-m-Figur
    static bool ErstelleKampfBot()
    {
        if (Existiert("KampfBotNetz")) return false;

        var go = new GameObject("KampfBotNetz");
        go.AddComponent<NetworkIdentity>();
        var nt = go.AddComponent<NetworkTransformReliable>();
        nt.syncDirection = SyncDirection.ServerToClient;

        var cc = go.AddComponent<CharacterController>();
        cc.height = 0.75f;
        cc.radius = 0.19f;
        cc.center = new Vector3(0f, 0.375f, 0f);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.08f;

        go.AddComponent<KampfNetzwerk>();

        Speichere(go, "KampfBotNetz");
        return true;
    }
}
