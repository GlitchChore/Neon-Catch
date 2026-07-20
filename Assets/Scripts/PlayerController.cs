using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonCatch
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // Erneut umbenannt (moveSpeed → walkSpeed): erzwingt den neuen,
        // langsameren Standardwert gegen gespeicherte Szenen-Werte.
        public float walkSpeed        = 2.5f;
        public float climbSpeed       = 2.5f;
        public float mouseSensitivity = 0.15f;
        public float gravity          = -20f;
        public float minPitch         = -80f;
        public float maxPitch         = 80f;
        public float interactDistance = 3f;

        // Diese Felder wurden bewusst UMBENANNT (z.B. characterHeight → bodyHeight):
        // Unity speichert Inspector-Werte pro Szene und ignoriert geänderte
        // Code-Standardwerte – durch den neuen Feldnamen verwerfen alle Szenen ihre
        // veralteten Werte und übernehmen die Defaults hier aus dem Code.
        [Header("Figur-Größe (passt durch Tore und Haus-Eingänge)")]
        // Erneut umbenannt (bodyHeight → figureHeight): erzwingt den neuen,
        // höheren Standardwert gegen die in der Szene gespeicherten alten Werte.
        // Höher gesetzt, damit die Kamera beim Durchgehen fast die Tor-Decke
        // berührt statt in Kniehöhe zu schweben.
        public float figureHeight = 0.5f;
        public float bodyRadius = 0.1f;
        public float eyeOffsetBelowTop = 0.04f; // Augen sitzen knapp unter der Kapsel-Oberkante

        [Header("Sprung")]
        public float jumpHeightMeters = 0.36f; // Sprunghöhe in Metern

        [Header("Ducken (Strg halten)")]
        public float duckenHoeheFaktor = 0.6f;   // Kapselhöhe im Ducken, Anteil von figureHeight
        public float duckenTempoFaktor = 0.5f;   // Lauftempo im Ducken

        [Header("Hindernisse")]
        // Klein halten: Unity's CharacterController steigt Hindernisse bis zu dieser Höhe
        // automatisch, ohne Sprung. Bei zu hohem Wert könnte man Zäune, Zinnen und Kisten
        // einfach übersteigen. Treppenstufen sind kleiner als der Wert und bleiben begehbar.
        public float maxStepHeight = 0.05f;

        // Kampfmodus (KampfModus.cs): Maus-Blick OHNE gehaltene Taste
        // (Cursor ist gesperrt) und Linksklick öffnet keine Türen –
        // der gehört dann der Pistole
        public static bool kampfModus;

        // Zusätzliche Kamera-Rollneigung (Z-Achse), z.B. für den Corkscrew-
        // Ausweich-Effekt in PistolenSchuetze – normalerweise 0
        public float extraRoll;

        CharacterController cc;
        Transform           camTransform;
        float               verticalVel;
        float               pitch;

        bool   inLadderZone; // Spieler berührt die Trigger-Zone einer Leiter
        bool   isClimbing;   // Spieler klettert tatsächlich (erst nach Vorwärts-Eingabe)
        Ladder currentLadder;
        bool   duckt;        // Strg gehalten: Kapsel kleiner, Kamera tiefer, langsamer

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.height     = figureHeight;
            cc.radius     = bodyRadius;
            cc.center     = new Vector3(0f, figureHeight / 2f, 0f);
            cc.stepOffset = maxStepHeight;
            // Unity-Standard (45°) ist flacher als die Boeschung des Burggrabens
            // (~53°) - man musste bisher hochspringen, um aus dem Graben zu
            // kommen, und blieb an der Boeschung "haengen" (wirkte wie eingefroren).
            // Erzwingt den neuen Wert gegen gespeicherte Szenen-Werte.
            cc.slopeLimit = 63f;

            Transform named = transform.Find("Spieler_Kamera");
            if (named != null)
                camTransform = named;
            else
            {
                Camera cam = GetComponentInChildren<Camera>();
                if (cam == null) cam = Camera.main;
                if (cam != null) camTransform = cam.transform;
            }

            // Kamera an die (kleinere) Figur anpassen: Augenhöhe knapp unter der
            // Kapsel-Oberkante, unabhängig davon, wo die Kamera ursprünglich saß
            if (camTransform != null)
            {
                Vector3 lp = camTransform.localPosition;
                lp.y = Mathf.Max(0.05f, figureHeight - eyeOffsetBelowTop);
                camTransform.localPosition = lp;

                // Near-Clip-Plane kleiner als der Charakter-Radius halten, sonst "schaut"
                // die Kamera durch Wände, wenn der Spieler dicht davorsteht
                Camera cam = camTransform.GetComponent<Camera>();
                if (cam != null)
                    cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, bodyRadius * 0.5f);
            }
        }

        void Start()
        {
            // Maus bleibt frei sichtbar – Blick nur bei gehaltener linker Taste,
            // Klick auf eine Tür öffnet/schließt sie
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            // Eigene Figur in Erste-Person ausblenden (Kamera steckt sonst im Mesh)
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }

        void Update()
        {
            var kb    = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // Kamera schwenken – normal nur bei gehaltener linker Maustaste,
            // im Kampfmodus dauerhaft frei (Cursor ist dann gesperrt)
            if (camTransform != null && (kampfModus || mouse.leftButton.isPressed))
            {
                Vector2 look = mouse.delta.ReadValue();
                transform.Rotate(0f, look.x * mouseSensitivity, 0f);
                pitch -= look.y * mouseSensitivity;
                pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);
                camTransform.localRotation = Quaternion.Euler(pitch, 0f, extraRoll);
            }

            // Linksklick: Tür in Blickrichtung öffnen/schließen –
            // im Kampfmodus gehört der Klick der Pistole
            if (!kampfModus && mouse.leftButton.wasPressedThisFrame)
                TryInteract();

            float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

            // Ducken (Strg halten): Kapsel kleiner, Kamera tiefer, langsamer.
            // Beim Klettern nicht (würde die Leiter-Höhenlogik durcheinanderbringen).
            bool willDucken = kb.leftCtrlKey.isPressed && !isClimbing;
            if (willDucken != duckt)
            {
                duckt = willDucken;
                AktualisiereDuckHoehe();
            }

            // Klettern beginnt erst, wenn man die Leiter berührt, zu ihr hinschaut UND
            // nach vorne geht – bloßes Berühren reicht nicht, so kann man auf dem
            // Wehrgang seitlich an der Leiter vorbeilaufen.
            if (!isClimbing && inLadderZone && currentLadder != null && v > 0f)
            {
                Vector3 toLadder = currentLadder.transform.position - transform.position;
                toLadder.y = 0f;
                if (toLadder.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(transform.forward, toLadder.normalized) > 0.5f)
                    isClimbing = true;
            }

            // Unten angekommen und S gedrückt: von der Leiter absteigen
            if (isClimbing && cc.isGrounded && v < 0f)
                isClimbing = false;

            if (isClimbing)
                ClimbMove(h, v);
            else
                WalkMove(h, v);

            CheckPlatformHotkeys(kb);
        }

        void WalkMove(float h, float v)
        {
            Vector3 dir = transform.forward * v + transform.right * h;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            var kb = Keyboard.current;
            if (cc.isGrounded)
            {
                verticalVel = -1f;
                if (kb != null && kb.spaceKey.wasPressedThisFrame)
                    verticalVel = Mathf.Sqrt(2f * jumpHeightMeters * -gravity);
            }
            else
            {
                verticalVel += gravity * Time.deltaTime;
            }

            float tempo = duckt ? walkSpeed * duckenTempoFaktor : walkSpeed;
            cc.Move((dir * tempo + Vector3.up * verticalVel) * Time.deltaTime);
        }

        // Kapselhöhe + Kamera-Augenhöhe an den Duck-Zustand anpassen
        void AktualisiereDuckHoehe()
        {
            float h = duckt ? figureHeight * duckenHoeheFaktor : figureHeight;
            cc.height = h;
            cc.center = new Vector3(0f, h * 0.5f, 0f);

            if (camTransform != null)
            {
                Vector3 lp = camTransform.localPosition;
                lp.y = Mathf.Max(0.05f, h - eyeOffsetBelowTop);
                camTransform.localPosition = lp;
            }
        }

        void ClimbMove(float h, float v)
        {
            // Auf der Leiter: W/S klettert hoch/runter, keine Schwerkraft.
            // Leichtes seitliches Nachsteuern bleibt möglich (z.B. um oben abzusteigen).
            verticalVel = 0f;
            Vector3 move = Vector3.up * v * climbSpeed + transform.right * h * walkSpeed * 0.3f;
            cc.Move(move * Time.deltaTime);
        }

        void TryInteract()
        {
            if (camTransform == null) return;
            if (Physics.Raycast(camTransform.position, camTransform.forward, out RaycastHit hit, interactDistance))
            {
                Door door = hit.collider.GetComponent<Door>();
                if (door != null) door.Toggle();
            }
        }

        void OnTriggerEnter(Collider other)
        {
            Ladder l = other.GetComponent<Ladder>();
            if (l != null)
            {
                inLadderZone  = true;
                currentLadder = l;
            }
        }

        void OnTriggerExit(Collider other)
        {
            Ladder l = other.GetComponent<Ladder>();
            if (l != null && l == currentLadder)
            {
                inLadderZone  = false;
                isClimbing    = false;
                currentLadder = null;
            }
        }

        void CheckPlatformHotkeys(Keyboard kb)
        {
            int index = -1;
            if      (kb.digit1Key.wasPressedThisFrame) index = 1;
            else if (kb.digit2Key.wasPressedThisFrame) index = 2;
            else if (kb.digit3Key.wasPressedThisFrame) index = 3;
            else if (kb.digit4Key.wasPressedThisFrame) index = 4;
            else if (kb.digit5Key.wasPressedThisFrame) index = 5;
            else if (kb.digit6Key.wasPressedThisFrame) index = 6;
            else if (kb.digit7Key.wasPressedThisFrame) index = 7;
            else if (kb.digit8Key.wasPressedThisFrame) index = 8;
            else if (kb.digit9Key.wasPressedThisFrame) index = 9;
            else if (kb.digit0Key.wasPressedThisFrame) index = 10;

            if (index == -1) return;

            GameObject platform = GameObject.Find("Flaeche_" + index);
            if (platform == null) return;

            cc.enabled = false;
            transform.position = platform.transform.position + Vector3.up * 0.1f;
            cc.enabled = true;
            verticalVel = 0f;
        }
    }
}
