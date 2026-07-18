using UnityEngine;
using UnityEditor;
using System.IO;

namespace NeonCatch.Editor
{
    // Sorgt dafür, dass die Spieler-/Bot-Animationen (Mixamo-FBX in
    // "Assets/Scripts/Animation Spieler.cs" und der Resources-Kopie
    // "Assets/Resources/KI/AnimationSpieler") RICHTIG importiert werden:
    //  - Rig = Humanoid (nur so passen sie auf die Sidekick-Figuren)
    //  - Clip-Name = Dateiname (Mixamo nennt sonst alles "mixamo.com")
    //  - Loop an für Idle/Gehen/Rennen/Treppe/Leiter/Hocken,
    //    Loop aus für Sterben/Springen/Übergänge
    //  - Root-Bewegung eingefroren (unser Code bewegt die Figur selbst)
    public class SpielerAnimationImporter : AssetPostprocessor
    {
        static bool IstSpielerAnimation(string pfad)
        {
            return pfad.Contains("Animation Spieler") || pfad.Contains("KI/AnimationSpieler");
        }

        void OnPreprocessModel()
        {
            if (!IstSpielerAnimation(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessAnimation()
        {
            if (!IstSpielerAnimation(assetPath)) return;
            var importer = (ModelImporter)assetImporter;

            string dateiName = Path.GetFileNameWithoutExtension(assetPath);
            bool loop = !(dateiName.Contains("Death") || dateiName.Contains("Dying") ||
                          dateiName.Contains("Jumping") || dateiName.Contains("Stand To"));

            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = dateiName;
                clip.loopTime = loop;
                clip.loopPose = loop;
                // Figur bleibt an Ort und Stelle – Position/Drehung übernimmt
                // unser Bewegungs-Code, nicht die Animation
                clip.lockRootRotation = true;
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        // Bereits importierte Dateien einmalig auf Humanoid umstellen –
        // der Postprozessor oben greift nur bei (Re-)Importen
        [InitializeOnLoadMethod]
        static void ReimportFallsNoetig()
        {
            EditorApplication.delayCall += () =>
            {
                string[] ordner =
                {
                    "Assets/Scripts/Animation Spieler.cs",
                    "Assets/Resources/KI/AnimationSpieler",
                };
                foreach (string o in ordner)
                {
                    if (!Directory.Exists(o)) continue;
                    foreach (string pfad in Directory.GetFiles(o, "*.fbx"))
                    {
                        string sauber = pfad.Replace('\\', '/');
                        var importer = AssetImporter.GetAtPath(sauber) as ModelImporter;
                        if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
                        {
                            importer.SaveAndReimport();
                            Debug.Log("SpielerAnimationImporter: '" + sauber + "' auf Humanoid umgestellt.");
                        }
                    }
                }
            };
        }
    }
}
