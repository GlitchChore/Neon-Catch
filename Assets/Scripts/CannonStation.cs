using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace NeonCatch
{
    // Stationäre Kanone zum Einsteigen: Linksklick auf die Kanone (OnMouseDown –
    // braucht einen normalen, nicht-Trigger Collider) setzt den Spieler hinein.
    // Danach steuert die Maus die Drehung (horizontal) und Neigung (vertikal)
    // des Rohrs, ein weiterer Linksklick feuert. Escape bricht das Zielen ab,
    // ohne zu schießen.
    //
    // Aufbau (wird von KI_SzeneAufbau automatisch erzeugt, siehe dort):
    //  root (dieses Script + klickbarer BoxCollider, feste Blickrichtung)
    //   └─ pivotHorizontal (dreht sich links/rechts – begrenzt durch minYaw/maxYaw)
    //       └─ pivotVertical (dreht sich hoch/runter – begrenzt durch minPitch/maxPitch)
    //           ├─ sichtbares Kanonen-Modell
    //           ├─ muzzle (Mündung – hier entsteht die Kugel, +Z = Schussrichtung)
    //           └─ cameraMount (wohin die Spielkamera beim Zielen wandert)
    public class CannonStation : MonoBehaviour
    {
        [Header("Drehteile (werden beim automatischen Aufbau gesetzt)")]
        public Transform pivotHorizontal;
        public Transform pivotVertical;
        public Transform muzzle;
        public Transform cameraMount;

        [Header("Zielen")]
        public float mouseSensitivity = 2f;
        public float minPitch = -8f;
        public float maxPitch = 55f;
        // Relativ zur festen Blickrichtung der Kanone (transform.rotation).
        // Bei den 2 Landschafts-Kanonen auf ±45° begrenzt, damit man nicht aus
        // der Map schießen kann; bei den 2 Burghof-Kanonen praktisch frei.
        public float minYaw = -170f;
        public float maxYaw = 170f;

        [Header("Schuss")]
        public GameObject cannonballPrefab;
        public float shootPower = 22f;
        public float cooldown = 1f;

        float yaw, pitch;
        bool besetzt;
        bool kannSchiessen = true;
        PlayerCannonController spieler;

        void Awake()
        {
            // Absicherung, falls das Script mal ohne automatischen Aufbau
            // manuell auf ein Objekt gezogen wird
            if (pivotHorizontal == null)
            {
                pivotHorizontal = new GameObject("PivotHorizontal").transform;
                pivotHorizontal.SetParent(transform, false);
            }
            if (pivotVertical == null)
            {
                pivotVertical = new GameObject("PivotVertical").transform;
                pivotVertical.SetParent(pivotHorizontal, false);
            }
            if (muzzle == null) muzzle = pivotVertical;
        }

        // Von Unity automatisch aufgerufen, wenn der Spieler auf einen Collider
        // dieses Objekts klickt (Linksklick, "Both"/"Old" Active Input Handling)
        void OnMouseDown()
        {
            if (besetzt) return;

            GameObject spielerObj = GameObject.FindGameObjectWithTag("Player");
            if (spielerObj == null) return;

            var controller = spielerObj.GetComponent<PlayerCannonController>();
            if (controller == null) controller = spielerObj.AddComponent<PlayerCannonController>();
            if (controller.IstBeschaeftigt) return;   // sitzt schon in einer anderen Kanone/Kugel

            spieler = controller;
            besetzt = true;
            kannSchiessen = true;
            yaw = 0f;
            pitch = 0f;
            pivotHorizontal.localRotation = Quaternion.identity;
            pivotVertical.localRotation = Quaternion.identity;

            controller.SteigeInKanoneEin(this);
        }

        void Update()
        {
            if (!besetzt) return;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                VerlasseOhneSchuss();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue();
            yaw   = Mathf.Clamp(yaw + delta.x * mouseSensitivity * 0.05f, minYaw, maxYaw);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity * 0.05f, minPitch, maxPitch);

            pivotHorizontal.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pivotVertical.localRotation   = Quaternion.Euler(pitch, 0f, 0f);

            if (mouse.leftButton.wasPressedThisFrame && kannSchiessen)
                Schiessen();
        }

        void Schiessen()
        {
            if (cannonballPrefab == null || muzzle == null || spieler == null)
            {
                Debug.LogWarning("CannonStation: Kugel-Prefab, Mündung oder Spieler fehlt – kein Schuss.");
                return;
            }
            kannSchiessen = false;

            // Rauchwolke an der Mündung (Legacy-Partikel-Pack)
            SpawneRauchwolke(muzzle.position);

            GameObject kugel = Instantiate(cannonballPrefab, muzzle.position, muzzle.rotation);
            Rigidbody rb = kugel.GetComponent<Rigidbody>();
            if (rb == null) rb = kugel.AddComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.AddForce(muzzle.forward * shootPower, ForceMode.Impulse);

            Cannonball ball = kugel.GetComponent<Cannonball>();
            if (ball == null) ball = kugel.AddComponent<Cannonball>();

            spieler.SteigeInKugelEin(ball);
            besetzt = false;
            spieler = null;

            StartCoroutine(CooldownZuruecksetzen());
        }

        // Zielen abbrechen (Escape) – Spieler bleibt an Ort und Stelle stehen
        void VerlasseOhneSchuss()
        {
            besetzt = false;
            if (spieler != null) spieler.VerlasseKanoneOhneSchuss();
            spieler = null;
        }

        IEnumerator CooldownZuruecksetzen()
        {
            yield return new WaitForSeconds(cooldown);
            kannSchiessen = true;
        }

        // ------------------------------------------------------------------
        // Rauchwolke aus dem Partikel-Pack (Resources/KI/Rauchwolke) – wird
        // beim Abschuss (Mündung) UND bei der Landung der Kugel gerufen
        // ------------------------------------------------------------------
        static GameObject rauchPrefab;
        static bool rauchGesucht;

        public static void SpawneRauchwolke(Vector3 position)
        {
            if (!rauchGesucht)
            {
                rauchGesucht = true;
                rauchPrefab = Resources.Load<GameObject>("KI/Rauchwolke");
                if (rauchPrefab == null)
                    Debug.LogWarning("CannonStation: 'Resources/KI/Rauchwolke' fehlt – keine Rauchwolke.");
            }
            if (rauchPrefab == null) return;

            GameObject rauch = Instantiate(rauchPrefab, position, Quaternion.identity);
            rauch.name = "Rauchwolke";
            // Pack-Effekt ist für normalgroße Welten gedacht – verkleinern
            rauch.transform.localScale *= 0.35f;
            Destroy(rauch, 5f);
        }
    }
}
