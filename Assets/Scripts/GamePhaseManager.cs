using System;
using Mirror;
using UnityEngine;

public enum SpielPhase : byte
{
    Lobby,   // warten auf Spieler
    Malen,   // 90s: anmalen & verstecken
    Suchen,  // 30s: Sucher unterwegs, alle anderen frozen
    Ende
}

/// <summary>
/// Steuert die Spielphasen und den Timer - komplett servergesteuert.
/// SyncVars: phase, restSekunden, sucherNetId, sucherAktiv, sucherSpawnRest, sucherSpawnPosition.
/// Baut zusaetzlich auf JEDEM Client eine Lichtsaeule + "SPAWN DES SUCHERS"-
/// Beschriftung an der Spawn-Stelle, solange der Sucher noch nicht da ist.
/// Gehoert auf ein leeres GameObject "GamePhaseManager" in der FARBMIMIK-Szene,
/// zusammen mit einer NetworkIdentity-Komponente.
/// </summary>
public class GamePhaseManager : NetworkBehaviour
{
    public static GamePhaseManager Instance { get; private set; }

    /// <summary>Wird auf jedem Client gefeuert, sobald die Phase wechselt.</summary>
    public static event Action<SpielPhase> PhaseGewechselt;

    [Header("Dauer in Sekunden")]
    public int malenDauer = 90;
    public int suchenDauer = 30;
    public int sucherVorbereitungsDauer = 5;   // Wartezeit, bevor der Sucher auf der Map erscheint

    [SyncVar(hook = nameof(BeiPhasenwechsel))]
    public SpielPhase phase = SpielPhase.Lobby;

    [SyncVar]
    public int restSekunden;

    [SyncVar]
    public uint sucherNetId;

    /// <summary>Ist der Sucher schon auf der Map (Vorbereitungszeit vorbei)?</summary>
    [SyncVar]
    public bool sucherAktiv;

    /// <summary>Sekunden bis der Sucher spawnt - fuer den Wartebildschirm des Suchers.</summary>
    [SyncVar]
    public int sucherSpawnRest;

    /// <summary>Wo die Lichtsaeule steht und der Sucher spawnt.</summary>
    [SyncVar]
    public Vector3 sucherSpawnPosition;

    double phasenEnde;              // nur auf dem Server gueltig (NetworkTime)
    double sucherVorbereitungEnde;  // nur auf dem Server gueltig (NetworkTime)

