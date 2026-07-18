using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Bewegung + Freeze-Strafe:
/// - WASD-Bewegung (W/S vor und zurueck, A/D drehen) + einfache Verfolgerkamera
/// - PHASE 2 (Suchen): Wer NICHT Sucher ist und sich trotzdem bewegt,
///   blinkt fuer 2 Sekunden Neon-Cyan - sichtbar fuer ALLE Spieler.
///   Der Glow kommt ueber URP-Post-Processing (Bloom-Volume, automatisch erzeugt).
/// Gehoert auf das Spieler-Prefab (zusammen mit NetworkIdentity + NetworkTransformReliable).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FreezePenalty : NetworkBehaviour
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
    Vector3 freezePosition;
    bool blinkt;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        eigeneRenderer = GetComponentsInChildren<Renderer>();
    }

    void OnEnable() { GamePhaseManager.PhaseGewechselt += BeiPhasenwechsel; }
    void OnDisable() { GamePhaseManager.PhaseGewechselt -= BeiPhasenwechsel; }

    void BeiPhasenwechsel(SpielPhase neu)
    {
        if (neu == SpielPhase.Suchen)
            freezePosition = transform.position;
    }

    public override void OnStartLocalPlayer()
    {
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
        if (!isLocalPlayer)
            return;

        Bewege();
        UeberwacheFreeze();
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
        if (phasen == null || phasen.phase != SpielPhase.Suchen)
            return;
        if (phasen.IstSucher(netIdentity) || blinkt)
            return;

        if (Vector3.Distance(transform.position, freezePosition) > bewegungsToleranz)
        {
            freezePosition = transform.position;
            CmdBewegtWaehrendFreeze();
        }
    }

    void LateUpdate()
    {
        // einfache Verfolgerkamera fuer den lokalen Spieler
        if (!isLocalPlayer)
            return;
        var kamera = Camera.main;
        if (kamera == null)
            return;

        Vector3 ziel = transform.position - transform.forward * 5f + Vector3.up * 3f;
        kamera.transform.position = Vector3.Lerp(kamera.transform.position, ziel, 8f * Time.deltaTime);
        kamera.transform.LookAt(transform.position + Vector3.up * 1.5f);
    }

    [Command]
    void CmdBewegtWaehrendFreeze()
    {
        var phasen = GamePhaseManager.Instance;
        if (phasen != null && phasen.phase == SpielPhase.Suchen && !phasen.IstSucher(netIdentity))
            RpcBlinke();
    }

    [ClientRpc]
    void RpcBlinke()
    {
        if (!blinkt)
            StartCoroutine(Blinken());
    }

    IEnumerator Blinken()
    {
        blinkt = true;

        // HDR-Cyan (Wert > 1) -> liegt ueber der Bloom-Schwelle -> glueht neon
        Color glueh = NeonCyan * 6f;
        float ende = Time.time + blinkDauer;
        bool an = false;

        while (Time.time < ende)
        {
            an = !an;
            foreach (var r in eigeneRenderer)
            {
                foreach (var m in r.materials)
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
            }
            yield return new WaitForSeconds(0.15f);
        }

        foreach (var r in eigeneRenderer)
            foreach (var m in r.materials)
                m.SetColor("_EmissionColor", Color.black);

        blinkt = false;
    }
}
