using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public enum SpielPhase : byte
{
    Lobby,       // warten, Sucher waehlen
    Verstecken,  // Sucher wartet (schwarzer Bildschirm), andere verstecken sich + malen
    Suchen,      // Sucher sucht, Versteckte sind eingefroren (koennen nur malen)
    Ende
}

/// <summary>
/// Steuert die FARBMIMIK-Phasen - der MasterClient rechnet, alle lesen ueber
/// Photon Room Custom Properties mit. Ablauf wie beim Schiess-Spiel, nur:
///   1. Lobby: Host waehlt den Sucher (bestimmt ODER zufaellig), dann Start.
///   2. Verstecken (Timer): Der SUCHER sieht einen schwarzen Bildschirm mit
///      "DU BIST SUCHER" + Countdown. Alle anderen laufen frei herum,
///      verstecken sich und koennen sich anmalen (Taste E).
///   3. Suchen: Der Sucher wird freigelassen und faengt durch BERUEHREN im
///      2-Meter-Radius. Die Versteckten sind jetzt EINGEFROREN (koennen sich
///      nicht mehr bewegen), koennen aber weiter malen.
///   4. Ende: Platzierung (wer zuletzt gefunden wurde, ist besser platziert;
///      hat der Sucher alle gefunden, gewinnt er).
/// Fehlende Mitspieler werden bis zielSpieler mit Bots aufgefuellt.
/// Gehoert auf ein GameObject in der FARBMIMIK-Szene (wird von LobbyUI
/// automatisch erzeugt) - braucht KEINE PhotonView.
/// </summary>
public class GamePhaseManager : MonoBehaviourPunCallbacks
{
    public static GamePhaseManager Instance { get; private set; }

    /// <summary>Wird auf jedem Client gefeuert, sobald die Phase wechselt.</summary>
    public static event System.Action<SpielPhase> PhaseGewechselt;

    [Header("Dauer in Sekunden")]
    public int versteckenDauer = 60;   // so lange verstecken sich die anderen
    public int suchenDauer = 180;      // 3 Minuten Suchzeit

    [Header("Fangen")]
    public float fangRadius = 2f;      // Sucher faengt durch Beruehren in diesem Radius

    [Header("Bots (fuellen jede Runde auf)")]
    public int zielSpieler = 5;

    // Lokale Kopie der synchronisierten Werte (jeder Client liest sie)
    public SpielPhase phase = SpielPhase.Lobby;
    public int restSekunden;
    public int sucherViewId;

    /// <summary>Host-Wahl: gewuenschter Sucher (ViewID) oder 0 = zufaellig.</summary>
    public int gewaehlterSucher;

    const string K_PHASE = "phase";
    const string K_REST = "restSek";
    const string K_SUCHER = "sucherView";

    double phasenEnde;
    int letzterRest = -1;
    readonly List<int> gefundenReihenfolge = new List<int>();

    void Awake() { Instance = this; }

    void Start()
    {
        LiesEigenschaften(PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.CustomProperties : null);
    }

