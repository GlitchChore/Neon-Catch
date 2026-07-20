using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// FARBMIMIK-Spielersteuerung - gleiche Ego-Steuerung wie NEON BLASTER
/// (Maus schauen + WASD laufen), nur OHNE Pistole.
///
/// Freeze-Regeln:
///  - Waehrend VERSTECKEN kann sich der SUCHER nicht bewegen (er sieht einen
///    schwarzen Bildschirm mit Countdown, gezeichnet von LobbyUI).
///  - Waehrend SUCHEN sind die VERSTECKTEN eingefroren (koennen sich nicht
///    mehr bewegen), koennen aber weiter malen (SelfPaintSystem).
///  - Sonst laeuft man frei.
///
/// Kein Aufleuchten-beim-Bewegen mehr. Gefangen wird ueber Beruehrung im
/// 2-Meter-Radius (GamePhaseManager.PruefeFangen).
/// Gehoert auf das Spieler-Prefab (mit PhotonView + PhotonTransformView).
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PhotonView))]
public class FreezePenalty : MonoBehaviourPun
{
    [Header("Bewegung")]
    public float tempo = 3.5f;
    public float mausEmpfindlichkeit = 0.12f;

    CharacterController controller;
    SelfPaintSystem paint;
    Camera eigeneKamera;
    static GameObject soloSpieler;
    float yaw, pitch, vertikal;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        paint = GetComponent<SelfPaintSystem>();
    }

    void Start()
    {
        if (photonView.IsMine && (paint == null || !paint.istBot))
            LokalStart();
    }

    void LokalStart()
    {
        yaw = transform.eulerAngles.y;

        // NEON-BLASTER-Solo-Figur (falls in der Szene) schlafen legen
        GameObject solo = GameObject.FindGameObjectWithTag("Player");
        if (solo != null && solo != gameObject) { soloSpieler = solo; solo.SetActive(false); }

        // Eigene Ego-Kamera auf Augenhoehe (wie NEON BLASTER)
        var kamGO = new GameObject("FarbmimikKamera") { tag = "MainCamera" };
        kamGO.transform.SetParent(transform, false);
        kamGO.transform.localPosition = new Vector3(0f, 0.46f, 0f);
        eigeneKamera = kamGO.AddComponent<Camera>();
        if (Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
            kamGO.AddComponent<AudioListener>();

        // Eigenen Koerper ausblenden (Ego-Perspektive) - Mitspieler sehen ihn weiter
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
    }

    void OnDestroy()
    {
        if (photonView.IsMine && (paint == null || !paint.istBot) && soloSpieler != null)
        {
            soloSpieler.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Update()
    {
        if (!photonView.IsMine || (paint != null && paint.istBot))
            return;

        var phasen = GamePhaseManager.Instance;
        SpielPhase ph = phasen != null ? phasen.phase : SpielPhase.Lobby;
        bool binSucher = phasen != null && phasen.IstSucher(photonView.ViewID);

        // Eingefroren: Sucher waehrend Verstecken, Versteckte waehrend Suchen
        bool eingefroren = (ph == SpielPhase.Verstecken && binSucher)
                        || (ph == SpielPhase.Suchen && !binSucher);
        bool imSpiel = ph == SpielPhase.Verstecken || ph == SpielPhase.Suchen;
        bool malenOffen = SelfPaintSystem.MalUiOffen;

        // Aktiv steuern nur wenn: im Spiel, nicht eingefroren, Mal-UI zu
        bool aktiv = imSpiel && !eingefroren && !malenOffen;

        // Cursor: beim aktiven Spielen gesperrt (Maus-Blick), sonst frei (Menue/Malen)
        var sollLock = aktiv ? CursorLockMode.Locked : CursorLockMode.None;
        if (Cursor.lockState != sollLock)
        {
            Cursor.lockState = sollLock;
            Cursor.visible = !aktiv;
        }

        if (aktiv)
        {
            Umschauen();
            Laufen();
        }
        else
        {
            // Nur Schwerkraft, damit man nicht schwebt
            vertikal = controller.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
            controller.Move(Vector3.up * vertikal * Time.deltaTime);
        }
    }

    void Umschauen()
    {
        var maus = Mouse.current;
        if (maus == null || eigeneKamera == null) return;
        Vector2 d = maus.delta.ReadValue();
        yaw += d.x * mausEmpfindlichkeit;
        pitch = Mathf.Clamp(pitch - d.y * mausEmpfindlichkeit, -80f, 80f);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        eigeneKamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Laufen()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Vector3 richtung = (transform.right * x + transform.forward * z).normalized * tempo;
        vertikal = controller.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
        controller.Move((richtung + Vector3.up * vertikal) * Time.deltaTime);
    }
}
