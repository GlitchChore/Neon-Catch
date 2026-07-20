using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public enum SpielPhase : byte
{
    Lobby,   // warten auf Spieler
    Malen,   // 90s: anmalen & verstecken
    Suchen,  // 5 Minuten: Sucher unterwegs, alle anderen frozen
    Ende
}

/// <summary>
/// Steuert die Spielphasen, den Timer, das Fangen und die Endplatzierung -
/// der MasterClient rechnet (Photons Entsprechung zu Mirrors "Server"),
/// alle anderen lesen mit. Sync laeuft ueber Photon Room Custom Properties
/// (Ersatz fuer Mirrors SyncVars - jeder Client bekommt Aenderungen und,
/// wichtig, auch den AKTUELLEN Stand beim Beitreten automatisch mit).
///
/// Suchzeit: 5 Minuten. Findet der Sucher ALLE Versteckten, gewinnt er
/// sofort (Platz 1); sonst laeuft die Zeit ab und die nie Gefundenen
/// bekommen die besten Plaetze. Unter den Gefundenen gilt: wer spaeter
/// gefunden wurde, hat sich laenger versteckt und bekommt den besseren
/// Platz. Fehlende Mitspieler werden bis zu zielSpieler (Standard 5) mit
/// Bots aufgefuellt - echte Freunde werden dabei nie verdraengt.
///
/// Baut zusaetzlich auf JEDEM Client eine Lichtsaeule + "SPAWN DES
/// SUCHERS"-Beschriftung an der Spawn-Stelle, solange der Sucher noch
/// nicht da ist.
///
/// Gehoert auf ein leeres GameObject "GamePhaseManager" in der FARBMIMIK-
/// Szene. Braucht KEINE PhotonView - es nutzt nur Raum-Eigenschaften und
/// ruft RPCs auf den PhotonViews der einzelnen Spieler/Bots auf.
/// </summary>
public class GamePhaseManager : MonoBehaviourPunCallbacks
{
    public static GamePhaseManager Instance { get; private set; }

    /// <summary>Wird auf jedem Client gefeuert, sobald die Phase wechselt.</summary>
    public static event System.Action<SpielPhase> PhaseGewechselt;

    [Header("Dauer in Sekunden")]
    public int malenDauer = 90;
    public int suchenDauer = 300;   // 5 Minuten
    public int sucherVorbereitungsDauer = 5;   // Wartezeit, bevor der Sucher auf der Map erscheint

    [Header("Fangen")]
    public float fangRadius = 1.2f;   // Abstand, ab dem der Sucher jemanden "findet"

    [Header("Bots (fuellen jede Runde auf, egal wie viele Freunde da sind)")]
    public int zielSpieler = 5;   // Menschen + Bots zusammen

    // Lokale, auf JEDEM Client gepflegte Kopie der Raum-Eigenschaften -
    // andere Scripts lesen einfach GamePhaseManager.Instance.phase usw.,
    // genau wie vorher bei den Mirror-SyncVars.
    public SpielPhase phase = SpielPhase.Lobby;
    public int restSekunden;
    public int sucherViewId;
    public bool sucherAktiv;
    public int sucherSpawnRest;
    public Vector3 sucherSpawnPosition;

    const string K_PHASE = "phase";
    const string K_REST = "restSek";
    const string K_SUCHER = "sucherView";
    const string K_SUCHER_AKTIV = "sucherAktiv";
    const string K_SPAWN_REST = "spawnRest";
    const string K_SPAWN_POS = "spawnPos";

    double phasenEnde;               // nur MasterClient gueltig (PhotonNetwork.Time)
    double sucherVorbereitungEnde;   // nur MasterClient gueltig
    int letzterGesendeterRest = -1;
    int letzterGesendeterSpawnRest = -1;
    readonly List<int> gefundenReihenfolge = new List<int>();   // MasterClient: Fang-Reihenfolge (ViewIDs)

