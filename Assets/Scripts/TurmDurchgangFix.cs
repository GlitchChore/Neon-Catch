using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonCatch
{
    // ======================================================================
    // Oeffnet die Turm-Durchgaenge der gebauten Burg zur Laufzeit:
    // Turm-Basen sind "wall-fortified"-Segmente MIT einer "structure-wall"-
    // Kappe direkt darueber. Der aktuelle BurgBuilder baut solche Basen ohne
    // Kollision ("man soll unter den Tuermen hindurchgehen koennen") - die
    // aelter gebaute, in der Szene eingebackene Burg hat dort aber noch
    // Collider, deshalb kam man nicht durch den Turm in der Mauermitte.
    //
    // WICHTIG: Normale Mauerstuecke heissen GENAUSO (wall-fortified), haben
    // aber KEINE Kappe ueber sich - die bleiben solide. Gate- und Half-
    // Segmente werden ausdruecklich uebersprungen.
    // ======================================================================
    public static class TurmDurchgangFix
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            SceneManager.sceneLoaded += (szene, modus) => Fixe();
            Fixe();
        }

        static void Fixe()
        {
            var kappen = new List<Transform>();
            var basen = new List<Transform>();

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                string n = t.name;
                if (n.StartsWith("structure-wall"))
                    kappen.Add(t);
                else if (n.StartsWith("wall-fortified") && !n.Contains("gate") && !n.Contains("half"))
                    basen.Add(t);
            }

            int geoeffnet = 0;
            foreach (var kappe in kappen)
            {
                foreach (var basis in basen)
                {
                    Vector3 d = kappe.position - basis.position;
                    float dy = d.y;
                    d.y = 0f;
                    // Kappe muss (fast) exakt UEBER der Basis sitzen - der Builder
                    // platziert sie auf identischem XZ, Nachbar-Wandstuecke sind
                    // mindestens eine halbe Kachel entfernt
                    if (d.sqrMagnitude > 0.5f * 0.5f || dy < 0.3f)
                        continue;

                    foreach (var c in basis.GetComponentsInChildren<Collider>())
                        c.enabled = false;
                    geoeffnet++;
                    break;
                }
            }

            if (geoeffnet > 0)
                Debug.Log("TurmDurchgangFix: " + geoeffnet + " Turm-Durchgaenge geoeffnet.");
        }
    }
}
