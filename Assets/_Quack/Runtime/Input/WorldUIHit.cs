using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Makes a uGUI control on a world-space canvas usable by any ray (VR controller or mouse) without
    /// a device-specific UI module: a BoxCollider matches the control's rect, and presses/drags are
    /// translated into Button clicks, Toggle flips and Slider values. Keeps one interaction path for all devices.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorldUIHit : MonoBehaviour
    {
        public static WorldUIHit Active { get; private set; }
        public System.Action OnPressExtra, OnReleaseExtra;

        Selectable target;
        RectTransform rt;

        public static void AttachAll(Canvas canvas)
        {
            foreach (var s in canvas.GetComponentsInChildren<Selectable>(true))
                if (s.GetComponent<WorldUIHit>() == null && !(s is InputField)) s.gameObject.AddComponent<WorldUIHit>();
        }

        void Awake()
        {
            target = GetComponent<Selectable>();
            rt = (RectTransform)transform;
            Fit();
        }

        void LateUpdate() => Fit();   // layout groups resize controls after creation

        void Fit()
        {
            var box = GetComponent<BoxCollider>();
            var r = rt.rect;
            box.center = new Vector3(r.center.x, r.center.y, 0);
            box.size = new Vector3(Mathf.Max(1, r.width), Mathf.Max(1, r.height), 4);
        }

        public void Press(Vector3 worldPoint)
        {
            if (target == null || !target.IsInteractable() || !target.gameObject.activeInHierarchy) return;
            Active = this;
            OnPressExtra?.Invoke();
            // forward to EventTrigger so slider begin/end-edit hooks fire exactly as with a mouse
            var et = GetComponent<EventTrigger>(); if (et) et.OnPointerDown(new PointerEventData(EventSystem.current));
            switch (target)
            {
                case Button b: b.onClick.Invoke(); Active = null; break;
                case Toggle t: t.isOn = !t.isOn; Active = null; break;
                case Slider s: SetSlider(s, worldPoint); break;
            }
        }

        public void Drag(Ray ray)
        {
            if (target is Slider s)
            {
                var plane = new Plane(rt.forward, rt.position);
                if (plane.Raycast(ray, out float d)) SetSlider(s, ray.GetPoint(d));
            }
        }

        public void Release()
        {
            if (Active == this)
            {
                Active = null;
                OnReleaseExtra?.Invoke();
                var et = GetComponent<EventTrigger>(); if (et) et.OnPointerUp(new PointerEventData(EventSystem.current));
            }
        }

        void SetSlider(Slider s, Vector3 worldPoint)
        {
            var local = rt.InverseTransformPoint(worldPoint);
            var r = rt.rect;
            float t = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
            s.value = Mathf.Lerp(s.minValue, s.maxValue, t);
        }
    }
}
