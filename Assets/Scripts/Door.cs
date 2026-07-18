using UnityEngine;

namespace NeonCatch
{
    // Tür, die sich beim Öffnen um eine Scharnier-Achse (hingeWorldPosition) dreht.
    // Wird per PlayerController.Toggle() nach einem Linksklick aufgerufen.
    public class Door : MonoBehaviour
    {
        public Vector3 hingeWorldPosition;
        public float   openAngle = 100f;
        public float   openSeconds = 0.6f;

        bool       isOpen;
        float      t; // 0 = zu, 1 = offen
        bool       initialized;
        Vector3    closedPos;
        Quaternion closedRot;
        Vector3    openPos;
        Quaternion openRot;

        void Init()
        {
            if (initialized) return;
            initialized = true;

            closedPos = transform.position;
            closedRot = transform.rotation;

            // Position/Rotation berechnen, die entstehen, wenn man das Objekt
            // um den Scharnierpunkt dreht (Transform.RotateAround-Logik, vorab berechnet)
            Quaternion swing = Quaternion.AngleAxis(openAngle, Vector3.up);
            openPos = hingeWorldPosition + swing * (closedPos - hingeWorldPosition);
            openRot = swing * closedRot;
        }

        public void Toggle()
        {
            Init();
            isOpen = !isOpen;
        }

        void Update()
        {
            Init();
            float target = isOpen ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;

            t = Mathf.MoveTowards(t, target, Time.deltaTime / Mathf.Max(0.01f, openSeconds));
            transform.position = Vector3.Lerp(closedPos, openPos, t);
            transform.rotation = Quaternion.Slerp(closedRot, openRot, t);
        }
    }
}