    public override void OnJoinedRoom()
    {
        LiesEigenschaften(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IstSucher(int viewId)
    {
        return viewId != 0 && sucherViewId != 0 && viewId == sucherViewId;
    }

    public override void OnRoomPropertiesUpdate(Hashtable geaendert)
    {
        SpielPhase alt = phase;
        LiesEigenschaften(geaendert);
        if (phase != alt) PhaseGewechselt?.Invoke(phase);
    }

    void LiesEigenschaften(Hashtable p)
    {
        if (p == null) return;
        if (p.TryGetValue(K_PHASE, out object a)) phase = (SpielPhase)(byte)a;
        if (p.TryGetValue(K_REST, out object b)) restSekunden = (int)b;
        if (p.TryGetValue(K_SUCHER, out object c)) sucherViewId = (int)c;
    }

    static void Setze(Hashtable p) => PhotonNetwork.CurrentRoom.SetCustomProperties(p);

    void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (phase != SpielPhase.Verstecken && phase != SpielPhase.Suchen) return;

        if (phase == SpielPhase.Suchen)
        {
            PruefeFangen();
            if (phase != SpielPhase.Suchen) return;   // Runde gerade beendet
        }

        int rest = Mathf.Max(0, Mathf.CeilToInt((float)(phasenEnde - PhotonNetwork.Time)));
        if (rest != letzterRest)
        {
            letzterRest = rest;
            Setze(new Hashtable { { K_REST, rest } });
        }

        if (rest <= 0)
        {
            if (phase == SpielPhase.Verstecken)
                WechslePhase(SpielPhase.Suchen);   // Versteck-Zeit vorbei -> Sucher los, andere eingefroren
            else
            {
                BerechnePlatzierungen(false);      // Zeit abgelaufen, nicht alle gefunden
                WechslePhase(SpielPhase.Ende);
            }
        }
    }

    // Sucher faengt durch Beruehren im 2-Meter-Radius
    void PruefeFangen()
    {
        if (sucherViewId == 0) return;
        PhotonView sv = PhotonView.Find(sucherViewId);
        if (sv == null) return;
        Vector3 sucherPos = sv.transform.position;

        var versteckte = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                          .Where(s => s.photonView.ViewID != sucherViewId).ToArray();

        foreach (var v in versteckte)
        {
            if (v.gefunden) continue;
            if (Vector3.Distance(sucherPos, v.transform.position) <= fangRadius)
            {
                v.photonView.RPC(nameof(SelfPaintSystem.RpcSetzeGefunden), RpcTarget.AllBuffered);
                gefundenReihenfolge.Add(v.photonView.ViewID);
            }
        }

        if (versteckte.Length > 0 && versteckte.All(s => s.gefunden))
        {
            BerechnePlatzierungen(true);
            WechslePhase(SpielPhase.Ende);
        }
    }

    void BerechnePlatzierungen(bool alleGefunden)
    {
        var alle = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None);
        var sucher = alle.FirstOrDefault(s => s.photonView.ViewID == sucherViewId);
        var versteckte = alle.Where(s => s.photonView.ViewID != sucherViewId).ToList();

        int platz;
        if (alleGefunden)
        {
            if (sucher != null) sucher.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, 1);
            platz = 2;
        }
        else
        {
            if (sucher != null) sucher.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, versteckte.Count + 1);
            platz = 1;
            foreach (var s in versteckte.Where(x => !x.gefunden))
            {
                s.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, platz);
                platz++;
            }
        }

        // zuletzt gefunden = besser platziert
        for (int i = gefundenReihenfolge.Count - 1; i >= 0; i--)
        {
            var s = versteckte.FirstOrDefault(x => x.photonView.ViewID == gefundenReihenfolge[i]);
            if (s != null) { s.photonView.RPC(nameof(SelfPaintSystem.RpcSetzePlatz), RpcTarget.AllBuffered, platz); platz++; }
        }
    }

    // ---------- Rundensteuerung (nur MasterClient) ----------

    /// <summary>Host startet: Sucher festlegen (Wahl oder zufaellig), Bots auffuellen, Verstecken beginnt.</summary>
    public void StarteSpiel()
    {
        if (!PhotonNetwork.IsMasterClient || phase != SpielPhase.Lobby) return;

        gefundenReihenfolge.Clear();
        SpawneFehlendeBots();
        WaehleSucher();
        WechslePhase(SpielPhase.Verstecken);
    }

    void WaehleSucher()
    {
        var menschen = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None)
                       .Where(s => !s.istBot).ToArray();

        int neu = 0;
        // Bestimmter Sucher gewaehlt und noch da?
        if (gewaehlterSucher != 0 && menschen.Any(s => s.photonView.ViewID == gewaehlterSucher))
            neu = gewaehlterSucher;
        else if (menschen.Length > 0)
            neu = menschen[Random.Range(0, menschen.Length)].photonView.ViewID;

        Setze(new Hashtable { { K_SUCHER, neu } });
    }

    void SpawneFehlendeBots()
    {
        foreach (var b in FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None))
            if (b.istBot) PhotonNetwork.Destroy(b.gameObject);

        int menschen = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None).Count(s => !s.istBot);
        int noetig = Mathf.Max(0, zielSpieler - menschen);
        if (Resources.Load<GameObject>("Spieler") == null)
        {
            Debug.LogWarning("GamePhaseManager: Prefab 'Spieler' fehlt - keine Bots.");
            return;
        }

        for (int i = 0; i < noetig; i++)
        {
            Color farbe = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.85f, 1f);
            object[] daten = { "Bot " + (i + 1), farbe.r, farbe.g, farbe.b };
            PhotonNetwork.InstantiateRoomObject("Spieler", FindeZufallsPosition(), Quaternion.identity, 0, daten);
        }
    }

    Vector3 FindeZufallsPosition()
    {
        Vector3 mitte = FindeKartenMitte();
        for (int v = 0; v < 20; v++)
        {
            float w = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float r = Random.Range(4f, 20f);
            Vector3 k = mitte + new Vector3(Mathf.Cos(w) * r, 0f, Mathf.Sin(w) * r);
            if (Physics.Raycast(k + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~(1 << 4), QueryTriggerInteraction.Ignore))
                return hit.point;
        }
        return mitte;
    }

    public void ZurueckZurLobby()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        foreach (var s in FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None))
        {
            if (s.istBot) PhotonNetwork.Destroy(s.gameObject);
            else s.photonView.RPC(nameof(SelfPaintSystem.RpcResetFuerLobby), RpcTarget.All);
        }
        gefundenReihenfolge.Clear();
        Setze(new Hashtable
        {
            { K_PHASE, (byte)SpielPhase.Lobby },
            { K_SUCHER, 0 },
            { K_REST, 0 },
        });
    }

    void WechslePhase(SpielPhase neu)
    {
        int dauer = neu == SpielPhase.Verstecken ? versteckenDauer
                  : neu == SpielPhase.Suchen ? suchenDauer
                  : 0;
        phasenEnde = PhotonNetwork.Time + dauer;
        letzterRest = dauer;
        Setze(new Hashtable
        {
            { K_PHASE, (byte)neu },
            { K_REST, dauer },
        });
    }

    static Vector3 FindeKartenMitte()
    {
        GameObject burg = GameObject.Find("Burg");
        Vector3 mitte = burg != null ? burg.transform.position : Vector3.zero;
        if (Physics.Raycast(mitte + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~(1 << 4), QueryTriggerInteraction.Ignore))
            mitte.y = hit.point.y;
        return mitte;
    }
}