    GameObject laserObjekt;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Aktuelle Werte sofort uebernehmen - wichtig fuer Clients, die einem
        // schon laufenden Raum beitreten (Raum-Eigenschaften sind sofort da,
        // anders als bei RPCs muss man nicht auf die naechste Aenderung warten)
        LiesRaumEigenschaften(PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.CustomProperties : null);
    }

    public override void OnJoinedRoom()
    {
        LiesRaumEigenschaften(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (laserObjekt != null)
            Destroy(laserObjekt);
    }

    public bool IstSucher(int viewId)
    {
        return viewId != 0 && sucherViewId != 0 && viewId == sucherViewId;
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        SpielPhase alt = phase;
        LiesRaumEigenschaften(propertiesThatChanged);
        if (phase != alt)
            PhaseGewechselt?.Invoke(phase);
    }

    void LiesRaumEigenschaften(Hashtable props)
    {
        if (props == null) return;
        if (props.TryGetValue(K_PHASE, out object p)) phase = (SpielPhase)(byte)p;
        if (props.TryGetValue(K_REST, out object r)) restSekunden = (int)r;
        if (props.TryGetValue(K_SUCHER, out object s)) sucherViewId = (int)s;
        if (props.TryGetValue(K_SUCHER_AKTIV, out object sa)) sucherAktiv = (bool)sa;
        if (props.TryGetValue(K_SPAWN_REST, out object sr)) sucherSpawnRest = (int)sr;
        if (props.TryGetValue(K_SPAWN_POS, out object sp)) sucherSpawnPosition = (Vector3)sp;
    }

    static void SetzeRaumEigenschaften(Hashtable props)
    {
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    void Update()
    {
        // Lichtsaeule + Beschriftung verwaltet JEDER Client, nicht nur der MasterClient
        AktualisiereLaser();

        if (!PhotonNetwork.IsMasterClient)
            return;
        if (phase != SpielPhase.Malen && phase != SpielPhase.Suchen)
            return;

        // Suchphase startet mit einer Vorbereitungszeit: der Sucher wartet auf
        // einem schwarzen Bildschirm, alle anderen sehen die Lichtsaeule -
        // erst danach zaehlt die eigentliche Suchzeit (5 Minuten)
        if (phase == SpielPhase.Suchen && !sucherAktiv)
        {
            int vorbereitungRest = Mathf.Max(0, Mathf.CeilToInt((float)(sucherVorbereitungEnde - PhotonNetwork.Time)));
            if (vorbereitungRest != letzterGesendeterSpawnRest)
            {
                letzterGesendeterSpawnRest = vorbereitungRest;
                SetzeRaumEigenschaften(new Hashtable { { K_SPAWN_REST, vorbereitungRest } });
            }

            if (vorbereitungRest <= 0)
            {
                phasenEnde = PhotonNetwork.Time + suchenDauer;
                letzterGesendeterRest = suchenDauer;
                SetzeRaumEigenschaften(new Hashtable
                {
                    { K_SUCHER_AKTIV, true },
                    { K_REST, suchenDauer },
                });
            }
            return;
        }

        if (phase == SpielPhase.Suchen && sucherAktiv)
        {
            PruefeFangen();
            if (phase != SpielPhase.Suchen)
                return;   // Runde wurde gerade vorzeitig beendet (alle gefunden)
        }

        int rest = Mathf.Max(0, Mathf.CeilToInt((float)(phasenEnde - PhotonNetwork.Time)));
        if (rest != letzterGesendeterRest)
        {
            letzterGesendeterRest = rest;
            SetzeRaumEigenschaften(new Hashtable { { K_REST, rest } });
        }

        if (rest <= 0)
            NaechstePhase();
    }

    // Prueft, ob der Sucher gerade nah genug an einem noch unentdeckten
    // Versteckten steht - dann gilt der als gefunden. Sind danach ALLE
    // gefunden, endet die Runde sofort (Sucher gewinnt), statt die vollen
    // 5 Minuten abzuwarten. Nur der MasterClient prueft das.
    void PruefeFangen()
    {
        if (sucherViewId == 0)
            return;
        PhotonView sucherView = PhotonView.Find(sucherViewId);
        if (sucherView == null)
            return;
        Vector3 sucherPos = sucherView.transform.position;

        var versteckte = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                          .Where(s => s.photonView.ViewID != sucherViewId).ToArray();

        foreach (var spieler in versteckte)
        {
            if (spieler.gefunden) continue;
            if (Vector3.Distance(sucherPos, spieler.transform.position) <= fangRadius)
            {
                // AllBuffered fuehrt sofort auch lokal aus - "spieler.gefunden"
                // ist direkt danach schon aktuell (wichtig fuer die Pruefung unten)
                spieler.photonView.RPC(nameof(SelfPaintSystem.RpcSetzeGefunden), RpcTarget.AllBuffered);
                gefundenReihenfolge.Add(spieler.photonView.ViewID);
            }
        }

        if (versteckte.Length > 0 && versteckte.All(s => s.gefunden))
        {
            BerechnePlatzierungen(alleGefunden: true);
            WechslePhase(SpielPhase.Ende);
        }
    }

    // Endplatzierung der Runde: Sucher gewinnt nur, wenn wirklich ALLE
    // gefunden wurden. Sonst bekommen die nie gefundenen Spieler die besten
    // Plaetze. Unter den Gefundenen zaehlt: wer SPAETER gefunden wurde, hat
    // sich laenger versteckt und bekommt den besseren Platz.
    void BerechnePlatzierungen(bool alleGefunden)
    {
        var alle = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None);
        var sucher = alle.FirstOrDefault(s => s.photonView.ViewID == sucherViewId);
        var versteckte = alle.Where(s => s.photonView.ViewID != sucherViewId).ToList();

        int naechsterPlatz;
        if (alleGefunden)
        {
            if (sucher != null)
                sucher.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, 1);
            naechsterPlatz = 2;
        }
        else
        {
            // Sucher hat NICHT alle gefunden -> kein Sieg, schlechtester Platz
            if (sucher != null)
                sucher.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, versteckte.Count + 1);

            naechsterPlatz = 1;
            foreach (var s in versteckte.Where(s => !s.gefunden))
            {
                s.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, naechsterPlatz);
                naechsterPlatz++;
            }
        }

        // Gefundene: rueckwaerts durch die Fang-Reihenfolge (zuletzt gefunden = besser)
        for (int i = gefundenReihenfolge.Count - 1; i >= 0; i--)
        {
            var s = versteckte.FirstOrDefault(x => x.photonView.ViewID == gefundenReihenfolge[i]);
            if (s != null)
            {
                s.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, naechsterPlatz);
                naechsterPlatz++;
            }
        }
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

    /// <summary>Nur der Host (MasterClient) darf starten (LobbyUI prueft PhotonNetwork.IsMasterClient).</summary>
    public void StarteSpiel()
    {
        if (!PhotonNetwork.IsMasterClient || phase != SpielPhase.Lobby)
            return;

        gefundenReihenfolge.Clear();
        SpawneFehlendeBots();
        WechslePhase(SpielPhase.Malen);
    }

    // Fuellt die Runde mit Bots auf zielSpieler Kaempfer auf (echte Freunde
    // gehen dabei nie verloren - es werden nur so viele Bots hinzugefuegt,
    // wie noch Plaetze frei sind). Alte Bots der letzten Runde zuerst raeumen.
    // Bots sind Raum-Objekte (InstantiateRoomObject) - sie ueberleben einen
    // MasterClient-Wechsel, statt mit dem Ersteller zu verschwinden.
    void SpawneFehlendeBots()
    {
        foreach (var alterBot in FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None))
            if (alterBot.istBot)
                PhotonNetwork.Destroy(alterBot.gameObject);

        int menschen = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None).Count(s => !s.istBot);
        int botsNoetig = Mathf.Max(0, zielSpieler - menschen);

        GameObject prefab = Resources.Load<GameObject>("Spieler");
        if (prefab == null)
        {
            Debug.LogWarning("GamePhaseManager: Prefab 'Spieler' fehlt - keine Bots erzeugt.");
            return;
        }

        for (int i = 0; i < botsNoetig; i++)
        {
            Vector3 pos = FindeZufallsPosition();
            Color farbe = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.85f, 1f);
            // Bot-Daten (Name + Farbe) als InstantiationData mitgeben - die sind
            // in SelfPaintSystem.Awake SOFORT verfuegbar, ohne RPC-Frame-Verzug
            object[] daten = { "Bot " + (i + 1), farbe.r, farbe.g, farbe.b };
            PhotonNetwork.InstantiateRoomObject("Spieler", pos, Quaternion.identity, 0, daten);
        }
    }

    Vector3 FindeZufallsPosition()
    {
        Vector3 mitte = FindeKartenMitte();
        for (int versuch = 0; versuch < 20; versuch++)
        {
            float winkel = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = Random.Range(4f, 20f);
            Vector3 kandidat = mitte + new Vector3(Mathf.Cos(winkel) * radius, 0f, Mathf.Sin(winkel) * radius);
            if (Physics.Raycast(kandidat + Vector3.up * 50f, Vector3.down, out RaycastHit hit,
                    200f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                return hit.point;
        }
        return mitte;
    }

    public void ZurueckZurLobby()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        foreach (var spieler in FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None))
        {
            if (spieler.istBot)
                PhotonNetwork.Destroy(spieler.gameObject);
            else
                spieler.photonView.RPC(nameof(SelfPaintSystem.RpcResetFuerLobby), RpcTarget.All);
        }

        gefundenReihenfolge.Clear();
        SetzeRaumEigenschaften(new Hashtable
        {
            { K_PHASE, (byte)SpielPhase.Lobby },
            { K_SUCHER, 0 },
            { K_SUCHER_AKTIV, false },
            { K_REST, 0 },
        });
    }

    void NaechstePhase()
    {
        if (phase == SpielPhase.Malen)
        {
            WaehleSucher();
            StarteSucherVorbereitung();
        }
        else if (phase == SpielPhase.Suchen)
        {
            // Zeit abgelaufen, bevor der Sucher alle gefunden hat
            BerechnePlatzierungen(alleGefunden: false);
            WechslePhase(SpielPhase.Ende);
        }
    }

    void StarteSucherVorbereitung()
    {
        sucherVorbereitungEnde = PhotonNetwork.Time + sucherVorbereitungsDauer;
        letzterGesendeterSpawnRest = sucherVorbereitungsDauer;
        letzterGesendeterRest = suchenDauer;
        SetzeRaumEigenschaften(new Hashtable
        {
            { K_PHASE, (byte)SpielPhase.Suchen },
            { K_SUCHER_AKTIV, false },
            { K_SPAWN_REST, sucherVorbereitungsDauer },
            { K_REST, suchenDauer },
        });
    }

    void WechslePhase(SpielPhase neu)
    {
        int dauer = neu == SpielPhase.Malen ? malenDauer : 0;
        phasenEnde = PhotonNetwork.Time + dauer;
        letzterGesendeterRest = dauer;
        SetzeRaumEigenschaften(new Hashtable
        {
            { K_PHASE, (byte)neu },
            { K_SUCHER_AKTIV, false },
            { K_REST, dauer },
        });
    }

    void WaehleSucher()
    {
        // Nur echte Spieler koennen Sucher werden - Bots haben keine KI zum Suchen
        var moegliche = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                         .Where(s => !s.istBot).ToArray();
        int neuerSucherView = moegliche.Length > 0
            ? moegliche[Random.Range(0, moegliche.Length)].photonView.ViewID
            : 0;

        SetzeRaumEigenschaften(new Hashtable
        {
            { K_SUCHER, neuerSucherView },
            { K_SPAWN_POS, FindeKartenMitte() },
        });
    }

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
