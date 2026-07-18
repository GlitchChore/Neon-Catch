using UnityEngine;
using System.Collections;

namespace NeonCatch
{
    // Lila/Rosa-Reparatur für JEDE Szene, egal wie die Welt aufgebaut wird
    // (KI_SzeneAufbau ODER fertige Szene mit eigenem Burggraben-System):
    // Materialien mit alten Nicht-URP-Shadern werden in URP magenta/lila
    // dargestellt – betroffen waren z.B. die Living-Birds-Vögel und die
    // Sardinen-Fische. Hier bekommen sie den URP-Lit-Shader und behalten
    // ihre Textur und Farbe.
    //
    // Startet sich über [RuntimeInitializeOnLoadMethod] selbst und repariert
    // mehrfach kurz nach dem Szenenstart, damit auch spät gespawnte Objekte
    // erfasst werden. Schlafende (deaktivierte) Tiere werden mitrepariert.
    public static class ShaderRosaFix
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            var go = new GameObject("ShaderRosaFix");
            go.AddComponent<ShaderRosaFixLaeufer>();
        }
    }

    public class ShaderRosaFixLaeufer : MonoBehaviour
    {
        IEnumerator Start()
        {
            // Spawner laufen in Start-Methoden und teils zeitversetzt –
            // deshalb mehrere Durchgänge statt nur einem
            float[] wartezeiten = { 0.5f, 1.5f, 3f };
            foreach (float wartezeit in wartezeiten)
            {
                yield return new WaitForSeconds(wartezeit);
                Repariere();
            }
            Destroy(gameObject);
        }

        static void Repariere()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;

            int repariert = 0;
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Partikel, Sprites und Linien haben eigene Shader-Welten –
                // eine Umstellung auf Lit würde sie kaputt machen
                if (renderer is ParticleSystemRenderer || renderer is SpriteRenderer ||
                    renderer is LineRenderer || renderer is TrailRenderer) continue;

                foreach (Material mat in renderer.materials)
                {
                    if (mat == null || mat.shader == null) continue;
                    string alterShader = mat.shader.name;
                    if (alterShader.StartsWith("Universal Render Pipeline")) continue;
                    // ShaderGraph-Shader sind URP-nativ und funktionieren –
                    // NICHT anfassen (die Umstellung hatte z.B. die Sidekick-
                    // Figuren komplett WEISS gemacht, weil ihre Texturen in
                    // eigenen Shader-Eigenschaften stecken)
                    if (alterShader.StartsWith("Shader Graphs")) continue;
                    if (alterShader.StartsWith("Sprites") || alterShader.StartsWith("UI") ||
                        alterShader.StartsWith("TextMeshPro") || alterShader.StartsWith("Skybox") ||
                        alterShader.StartsWith("Hidden")) continue;

                    Texture textur = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    Color farbe = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                    float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
                    // Standard-Shader speichert seinen Modus in _Mode:
                    // 0 = deckend, 1 = Cutout, 2/3 = transparent
                    float standardModus = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;

                    mat.shader = urpLit;
                    if (textur != null) mat.SetTexture("_BaseMap", textur);
                    mat.SetColor("_BaseColor", farbe);

                    // Ausgestanzte Texturen (Schmetterlingsflügel, Blätter, Federn)
                    // brauchen Alpha-Clipping, sonst bekommen sie weiße Flächen
                    if (alterShader.Contains("Cutout") || alterShader.Contains("Leaves") ||
                        alterShader.Contains("Leaf") || alterShader.Contains("Transparent") ||
                        standardModus >= 0.5f)
                    {
                        mat.SetFloat("_AlphaClip", 1f);
                        mat.SetFloat("_Cutoff", cutoff);
                        mat.EnableKeyword("_ALPHATEST_ON");
                    }
                    repariert++;
                }
            }
            if (repariert > 0)
                Debug.Log("ShaderRosaFix: " + repariert + " alte Materialien auf URP umgestellt (Lila-Fix).");
        }
    }
}
