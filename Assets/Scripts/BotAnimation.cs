using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace NeonCatch
{
    // Spielt die Mixamo-Animationen (Resources/KI/AnimationSpieler) auf einer
    // Humanoid-Figur ab – OHNE Animator-Controller, direkt über Playables.
    // Der Zustand wird automatisch gewählt:
    //   stehen        → Idle (im Kampf: Crouching Idle)
    //   gehen         → Walking, mit Ziel → Running
    //   bergauf/Stufe → Running Up Stairs   (automatisch erkannt)
    //   bergab        → Descending Stairs   (automatisch erkannt)
    //   0 Leben       → zufällige Sterbe-Animation (einmalig, bleibt liegen)
    // Alles blendet weich ineinander über.
    public class BotAnimation : MonoBehaviour
    {
        // Reihenfolge = Mixer-Eingänge. 0-6 sind Bewegungs-Zustände (dauerhaft,
        // von MeldeBewegung gewählt), 7-13 sind kurze Einmal-Animationen
        // (SpieleEinmalig – blenden sich kurz ein und geben danach die
        // Kontrolle automatisch an MeldeBewegung zurück). Alle Clips aus
        // Resources/KI/AnimationSpieler werden auf Wunsch verwendet.
        static readonly string[] clipNamen =
        {
            "Idle",                    // 0
            "Walking",                 // 1
            "Running",                 // 2
            "Running Up Stairs",       // 3
            "Descending Stairs",       // 4
            "Crouching Idle",          // 5
            "Climbing Ladder",         // 6
            "Corkscrew Evade",         // 7
            "Pistol Stand To Kneel",   // 8
            "Victory Idle",            // 9
            "Sad Idle",                // 10
            "Dizzy Idle",              // 11
            "Hip Hop Dancing",         // 12
            "Jumping Up",              // 13
        };

        public const int CLIP_AUSWEICHEN    = 7;
        public const int CLIP_KNIEFALL      = 8;
        public const int CLIP_SIEG          = 9;
        public const int CLIP_TREFFER_SAD   = 10;
        public const int CLIP_TREFFER_DIZZY = 11;
        public const int CLIP_TANZ          = 12;
        public const int CLIP_SPRUNG        = 13;

        static AnimationClip[] clips;
        static AnimationClip[] sterbeClips;
        static bool geladen;

        // "Falling Back Death" fliegt beim Treffer nach hinten weg und
        // "Zombie Dying" (das Wackeln) ist jetzt die TREFFER-Reaktion statt
        // eine Sterbe-Animation - es bleiben nur die freundlichen
        // Schulterzucken-/Haende-vors-Gesicht-Tode (Standing Death Left/Right).
        static readonly string[] ausgeschlosseneSterbeClips = { "Falling Back Death", "Zombie Dying" };

        // "Zombie Dying" ist die Animation, bei der der Körper wackelt –
        // dafür soll beim Aufrufer ein Sterne-Effekt um den Kopf erscheinen
        public bool LetzterTodWarWackelClip { get; private set; }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        float[] gewichte;
        int aktiv;
        bool tot;
        bool bereit;
        float einmalRest;   // >0, solange eine kurze Einmal-Animation läuft

        static void LadeClips()
        {
            if (geladen) return;
            geladen = true;

            AnimationClip[] alle = Resources.LoadAll<AnimationClip>("KI/AnimationSpieler");
            clips = new AnimationClip[clipNamen.Length];
            var sterben = new System.Collections.Generic.List<AnimationClip>();

            foreach (AnimationClip clip in alle)
            {
                if (clip.name.Contains("Death") || clip.name.Contains("Dying"))
                {
                    bool ausgeschlossen = false;
                    foreach (string name in ausgeschlosseneSterbeClips)
                        if (clip.name == name) { ausgeschlossen = true; break; }
                    if (!ausgeschlossen) sterben.Add(clip);
                    continue;
                }
                for (int i = 0; i < clipNamen.Length; i++)
                    if (clip.name == clipNamen[i]) clips[i] = clip;
            }
            sterbeClips = sterben.ToArray();

            if (alle.Length == 0)
                Debug.LogWarning("BotAnimation: keine Clips in Resources/KI/AnimationSpieler gefunden.");
        }

        void Start()
        {
            LadeClips();

            Animator animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman ||
                clips == null || clips[0] == null)
                return;   // keine Humanoid-Figur oder Clips fehlen: still bleiben

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            graph = PlayableGraph.Create("BotAnimation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // +1 Eingang für die einmalige Sterbe-Animation
            mixer = AnimationMixerPlayable.Create(graph, clipNamen.Length + 1);
            gewichte = new float[clipNamen.Length + 1];

            for (int i = 0; i < clipNamen.Length; i++)
            {
                AnimationClip clip = clips[i] != null ? clips[i] : clips[0];
                var abspieler = AnimationClipPlayable.Create(graph, clip);
                graph.Connect(abspieler, 0, mixer, i);
            }
            // Sterbe-Eingang sofort mit einem Platzhalter (Idle) verbinden –
            // sonst zeigt Update()s SetInputWeight-Schleife auf einen leeren
            // Port und wirft eine ArgumentNullException ("The Playable is
            // null"), solange SpieleTod() noch nicht lief. SpieleTod()
            // verbindet den echten Sterbe-Clip später einfach darüber.
            var sterbePlatzhalter = AnimationClipPlayable.Create(graph, clips[0]);
            graph.Connect(sterbePlatzhalter, 0, mixer, clipNamen.Length);

            gewichte[0] = 1f;
            mixer.SetInputWeight(0, 1f);

            var ausgabe = AnimationPlayableOutput.Create(graph, "Ausgabe", animator);
            ausgabe.SetSourcePlayable(mixer);
            graph.Play();
            bereit = true;
        }

        // Wird jeden Frame vom KampfBot gemeldet – wählt den Zustand.
        // Läuft gerade eine kurze Einmal-Animation (SpieleEinmalig), hat die
        // Vorrang und wird hier nicht überschrieben.
        public void MeldeBewegung(bool bewegtSich, bool imKampf, bool amBoden, float steigTempo)
        {
            if (!bereit || tot || einmalRest > 0f) return;

            int ziel;
            if (bewegtSich && amBoden && steigTempo > 0.25f)       ziel = 3;   // Treppe/Hang rauf
            else if (bewegtSich && amBoden && steigTempo < -0.25f) ziel = 4;   // Treppe/Hang runter
            else if (bewegtSich)                                   ziel = imKampf ? 2 : 1;   // rennen/gehen
            else                                                   ziel = imKampf ? 5 : 0;   // hocken/stehen

            aktiv = ziel;
        }

        // Kurze, einmalige Auflockerungs-/Reaktions-Animation (z.B. Ausweichen,
        // Kniefall beim Schuss, Sieges-Jubel, Treffer-Reaktion, Leerlauf-Tanz).
        // Blendet für "dauer" Sekunden ein, danach übernimmt MeldeBewegung
        // automatisch wieder den normalen Bewegungs-Zustand.
        public void SpieleEinmalig(int clipIndex, float dauer)
        {
            if (!bereit || tot) return;
            if (clipIndex < 0 || clipIndex >= clipNamen.Length || clips[clipIndex] == null) return;
            aktiv = clipIndex;
            einmalRest = dauer;
        }

        // Einmalige Sterbe-Animation; die Figur bleibt am Ende liegen
        public void SpieleTod()
        {
            if (!bereit || tot) return;
            tot = true;

            if (sterbeClips == null || sterbeClips.Length == 0) return;
            AnimationClip clip = sterbeClips[Random.Range(0, sterbeClips.Length)];
            LetzterTodWarWackelClip = clip.name == "Zombie Dying";
            var abspieler = AnimationClipPlayable.Create(graph, clip);
            abspieler.SetDuration(clip.length);
            graph.Connect(abspieler, 0, mixer, clipNamen.Length);
            aktiv = clipNamen.Length;
        }

        void Update()
        {
            if (!bereit) return;

            if (einmalRest > 0f) einmalRest -= Time.deltaTime;

            // Weich zum aktiven Zustand überblenden (Tod etwas schneller)
            float tempo = (tot ? 10f : 6f) * Time.deltaTime;
            for (int i = 0; i < gewichte.Length; i++)
            {
                gewichte[i] = Mathf.MoveTowards(gewichte[i], i == aktiv ? 1f : 0f, tempo);
                mixer.SetInputWeight(i, gewichte[i]);
            }
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
