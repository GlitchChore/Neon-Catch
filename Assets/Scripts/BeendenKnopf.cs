using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonCatch
{
    // ======================================================================
    // Roter "Spiel beenden"-Knopf oben rechts - startet sich in jeder Szene
    // selbst und ueberlebt Szenenwechsel (DontDestroyOnLoad).
    // Erster Klick (oder Taste B) fragt nach ("Wirklich beenden?"), zweiter
    // Klick/Taste B schliesst das Spiel komplett. So kommt man auch raus,
    // wenn der Mauszeiger im Spiel gesperrt ist. Laufende Online-Runden
    // werden vorher sauber getrennt.
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

        void Update()
        {
            // Taste B = beenden (erst nachfragen, zweites B beendet) - auch
            // wenn der Mauszeiger im Spiel gesperrt ist und der Knopf nicht
            // klickbar waere
            var kb = Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame)
            {
                if (!bestaetigen)
                {
                    bestaetigen = true;
                    bestaetigenBis = Time.unscaledTime + 3f;
                }
                else Beende();
            }
        }

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

            string text = bestaetigen ? "Wirklich beenden? (B)" : "Spiel beenden (B)";
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
            // Laufende Online-Verbindung sauber trennen (Photon)
            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;   // im Editor: Play-Modus stoppen
#else
            Application.Quit();                                // im Build: Spiel komplett schliessen
#endif
        }
    }
}