    GameObject laserObjekt;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (laserObjekt != null)
            Destroy(laserObjekt);
    }

    public bool IstSucher(NetworkIdentity identitaet)
    {
        return identitaet != null && sucherNetId != 0 && identitaet.netId == sucherNetId;
    }

    void BeiPhasenwechsel(SpielPhase alt, SpielPhase neu)
    {
        PhaseGewechselt?.Invoke(neu);
    }

    [ServerCallback]
    void Update()
    {
        if (phase != SpielPhase.Malen && phase != SpielPhase.Suchen)
            return;

        // Suchphase startet mit einer Vorbereitungszeit: der Sucher wartet auf
        // einem schwarzen Bildschirm, alle anderen sehen die Lichtsaeule -
        // erst danach zaehlt die eigentliche 30-Sekunden-Suchzeit
        if (phase == SpielPhase.Suchen && !sucherAktiv)
        {
            int vorbereitungRest = Mathf.Max(0, Mathf.CeilToInt((float)(sucherVorbereitungEnde - NetworkTime.time)));
            if (vorbereitungRest != sucherSpawnRest)
                sucherSpawnRest = vorbereitungRest;

            if (vorbereitungRest <= 0)
            {
                sucherAktiv = true;
                phasenEnde = NetworkTime.time + suchenDauer;
                restSekunden = suchenDauer;
            }
            return;
        }

        int rest = Mathf.Max(0, Mathf.CeilToInt((float)(phasenEnde - NetworkTime.time)));

        // SyncVar nur bei Aenderung setzen -> geht nur 1x pro Sekunde uebers Netz
        if (rest != restSekunden)
            restSekunden = rest;

        if (rest <= 0)
            NaechstePhase();
    }

    // Lichtsaeule + Beschriftung auf JEDEM Client verwalten (nicht nur Server) -
    // LateUpdate statt Update, damit es nicht mit dem [ServerCallback] oben kollidiert
    void LateUpdate()
    {
        AktualisiereLaser();
    }

    void AktualisiereLaser()
    {
        bool sollZeigen = phase == SpielPhase.Suchen && !sucherAktiv;

        if (sollZeigen && laserObjekt == null)
            laserObjekt = ErzeugeLaser();
        else if (!sollZeigen && laserObjekt != null)
        {
            Destroy(laserObjekt);
            laserObjekt = null;
        }

        if (laserObjekt != null)
            laserObjekt.transform.position = sucherSpawnPosition;
    }

    GameObject ErzeugeLaser()
    {
        const float hoehe = 18f;
        Color neonCyan = new Color(0f, 1f, 1f);

        var go = new GameObject("SucherSpawn_Laser");

        var strahl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        strahl.name = "Lichtsaeule";
        Destroy(strahl.GetComponent<Collider>());
        strahl.transform.SetParent(go.transform, false);
        strahl.transform.localPosition = Vector3.up * (hoehe * 0.5f);
        strahl.transform.localScale = new Vector3(0.35f, hoehe * 0.5f, 0.35f);   // Cylinder-Hoehe = 2x Y-Scale

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "SucherLaser_Mat" };
        mat.SetColor("_BaseColor", neonCyan * 3f);   // HDR -> gluehen ueber die Bloom-Schwelle
        strahl.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var textGo = new GameObject("SpawnText");
        textGo.transform.SetParent(go.transform, false);
        textGo.transform.localPosition = Vector3.up * (hoehe + 1.2f);
        var text = textGo.AddComponent<TextMesh>();
        text.text = "SPAWN DES SUCHERS";
        text.color = neonCyan;
        text.fontSize = 96;
        text.characterSize = 0.3f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        textGo.AddComponent<LaserTextBillboard>();

        return go;
    }

    /// <summary>Nur der Host darf starten (LobbyUI prueft NetworkServer.active).</summary>
    [Server]
    public void StarteSpiel()
    {
        if (phase == SpielPhase.Lobby)
            WechslePhase(SpielPhase.Malen);
    }

    [Server]
    public void ZurueckZurLobby()
    {
        foreach (var spieler in FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None))
            spieler.ResetFuerLobby();

        sucherNetId = 0;
        WechslePhase(SpielPhase.Lobby);
    }

    [Server]
    void NaechstePhase()
    {
        if (phase == SpielPhase.Malen)
        {
            WaehleSucher();
            StarteSucherVorbereitung();
        }
        else if (phase == SpielPhase.Suchen)
        {
            WechslePhase(SpielPhase.Ende);
        }
    }

    [Server]
    void StarteSucherVorbereitung()
    {
        phase = SpielPhase.Suchen;
        sucherAktiv = false;
        sucherSpawnRest = sucherVorbereitungsDauer;
        sucherVorbereitungEnde = NetworkTime.time + sucherVorbereitungsDauer;
        restSekunden = suchenDauer;
    }

    [Server]
    void WechslePhase(SpielPhase neu)
    {
        phase = neu;
        sucherAktiv = false;
        int dauer = neu == SpielPhase.Malen ? malenDauer : 0;
        phasenEnde = NetworkTime.time + dauer;
        restSekunden = dauer;
    }

    [Server]
    void WaehleSucher()
    {
        var alle = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None);
        if (alle.Length > 0)
            sucherNetId = alle[UnityEngine.Random.Range(0, alle.Length)].netId;
        sucherSpawnPosition = FindeKartenMitte();
    }

    [Server]
    static Vector3 FindeKartenMitte()
    {
        GameObject burg = GameObject.Find("Burg");
        Vector3 mitte = burg != null ? burg.transform.position : Vector3.zero;
        if (Physics.Raycast(mitte + Vector3.up * 50f, Vector3.down, out RaycastHit hit,
                200f, ~(1 << 4), QueryTriggerInteraction.Ignore))
            mitte.y = hit.point.y;
        return mitte;
    }
}

/// <summary>Dreht sich staendig zur Hauptkamera - fuer die Spawn-Beschriftung.</summary>
public class LaserTextBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        Camera kamera = Camera.main;
        if (kamera != null)
            transform.rotation = kamera.transform.rotation;
    }
}
