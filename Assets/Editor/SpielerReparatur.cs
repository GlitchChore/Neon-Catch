using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace NeonCatch.Editor
{
    // Ein-Klick-Reparatur für die Spieler-Steuerung: Menü "NeonCatch/Spieler reparieren".
    //
    // Stellt sicher, dass es in der offenen Szene genau EINE steuerbare
    // Spieler-Figur gibt – eine unsichtbare Kapsel in Kopfhöhe, nur mit Kamera:
    //
    //   Spieler (Tag "Player")
    //    ├─ CharacterController   (klein genug für die Burg-Tore)
    //    ├─ PlayerController      (WASD gehen, Linksklick halten + Maus = schwenken)
    //    └─ Spieler_Kamera        (Tag "MainCamera", sitzt auf Augenhöhe)
    //
    // Fehlt etwas davon, wird es ergänzt. Existiert nur eine lose "Main Camera",
    // wird sie unter den Spieler gehängt und zur Spieler_Kamera gemacht –
    // so gehen keine Kamera-Einstellungen verloren.
    public static class SpielerReparatur
    {
        [MenuItem("NeonCatch/Spieler reparieren")]
        static void Reparieren()
        {
            // 1. Spieler-Objekt finden oder neu anlegen
            GameObject spieler = GameObject.Find("Spieler");
            if (spieler == null)
            {
                PlayerController vorhandener = Object.FindFirstObjectByType<PlayerController>();
                if (vorhandener != null) spieler = vorhandener.gameObject;
            }
            if (spieler == null)
            {
                spieler = new GameObject("Spieler");
                Undo.RegisterCreatedObjectUndo(spieler, "Spieler reparieren");
                spieler.transform.position = new Vector3(0f, 0.05f, 0f);
            }

            // 2. Tag "Player" (brauchen Schwimmen, Falltüren, Gras usw.)
            spieler.tag = "Player";

            // 3. CharacterController + PlayerController sicherstellen –
            //    die genauen Maße setzt PlayerController.Awake() beim Start selbst
            if (spieler.GetComponent<CharacterController>() == null)
                Undo.AddComponent<CharacterController>(spieler);
            if (spieler.GetComponent<PlayerController>() == null)
                Undo.AddComponent<PlayerController>(spieler);

            // 4. Kamera: erst nach vorhandener Spieler_Kamera suchen, sonst eine
            //    lose Szenen-Kamera (z.B. "Main Camera") übernehmen, sonst neu bauen
            Transform kamera = spieler.transform.Find("Spieler_Kamera");
            if (kamera == null)
            {
                Camera lose = Object.FindFirstObjectByType<Camera>();
                if (lose != null && !lose.transform.IsChildOf(spieler.transform))
                {
                    Undo.SetTransformParent(lose.transform, spieler.transform, "Spieler reparieren");
                    kamera = lose.transform;
                }
                else if (lose != null)
                {
                    kamera = lose.transform;
                }
                else
                {
                    var neu = new GameObject("Spieler_Kamera", typeof(Camera), typeof(AudioListener));
                    Undo.RegisterCreatedObjectUndo(neu, "Spieler reparieren");
                    neu.transform.SetParent(spieler.transform, false);
                    kamera = neu.transform;
                }
            }

            // 5. Kamera ausrichten: Name, Tag, Augenhöhe (PlayerController.Awake()
            //    rechnet die Höhe beim Start nochmal exakt – das hier ist der
            //    sichtbare Editor-Zustand)
            kamera.name = "Spieler_Kamera";
            kamera.gameObject.tag = "MainCamera";
            kamera.localPosition = new Vector3(0f, 0.46f, 0f);
            kamera.localRotation = Quaternion.identity;

            Selection.activeGameObject = spieler;
            EditorSceneManager.MarkSceneDirty(spieler.scene);
            Debug.Log("Spieler repariert: 'Spieler' mit PlayerController und 'Spieler_Kamera' ist bereit. " +
                      "Steuerung: WASD gehen, Linksklick halten + Maus bewegen = Kamera schwenken, Leertaste = springen.");
        }
    }
}
