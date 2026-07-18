using kcp2k;
using Mirror;
using UnityEngine;

/// <summary>
/// FARBMIMIK-Lobby: NetworkManager mit Room-Code-Schutz.
/// - Erzwingt KcpTransport (kcp2k), egal was eingestellt ist
/// - Generiert beim Serverstart einen 4-stelligen Room-Code (z.B. X7K9)
/// - Clients muessen den richtigen Code schicken, sonst fliegen sie raus
/// Gehoert auf ein leeres GameObject "Netzwerk" in der FARBMIMIK-Szene.
/// </summary>
public class LobbyManager : NetworkManager
{
    /// <summary>Vom Server generierter Code (auf dem Host gueltig).</summary>
    public static string RoomCode = "";

    /// <summary>Was der Client im Menue eingetippt hat.</summary>
    public static string EingegebenerCode = "";

    [Header("FARBMIMIK")]
    public int maxSpieler = 7;

    public override void Awake()
    {
        // kcp2k erzwingen - NICHT Telepathy
        if (!(transport is KcpTransport))
        {
            KcpTransport kcp = GetComponent<KcpTransport>();
            if (kcp == null)
                kcp = gameObject.AddComponent<KcpTransport>();
            transport = kcp;
            Debug.Log("LobbyManager: Transport auf KcpTransport (kcp2k) gestellt.");
        }

        maxConnections = maxSpieler;

        // Room-Code-Pruefung einhaengen
        if (authenticator == null)
            authenticator = gameObject.AddComponent<RoomCodeAuthenticator>();

        // Spieler-Prefab automatisch aus Resources laden, wenn nicht zugewiesen
        if (playerPrefab == null)
        {
            playerPrefab = Resources.Load<GameObject>("Spieler");
            if (playerPrefab == null)
                Debug.LogError("LobbyManager: Kein Spieler-Prefab! Prefab 'Spieler' in einen " +
                               "Resources-Ordner legen oder im Inspector zuweisen.");
        }

        base.Awake();
    }

    public override void OnStartServer()
    {
        RoomCode = GeneriereCode();
        base.OnStartServer();
        Debug.Log("FARBMIMIK Room-Code: " + RoomCode);
    }

    static string GeneriereCode()
    {
        // ohne O/0 und I/1, damit man sich nicht vertippt
        const string zeichen = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code = "";
        for (int i = 0; i < 4; i++)
            code += zeichen[Random.Range(0, zeichen.Length)];
        return code;
    }
}

// ---------- Room-Code-Pruefung ----------

public struct RoomCodeAnfrage : NetworkMessage
{
    public string code;
}

public struct RoomCodeAntwort : NetworkMessage
{
    public bool ok;
}

public class RoomCodeAuthenticator : NetworkAuthenticator
{
    public override void OnStartServer()
    {
        NetworkServer.RegisterHandler<RoomCodeAnfrage>(BeiAnfrage, false);
    }

    public override void OnStopServer()
    {
        NetworkServer.UnregisterHandler<RoomCodeAnfrage>();
    }

    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<RoomCodeAntwort>(BeiAntwort, false);
    }

    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<RoomCodeAntwort>();
    }

    public override void OnServerAuthenticate(NetworkConnectionToClient conn)
    {
        // warten auf die RoomCodeAnfrage des Clients
    }

    public override void OnClientAuthenticate()
    {
        // Der Host kennt seinen eigenen Code, Clients schicken die Eingabe
        string code = NetworkServer.active ? LobbyManager.RoomCode : LobbyManager.EingegebenerCode;
        NetworkClient.connection.Send(new RoomCodeAnfrage { code = code });
    }

    void BeiAnfrage(NetworkConnectionToClient conn, RoomCodeAnfrage nachricht)
    {
        bool ok = nachricht.code != null &&
                  nachricht.code.Trim().ToUpper() == LobbyManager.RoomCode;

        conn.Send(new RoomCodeAntwort { ok = ok });

        if (ok)
        {
            ServerAccept(conn);
        }
        else
        {
            Debug.Log("Client mit falschem Room-Code abgelehnt: '" + nachricht.code + "'");
            ServerReject(conn);
        }
    }

    void BeiAntwort(RoomCodeAntwort nachricht)
    {
        if (nachricht.ok)
            ClientAccept();
        else
            ClientReject();
    }
}
