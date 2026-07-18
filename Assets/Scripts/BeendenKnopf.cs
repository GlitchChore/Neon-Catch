using Mirror;
using UnityEngine;

namespace NeonCatch
{
    // ======================================================================
    // Roter "Spiel beenden"-Knopf oben rechts - startet sich in jeder Szene
    // selbst und ueberlebt Szenenwechsel (DontDestroyOnLoad).
    // Erster Klick fragt nach ("Wirklich beenden?"), zweiter Klick schliesst
    // das Spiel komplett. Laufende Online-Runden werden vorher sauber
    // getrennt, damit Mitspieler nicht haengen bleiben.
    // ======================================================================
    public static class BeendenKnopfStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Object.FindAnyObjectByType<BeendenKnopf>() != null)
                return;
            var go = new GameObject("Beenden_Knopf");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<BeendenKnopf>();
        }
    }

    public class BeendenKnopf : MonoBehaviour
    {
        GUIStyle stil;
        bool bestaetigen;
        float bestaetigenBis;

        void OnGUI()
        {
            if (stil == null)
                stil = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };

            float sw = Screen.width, sh = Screen.height;
            stil.fontSize = Mathf.RoundToInt(sh * 0.02f);
            GUI.depth = -100;   // immer ueber allen anderen Anzeigen

            // Sicherheitsabfrage laeuft nach 3 Sekunden wieder ab
            if (bestaetigen && Time.unscaledTime > bestaetigenBis)
                bestaetigen = false;

            string text = bestaetigen ? "Wirklich beenden?" : "Spiel beenden  X";
            Color alt = GUI.backgroundColor;
            GUI.backgroundColor = bestaetigen ? new Color(1f, 0.25f, 0.2f) : new Color(0.7f, 0.12f, 0.12f);

            float breite = Mathf.Max(sw * 0.11f, 150f);
            if (GUI.Button(new Rect(sw - breite - 10f, 10f, breite, sh * 0.05f), text, stil))
            {
                if (!bestaetigen)
                {
                    bestaetigen = true;
                    bestaetigenBis = Time.unscaledTime + 3f;
                }
                else
                {
                    Beende();
                }
            }
            GUI.backgroundColor = alt;
        }

        void Beende()
        {
            // Laufende Online-Verbindung sauber trennen
            if (NetworkManager.singleton != null)
            {
                if (NetworkServer.active && NetworkClient.isConnected)
                    NetworkManager.singleton.StopHost();
                else if (NetworkClient.active)
                    NetworkManager.singleton.StopClient();
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;   // im Editor: Play-Modus stoppen
#else
            Application.Quit();                                // im Build: Spiel komplett schliessen
#endif
        }
    }
}
