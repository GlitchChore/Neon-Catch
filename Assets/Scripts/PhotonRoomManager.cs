using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Ersetzt Mirror komplett: verbindet mit Photon (Cloud-Server, keine
/// Portfreigabe/Fritzbox noetig), erstellt/betritt Raeume ueber einen
/// 4-stelligen Code (= der Photon-Raumname) und spawnt danach das passende
/// Spieler-Prefab. Gemeinsam genutzt von FARBMIMIK und NEON BLASTER -
/// unterschieden ueber den "modus"-Namen und das Prefab, das beim Aufruf
/// mitgegeben wird.
///
/// Einrichtung: EIN GameObject "PhotonManager" in JEDER Netzwerk-Szene
/// (FARBMIMIK und die Kampf-Szene), dieses Script drauf. Ueberlebt
/// Szenenwechsel selbst (DontDestroyOnLoad) - kommt also nur einmal vor,
/// auch wenn beide Szenen es referenzieren.
/// </summary>
public class PhotonRoomManager : MonoBehaviourPunCallbacks
{
    public static PhotonRoomManager Instanz { get; private set; }

    /// <summary>Aktueller Raum-Code (= Photon-Raumname).</summary>
    public static string RoomCode { get; private set; } = "";

    /// <summary>Letzter Fehler (z.B. "Das ist nicht korrekt!") - leer = kein Fehler.</summary>
    public static string FehlerText { get; private set; } = "";

    string wartetAufModus;
    string wartetAufPrefab;
    int wartetAufMaxSpieler;
    int hostVersuche;

    // Erzeugt sich beim Spielstart automatisch, egal in welcher Szene -
    // so muss niemand ein "PhotonManager"-Objekt von Hand in die Szene legen.
    // Dank DontDestroyOnLoad ueberlebt es den Wechsel FARBMIMIK <-> NEON BLASTER.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instanz == null && FindAnyObjectByType<PhotonRoomManager>() == null)
        {
            var go = new GameObject("PhotonManager");
            go.AddComponent<PhotonRoomManager>();
        }
    }

    void Awake()
    {
        if (Instanz != null && Instanz != this)
        {
            Destroy(gameObject);
            return;
        }
        Instanz = this;
        DontDestroyOnLoad(gameObject);

        // Wir wechseln Szenen selbst (FARBMIMIK <-> NEON BLASTER) - Photon
        // soll das nicht automatisch fuer alle Mitspieler mit erzwingen
        PhotonNetwork.AutomaticallySyncScene = false;

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
    }

    /// <summary>Neuen Raum erstellen und automatisch einen freien 4-stelligen Code finden.</summary>
    public void ErstelleRaum(string modus, string prefabName, int maxSpieler)
    {
        FehlerText = "";
        wartetAufModus = modus;
        wartetAufPrefab = prefabName;
        wartetAufMaxSpieler = maxSpieler;
        hostVersuche = 0;
        StartCoroutine(WartetAufVerbindungDann(HosteJetzt));
    }

    /// <summary>
    /// Solo gegen Bots - OHNE Server, OHNE Code. Nutzt Photons Offline-Modus:
    /// alle RPCs, Raum-Properties und InstantiateRoomObject laufen komplett
    /// lokal, du bist automatisch MasterClient. Fuer FARBMIMIK gegen Bots.
    /// </summary>
    public void StarteSolo(string modus, string prefabName)
    {
        FehlerText = "";
        wartetAufModus = modus;
        wartetAufPrefab = prefabName;
        wartetAufMaxSpieler = 1;
        RoomCode = "SOLO";
        StartCoroutine(StarteSoloAblauf());
    }

    // Beim Spielstart verbindet sich das Spiel automatisch mit dem Photon-
    // Server (fuer Online). Der Offline-Modus laesst sich aber NICHT
    // einschalten, solange diese Verbindung noch steht ("Can't start OFFLINE
    // mode while connected!") - genau daran ist FARBMIMIK-Solo bisher
    // gescheitert. Also: erst sauber trennen, dann offline gehen.
    IEnumerator StarteSoloAblauf()
    {
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.Disconnect();
            float timeout = Time.time + 5f;
            while (PhotonNetwork.IsConnected && Time.time < timeout)
                yield return null;
        }

        PhotonNetwork.OfflineMode = true;   // ab jetzt alles lokal, kein Server
        PhotonNetwork.CreateRoom("SOLO");   // offline: OnJoinedRoom feuert sofort
    }

    /// <summary>Laeuft gerade eine Solo-Runde (Offline-Modus)?</summary>
    public static bool IstSolo => PhotonNetwork.OfflineMode;

    /// <summary>Bestehendem Raum per Code beitreten.</summary>
    public void TretRaumBei(string code, string modus, string prefabName)
    {
        FehlerText = "";
        RoomCode = (code ?? "").Trim().ToUpper();
        wartetAufModus = modus;
        wartetAufPrefab = prefabName;

        if (RoomCode.Length == 0)
        {
            FehlerText = "Das ist nicht korrekt! Bitte einen Code eingeben.";
            return;
        }
        StartCoroutine(WartetAufVerbindungDann(() => PhotonNetwork.JoinRoom(RoomCode)));
    }

    IEnumerator WartetAufVerbindungDann(System.Action aktion)
    {
        float timeout = Time.time + 10f;
        while (!PhotonNetwork.IsConnectedAndReady && Time.time < timeout)
            yield return null;

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            FehlerText = "Keine Verbindung zum Server. Internetverbindung prüfen.";
            yield break;
        }
        aktion();
    }

    void HosteJetzt()
    {
        RoomCode = GeneriereCode();
        var optionen = new RoomOptions
        {
            MaxPlayers = (byte)wartetAufMaxSpieler,
            CustomRoomProperties = new Hashtable { { "modus", wartetAufModus } },
            CustomRoomPropertiesForLobby = new[] { "modus" }
        };
        PhotonNetwork.CreateRoom(RoomCode, optionen);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        hostVersuche++;
        if (hostVersuche < 5)
        {
            HosteJetzt();   // Code war schon vergeben - einfach neuen Code versuchen
        }
        else
        {
            FehlerText = "Raum konnte nicht erstellt werden: " + message;
            Debug.LogWarning("PhotonRoomManager: CreateRoom fehlgeschlagen - " + message);
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        FehlerText = "Das ist nicht korrekt! IP/Code prüfen und nochmal versuchen.";
    }

    public override void OnJoinedRoom()
    {
        // Falscher Modus? (jemand hat den Code vom jeweils anderen Spiel eingegeben)
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("modus", out object modus) &&
            wartetAufModus != null && (string)modus != wartetAufModus)
        {
            FehlerText = "Das ist nicht korrekt! Dieser Code gehört zum anderen Spiel-Modus.";
            PhotonNetwork.LeaveRoom();
            return;
        }

        RoomCode = PhotonNetwork.CurrentRoom.Name;
        SpielerSpawnen();
    }

    void SpielerSpawnen()
    {
        if (string.IsNullOrEmpty(wartetAufPrefab))
            return;

        GameObject prefab = Resources.Load<GameObject>(wartetAufPrefab);
        if (prefab == null)
        {
            FehlerText = "Spieler-Prefab '" + wartetAufPrefab + "' fehlt in Resources!";
            Debug.LogError("PhotonRoomManager: " + FehlerText);
            return;
        }

        // Referenz-Spawn: die vorhandene Solo-Figur (Tag "Player") steht bereits
        // korrekt am Boden. Frueher spawnten Online-Spieler bei (0,0,0) - das
        // liegt oft IM Dach/in der Geometrie, dann steckt der CharacterController
        // fest und man kann sich nicht bewegen.
        Vector3 basis = FindeSpawnBasis();
        float jx = Random.Range(-2.5f, 2.5f);
        Vector3 jitter = new Vector3(jx, 0f, Random.Range(-2.5f, 2.5f));

        // Etwas ueber dem Boden erzeugen, dann exakt auf den Boden setzen
        GameObject go = PhotonNetwork.Instantiate(wartetAufPrefab,
            basis + jitter + Vector3.up * 2f, Quaternion.identity);
        if (go == null) return;

        var cc = go.GetComponent<CharacterController>();
        if (cc != null)
        {
            // Fuss-Versatz = Unterkante der Kapsel relativ zum Pivot
            float fussVersatz = cc.center.y - cc.height / 2f;
            cc.enabled = false;   // CC deaktivieren, um sauber zu teleportieren
            go.transform.position = new Vector3(basis.x + jitter.x,
                basis.y - fussVersatz + 0.05f, basis.z + jitter.z);
            cc.enabled = true;
        }
    }

    // Bester bekannter Boden-Spawnpunkt: die Solo-Figur (Tag "Player") steht
    // schon richtig; sonst die Burg-Mitte per Raycast auf den Boden; sonst 0.
    static Vector3 FindeSpawnBasis()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            return player.transform.position;

        GameObject burg = GameObject.Find("Burg");
        if (burg != null)
        {
            Vector3 c = burg.transform.position;
            if (Physics.Raycast(c + Vector3.up * 100f, Vector3.down, out RaycastHit hit,
                    300f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                return hit.point;
            return c;
        }

        if (Physics.Raycast(Vector3.up * 100f, Vector3.down, out RaycastHit hit2,
                300f, ~(1 << 4), QueryTriggerInteraction.Ignore))
            return hit2.point;
        return Vector3.zero;
    }

    /// <summary>Raum verlassen (Host oder Mitspieler - Photon behandelt beide gleich,
    /// Photon waehlt bei Bedarf automatisch einen neuen MasterClient).</summary>
    public void VerlasseRaum()
    {
        RoomCode = "";
        if (PhotonNetwork.OfflineMode)
        {
            // Solo beenden: Offline-Modus aus -> danach wieder online-bereit
            PhotonNetwork.OfflineMode = false;
            return;
        }
        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        RoomCode = "";
        // Nach einer Solo-Runde (Offline-Modus war an) wieder mit dem Server
        // verbinden, damit spaeteres Online-Spielen sofort geht
        if (!PhotonNetwork.OfflineMode && !PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
    }

    static string GeneriereCode()
    {
        // ohne O/0 und I/1, damit man sich beim Abtippen nicht vertut
        const string zeichen = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code = "";
        for (int i = 0; i < 4; i++)
            code += zeichen[Random.Range(0, zeichen.Length)];
        return code;
    }
}
