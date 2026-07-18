using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace NeonCatch.Editor
{
    // Repariert lila/weiße Materialien (fehlender oder falscher Shader).
    // Unter URP sind Built-in-Shader ("Standard", Legacy, Tree Soft Occlusion …)
    // die Ursache für Lila – Ziel ist dann "Universal Render Pipeline/Lit".
    // Textur + Farbe werden übernommen; FBX-interne (nicht editierbare)
    // Materialien werden durch reparierte Kopien ersetzt.
    //
    // Die statische Klasse wird auch vom Burg-Deko- und Landschaft-Builder
    // nach dem Bauen automatisch aufgerufen.
    public static class MaterialReparatur
    {
        const string FIX_DIR = "Assets/Reparierte-Materialien";

        public static (int repariert, int ersetzt) Fix(GameObject[] roots, bool forceAlle)
        {
            Shader ziel = ZielShader();
            if (ziel == null) return (0, 0);

            var cache = new Dictionary<Material, Material>();
            int repariert = 0, ersetzt = 0;

            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    bool geaendert = false;

                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null)
                        {
                            mats[i] = StandardErsatz(ziel);
                            geaendert = true;
                            ersetzt++;
                            continue;
                        }
                        if (!BrauchtFix(m, forceAlle))
                        {
                            // Bereits umgestellte Laub-Materialien ggf. heilen
                            // (falsch aktivierter Cutout = Bäume ohne Blätter)
                            if (IstEditierbar(m) && m.HasProperty("_AlphaClip"))
                            {
                                Texture tx = FindeTextur(m);
                                if (WirktWieLaub(m, tx)) LaubAnpassen(m, true, tx);
                            }
                            continue;
                        }

                        if (IstEditierbar(m))
                        {
                            FixDirekt(m, ziel);
                            repariert++;
                        }
                        else
                        {
                            mats[i] = ErsatzFuer(m, ziel, cache);
                            geaendert = true;
                            ersetzt++;
                        }
                    }

                    if (geaendert)
                    {
                        Undo.RecordObject(r, "Material zuweisen");
                        r.sharedMaterials = mats;
                        EditorUtility.SetDirty(r);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            return (repariert, ersetzt);
        }

        static Shader ZielShader()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
                return Shader.Find("Universal Render Pipeline/Lit");
            return Shader.Find("Standard");
        }

        static bool BrauchtFix(Material m, bool forceAlle)
        {
            Shader s = m.shader;
            if (s == null || s.name == "Hidden/InternalErrorShader") return true;

            bool urp = GraphicsSettings.defaultRenderPipeline != null;
            if (!urp) return false;

            string n = s.name;
            if (n == "Standard" || n == "Standard (Specular setup)"
                || n.StartsWith("Legacy Shaders/") || n == "Autodesk Interactive"
                || n == "Diffuse" || n == "Bumped Diffuse"
                || n.StartsWith("Nature/"))
                return true;

            if (forceAlle
                && !n.Contains("Universal Render Pipeline") && !n.Contains("Shader Graphs")
                && !n.Contains("Particles") && !n.Contains("UI") && !n.Contains("Sprites")
                && !n.Contains("Skybox") && !n.Contains("TextMesh") && !n.Contains("Terrain"))
                return true;

            return false;
        }

        static void FixDirekt(Material m, Shader ziel)
        {
            Texture tex = FindeTextur(m);
            Color   col = FindeFarbe(m);
            bool laub   = WirktWieLaub(m, tex);

            Undo.RecordObject(m, "Shader umstellen");
            m.shader = ziel;
            if (tex != null) m.mainTexture = tex; // _BaseMap (URP) bzw. _MainTex (Standard)
            m.color = col;
            LaubAnpassen(m, laub, tex);
            EditorUtility.SetDirty(m);
        }

        // Blätter/Büsche brauchen Alpha-Cutout + zweiseitiges Rendern,
        // sonst erscheinen die Blatt-Texturen als komisch gefärbte Vollflächen
        static bool WirktWieLaub(Material m, Texture tex)
        {
            string s = (m.shader != null ? m.shader.name : "") + " " + m.name + " " + (tex != null ? tex.name : "");
            s = s.ToLower();
            // "billboard"/"lod": die Fern-Ansichten der Bäume sind Blatt-Karten
            // mit Alpha – ohne Cutout wirken die Bäume aus der Distanz kaputt
            return s.Contains("leaf") || s.Contains("leaves") || s.Contains("laub")
                || s.Contains("foliage") || s.Contains("branch") || s.Contains("cutout")
                || s.Contains("blatt") || s.Contains("bush")
                || s.Contains("billboard") || s.Contains("lod");
        }

        // Cutout NUR aktivieren, wenn die Textur wirklich einen Alphakanal hat!
        // Bäume mit soliden Blatt-Meshes (Textur ohne Alpha) würden sonst ALLE
        // Blätter wegschneiden – übrig bliebe nur das Holz. Diese Methode
        // korrigiert auch früher falsch eingestellte Materialien zurück.
        static void LaubAnpassen(Material m, bool laub, Texture tex)
        {
            if (!m.HasProperty("_AlphaClip")) return; // nur URP Lit
            bool clip = laub && HatAlphaKanal(tex);

            m.SetFloat("_AlphaClip", clip ? 1f : 0f);
            if (clip) m.EnableKeyword("_ALPHATEST_ON");
            else      m.DisableKeyword("_ALPHATEST_ON");
            // Niedriger Schwellwert (0.2): Blatt-Texturen mit weichem Alpha
            // (z.B. Nature Starter Kit) würden bei 0.4 KOMPLETT weggeschnitten
            // → unsichtbare Blätter, nur Stämme übrig
            if (clip && m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.2f);
            if (clip) SorgeFuerAlphaImport(tex);
            if (laub && m.HasProperty("_Cull"))   m.SetFloat("_Cull", 0f); // beidseitig
            EditorUtility.SetDirty(m);
        }

        // Import-Flag "Alpha Is Transparency" setzen – ohne das rendert der
        // Cutout die Blattränder falsch bzw. Blätter verschwinden ganz
        static void SorgeFuerAlphaImport(Texture tex)
        {
            var t2 = tex as Texture2D;
            if (t2 == null) return;
            string p = AssetDatabase.GetAssetPath(t2);
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            if (imp == null || imp.alphaIsTransparency) return;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }

        static bool HatAlphaKanal(Texture tex)
        {
            var t2 = tex as Texture2D;
            if (t2 == null) return false;
            string p = AssetDatabase.GetAssetPath(t2);
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            return imp != null && imp.DoesSourceTextureHaveAlpha();
        }

        static Material ErsatzFuer(Material alt, Shader ziel, Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(alt, out var vorhanden)) return vorhanden;

            // Gibt es aus einem früheren Lauf schon eine Kopie? Wiederverwenden.
            string wunschPfad = $"{FIX_DIR}/{alt.name}_fix.mat";
            var frueher = AssetDatabase.LoadAssetAtPath<Material>(wunschPfad);
            if (frueher != null)
            {
                // Laub-Einstellung der vorhandenen Kopie korrigieren (heilt
                // Kopien, die früher fälschlich auf Cutout gestellt wurden)
                Texture altTex = FindeTextur(alt);
                LaubAnpassen(frueher, WirktWieLaub(alt, altTex), altTex);
                cache[alt] = frueher;
                return frueher;
            }

            if (!AssetDatabase.IsValidFolder(FIX_DIR))
                AssetDatabase.CreateFolder("Assets", "Reparierte-Materialien");

            var neu = new Material(ziel) { name = alt.name + "_fix" };
            Texture tex = FindeTextur(alt);
            if (tex != null) neu.mainTexture = tex;
            neu.color = FindeFarbe(alt);
            LaubAnpassen(neu, WirktWieLaub(alt, tex), tex);

            AssetDatabase.CreateAsset(neu, wunschPfad);
            cache[alt] = neu;
            return neu;
        }

        static Material StandardErsatz(Shader ziel)
        {
            if (!AssetDatabase.IsValidFolder(FIX_DIR))
                AssetDatabase.CreateFolder("Assets", "Reparierte-Materialien");
            string path = FIX_DIR + "/Fehlend_fix.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(ziel) { color = Color.gray };
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static bool IstEditierbar(Material m)
        {
            string p = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(p)) return true; // Szenen-Instanz
            return p.EndsWith(".mat");                 // eigenes Material-Asset
        }

        static Texture FindeTextur(Material m)
        {
            foreach (string prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" })
                if (m.HasProperty(prop))
                {
                    var t = m.GetTexture(prop);
                    if (t != null) return t;
                }
            return null;
        }

        static Color FindeFarbe(Material m)
        {
            foreach (string prop in new[] { "_BaseColor", "_Color" })
                if (m.HasProperty(prop))
                    return m.GetColor(prop);
            return Color.white;
        }
    }

    // ─── Fenster ─────────────────────────────────────────
    public class MaterialFixer : EditorWindow
    {
        bool forceAlleShader = false;

        [MenuItem("NeonCatch/Material Fixer")]
        static void Open() => GetWindow<MaterialFixer>("Material Fixer");

        void OnGUI()
        {
            GUILayout.Label("Material Fixer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            bool urp = GraphicsSettings.defaultRenderPipeline != null;
            EditorGUILayout.HelpBox(
                urp
                    ? "URP erkannt → kaputte Materialien werden auf 'Universal Render Pipeline/Lit'\n" +
                      "umgestellt ('Standard' wäre unter URP weiterhin lila). Textur + Farbe bleiben.\n" +
                      "Hinweis: Burg-Deko- und Landschaft-Builder reparieren ihre Objekte jetzt\n" +
                      "automatisch nach dem Bauen."
                    : "Built-in-Pipeline erkannt → Ziel-Shader ist 'Standard'.",
                MessageType.Info);

            forceAlleShader = EditorGUILayout.Toggle(
                new GUIContent("Alle fremden Shader erzwingen",
                    "Aus: nur kaputte/Standard/Legacy/Nature-Shader (empfohlen).\n" +
                    "An: alles außer Partikel/UI/Skybox wird umgezwungen."),
                forceAlleShader);

            EditorGUILayout.Space(8);
            bool nichtsGewaehlt = Selection.gameObjects.Length == 0;

            using (new EditorGUI.DisabledScope(nichtsGewaehlt))
            {
                GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                if (GUILayout.Button($"Auswahl reparieren ({Selection.gameObjects.Length} Objekte)", GUILayout.Height(34)))
                    Melden(MaterialReparatur.Fix(Selection.gameObjects, forceAlleShader));
                GUI.backgroundColor = Color.white;
            }
            if (nichtsGewaehlt)
                EditorGUILayout.HelpBox("Für den grünen Knopf erst Objekte in der Hierarchy auswählen.", MessageType.None);

            if (GUILayout.Button("Ganze Szene reparieren", GUILayout.Height(28)))
                Melden(MaterialReparatur.Fix(SceneManager.GetActiveScene().GetRootGameObjects(), forceAlleShader));

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Baum-Blätter reparieren (ganze Szene)", GUILayout.Height(28)))
            {
                Melden(MaterialReparatur.Fix(SceneManager.GetActiveScene().GetRootGameObjects(), false));
                Debug.Log("[MaterialFixer] Blätter-Materialien geprüft: URP/Lit, Cutout 0.25, Alpha-Import gesetzt.");
            }
            if (GUILayout.Button("Weiße Böden → Gras", GUILayout.Height(28)))
                BodenBegruenen();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Hinweis: Beide Reparaturen laufen inzwischen AUTOMATISCH bei jedem\n" +
                "'Landschaft bauen' und 'Deko dazu bauen' mit – die Knöpfe hier braucht\n" +
                "man nur noch für manuell platzierte Objekte.",
                MessageType.None);
        }

        static void Melden((int repariert, int ersetzt) r)
            => Debug.Log($"[MaterialFixer] Fertig – {r.repariert} Materialien umgestellt, {r.ersetzt} ersetzt.");

        // ─── Gras-Textur ─────────────────────────────────
        // Bevorzugt den schönen Rasen-BODEN aus dem Nature Starter Kit 2:
        // Textures/ground01.tga (2048², ohne Alpha – echte Bodentextur).
        // ACHTUNG: grass01/grass02.tga sind dagegen Halm-SPRITES mit
        // Alphakanal – als Bodentextur wirken sie WEISS, nie verwenden!
        // Fällt ground01 weg, wird eine prozedurale Rasen-Textur erzeugt.
        public static Texture2D HoleGrasTextur()
        {
            var nsk = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NatureStarterKit2/Textures/ground01.tga");
            if (nsk != null) return nsk;

            const string pfad = "Assets/Landschaft/Gras_Prozedural.asset";
            var vorhanden = AssetDatabase.LoadAssetAtPath<Texture2D>(pfad);
            if (vorhanden != null) return vorhanden;

            if (!AssetDatabase.IsValidFolder("Assets/Landschaft"))
                AssetDatabase.CreateFolder("Assets", "Landschaft");

            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var g1 = new Color(0.32f, 0.53f, 0.23f); // Basis
            var g2 = new Color(0.41f, 0.63f, 0.29f); // hell
            var g3 = new Color(0.25f, 0.43f, 0.18f); // dunkel
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Pixel-Hash: strukturloses Feinrauschen → kachelt nahtlos
                    uint hsh = (uint)(x * 73856093 ^ y * 19349663);
                    hsh = hsh * 2654435761u;
                    float r = (hsh % 1000u) / 1000f;

                    Color c = Color.Lerp(g1, g2, r * r);
                    if (r > 0.93f) c = g3;                       // dunkle Sprenkel
                    else if (r < 0.045f) c = Color.Lerp(g2, Color.white, 0.15f);
                    px[y * n + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            AssetDatabase.CreateAsset(tex, pfad);
            return tex;
        }

        // Alle weißen/faden Boden-Flächen (Flaeche_*, Plane, Ground, Boden …)
        // bekommen das getexturierte Gras-Material. "Weiß" = kein eigenes
        // Texture-Material (Default-Material oder texturlose helle Farbe).
        // Public: wird von den Buildern nach jedem Bauen automatisch aufgerufen.
        public static void BodenBegruenen()
        {
            // Gras-Material laden oder anlegen (mit Gras-Textur aus den Packs)
            string grasPfad = "Assets/Deko-Materialien/Gras.mat";
            var gras = AssetDatabase.LoadAssetAtPath<Material>(grasPfad);
            if (gras == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Deko-Materialien"))
                    AssetDatabase.CreateFolder("Assets", "Deko-Materialien");
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                gras = new Material(shader) { color = new Color(0.32f, 0.52f, 0.22f) };
                AssetDatabase.CreateAsset(gras, grasPfad);
            }
            // Prozedurale Rasen-Textur drauf (die Pack-"grass"-Texturen sind
            // Büschel-Sprites → wirkten als Bodentextur weiß)
            gras.mainTexture = HoleGrasTextur();
            gras.mainTextureScale = new Vector2(10f, 10f);
            gras.color = Color.white;
            EditorUtility.SetDirty(gras);

            int n = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string nm = mr.gameObject.name.ToLower();
                    if (!nm.Contains("flaeche") && !nm.Contains("fläche") && !nm.Contains("plane")
                        && !nm.Contains("ground") && !nm.Contains("boden")) continue;

                    bool weiss = true;
                    foreach (Material m in mr.sharedMaterials)
                    {
                        if (m == null) continue;
                        if (m == gras) { weiss = false; break; } // schon grün
                        Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                                    : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                                : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                        bool hell = c.r > 0.7f && c.g > 0.7f && c.b > 0.7f;
                        if (tex != null || !hell) { weiss = false; break; }
                    }
                    if (!weiss) continue;

                    Undo.RecordObject(mr, "Boden begrünen");
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = gras;
                    mr.sharedMaterials = mats;
                    EditorUtility.SetDirty(mr);
                    n++;
                }

            AssetDatabase.SaveAssets();
            Debug.Log($"[MaterialFixer] {n} weiße Boden-Flächen auf Gras umgestellt.");
        }
    }
}
