using UnityEngine;

namespace NeonCatch
{
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public float distance     = 6f;
        public float heightOffset = 1.6f;
        public float sensitivity  = 3f;
        public float minPitch     = -15f;
        public float maxPitch     = 60f;

        float yaw;
        float pitch = 20f;

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;

            yaw   += Input.GetAxis("Mouse X") * sensitivity;
            pitch -= Input.GetAxis("Mouse Y") * sensitivity;
            pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);

            Quaternion rot   = Quaternion.Euler(pitch, yaw, 0f);
            Vector3    pivot = target.position + Vector3.up * heightOffset;
            transform.position = pivot + rot * new Vector3(0f, 0f, -distance);
            transform.LookAt(pivot);
        }
    }
}
