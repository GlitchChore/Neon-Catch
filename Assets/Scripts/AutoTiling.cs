using UnityEngine;

// Passt das Tiling aller Materialien automatisch an die Objekt-Skalierung an,
// damit die Textur-Schärfe unabhängig von der Größe des Objekts gleich bleibt.
[ExecuteInEditMode]
[RequireComponent(typeof(MeshRenderer))]
public class AutoTiling : MonoBehaviour
{
    MeshRenderer meshRenderer;
    Vector3 lastScale;

    void OnEnable()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        // Ungültiger Wert erzwingt eine einmalige Aktualisierung beim Aktivieren
        lastScale = Vector3.zero;
        ApplyTiling();
    }

    void Update()
    {
        // Nur neu berechnen, wenn sich die Skalierung seit dem letzten Frame geändert hat
        if (transform.localScale != lastScale)
            ApplyTiling();
    }

    void ApplyTiling()
    {
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            return;

        lastScale = transform.localScale;

        // Größte Achse der Skalierung bestimmt das Tiling,
        // damit die Textur bei ungleichmäßiger Skalierung nicht verzerrt wird
        float scale = Mathf.Max(lastScale.x, Mathf.Max(lastScale.y, lastScale.z));
        Vector2 tiling = new Vector2(scale, scale);

        // sharedMaterials statt materials, damit KEINE neuen Material-Instanzen
        // erzeugt werden (funktioniert mit "stones", "bricks", "roof" gleichzeitig)
        Material[] materials = meshRenderer.sharedMaterials;
        foreach (Material mat in materials)
        {
            if (mat == null)
                continue;
            mat.mainTextureScale = tiling;
        }
    }
}
