using UnityEngine;

namespace NeonCatch
{
    // Kanonenkugel: fliegt per Rigidbody-Physik, der Spieler "sitzt" mit der
    // Kamera darin (siehe PlayerCannonController.SteigeInKugelEin). Bleibt sie
    // liegen (kaum noch Bewegung), wird der Spieler an dieser Stelle wieder
    // hergestellt und die Kugel verschwindet.
    //
    // Einrichtung: auf das Kugel-Prefab. Braucht einen Rigidbody (wird beim
    // Abschuss automatisch ergänzt, falls keiner dran ist) und einen Collider
    // (Sphere Collider passt am besten).
    public class Cannonball : MonoBehaviour
    {
        public float landeGeschwindigkeit = 0.3f;   // darunter gilt sie als "liegengeblieben"
        public float landeWartezeit = 0.5f;         // so lange muss sie ruhig liegen
        public float maxFlugZeit = 15f;              // Sicherheitsnetz gegen ewiges Rollen/Fallen

        Rigidbody rb;
        PlayerCannonController spieler;
        float ruheZeit;
        float flugZeit;
        bool gelandet;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        }

        public void SetzeSpieler(PlayerCannonController player)
        {
            spieler = player;
        }

        void Update()
        {
            if (gelandet) return;

            flugZeit += Time.deltaTime;

            if (rb.linearVelocity.magnitude < landeGeschwindigkeit)
            {
                ruheZeit += Time.deltaTime;
                if (ruheZeit >= landeWartezeit || flugZeit >= maxFlugZeit)
                    Lande();
            }
            else
            {
                ruheZeit = 0f;
            }
        }

        void Lande()
        {
            gelandet = true;

            // Rauchwolke an der Aufschlagstelle – die Kugel "verschwindet im Rauch"
            CannonStation.SpawneRauchwolke(transform.position);

            if (spieler != null)
                spieler.LandeUndKehreZurueck(transform.position);
            Destroy(gameObject);
        }
    }
}
