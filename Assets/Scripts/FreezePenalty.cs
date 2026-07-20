using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Bewegung + Freeze-Strafe:
/// - WASD-Bewegung (W/S vor und zurueck, A/D drehen) + einfache Verfolgerkamera
/// - Der Sucher wartet die Vorbereitungszeit lang bewegungslos (schwarzer
///   Bildschirm kommt aus LobbyUI), teleportiert dann automatisch zur
///   Spawn-Position (Lichtsaeule) und kann erst ab da suchen
/// - PHASE 2 (Suchen, erst NACHDEM der Sucher gespawnt ist): Wer NICHT
///   Sucher ist und sich trotzdem bewegt, blinkt 2 Sekunden Neon-Cyan und
///   ist dabei durch Waende hindurch sichtbar (Roentgen-Effekt) - fuer
///   ALLE Spieler. Der Glow kommt ueber URP-Post-Processing (Bloom-Volume,
///   automatisch erzeugt).
/// Gehoert auf das Spieler-Prefab (zusammen mit PhotonView + PhotonTransformView).
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PhotonView))]
public class FreezePenalty : MonoBehaviourPun
{
    [Header("Bewegung")]
    public float tempo = 5f;
    public float drehTempo = 140f;

    [Header("Freeze")]
    public float bewegungsToleranz = 0.15f;   // Meter, ab denen "bewegt" zaehlt
    public float blinkDauer = 2f;

    static readonly Color NeonCyan = new Color(0f, 1f, 1f);
    static bool glowVolumeErzeugt;

