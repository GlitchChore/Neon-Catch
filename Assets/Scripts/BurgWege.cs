using UnityEngine;
using System.Collections.Generic;

namespace NeonCatch
{
    // Baut beim Start Trampelpfade, die das Burgtor mit allen Bauernhäusern
    // verbinden. Die Wege folgen dem Gelände und bleiben automatisch frei:
    // Gras und Tiere aus den anderen Scripts spawnen nicht auf ihnen.
    //
    // An ein leeres GameObject hängen und im Inspector zuweisen:
    //   - Burgtor: das Tor-Objekt (oder ein leeres Objekt davor)
    //   - Bauernhaeuser: alle Bauernhäuser (je ein Weg pro Haus)
    [DefaultExecutionOrder(-50)]   // Wege müssen vor allen Spawnern existieren
    public class BurgWege : MonoBehaviour
    {
        [Header("Verbindungen (im Inspector zuweisen)")]
        public Transform burgtor;
        public Transform[] bauernhaeuser;

        [Header("Aussehen")]
        public float wegBreite = 2f;
        public float freiraumNebenWeg = 1f;   // Streifen neben dem Weg, der auch freigehalten wird
        public Material wegMaterial;          // leer = automatisches URP-Material (sandbraun)

        struct Segment { public Vector3 von, nach; }
        static readonly List<Segment> segmente = new List<Segment>();
        static float sperrAbstand = 2f;

        void Awake()
        {
            segmente.Clear();
            sperrAbstand = wegBreite * 0.5f + freiraumNebenWeg;

            if (burgtor == null || bauernhaeuser == null || bauernhaeuser.Length == 0)
            {
                Debug.LogWarning("BurgWege: Burgtor oder Bauernhäuser nicht zugewiesen – es werden keine Wege gebaut.");
                return;
            }

            Transform eltern = new GameObject("Burg_Wege").transform;
            Material mat = wegMaterial != null ? wegMaterial : ErzeugeMaterial();

            foreach (Transform haus in bauernhaeuser)
            {
                if (haus == null) continue;
                BaueWeg(burgtor.position, haus.position, eltern, mat);
                segmente.Add(new Segment { von = burgtor.position, nach = haus.position });
            }
        }

        // Liegt die Position auf einem Weg (inklusive Freiraum daneben)?
        // Wird von den Spawn-Scripts abgefragt; ohne Wege immer false.
        public static bool IstAufWeg(Vector3 position)
        {
            Vector2 p = new Vector2(position.x, position.z);
            foreach (Segment s in segmente)
            {
                // Abstand Punkt -> Strecke, nur in der Draufsicht (XZ)
                Vector2 a = new Vector2(s.von.x, s.von.z);
                Vector2 b = new Vector2(s.nach.x, s.nach.z);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                if (Vector2.Distance(p, a + ab * t) < sperrAbstand) return true;
            }
            return false;
        }

        // Ein Weg = schmales Band aus Dreiecken, das dem Gelände folgt
        void BaueWeg(Vector3 von, Vector3 nach, Transform eltern, Material mat)
        {
            Vector3 strecke = nach - von;
            strecke.y = 0f;
            if (strecke.magnitude < 0.5f) return;

            int schritte = Mathf.Max(2, Mathf.CeilToInt(strecke.magnitude / 2f));
            Vector3 quer = Vector3.Cross(Vector3.up, strecke.normalized) * (wegBreite * 0.5f);

            var vertices = new Vector3[(schritte + 1) * 2];
            var uvs      = new Vector2[vertices.Length];
            var tris     = new int[schritte * 6];

            for (int i = 0; i <= schritte; i++)
            {
                float t = (float)i / schritte;
                Vector3 mitte = Vector3.Lerp(von, nach, t);
                mitte.y = BodenHoehe(mitte) + 0.04f;   // knapp über dem Boden gegen Flackern

                vertices[i * 2]     = mitte - quer;
                vertices[i * 2 + 1] = mitte + quer;
                uvs[i * 2]     = new Vector2(0f, t * strecke.magnitude * 0.5f);
                uvs[i * 2 + 1] = new Vector2(1f, t * strecke.magnitude * 0.5f);
            }

            for (int i = 0; i < schritte; i++)
            {
                int v = i * 2;
                int tIdx = i * 6;
                tris[tIdx]     = v;     tris[tIdx + 1] = v + 2; tris[tIdx + 2] = v + 1;
                tris[tIdx + 3] = v + 1; tris[tIdx + 4] = v + 2; tris[tIdx + 5] = v + 3;
            }

            var mesh = new Mesh { name = "Weg" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Weg_zum_Bauernhaus");
            go.transform.SetParent(eltern, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static float BodenHoehe(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f,
                    ~(1 << 4), QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return position.y;
        }

        Material ErzeugeMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Weg_Auto" };
            mat.SetColor("_BaseColor", new Color(0.55f, 0.45f, 0.30f));   // sandiger Trampelpfad
            mat.SetFloat("_Smoothness", 0.1f);
            return mat;
        }
    }
}
