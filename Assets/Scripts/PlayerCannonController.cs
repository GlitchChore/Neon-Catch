using UnityEngine;

namespace NeonCatch
{
    // Steuert den Wechsel zwischen normaler Steuerung, Sitzen in einer Kanone
    // (Zielen) und Mitfliegen in der abgeschossenen Kugel. Wird automatisch von
    // CannonStation an den Spieler gehängt, sobald er zum ersten Mal eine
    // Kanone anklickt – keine manuelle Einrichtung nötig.
    //
    // Der Spieler hat kein eigenes Rigidbody/Modell (Erste-Person, CharacterController,
    // Renderer sind laut PlayerController schon dauerhaft unsichtbar) – deshalb wird
    // hier nur die Steuerung an-/abgeschaltet und die eine vorhandene Kamera
    // zwischen Player, Kanone und Kugel hin- und hergehängt.
    public class PlayerCannonController : MonoBehaviour
    {
        CharacterController cc;
        PlayerController controller;
        Transform camTransform;
        Transform camHeimatEltern;
        Vector3 camHeimatPosition;
        Quaternion camHeimatRotation;
        bool camHeimatGemerkt;

        CannonStation aktuelleKanone;
        Cannonball aktuelleKugel;

        public bool IstBeschaeftigt { get { return aktuelleKanone != null || aktuelleKugel != null; } }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            controller = GetComponent<PlayerController>();

            Camera cam = GetComponentInChildren<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam != null) camTransform = cam.transform;
        }

        public void SteigeInKanoneEin(CannonStation kanone)
        {
            aktuelleKanone = kanone;

            if (controller != null) controller.enabled = false;
            if (cc != null) cc.enabled = false;

            KameraVerlegen(kanone.cameraMount != null ? kanone.cameraMount : kanone.pivotVertical);
        }

        // Zielen wurde abgebrochen (Escape) – Spieler bleibt an Ort und Stelle,
        // bekommt seine normale Steuerung zurück
        public void VerlasseKanoneOhneSchuss()
        {
            aktuelleKanone = null;
            KameraZurueckHolen();

            if (cc != null) cc.enabled = true;
            if (controller != null) controller.enabled = true;
        }

        public void SteigeInKugelEin(Cannonball kugel)
        {
            aktuelleKanone = null;
            aktuelleKugel = kugel;

            // WICHTIG: ohne das bleibt Cannonball.spieler null – beim Landen
            // würde LandeUndKehreZurueck() dann NIE aufgerufen, die Steuerung
            // bliebe für immer deaktiviert (genau das hat "man kann sich
            // nicht mehr bewegen" nach dem ersten Kanonenschuss verursacht)
            kugel.SetzeSpieler(this);

            KameraVerlegen(kugel.transform);
        }

        // Wird von Cannonball aufgerufen, sobald sie gelandet ist und liegen bleibt
        public void LandeUndKehreZurueck(Vector3 landePosition)
        {
            aktuelleKugel = null;
            KameraZurueckHolen();

            if (cc != null)
            {
                // Gleiches Muster wie beim Schwimmen/Kanonenflug im übrigen Projekt:
                // CharacterController kurz abschalten, um die Position zu erzwingen
                cc.enabled = false;
                transform.position = FreierLandeplatz(landePosition) + Vector3.up * 0.1f;
                cc.enabled = true;
            }
            if (controller != null) controller.enabled = true;
        }

        // Die Kugel kann direkt AN einer Wand liegen bleiben – würde der
        // Spieler exakt dort abgesetzt, steckt seine Kapsel in der Wand und
        // er rutscht hindurch. Deshalb: notfalls einen freien Platz in der
        // Nähe suchen (in wachsenden Ringen um die Landestelle).
        Vector3 FreierLandeplatz(Vector3 wunsch)
        {
            if (PlatzIstFrei(wunsch)) return wunsch;

            for (float radius = 0.4f; radius <= 2.4f; radius += 0.4f)
            {
                for (int i = 0; i < 8; i++)
                {
                    float w = i / 8f * Mathf.PI * 2f;
                    Vector3 kandidat = wunsch + new Vector3(Mathf.Cos(w), 0f, Mathf.Sin(w)) * radius;
                    kandidat.y = BurggrabenMittelalter.BodenHoehe(kandidat);
                    if (PlatzIstFrei(kandidat)) return kandidat;
                }
            }
            return wunsch;   // nichts Besseres gefunden
        }

        static bool PlatzIstFrei(Vector3 pos)
        {
            // Spieler-Kapsel (0.5 m hoch, 0.1 m Radius) mit etwas Luft prüfen
            return !Physics.CheckCapsule(pos + Vector3.up * 0.15f, pos + Vector3.up * 0.45f,
                0.12f, ~(1 << 4), QueryTriggerInteraction.Ignore);
        }

        void KameraVerlegen(Transform ziel)
        {
            if (camTransform == null || ziel == null) return;

            // Beim allerersten Wechsel merken, wo die Kamera eigentlich hingehört
            if (!camHeimatGemerkt)
            {
                camHeimatGemerkt  = true;
                camHeimatEltern   = camTransform.parent;
                camHeimatPosition = camTransform.localPosition;
                camHeimatRotation = camTransform.localRotation;
            }

            camTransform.SetParent(ziel, false);
            camTransform.localPosition = Vector3.zero;
            camTransform.localRotation = Quaternion.identity;
        }

        void KameraZurueckHolen()
        {
            if (camTransform == null || camHeimatEltern == null) return;
            camTransform.SetParent(camHeimatEltern, false);
            camTransform.localPosition = camHeimatPosition;
            camTransform.localRotation = camHeimatRotation;
        }
    }
}
