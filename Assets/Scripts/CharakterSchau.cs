using UnityEngine;

namespace NeonCatch
{
    // ======================================================================
    // CHARAKTER-SCHAU: zeigt die drei Start-Charaktere aus dem Synty
    // "Sidekick Free Starter Pack" als echte, langsam drehende 3D-Vorschau
    // mit Name und kleiner Geschichte. Wird vom NEON-BLASTER-Startmenue
    // (Knopf "CHARAKTER") ein- und ausgeschaltet.
    //
    // Die Figuren liegen bereits als KI/Bot_1..3 in Resources (dieselben
    // Sidekick-Starter-Figuren, die auch die Bots benutzen) und lassen sich
    // so ohne weiteres Setup laden. Die Vorschau wird auf eine RenderTexture
    // gerendert, die das IMGUI-Menue dann als Bild anzeigt - dadurch mischt
    // sich die 3D-Ansicht sauber in das bestehende Menue.
    // ======================================================================
    public class CharakterSchau : MonoBehaviour
    {
        public struct Figur
        {
            public string prefab;
            public string name;
            public string geschichte;
        }

        // Die 3 Charaktere, die man am Anfang auswaehlen kann.
        public static readonly Figur[] Figuren =
        {
            new Figur
            {
                prefab = "KI/Bot_1", name = "Rico",
                geschichte =
                    "Der schnellste Farbschütze der Neon-Stadt.\n\n" +
                    "Rico will jede Mauer der Burg bunt machen –\n" +
                    "und lässt sich dabei von niemandem erwischen."
            },
            new Figur
            {
                prefab = "KI/Bot_2", name = "Luna",
                geschichte =
                    "Luna findet in jeder Ecke ein Versteck.\n\n" +
                    "Nachts malt sie leuchtende Sterne an die Mauern,\n" +
                    "tagsüber ist sie die beste Sucherin weit und breit."
            },
            new Figur
            {
                prefab = "KI/Bot_3", name = "Max",
                geschichte =
                    "Max ist mutig und stürmt immer vorneweg.\n\n" +
                    "Sein Lieblingsspruch:\n" +
                    "'Wer zuerst trifft, lacht am buntesten!'"
            },
        };

        public static CharakterSchau Instanz { get; private set; }

        RenderTexture ziel;
        Camera vorschauKamera;
        Transform buehne;
        GameObject aktuelleFigur;
        int index = -1;
        bool aktiv;

        public RenderTexture Textur => ziel;
        public int Anzahl => Figuren.Length;
        public int Index => Mathf.Max(0, index);
        public bool Aktiv => aktiv;
        public string AktName => Figuren[Index].name;
        public string AktGeschichte => Figuren[Index].geschichte;

        // Holt die (einzige) Charakter-Schau oder erzeugt sie beim ersten Mal.
        public static CharakterSchau Hole()
        {
            if (Instanz == null)
            {
                var go = new GameObject("CharakterSchau");
                Instanz = go.AddComponent<CharakterSchau>();
            }
            return Instanz;
        }

        void Awake()
        {
            Instanz = this;
            BaueBuehne();
            SetzeAktiv(false);
        }

        void OnDestroy()
        {
            if (ziel != null) { ziel.Release(); Destroy(ziel); }
        }

        void BaueBuehne()
        {
            // Die "Buehne" liegt WEIT weg von der Spielwelt, damit garantiert
            // nichts anderes ins Vorschau-Bild geraet.
            buehne = new GameObject("CharakterBuehne").transform;
            buehne.SetParent(transform, false);
            buehne.position = new Vector3(2000f, -1000f, 2000f);

            ziel = new RenderTexture(512, 760, 16) { name = "CharakterVorschau" };
            ziel.Create();

            var kamGO = new GameObject("CharakterKamera");
            kamGO.transform.SetParent(buehne, false);
            kamGO.transform.localPosition = new Vector3(0f, 0.9f, 3.9f);
            kamGO.transform.localRotation = Quaternion.Euler(2f, 180f, 0f);   // schaut auf die Figur
            vorschauKamera = kamGO.AddComponent<Camera>();
            vorschauKamera.targetTexture = ziel;                 // rendert NUR ins Bild, nie auf den Schirm
            vorschauKamera.clearFlags = CameraClearFlags.SolidColor;
            vorschauKamera.backgroundColor = new Color(0.07f, 0.08f, 0.14f);
            vorschauKamera.fieldOfView = 30f;                    // vertikal - 1,7-m-Figur passt mit Rand
            vorschauKamera.nearClipPlane = 0.05f;
            vorschauKamera.farClipPlane = 14f;

            // Zwei Punktlichter fuer eine schoene, gleichmaessige Ausleuchtung.
            // Kleine Reichweite -> die weit entfernte Spielwelt bleibt unberuehrt.
            ErzeugeLicht(new Vector3(1.4f, 2.2f, 2.4f), new Color(1f, 0.96f, 0.9f), 4f, 9f);
            ErzeugeLicht(new Vector3(-1.6f, 1.6f, 1.8f), new Color(0.5f, 0.8f, 1f), 3f, 8f);
        }

