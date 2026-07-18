using UnityEngine;
using UnityEditor;

namespace NeonCatch.Editor
{
    public static class SkyCityBuilder
    {
        const string TAG        = "SkyCity";
        const string PREFAB_DIR = "Models/";

        [MenuItem("NeonCatch/Build Sky City")]
        public static void BuildSkyCity()
        {
            EnsureTagExists(TAG);

            // Alle alten SkyCity-Objekte löschen
            foreach (GameObject old in GameObject.FindGameObjectsWithTag(TAG))
                Undo.DestroyObjectImmediate(old);

            // Root-Objekt
            GameObject root = new GameObject("SkyCity_Root");
            SetTag(root, TAG);
            Undo.RegisterCreatedObjectUndo(root, "SkyCity Root");

            // Prefabs laden
            GameObject platformPrefab = Resources.Load<GameObject>(PREFAB_DIR + "Platform_Small");
            if (platformPrefab == null)
            {
                Debug.LogError("Prefab nicht in Resources/Models");
                Undo.DestroyObjectImmediate(root);
                return;
            }

            GameObject antennaPrefab = Resources.Load<GameObject>(PREFAB_DIR + "Antenna");
            if (antennaPrefab == null)
                Debug.LogError("Prefab nicht in Resources/Models");

            Material neonMat = Resources.Load<Material>(PREFAB_DIR + "NeonMaterial");
            if (neonMat == null)
                Debug.LogError("Prefab nicht in Resources/Models");

            // 5 Plattformen im Kreis (R=20), Höhen: 5 8 12 16 20
            float[]      heights   = { 5f, 8f, 12f, 16f, 20f };
            float        radius    = 20f;
            int          count     = heights.Length;
            GameObject[] platforms = new GameObject[count];

            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count * i) * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    radius * Mathf.Cos(angle),
                    heights[i],
                    radius * Mathf.Sin(angle));

                GameObject p = (GameObject)PrefabUtility.InstantiatePrefab(platformPrefab);
                p.name                  = "Platform_" + (i + 1);
                p.transform.parent      = root.transform;
                p.transform.position    = pos;
                SetTag(p, TAG);
                Undo.RegisterCreatedObjectUndo(p, "Platform");
                platforms[i] = p;
            }

            // Laser-Brücke zwischen Plattform 1 und 2
            Vector3 a          = platforms[0].transform.position;
            Vector3 b          = platforms[1].transform.position;
            Vector3 midpoint   = (a + b) * 0.5f;
            Vector3 direction  = (b - a).normalized;

            GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bridge.name                  = "LaserBridge_1_2";
            bridge.transform.parent      = root.transform;
            bridge.transform.position    = midpoint;
            bridge.transform.rotation    = Quaternion.LookRotation(direction);
            bridge.transform.localScale  = new Vector3(0.2f, 0.2f, 15f);
            SetTag(bridge, TAG);
            if (neonMat != null)
                bridge.GetComponent<Renderer>().sharedMaterial = neonMat;
            Undo.RegisterCreatedObjectUndo(bridge, "Laser Bridge");

            // 2 Antennen auf Plattform 4 und 5 (Index 3, 4)
            if (antennaPrefab != null)
            {
                int[] slots = { 3, 4 };
                for (int i = 0; i < slots.Length; i++)
                {
                    Vector3 antennaPos = platforms[slots[i]].transform.position + Vector3.up;
                    GameObject ant     = (GameObject)PrefabUtility.InstantiatePrefab(antennaPrefab);
                    ant.name           = "Antenna_" + (i + 1);
                    ant.transform.parent   = root.transform;
                    ant.transform.position = antennaPos;
                    SetTag(ant, TAG);
                    Undo.RegisterCreatedObjectUndo(ant, "Antenna");
                }
            }

            Selection.activeGameObject = root;
            Debug.Log("[SkyCityBuilder] Sky City gebaut: " + count + " Plattformen, 1 Laser-Brücke, 2 Antennen.");
        }

        static void SetTag(GameObject go, string tag)
        {
            try   { go.tag = tag; }
            catch { Debug.LogWarning("[SkyCityBuilder] Tag '" + tag + "' nicht gefunden – bitte in den Project Settings anlegen."); }
        }

        static void EnsureTagExists(string tag)
        {
            SerializedObject   tagManager = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            SerializedProperty tagsProp   = tagManager.FindProperty("tags");

            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) return;

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
        }
    }
}
