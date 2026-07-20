using System.Collections.Generic;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Baut alle Photon-Netzwerk-Prefabs automatisch:
/// Unity-Menue: Tools > FARBMIMIK > Netzwerk-Prefabs erstellen
/// - Spieler        (FARBMIMIK-Modus: Kapsel, SelfPaint + Freeze)
/// - KampfSpieler   (Online-Kampf: Mensch, Ego-Steuerung)
/// - KampfBotNetz   (Online-Kampf: Server-Bot)
/// Jedes Prefab bekommt PhotonView + PhotonTransformView (fuer die
/// Positions-Synchronisation) - das ist Photons Ersatz fuer Mirrors
/// NetworkIdentity + NetworkTransform.
/// Die Prefabs MUESSEN im Ordner "Resources" liegen, damit
/// PhotonNetwork.Instantiate("Spieler") sie findet.
/// Vorhandene Prefabs werden NICHT ueberschrieben.
/// </summary>
public static class NetzPrefabBauer
{
    const string Ordner = "Assets/Resources";

    // Laeuft automatisch nach jedem Editor-Start/Kompilieren: fehlende
    // Prefabs werden still erzeugt - niemand muss den Menuepunkt kennen.
    [InitializeOnLoadMethod]
    static void AutoErstellen()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (Existiert("Spieler") && Existiert("KampfSpieler") && Existiert("KampfBotNetz"))
                return;
            Erstelle(false);
        };
    }

    [MenuItem("Tools/FARBMIMIK/Netzwerk-Prefabs erstellen")]
    public static void ErstelleAlle()
    {
        Erstelle(true);
    }

    static void Erstelle(bool mitDialog)
    {
        if (!AssetDatabase.IsValidFolder(Ordner))
            AssetDatabase.CreateFolder("Assets", "Resources");

        int neu = 0;
        if (ErstelleFarbmimikSpieler()) neu++;
        if (ErstelleKampfSpieler()) neu++;
        if (ErstelleKampfBot()) neu++;

        AssetDatabase.SaveAssets();
        if (mitDialog)
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

    // Haengt PhotonView + PhotonTransformView an und verdrahtet sie
    // (der View beobachtet den TransformView -> Position/Rotation werden
    // automatisch an alle Clients uebertragen).
    static void FuegeNetzwerkHinzu(GameObject go)
    {
        var view = go.AddComponent<PhotonView>();
        var transformView = go.AddComponent<PhotonTransformView>();
        view.ObservedComponents = new List<Component> { transformView };
        view.Synchronization = ViewSynchronization.UnreliableOnChange;
        view.OwnershipTransfer = OwnershipOption.Fixed;
    }

    // Figur-Groesse wie die Solo-Figur (PlayerController): ~0.5 m hoch, schmal -
    // passt durch Tueren. Frueher war die FARBMIMIK-Kapsel 2 m hoch und 1 m breit.
    const float FigurHoehe = 0.5f;
    const float FigurRadius = 0.12f;

    // FARBMIMIK-Spieler: kleine sichtbare Kapsel (Kind), malt sich selbst an,
    // Freeze-Strafe. Wurzel bleibt Skalierung 1, damit der CharacterController
    // sauber bleibt; die sichtbare Kapsel ist ein kleines Kind-Objekt.
    static bool ErstelleFarbmimikSpieler()
    {
        if (Existiert("Spieler")) return false;

        var go = new GameObject("Spieler");
        FuegeNetzwerkHinzu(go);

        var cc = go.AddComponent<CharacterController>();
        cc.height = FigurHoehe;
        cc.radius = FigurRadius;
        cc.center = new Vector3(0f, FigurHoehe / 2f, 0f);

        // sichtbare kleine Kapsel als Kind (Default-Kapsel ist 2 m hoch/1 m breit)
        var figur = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        figur.name = "Figur";
        Object.DestroyImmediate(figur.GetComponent<CapsuleCollider>());
        figur.transform.SetParent(go.transform, false);
        figur.transform.localScale = new Vector3(FigurRadius * 2f, FigurHoehe / 2f, FigurRadius * 2f);
        figur.transform.localPosition = new Vector3(0f, FigurHoehe / 2f, 0f);

        go.AddComponent<SelfPaintSystem>();
        go.AddComponent<FreezePenalty>();

        Speichere(go, "Spieler");
        return true;
    }

    // Online-Kampf-Mensch: unsichtbarer Wurzel-Koerper (Ego-Perspektive),
    // Mitspieler bekommen zur Laufzeit ein Synty-Modell angehaengt. Gleiche
    // kleine Groesse wie die Solo-Figur, damit man durch Tueren passt.
    static bool ErstelleKampfSpieler()
    {
        if (Existiert("KampfSpieler")) return false;

        var go = new GameObject("KampfSpieler");
        FuegeNetzwerkHinzu(go);

        var cc = go.AddComponent<CharacterController>();
        cc.height = FigurHoehe;
        cc.radius = FigurRadius;
        cc.center = new Vector3(0f, FigurHoehe / 2f, 0f);

        go.AddComponent<NeonCatch.KampfNetzwerk>();

        Speichere(go, "KampfSpieler");
        return true;
    }

    // Online-Kampf-Bot: wird vom MasterClient gesteuert, Trefferbox passend
    // zur 0.75-m-Figur
    static bool ErstelleKampfBot()
    {
        if (Existiert("KampfBotNetz")) return false;

        var go = new GameObject("KampfBotNetz");
        FuegeNetzwerkHinzu(go);

        var cc = go.AddComponent<CharacterController>();
        cc.height = 0.75f;
        cc.radius = 0.19f;
        cc.center = new Vector3(0f, 0.375f, 0f);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.08f;

        go.AddComponent<NeonCatch.KampfNetzwerk>();

        Speichere(go, "KampfBotNetz");
        return true;
    }
}
