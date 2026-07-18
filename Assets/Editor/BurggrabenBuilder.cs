using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace NeonCatch.Editor
{
    // Füllt den (vom LandschaftBuilder gegrabenen) Burggraben mit Leben:
    //  1. Wasser-Plane – nutzt das Suimono-Material, falls das Pack installiert
    //     ist; sonst ein eigenes transparentes Wasser in dunklem Grün-Braun
    //  2. Darunter eine Schlamm-Plane (dunkles Braun)
    //  3. 15 zufällige Steine auf dem Schlamm (aus dem Burg-Kit-Ordner;
    //     hat das Kit keine Steine, springen die Rock_A–D-Felsen ein)
    //  4. 8 Schilf-Pflanzen + 3 Seerosen am Rand (Schilf = Bambus aus dem
    //     Nature-Ordner bzw. NSK2-Busch als Ersatz – NSK2 selbst hat kein
    //     Schilf; Seerosen = gebaute Schwimmblätter mit Blüte)
    // Alles liegt unter dem Szenen-Objekt "Burggraben"; ein vorhandenes wird
    // ersetzt. Die Wasserhöhe richtet sich nach der gemessenen Grabentiefe.
    public class BurggrabenBuilder : EditorWindow
    {
        const string ASSET_PATH = "Assets/3D Objekte/Burg/";
        const string MAT_DIR    = "Assets/Deko-Materialien";
        const string ROOT_NAME  = "Burggraben";

        float ts, wh, k;
        Material wasserMat, schlammMat, blattMat, blueteMat;

        [MenuItem("NeonCatch/Burggraben Builder")]
        static void Open() => GetWindow<BurggrabenBuilder>("Burggraben");

        void OnGUI()
        {
            GUILayout.Label("Burggraben Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Füllt den Burggraben: dunkles grün-braunes Wasser, Schlammgrund,\n" +
                "15 Steine, 8 Schilf-Pflanzen und 3 Seerosen.\n" +
                "Voraussetzung: Burg + Landschaft (Graben) sind gebaut.\n" +
                "Suimono-Wasser wird automatisch genutzt, falls installiert.",
                MessageType.Info);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Burggraben füllen", GUILayout.Height(45)))
                Build();
            GUI.backgroundColor = Color.white;
        }

        // ─────────────────────────────────────────────
        // internal statt private: der MapMittelalterBuilder ruft das direkt
        // auf einer unsichtbaren Instanz auf (kein Fenster nötig)
        internal void Build()
        {
            GameObject burg = null;
            Terrain terrain = null;
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == "Burg") burg = go;
                if (go.name == "Landschaft") terrain = go.GetComponentInChildren<Terrain>();
            }
            if (burg == null)
            {
                Debug.LogError("BurggrabenBuilder: Keine 'Burg' in der Szene.");
                return;
            }

            var wallSrc = Load("wall.fbx");
            if (wallSrc == null) return;
            (ts, wh) = MeasureDimensions(wallSrc);
            k = Mathf.Clamp(Mathf.Min(wh / 2.5f, ts / 2.1f), 0.05f, 2f);

            // Burg-Größe wie in den anderen Buildern ableiten
            float half = 12f * ts;
            var basis = burg.transform.Find("Mauerweg");
            if (basis == null) basis = burg.transform.Find("Außenmauern");
            if (basis != null)
            {
                Bounds b = CalcBounds(basis);
                if (b.extents.x > ts) half = Mathf.Floor(b.extents.x / ts + 0.01f) * ts;
            }
            Vector3 o = burg.transform.position;

            // Graben-Geometrie: identisch zum LandschaftBuilder
            float ringIn  = half + 1.0f * ts;
            float ringOut = ringIn + 1.8f * ts;
            float ringMid = (ringIn + ringOut) * 0.5f;

            // Grabentiefe am Terrain messen (Mitte der Süd-Seite); ohne
            // Landschaft wird die Standard-Tiefe 2 angenommen
            float sohleY = -2f;
            if (terrain != null)
            {
                Vector3 mess = o + new Vector3(2f * ts, 0, -ringMid);
                sohleY = terrain.SampleHeight(mess) + terrain.transform.position.y;
            }
            // Fast randvoll: Wasser auf 95 % der Grabentiefe
            float wasserY  = sohleY + (o.y - sohleY) * 0.95f;
            float schlammY = sohleY + 0.05f;

            // Falls die Flaeche_1-Platte noch da ist (verdeckt das Wasser und
            // flimmert mit dem Terrain): Renderer/Mesh/Collider entfernen –
            // das Objekt bleibt als Teleport-Punkt erhalten
            var flaeche = GameObject.Find("Flaeche_1");
            if (flaeche != null)
            {
                var fmr = flaeche.GetComponent<MeshRenderer>();
                if (fmr != null) Undo.DestroyObjectImmediate(fmr);
                var fmf = flaeche.GetComponent<MeshFilter>();
                if (fmf != null) Undo.DestroyObjectImmediate(fmf);
                var fmc = flaeche.GetComponent<Collider>();
                if (fmc != null) Undo.DestroyObjectImmediate(fmc);
            }

            LoadMaterials();

            foreach (GameObject ex in SceneManager.GetActiveScene().GetRootGameObjects())
                if (ex.name == ROOT_NAME)
                    DestroyImmediate(ex);

            var root = new GameObject(ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, ROOT_NAME);

            // ── 1+2: Wasser- und Schlamm-Plane ───────────
            // EINE große Plane über die ganze Grabenfläche: außerhalb des
            // Grabens liegt das Gelände höher, dort ist sie unsichtbar –
            // sichtbar wird das Wasser genau in der Grabenrinne.
            float planeScale = (2f * (ringOut + 0.5f * ts)) / 10f; // Unity-Plane ist 10×10
            var wasser = GameObject.CreatePrimitive(PrimitiveType.Plane);
            wasser.name = "Wasser";
            wasser.transform.SetParent(root.transform, false);
            wasser.transform.position   = o + new Vector3(0, wasserY - o.y, 0);
            wasser.transform.localScale = new Vector3(planeScale, 1f, planeScale);
            wasser.GetComponent<Renderer>().sharedMaterial = wasserMat;
            RemoveColliders(wasser); // Wasser blockiert nicht

            var schlamm = GameObject.CreatePrimitive(PrimitiveType.Plane);
            schlamm.name = "Schlamm";
            schlamm.transform.SetParent(root.transform, false);
            schlamm.transform.position   = o + new Vector3(0, schlammY - o.y, 0);
            schlamm.transform.localScale = new Vector3(planeScale, 1f, planeScale);
            schlamm.GetComponent<Renderer>().sharedMaterial = schlammMat;
            // Schlamm behält seinen Collider: Grund zum Drüberlaufen

            // ── 3: 15 zufällige Steine auf dem Schlamm ───
            var steineGrp = Child(root, "Steine");
            var steinQuellen = SammleSteine();
            for (int i = 0; i < 15 && steinQuellen.Count > 0; i++)
            {
                Vector2 p = ZufallImGraben(ringIn + 0.4f * ts, ringOut - 0.4f * ts);
                var src = steinQuellen[Random.Range(0, steinQuellen.Count)];
                var go  = (GameObject)PrefabUtility.InstantiatePrefab(src, steineGrp.transform);
                go.transform.SetPositionAndRotation(
                    o + new Vector3(p.x, schlammY - o.y + 0.02f, p.y),
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                float h = MeasureDimensions(src).height;
                if (h > 0.01f)
                    go.transform.localScale = src.transform.localScale * (Random.Range(0.25f, 0.5f) * wh / h);
                EnsureCollider(go);
            }

            // ── 4: 8 Schilf + 3 Seerosen am Rand ─────────
            var pflanzen = Child(root, "Pflanzen");
            var schilfQuellen = SammleSchilf();
            for (int i = 0; i < 8 && schilfQuellen.Count > 0; i++)
            {
                // Am Rand: abwechselnd innere und äußere Böschung
                float rand = i % 2 == 0 ? ringIn + 0.35f * ts : ringOut - 0.35f * ts;
                Vector2 p = ZufallImGraben(rand, rand);
                var src = schilfQuellen[i % schilfQuellen.Count];
                var go  = (GameObject)PrefabUtility.InstantiatePrefab(src, pflanzen.transform);
                go.transform.SetPositionAndRotation(
                    o + new Vector3(p.x, wasserY - o.y - 0.15f * wh, p.y),
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                float h = MeasureDimensions(src).height;
                if (h > 0.01f)
                    go.transform.localScale = src.transform.localScale * (Random.Range(0.7f, 1.1f) * wh / h);
                RemoveColliders(go); // durchlaufbar
            }
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = ZufallImGraben(ringIn + 0.5f * ts, ringOut - 0.5f * ts);
                BuildSeerose(pflanzen, o + new Vector3(p.x, wasserY - o.y + 0.015f, p.y));
            }

            var fixErg = MaterialReparatur.Fix(new[] { root }, true);
            Selection.activeGameObject = root;
            Debug.Log($"[BurggrabenBuilder] Fertig – Wasser (dunkel grün-braun), Schlamm, 15 Steine, 8 Schilf, 3 Seerosen. " +
                      $"Materialien: {fixErg.repariert}/{fixErg.ersetzt} repariert.");
        }

        // Zufälliger Punkt im quadratischen Graben-Ring (rectD ∈ [rMin, rMax])
        static Vector2 ZufallImGraben(float rMin, float rMax)
        {
            int   seite = Random.Range(0, 4);
            float r     = Random.Range(rMin, rMax);
            float t     = Random.Range(-rMax, rMax);
            switch (seite)
            {
                case 0:  return new Vector2(t, r);   // Nord
                case 1:  return new Vector2(r, -t);  // Ost
                case 2:  return new Vector2(-t, -r); // Süd
                default: return new Vector2(-r, t);  // West
            }
        }

        // Steine: erst im Burg-Kit-Ordner suchen (brick/stone/rock im Namen),
        // sonst die Polyeler-Felsen Rock_A–D
        static List<GameObject> SammleSteine()
        {
            var list = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ASSET_PATH.TrimEnd('/') }))
            {
                string p  = AssetDatabase.GUIDToAssetPath(guid);
                string nl = System.IO.Path.GetFileNameWithoutExtension(p).ToLower();
                if (nl.Contains("brick") || nl.Contains("stone") || nl.Contains("rock"))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go != null) list.Add(go);
                }
            }
            if (list.Count == 0)
                foreach (string pfad in new[]
                {
                    "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_A.prefab",
                    "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_B.prefab",
                    "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_C.prefab",
                    "Assets/Polyeler/EssentialNaturePack/Prefabs/Rock_D.prefab",
                })
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
                    if (go != null) list.Add(go);
                }
            return list;
        }

        // Schilf: Bambus aus dem Polyeler-Nature-Pack sieht aus wie Schilf;
        // Ersatz: schmale NSK2-Büsche (NSK2 selbst hat kein Schilf)
        static List<GameObject> SammleSchilf()
        {
            var list = new List<GameObject>();
            foreach (string pfad in new[]
            {
                "Assets/Polyeler/EssentialNaturePack/Prefabs/LittleBamboo.prefab",
                "Assets/Polyeler/EssentialNaturePack/Prefabs/Bamboo.prefab",
            })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
                if (go != null) list.Add(go);
            }
            if (list.Count == 0)
                foreach (string pfad in new[]
                {
                    "Assets/NatureStarterKit2/Nature/bush05.prefab",
                    "Assets/NatureStarterKit2/Nature/bush06.prefab",
                })
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
                    if (go != null) list.Add(go);
                }
            return list;
        }

        // Seerose: 2 flache Schwimmblätter + kleine weiße Blüte
        void BuildSeerose(GameObject parent, Vector3 pos)
        {
            var grp = Child(parent, "Seerose");
            grp.transform.position = pos;

            var b1 = Prim(PrimitiveType.Cylinder, "Blatt1", grp, pos,
                new Vector3(0.45f, 0.006f, 0.45f) * k, blattMat, new Vector3(0, Random.Range(0f, 360f), 0));
            var b2 = Prim(PrimitiveType.Cylinder, "Blatt2", grp,
                pos + new Vector3(0.3f, 0.002f, 0.15f) * k,
                new Vector3(0.32f, 0.006f, 0.32f) * k, blattMat, new Vector3(0, Random.Range(0f, 360f), 0));
            var bl = Prim(PrimitiveType.Sphere, "Blüte", grp,
                pos + new Vector3(0.05f, 0.05f, -0.05f) * k,
                new Vector3(0.14f, 0.10f, 0.14f) * k, blueteMat);
            RemoveColliders(grp);
        }

        // ─── Materialien ─────────────────────────────────
        void LoadMaterials()
        {
            // 1) Suimono-Wasser nutzen, falls das Pack installiert ist
            wasserMat = null;
            foreach (string guid in AssetDatabase.FindAssets("Suimono t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null) { wasserMat = m; break; }
            }
            // 2) Sonst eigenes transparentes Wasser in dunklem Grün-Braun
            if (wasserMat == null)
            {
                wasserMat = GetOrCreateMat("GrabenWasser", new Color(0.16f, 0.23f, 0.13f, 0.85f));
                if (wasserMat.HasProperty("_Surface"))
                {
                    wasserMat.SetFloat("_Surface", 1f); // Transparent
                    wasserMat.SetOverrideTag("RenderType", "Transparent");
                    wasserMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    wasserMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    wasserMat.SetFloat("_Blend", 0f);
                    wasserMat.SetFloat("_ZWrite", 0f);
                }
                if (wasserMat.HasProperty("_Smoothness")) wasserMat.SetFloat("_Smoothness", 0.9f);
                var c = new Color(0.16f, 0.23f, 0.13f, 0.85f);
                wasserMat.color = c;
                EditorUtility.SetDirty(wasserMat);
            }

            schlammMat = GetOrCreateMat("Schlamm", new Color(0.20f, 0.15f, 0.10f));
            blattMat   = GetOrCreateMat("Seerosenblatt", new Color(0.16f, 0.40f, 0.16f));
            blueteMat  = GetOrCreateMat("Seerosenbluete", new Color(0.95f, 0.92f, 0.85f));
        }

        static Material GetOrCreateMat(string name, Color c)
        {
            if (!AssetDatabase.IsValidFolder(MAT_DIR))
                AssetDatabase.CreateFolder("Assets", "Deko-Materialien");
            string path = MAT_DIR + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader) { color = c };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ─── Hilfsmethoden ───────────────────────────────
        static GameObject Prim(PrimitiveType type, string name, GameObject parent,
            Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, true);
            go.transform.position   = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.rotation = Quaternion.Euler(euler.Value);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static Bounds CalcBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        static void EnsureCollider(GameObject go)
        {
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;
                Bounds b = mf.sharedMesh.bounds;
                var bc = mf.gameObject.AddComponent<BoxCollider>();
                bc.center = b.center;
                bc.size   = b.size;
            }
        }

        static void RemoveColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                DestroyImmediate(c);
        }

        static (float footprint, float height) MeasureDimensions(GameObject src)
        {
            float maxXZ = 0f, maxY = 0f;
            if (src != null)
                foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    Vector3 sc = mf.transform.lossyScale;
                    Bounds  mb = mf.sharedMesh.bounds;
                    maxXZ = Mathf.Max(maxXZ, mb.size.x * Mathf.Abs(sc.x), mb.size.z * Mathf.Abs(sc.z));
                    maxY  = Mathf.Max(maxY,  mb.size.y * Mathf.Abs(sc.y));
                }
            float fp = maxXZ > 0.01f ? maxXZ : 1f;
            float h  = maxY  > 0.01f ? maxY  : fp;
            return (fp, h);
        }

        static GameObject Load(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ASSET_PATH + name);
            if (go == null) Debug.LogWarning($"BurggrabenBuilder: '{name}' nicht gefunden.");
            return go;
        }
    }
}
