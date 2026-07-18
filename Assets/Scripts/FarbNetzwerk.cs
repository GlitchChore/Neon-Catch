using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Netzwerk-Logik pro Spieler: bekommt vom Server eine Farbe zugewiesen
/// (Rot, Blau oder Gelb) und schickt Mal-Befehle an alle Clients.
/// Gehoert auf das Spieler-Prefab (zusammen mit NetworkIdentity).
/// </summary>
public class FarbNetzwerk : NetworkBehaviour
{
    [SyncVar]
    public int farbIndex = -1;   // -1 = noch keine Farbe

    public override void OnStartServer()
    {
        // Erste freie Farbe nehmen: 0 = Rot, 1 = Blau, 2 = Gelb
        bool[] belegt = new bool[3];
        foreach (var spieler in FindObjectsByType<FarbNetzwerk>(FindObjectsSortMode.None))
        {
            if (spieler != this && spieler.farbIndex >= 0 && spieler.farbIndex < 3)
                belegt[spieler.farbIndex] = true;
        }
        for (int i = 0; i < 3; i++)
        {
            if (!belegt[i])
            {
                farbIndex = i;
                break;
            }
        }
    }

    void Update()
    {
        if (!isLocalPlayer || farbIndex < 0)
            return;

        var maus = Mouse.current;
        if (maus == null || !maus.leftButton.isPressed)
            return;

        var kamera = Camera.main;
        if (kamera == null)
            return;

        Ray strahl = kamera.ScreenPointToRay(maus.position.ReadValue());
        if (Physics.Raycast(strahl, out RaycastHit treffer, 1000f))
        {
            // textureCoord funktioniert nur mit einem MeshCollider auf der Leinwand!
            if (treffer.collider.GetComponent<FarbManager>() != null)
                CmdMale(treffer.textureCoord);
        }
    }

    [Command]
    void CmdMale(Vector2 uv)
    {
        // Server entscheidet mit der SyncVar-Farbe -> Clients koennen nicht schummeln
        RpcMale(uv, farbIndex);
    }

    [ClientRpc]
    void RpcMale(Vector2 uv, int index)
    {
        if (FarbManager.Instance != null)
            FarbManager.Instance.Male(uv, index);
    }
}
