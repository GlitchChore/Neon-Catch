using kcp2k;
using Mirror;
using UnityEngine;

/// <summary>
/// Sorgt automatisch fuer einen fertigen NetworkManager mit kcp2k-Transport:
/// - Ist KEIN NetworkManager in der Szene, wird einer erzeugt.
/// - Ist einer da, aber mit falschem Transport (z.B. Telepathy), wird auf Kcp umgestellt.
/// Gehoert auf ein leeres GameObject in der Szene (z.B. "Netzwerk").
/// </summary>
[DefaultExecutionOrder(-100)]
public class NetworkManagerSetup : MonoBehaviour
{
    [Header("Optional: Spieler-Prefab (mit NetworkIdentity)")]
    [Tooltip("Leer lassen, um automatisch 'Spieler' aus einem Resources-Ordner zu laden.")]
    public GameObject spielerPrefab;

    [Header("Einstellungen")]
    public int maxSpieler = 3;
    public ushort port = 7777;

    void Awake()
    {
        NetworkManager manager = FindAnyObjectByType<NetworkManager>();

        if (manager == null)
            manager = ErzeugeNetworkManager();
        else
            StelleAufKcpUm(manager);

        KonfiguriereManager(manager);
    }

    NetworkManager ErzeugeNetworkManager()
    {
        // GameObject zuerst inaktiv erzeugen, damit wir alles einstellen koennen,
        // BEVOR NetworkManager.Awake laeuft
        var go = new GameObject("NetworkManager (automatisch)");
        go.SetActive(false);

        var kcp = go.AddComponent<KcpTransport>();
        kcp.port = port;

        var manager = go.AddComponent<NetworkManager>();
        manager.transport = kcp;

        go.SetActive(true);
        Debug.Log("NetworkManagerSetup: NetworkManager mit KcpTransport erzeugt.");
        return manager;
    }

    void StelleAufKcpUm(NetworkManager manager)
    {
        if (manager.transport is KcpTransport)
            return;

        KcpTransport kcp = manager.GetComponent<KcpTransport>();
        if (kcp == null)
            kcp = manager.gameObject.AddComponent<KcpTransport>();
        kcp.port = port;

        manager.transport = kcp;
        Transport.active = kcp;   // Awake des Managers lief evtl. schon -> aktiv nachziehen
        Debug.Log("NetworkManagerSetup: Transport auf Kcp (kcp2k) umgestellt.");
    }

    void KonfiguriereManager(NetworkManager manager)
    {
        manager.maxConnections = maxSpieler;

        // Spieler-Prefab: erst Inspector-Feld, dann Resources/Spieler, sonst Warnung
        if (manager.playerPrefab == null)
        {
            GameObject prefab = spielerPrefab;
            if (prefab == null)
                prefab = Resources.Load<GameObject>("Spieler");

            if (prefab != null)
            {
                manager.playerPrefab = prefab;
            }
            else
            {
                manager.autoCreatePlayer = false;
                Debug.LogWarning("NetworkManagerSetup: Kein Spieler-Prefab gefunden! " +
                                 "Entweder im Inspector zuweisen oder ein Prefab namens 'Spieler' " +
                                 "in einen Resources-Ordner legen.");
            }
        }

        // Altes Mirror-HUD abschalten, damit es sich nicht mit MenuUI doppelt
        var hud = manager.GetComponent<NetworkManagerHUD>();
        if (hud != null)
        {
            hud.enabled = false;
            Debug.Log("NetworkManagerSetup: NetworkManagerHUD deaktiviert (MenuUI uebernimmt).");
        }
    }
}
