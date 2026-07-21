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
    public float sprungHoehe = 0.6f;

    CharacterController controller;
    SelfPaintSystem paint;
    Camera eigeneKamera;
    static GameObject soloSpieler;
    float yaw, pitch, vertikal;
    bool imWasser;

    void OnTriggerEnter(Collider o) { if (IstWasser(o)) imWasser = true; }
    void OnTriggerExit(Collider o) { if (IstWasser(o)) imWasser = false; }
    static bool IstWasser(Collider o) => o != null && o.isTrigger && o.name.Contains("Wasser");

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
        // Bots: passiv verstecken - AUSSER der Bot ist der SUCHER, dann jagt
        // er in der Suchphase die Versteckten (laeuft auf dem MasterClient,
        // dort ist das Raum-Objekt "IsMine")
        if (paint != null && paint.istBot)
        {
            if (photonView.IsMine) BotSucherKI();
            return;
        }
        if (!photonView.IsMine)
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
        else if (imWasser)
        {
            // Eingefroren/Menue im Wasser: sanft auftreiben statt versinken
            controller.Move(Vector3.up * 1f * Time.deltaTime);
        }
        else
        {
            // Nur Schwerkraft, damit man nicht schwebt
            vertikal = controller.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
            controller.Move(Vector3.up * vertikal * Time.deltaTime);
        }
    }

    // Bot als SUCHER: laeuft in der Suchphase auf den naechsten noch nicht
    // gefundenen Versteckten zu - gefangen wird automatisch ueber den
    // 2-Meter-Radius (GamePhaseManager.PruefeFangen).
    void BotSucherKI()
    {
        var pm = GamePhaseManager.Instance;
        if (pm == null || pm.phase != SpielPhase.Suchen || !pm.IstSucher(photonView.ViewID))
            return;

        SelfPaintSystem ziel = null;
        float beste = float.MaxValue;
        foreach (var s in Object.FindObjectsByType<SelfPaintSystem>(FindObjectsSortMode.None))
        {
            if (s.gefunden || s.photonView.ViewID == photonView.ViewID) continue;
            float d = Vector3.Distance(transform.position, s.transform.position);
            if (d < beste) { beste = d; ziel = s; }
        }
        if (ziel == null) return;

        Vector3 richtung = ziel.transform.position - transform.position;
        richtung.y = 0f;
        if (richtung.sqrMagnitude > 0.01f)
        {
            richtung.Normalize();
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(richtung), Time.deltaTime * 4f);
        }
        vertikal = controller.isGrounded ? -1f : vertikal - 20f * Time.deltaTime;
        controller.Move((richtung * (tempo * 0.8f) + Vector3.up * vertikal) * Time.deltaTime);
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
        Vector3 horizontal = (transform.right * x + transform.forward * z).normalized * tempo;

        if (imWasser)
        {
            // Schwimmen: Leertaste hoch, Strg runter, sanfter Auftrieb -
            // so kommt man auch wieder aus dem Burggraben heraus
            float auf = (kb.spaceKey.isPressed ? 1f : 0f) - (kb.leftCtrlKey.isPressed ? 1f : 0f);
            vertikal = 0f;
            controller.Move((horizontal + Vector3.up * (auf * tempo + 1f)) * Time.deltaTime);
            return;
        }

        // Springen (Leertaste) am Boden
        if (controller.isGrounded)
        {
            vertikal = -1f;
            if (kb.spaceKey.wasPressedThisFrame)
                vertikal = Mathf.Sqrt(2f * sprungHoehe * 20f);
        }
        else vertikal -= 20f * Time.deltaTime;

        controller.Move((horizontal + Vector3.up * vertikal) * Time.deltaTime);
    }
}
