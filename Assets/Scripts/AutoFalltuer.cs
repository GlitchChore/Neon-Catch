using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace NeonCatch
{
    // Teleport-Hütte: Der Spieler GEHT durch die offene Tür HINEIN →
    // Bildschirm wird ~1 Sekunde schwarz → er taucht vor einer ZUFÄLLIGEN
    // anderen Hütte wieder auf. Danach sind alle Hütten für sperrZeit
    // Sekunden gesperrt – der Countdown ist oben am Bildschirm sichtbar.
    //
    // Wird vom FalltuerSpawner an jede Hütte gehängt und über einen
    // Trigger-Collider im Inneren der Hütte ausgelöst.
    public class AutoFalltuer : MonoBehaviour
    {
        [Header("Ziel")]
        public Transform ausgang;             // wo Ankommende auftauchen (vor der Tür)

        [Header("Schwarzblende")]
        public float ausblendZeit = 0.15f;
        public float schwarzZeit = 0.7f;      // reine Schwarz-Haltezeit (Teleport passiert hier)
        public float einblendZeit = 0.15f;    // ausblendZeit + schwarzZeit + einblendZeit ≈ 1 Sekunde

        [Header("Effekte (optional)")]
        public AudioClip reinSound;
        public AudioClip rausSound;
        public ParticleSystem portalEffekt;

        [Header("Sperre nach jeder Reise (Countdown ist am Bildschirm sichtbar)")]
        public float sperrZeit = 15f;

        static readonly List<AutoFalltuer> alleTueren = new List<AutoFalltuer>();
        static bool unterwegs;   // verhindert, dass während einer Reise eine zweite startet

        void OnEnable()  { alleTueren.Add(this); }
        void OnDisable() { alleTueren.Remove(this); }

        // Wird von Unity aufgerufen, wenn etwas den Trigger im Hütten-Inneren
        // betritt – reagiert nur auf den Spieler
        void OnTriggerEnter(Collider other)
        {
            if (unterwegs) return;
            if (FalltuerTimer.Gesperrt) return;   // Countdown läuft noch (oben sichtbar)
            if (!other.CompareTag("Player")) return;

            AutoFalltuer ziel = ZufaelligeAndereTuer();
            if (ziel == null) return;   // es gibt keine andere Hütte

            StartCoroutine(ReiseZu(other.gameObject, ziel));
        }

        AutoFalltuer ZufaelligeAndereTuer()
        {
            var kandidaten = new List<AutoFalltuer>();
            foreach (AutoFalltuer tuer in alleTueren)
                if (tuer != this) kandidaten.Add(tuer);
            if (kandidaten.Count == 0) return null;
            return kandidaten[Random.Range(0, kandidaten.Count)];
        }

        IEnumerator ReiseZu(GameObject spieler, AutoFalltuer ziel)
        {
            unterwegs = true;

            var controller = spieler.GetComponent<PlayerController>();
            var cc = spieler.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            if (cc != null) cc.enabled = false;

            if (reinSound != null) AudioSource.PlayClipAtPoint(reinSound, transform.position);
            if (portalEffekt != null) portalEffekt.Play();

            BildschirmBlende blende = BildschirmBlende.Hole();
            yield return blende.Ausblenden(ausblendZeit);

            // Während der Bildschirm komplett schwarz ist, ist der Spieler
            // "weg": Position wechselt hier, unsichtbar für den Spieler.
            // Ankunft VOR der Ziel-Hütte (ausgang zeigt vor deren Tür).
            Vector3 zielPos = ziel.ausgang != null
                ? ziel.ausgang.position
                : ziel.transform.position + ziel.transform.forward * 1.8f + Vector3.up * 0.5f;

            if (cc != null) cc.enabled = false;   // bleibt aus, Position wird direkt gesetzt
            spieler.transform.position = zielPos + Vector3.up * 0.05f;

            // Spieler UMDREHEN: Blick von der Ziel-Hütte weg – als wäre man
            // hineingegangen und käme aus der anderen Hütte herausspaziert
            Vector3 blick = ziel.transform.forward;
            blick.y = 0f;
            if (blick.sqrMagnitude > 0.001f)
                spieler.transform.rotation = Quaternion.LookRotation(blick.normalized);

            if (rausSound != null) AudioSource.PlayClipAtPoint(rausSound, zielPos);
            if (ziel.portalEffekt != null) ziel.portalEffekt.Play();

            yield return new WaitForSeconds(schwarzZeit);
            yield return blende.Einblenden(einblendZeit);

            if (cc != null) cc.enabled = true;
            if (controller != null) controller.enabled = true;

            // Ab jetzt 15 Sekunden Hütten-Sperre – der Countdown ist als
            // Anzeige oben am Bildschirm sichtbar
            FalltuerTimer.Starte(sperrZeit);

            unterwegs = false;
        }
    }

    // ======================================================================
    // Sichtbarer Teleport-Countdown: nach jeder Reise zeigt ein Kasten oben
    // mittig "Hütte bereit in X s" an, bis die Sperre abgelaufen ist.
    // Einziges Objekt für das ganze Spiel, entsteht beim ersten Gebrauch.
    // ======================================================================
    public class FalltuerTimer : MonoBehaviour
    {
        static FalltuerTimer instanz;
        float bereitAb;
        GUIStyle stil;

        public static void Starte(float dauer)
        {
            Hole().bereitAb = Time.time + dauer;
        }

        // Für den Reset-Knopf: Sperre sofort aufheben
        public static void Zuruecksetzen()
        {
            if (instanz != null) instanz.bereitAb = 0f;
        }

        public static bool Gesperrt
        {
            get { return instanz != null && Time.time < instanz.bereitAb; }
        }

        static FalltuerTimer Hole()
        {
            if (instanz == null)
            {
                var go = new GameObject("Falltuer_Timer");
                DontDestroyOnLoad(go);
                instanz = go.AddComponent<FalltuerTimer>();
            }
            return instanz;
        }

        void OnGUI()
        {
            float rest = bereitAb - Time.time;
            if (rest <= 0f) return;

            // KEIN Kästchen mehr – Text mit Schatten ganz oben am Rand,
            // dadurch passt jede Textlänge
            if (stil == null)
            {
                stil = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontStyle = FontStyle.Bold,
                };
            }
            stil.fontSize = Mathf.RoundToInt(Screen.height * 0.032f);

            string text = "Hütte bereit in " + Mathf.CeilToInt(rest) + " s";
            var rect = new Rect(0f, 4f, Screen.width, Screen.height * 0.06f);

            stil.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, stil);
            stil.normal.textColor = Color.white;
            GUI.Label(rect, text, stil);
        }
    }
}