    CharacterController controller;
    Renderer[] eigeneRenderer;
    SelfPaintSystem paint;
    Vector3 freezePosition;
    bool blinkt;
    bool zumSpawnTeleportiert;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        eigeneRenderer = GetComponentsInChildren<Renderer>();
        paint = GetComponent<SelfPaintSystem>();
    }

    void OnEnable() { GamePhaseManager.PhaseGewechselt += BeiPhasenwechsel; }
    void OnDisable() { GamePhaseManager.PhaseGewechselt -= BeiPhasenwechsel; }

    void BeiPhasenwechsel(SpielPhase neu)
    {
        if (neu == SpielPhase.Suchen)
            freezePosition = transform.position;
        else if (neu == SpielPhase.Malen)
            zumSpawnTeleportiert = false;   // naechste Runde: wieder frisch warten+teleportieren
    }

    void Start()
    {
        if (photonView.IsMine)
            ErzeugeGlowVolume();
    }

    /// <summary>Globales URP-Post-Processing-Volume mit Bloom, damit das Neon-Blinken gluehen kann.</summary>
    static void ErzeugeGlowVolume()
    {
        if (glowVolumeErzeugt)
            return;
        glowVolumeErzeugt = true;

        var go = new GameObject("NeonGlow_PostProcessing_Volume");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;

        var profil = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profil.Add<Bloom>(true);
        bloom.intensity.Override(1.6f);
        bloom.threshold.Override(1.1f);   // nur HDR-Farben (unser Cyan) gluehen
        volume.profile = profil;
    }

    void Update()
    {
        if (!photonView.IsMine)
            return;

        // Bots sind passive Verstecke: sie stehen still (auf dem MasterClient
        // ist ein Bot-Raumobjekt zwar "IsMine", darf aber NICHT auf die
        // Host-Tastatur reagieren)
        if (paint != null && paint.istBot)
            return;

        var phasen = GamePhaseManager.Instance;
        bool binSucher = phasen != null && phasen.IstSucher(photonView.ViewID);

        // Sucher wartet auf den Spawn: keine Bewegung, schwarzer Bildschirm kommt aus LobbyUI
        if (binSucher && phasen.phase == SpielPhase.Suchen && !phasen.sucherAktiv)
            return;

        // Der Moment, in dem der Sucher spawnt: einmalig zur Lichtsaeule teleportieren
        if (binSucher && phasen != null && phasen.phase == SpielPhase.Suchen &&
            phasen.sucherAktiv && !zumSpawnTeleportiert)
        {
            zumSpawnTeleportiert = true;
            TeleportZuSpawn(phasen.sucherSpawnPosition);
        }

        Bewege();
        UeberwacheFreeze();
    }

    void TeleportZuSpawn(Vector3 position)
    {
        controller.enabled = false;
        transform.position = position + Vector3.up * 0.1f;
        controller.enabled = true;
    }

    void Bewege()
    {
        var tastatur = Keyboard.current;
        if (tastatur == null)
            return;

        float drehung = (tastatur.dKey.isPressed ? 1f : 0f) - (tastatur.aKey.isPressed ? 1f : 0f);
        float vorwaerts = (tastatur.wKey.isPressed ? 1f : 0f) - (tastatur.sKey.isPressed ? 1f : 0f);

        transform.Rotate(0f, drehung * drehTempo * Time.deltaTime, 0f);

        Vector3 bewegung = transform.forward * (vorwaerts * tempo);
        bewegung.y = -9.81f;   // simple Schwerkraft
        controller.Move(bewegung * Time.deltaTime);
    }

    void UeberwacheFreeze()
    {
        var phasen = GamePhaseManager.Instance;
        // Neon-Strafe erst, wenn der Sucher WIRKLICH auf der Map ist -
        // waehrend der Vorbereitungszeit (Sucher noch nicht gespawnt) gilt
        // die Freeze-Regel noch nicht
        if (phasen == null || phasen.phase != SpielPhase.Suchen || !phasen.sucherAktiv)
            return;
        if (phasen.IstSucher(photonView.ViewID) || blinkt)
            return;

        if (Vector3.Distance(transform.position, freezePosition) > bewegungsToleranz)
        {
            freezePosition = transform.position;
            // Photon hat keinen dedizierten Server: der Spieler, der sich
            // bewegt hat, meldet das direkt an alle (kein Umweg mehr ueber
            // eine vertrauenswuerdige Zwischenstelle noetig fuer ein
            // Party-Spiel dieser Groesse)
            photonView.RPC(nameof(RpcBlinke), RpcTarget.All);
        }
    }

    void LateUpdate()
    {
        // einfache Verfolgerkamera fuer den lokalen Spieler
        if (!photonView.IsMine)
            return;
        var kamera = Camera.main;
        if (kamera == null)
            return;

        Vector3 ziel = transform.position - transform.forward * 5f + Vector3.up * 3f;
        kamera.transform.position = Vector3.Lerp(kamera.transform.position, ziel, 8f * Time.deltaTime);
        kamera.transform.LookAt(transform.position + Vector3.up * 1.5f);
    }

    [PunRPC]
    void RpcBlinke()
    {
        if (!blinkt)
            StartCoroutine(Blinken());
    }

    IEnumerator Blinken()
    {
        blinkt = true;

        // Waehrend des GESAMTEN Blinkens durch Waende hindurch sichtbar:
        // ZTest immer bestehen lassen (zeichnet ueber alles drueber) und
        // die Warteschlange weit nach hinten setzen - das ist der "Roentgen"-Effekt.
        // Urspruengliche Werte merken, um sie am Ende wiederherzustellen.
        var materialien = new System.Collections.Generic.List<Material>();
        var urspruenglicheQueues = new System.Collections.Generic.List<int>();
        foreach (var r in eigeneRenderer)
        {
            foreach (var m in r.materials)
            {
                materialien.Add(m);
                urspruenglicheQueues.Add(m.renderQueue);
                if (m.HasProperty("_ZTest"))
                    m.SetInt("_ZTest", (int)CompareFunction.Always);
                m.renderQueue = 4000;   // Overlay - zeichnet nach allem anderen
            }
        }

        // HDR-Cyan (Wert > 1) -> liegt ueber der Bloom-Schwelle -> glueht neon
        Color glueh = NeonCyan * 6f;
        float ende = Time.time + blinkDauer;
        bool an = false;

        while (Time.time < ende)
        {
            an = !an;
            foreach (var m in materialien)
            {
                if (an)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", glueh);
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                }
            }
            yield return new WaitForSeconds(0.15f);
        }

        for (int i = 0; i < materialien.Count; i++)
        {
            materialien[i].SetColor("_EmissionColor", Color.black);
            if (materialien[i].HasProperty("_ZTest"))
                materialien[i].SetInt("_ZTest", (int)CompareFunction.LessEqual);
            materialien[i].renderQueue = urspruenglicheQueues[i];
        }

        blinkt = false;
    }
}