        void ErzeugeLicht(Vector3 lokalePos, Color farbe, float intensitaet, float reichweite)
        {
            var lichtGO = new GameObject("VorschauLicht");
            lichtGO.transform.SetParent(buehne, false);
            lichtGO.transform.localPosition = lokalePos;
            var l = lichtGO.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = farbe;
            l.intensity = intensitaet;
            l.range = reichweite;
        }

        // Ein- oder ausschalten: nur wenn aktiv wird gerendert und gedreht.
        public void SetzeAktiv(bool an)
        {
            aktiv = an;
            if (vorschauKamera != null) vorschauKamera.enabled = an;

            if (an)
            {
                if (index < 0) Zeige(0);
            }
            else if (aktuelleFigur != null)
            {
                Destroy(aktuelleFigur);
                aktuelleFigur = null;
                index = -1;
            }
        }

        // Charakter Nr. i anzeigen (0..Anzahl-1).
        public void Zeige(int neu)
        {
            index = ((neu % Anzahl) + Anzahl) % Anzahl;

            if (aktuelleFigur != null) Destroy(aktuelleFigur);

            var prefab = Resources.Load<GameObject>(Figuren[index].prefab);
            if (prefab == null)
            {
                Debug.LogWarning("CharakterSchau: Prefab '" + Figuren[index].prefab + "' fehlt in Resources.");
                return;
            }

            aktuelleFigur = Instantiate(prefab, buehne);
            aktuelleFigur.transform.localRotation = Quaternion.identity;

            // Auf angenehme Vorschau-Groesse bringen und Fuesse auf den Buehnenboden.
            AufHoeheUndBoden(aktuelleFigur, 1.7f);

            // Alles entfernen, was nur im echten Spiel Sinn ergibt.
            foreach (var cc in aktuelleFigur.GetComponentsInChildren<CharacterController>()) Destroy(cc);
            foreach (var col in aktuelleFigur.GetComponentsInChildren<Collider>()) Destroy(col);
        }

        // Figur auf Zielhoehe skalieren und so verschieben, dass die Fuesse
        // genau auf dem Buehnen-Nullpunkt stehen (mittig vor der Kamera).
        static void AufHoeheUndBoden(GameObject go, float zielHoehe)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            if (b.size.y > 0.01f)
                go.transform.localScale *= zielHoehe / b.size.y;

            // Nach dem Skalieren neu messen und Fuesse auf Buehnenhoehe setzen
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float fussVersatz = b.min.y - go.transform.parent.position.y;
            go.transform.position -= new Vector3(0f, fussVersatz, 0f);
            // horizontal exakt mittig vor die Kamera
            Vector3 lp = go.transform.localPosition;
            go.transform.localPosition = new Vector3(0f, lp.y, 0f);
        }

        void Update()
        {
            // Kein Selbstdrehen mehr: der Spieler dreht die Figur selbst,
            // indem er mit gedrueckter Maus (oder dem Finger) zieht.
            if (!aktiv || aktuelleFigur == null) return;

            float dx = 0f;
            var maus = UnityEngine.InputSystem.Mouse.current;
            if (maus != null && maus.leftButton.isPressed)
                dx = maus.delta.ReadValue().x;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
                dx = touch.primaryTouch.delta.ReadValue().x;

            if (Mathf.Abs(dx) > 0.01f)
                aktuelleFigur.transform.Rotate(0f, -dx * 0.4f, 0f, Space.World);
        }
    }
}
