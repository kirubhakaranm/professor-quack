using UnityEngine;

namespace Quack
{
    /// <summary>
    /// VR comfort: keeps a world-space panel in front of the learner's head. It stays still while the learner looks
    /// around it, and glides back to centre only when they turn well away or walk off (no head-locked panels).
    /// </summary>
    public sealed class LazyFollow : MonoBehaviour
    {
        public Transform Head;
        public float Distance = 1.0f;       // metres in front of the head
        public float Below = 0.35f;         // metres below eye level
        public float Pitch = 20f;           // tilt toward the eyes (degrees)
        public float RecentreAngle = 35f;   // yaw difference that triggers a recentre
        public float RecentreDistance = 0.45f;
        bool moving;

        Vector3 Target(out Quaternion rot)
        {
            var fwd = Head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(Pitch, 0, 0);
            return Head.position + fwd * Distance + Vector3.down * Below;
        }

        public void Snap()
        {
            if (Head == null) return;
            transform.position = Target(out var r); transform.rotation = r;
        }

        void OnEnable() => Snap();

        void LateUpdate()
        {
            if (Head == null) return;
            var target = Target(out var rot);
            var toPanel = transform.position - Head.position; toPanel.y = 0;
            var fwd = Head.forward; fwd.y = 0;
            float yawOff = toPanel.sqrMagnitude > 1e-4f && fwd.sqrMagnitude > 1e-4f ? Vector3.Angle(toPanel, fwd) : 0;
            if (!moving && (yawOff > RecentreAngle || (transform.position - target).magnitude > RecentreDistance)) moving = true;
            if (!moving) return;
            float k = 1 - Mathf.Exp(-Time.deltaTime * 4f);
            transform.position = Vector3.Lerp(transform.position, target, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, k);
            if ((transform.position - target).magnitude < 0.02f) moving = false;
        }
    }
}
