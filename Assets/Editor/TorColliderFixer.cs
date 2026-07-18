using UnityEngine;
using UnityEditor;

namespace NeonCatch.Editor
{
    // Repariert Tor-Collider: Ein einziger großer BoxCollider über dem ganzen
    // Wandstück deckt auch die Tor-Öffnung ab – der Spieler kann nicht durchs
    // Tor. Dieses Tool entfernt alle vorhandenen Collider am ausgewählten
    // Objekt und baut stattdessen diese Struktur:
    //
    //   Ausgewähltes Wandstück
    //    ├─ Wall         → 3 BoxCollider: Rahmen links + rechts, Sturz oben
    //    └─ GateOpening  → KEIN Collider (markiert die freie Durchfahrt)
    //
    // Die Durchfahrt bleibt frei, die Steinwand drumherum ist solide.
    // Alternativ Variante B: exakter (nicht-konvexer) MeshCollider, der dem
    // Loch im Mesh genau folgt – gleiche Technik wie beim BurgBuilder-Tor.
    public class TorColliderFixer : EditorWindow
    {
        float openWidth  = 0.40f; // Tor-Breite als Anteil der Wandbreite
        float openHeight = 0.65f; // Tor-Höhe als Anteil der Wandhöhe

        [MenuItem("NeonCatch/Tor-Collider Fixer")]
        static void Open() => GetWindow<TorColliderFixer>("Tor-Collider");

        void OnGUI()
        {
            GUILayout.Label("Tor-Collider Fixer", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "1. Wandstück mit Tor in der Hierarchy auswählen\n" +
                "2. Tor-Breite/-Höhe unten einstellen (Anteil der Wandgröße)\n" +
                "3. Variante A oder B klicken\n\n" +
                "A: teilt in 'Wall' (3 Box-Collider: Rahmen + Sturz) und 'GateOpening' (ohne Collider).\n" +
                "B: exakter MeshCollider – folgt dem Loch im Mesh automatisch.",
                MessageType.Info);

            openWidth  = EditorGUILayout.Slider("Tor-Breite (Anteil)", openWidth, 0.1f, 0.9f);
            openHeight = EditorGUILayout.Slider("Tor-Höhe (Anteil)",  openHeight, 0.1f, 0.95f);

            EditorGUILayout.Space(8);
            bool nichtsGewaehlt = Selection.gameObjects.Length == 0;
            if (nichtsGewaehlt)
                EditorGUILayout.HelpBox("Kein Objekt ausgewählt.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(nichtsGewaehlt))
            {
                GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                if (GUILayout.Button("A: Wall + GateOpening (Box-Collider)", GUILayout.Height(34)))
                    foreach (var go in Selection.gameObjects) SplitInWallUndTor(go);
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("B: Exakter MeshCollider", GUILayout.Height(28)))
                    foreach (var go in Selection.gameObjects) UseMeshColliders(go);
            }
        }

        // ─── Variante A: Wall + GateOpening ──────────────
        void SplitInWallUndTor(GameObject target)
        {
            Undo.SetCurrentGroupName("Tor-Collider fixen");
            int undoGroup = Undo.GetCurrentGroup();

            // Alte Collider und frühere Ergebnisse dieses Tools entfernen
            RemoveAllColliders(target);
            DeleteChild(target, "Wall");
            DeleteChild(target, "GateOpening");

            // Gesamtmaße aller Meshes im LOKALEN Raum des Ziels – so stimmen die
            // Collider auch bei gedrehten/skalierten Wandstücken
            Bounds b = LocalBounds(target);
            if (b.size.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"TorColliderFixer: '{target.name}' hat kein Mesh – übersprungen.");
                return;
            }

            float openW = b.size.x * openWidth;   // Breite der Durchfahrt
            float openH = b.size.y * openHeight;  // Höhe der Durchfahrt (ab Unterkante)
            float sideW = (b.size.x - openW) * 0.5f; // Breite je Rahmen-Seite

            // "Wall": trägt die soliden Collider (Torrahmen links/rechts + Sturz)
            var wall = new GameObject("Wall");
            wall.transform.SetParent(target.transform, false);
            Undo.RegisterCreatedObjectUndo(wall, "Wall");

            // Rahmen links
            AddBox(wall,
                new Vector3(b.min.x + sideW * 0.5f, b.center.y, b.center.z),
                new Vector3(sideW, b.size.y, b.size.z));
            // Rahmen rechts
            AddBox(wall,
                new Vector3(b.max.x - sideW * 0.5f, b.center.y, b.center.z),
                new Vector3(sideW, b.size.y, b.size.z));
            // Sturz über der Durchfahrt (nur wenn oben noch Wand übrig ist)
            float restH = b.size.y - openH;
            if (restH > 0.005f)
                AddBox(wall,
                    new Vector3(b.center.x, b.min.y + openH + restH * 0.5f, b.center.z),
                    new Vector3(openW, restH, b.size.z));

            // "GateOpening": bewusst OHNE Collider – hier geht der Spieler durch.
            // Nur ein Marker-Objekt, damit die Öffnung in der Hierarchy sichtbar ist.
            var opening = new GameObject("GateOpening");
            opening.transform.SetParent(target.transform, false);
            opening.transform.localPosition = new Vector3(b.center.x, b.min.y + openH * 0.5f, b.center.z);
            Undo.RegisterCreatedObjectUndo(opening, "GateOpening");

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[TorColliderFixer] '{target.name}': Wall (Rahmen links/rechts + Sturz) und GateOpening (frei) angelegt.");
        }

        static void AddBox(GameObject wall, Vector3 center, Vector3 size)
        {
            var bc = wall.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size   = size;
        }

        // ─── Variante B: exakter MeshCollider ────────────
        // Nicht-konvexe MeshCollider folgen dem Mesh samt Tor-Loch exakt –
        // gleiche Technik wie beim Torbogen des BurgBuilders.
        static void UseMeshColliders(GameObject target)
        {
            Undo.SetCurrentGroupName("Tor-MeshCollider");
            RemoveAllColliders(target);
            DeleteChild(target, "Wall");
            DeleteChild(target, "GateOpening");

            foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false; // konvex würde das Loch "zuspannen"
            }
            Debug.Log($"[TorColliderFixer] '{target.name}': exakte MeshCollider gesetzt – Durchfahrt folgt dem Mesh-Loch.");
        }

        // ─── Hilfsmethoden ───────────────────────────────
        static void RemoveAllColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                Undo.DestroyObjectImmediate(c);
        }

        static void DeleteChild(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        // Alle Mesh-Bounds in den lokalen Raum des Ziels transformiert und
        // zusammengefasst – Pivot-/Kind-Transform-unabhängig.
        static Bounds LocalBounds(GameObject target)
        {
            Matrix4x4 toLocal = target.transform.worldToLocalMatrix;
            bool any = false;
            Bounds result = new Bounds();

            foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }
    }
}
