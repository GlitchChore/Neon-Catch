using UnityEngine;
using UnityEditor;
using NeonCatch;

namespace NeonCatch.Editor
{
    public class PlaneSetupTool : EditorWindow
    {
        [MenuItem("Tools/Szene aufbauen (Flaechen + Figuren + Kamera)")]
        public static void CreateScene()
        {
            // Standard-Szenenobjekte entfernen
            foreach (string name in new[] { "Main Camera", "Cube", "Directional Light", "ch19", "Szene" })
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null) Undo.DestroyObjectImmediate(obj);
            }

            GameObject parent = new GameObject("Szene");
            Undo.RegisterCreatedObjectUndo(parent, "Szene erstellen");

            // Richtungslicht
            GameObject lightObj = new GameObject("Licht");
            lightObj.transform.parent        = parent.transform;
            lightObj.transform.rotation      = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObj.AddComponent<Light>();
            light.type    = LightType.Directional;
            light.intensity = 1f;
            Undo.RegisterCreatedObjectUndo(lightObj, "Licht erstellen");

            int   count     = 10;
            int   columns   = 3;
            float planeSize = 50f;
            float gap       = 10f;
            float scale     = planeSize / 10f;
            float spacing   = planeSize + gap;

            string     fbxPath    = "Assets/3D Modelle/Unarmed Walk Forward.fbx";
            GameObject charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (charPrefab == null)
                Debug.LogWarning("[PlaneSetupTool] FBX nicht gefunden: " + fbxPath);

            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                // Plattform
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name = "Flaeche_" + (i + 1);
                plane.transform.parent     = parent.transform;
                plane.transform.localScale = new Vector3(scale, 1f, scale);
                plane.transform.position   = new Vector3(col * spacing, 0f, row * spacing);
                Undo.RegisterCreatedObjectUndo(plane, "Flaeche erstellen");

                Vector3 platformPos = plane.transform.position;

                if (i == 0)
                {
                    // --- Spieler-Container (leer, kein Mesh) ---
                    GameObject player = new GameObject("Spieler");
                    player.transform.parent   = parent.transform;
                    player.transform.position = platformPos + Vector3.up * 0.05f;
                    Undo.RegisterCreatedObjectUndo(player, "Spieler erstellen");

                    CharacterController cc = player.AddComponent<CharacterController>();
                    cc.center = new Vector3(0f, 1f, 0f);
                    cc.height = 2f;
                    cc.radius = 0.3f;

                    player.AddComponent<PlayerController>();

                    // Erste-Person-Kamera als Kind des Spielers
                    GameObject camObj = new GameObject("Spieler_Kamera");
                    camObj.tag = "MainCamera";
                    camObj.transform.parent        = player.transform;
                    camObj.transform.localPosition = new Vector3(0f, 1.7f, 0f);
                    camObj.transform.localRotation = Quaternion.identity;
                    camObj.AddComponent<Camera>();
                    camObj.AddComponent<AudioListener>();
                    Undo.RegisterCreatedObjectUndo(camObj, "Kamera erstellen");

                    // FBX-Figur als visuelles Kind des Spielers
                    if (charPrefab != null)
                    {
                        GameObject visual = Object.Instantiate(charPrefab);
                        visual.name = "Spieler_Figur";
                        visual.transform.parent        = player.transform;
                        visual.transform.localPosition = Vector3.zero;
                        visual.transform.localRotation = Quaternion.identity;
                        Undo.RegisterCreatedObjectUndo(visual, "Spieler Figur erstellen");
                    }
                }
                else if (charPrefab != null)
                {
                    // Dekorations-Figur auf allen anderen Plattformen
                    GameObject deco = Object.Instantiate(charPrefab);
                    deco.name = "Figur_" + (i + 1);
                    deco.transform.parent   = parent.transform;
                    deco.transform.position = platformPos;
                    Undo.RegisterCreatedObjectUndo(deco, "Figur erstellen");
                }
            }

            Selection.activeGameObject = parent;
            Debug.Log("[PlaneSetupTool] Szene aufgebaut.");
        }
    }
}
