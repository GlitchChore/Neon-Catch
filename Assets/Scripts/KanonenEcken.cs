using UnityEngine;
using System.Collections;

namespace NeonCatch
{
    // Auf Nutzerwunsch: KEINE Kanonen mehr im Spiel. Baut nichts mehr –
    // räumt beim Szenenstart nur noch übrig gebliebene Kanonen aus alten
    // gespeicherten Szenen weg (z.B. das Szenen-Objekt "Burgkanone" oder
    // vorher gebaute Eck-/Hof-/Landschafts-Kanonen).
    public static class KanonenEcken
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            var go = new GameObject("Kanonen_Ecken_Aufbau");
            go.AddComponent<KanonenEckenAufbau>();
        }
    }

    public class KanonenEckenAufbau : MonoBehaviour
    {
        IEnumerator Start()
        {
            // 2 Frames warten: erst sollen Terrain-Erkennung und die übrigen
            // Aufbau-Scripts (Start-Methoden) durchgelaufen sein
            yield return null;
            yield return null;
            RaeumeAlteKanonenWeg();
            Destroy(gameObject);
        }

        void RaeumeAlteKanonenWeg()
        {
            foreach (CannonStation alt in FindObjectsByType<CannonStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Destroy(alt.gameObject);

            GameObject burgkanone = GameObject.Find("Burgkanone");
            if (burgkanone != null) Destroy(burgkanone);
        }
    }
}
