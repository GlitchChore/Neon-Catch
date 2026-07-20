using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonCatch
{
    // Verbessert alle Leitern in der Szene zur Laufzeit (ohne die Burg neu zu
    // bauen): die Trigger-Zone wird
    //   - SYMMETRISCH (beide Seiten) -> man kann von vorne UND hinten klettern
    //   - HOEHER (reicht ueber den oberen Boden) -> beim Hochklettern landet man
    //     leichter auf dem naechsten Stockwerk statt an der Kante haengen zu bleiben
    //   - etwas BREITER -> man findet die Leiter leichter
    // Das Klettern selbst startet weiterhin nur, wenn man zur Leiter schaut und
    // W drueckt (PlayerController) - versehentliches Klettern bleibt also aus.
    public static class LeiterFixStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            SceneManager.sceneLoaded += (szene, modus) => Fixe();
            Fixe();
        }

        static void Fixe()
        {
            foreach (var leiter in Object.FindObjectsByType<Ladder>(FindObjectsSortMode.None))
            {
                var box = leiter.GetComponent<BoxCollider>();
                if (box == null || !box.isTrigger) continue;

                Vector3 c = box.center, s = box.size;
                float unten = c.y - s.y * 0.5f;
                float oben  = c.y + s.y * 0.5f + 1.6f;   // 1.6 m hoeher = leichter auf den oberen Boden

                box.center = new Vector3(c.x, (unten + oben) * 0.5f, 0f);   // z = 0 -> beide Seiten
                box.size   = new Vector3(Mathf.Max(s.x * 1.6f, 1.0f),
                                         oben - unten,
                                         Mathf.Max(s.z * 2f, 1.6f));
            }
        }
    }
}
