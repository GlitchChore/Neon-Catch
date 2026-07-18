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
/// SyncVars: phase, restSekunden, sucherNetId.
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

    [SyncVar(hook = nameof(BeiPhasenwechsel))]
    public SpielPhase phase = SpielPhase.Lobby;

    [SyncVar]
    public int restSekunden;

    [SyncVar]
    public uint sucherNetId;

    double phasenEnde;   // nur auf dem Server gueltig (NetworkTime)

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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

        int rest = Mathf.Max(0, Mathf.CeilToInt((float)(phasenEnde - NetworkTime.time)));

        // SyncVar nur bei Aenderung setzen -> geht nur 1x pro Sekunde uebers Netz
        if (rest != restSekunden)
            restSekunden = rest;

        if (rest <= 0)
            NaechstePhase();
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
            WechslePhase(SpielPhase.Suchen);
        }
        else if (phase == SpielPhase.Suchen)
        {
            WechslePhase(SpielPhase.Ende);
        }
    }

    [Server]
    void WechslePhase(SpielPhase neu)
    {
        phase = neu;
        int dauer = neu == SpielPhase.Malen ? malenDauer
                  : neu == SpielPhase.Suchen ? suchenDauer
                  : 0;
        phasenEnde = NetworkTime.time + dauer;
        restSekunden = dauer;
    }

    [Server]
    void WaehleSucher()
    {
        var alle = FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None);
        if (alle.Length > 0)
            sucherNetId = alle[UnityEngine.Random.Range(0, alle.Length)].netId;
    }
}
