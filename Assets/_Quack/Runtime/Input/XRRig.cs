using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace Quack
{
    /// <summary>
    /// Minimal OpenXR rig built in code: tracked head (the main camera), two tracked controllers with
    /// laser pointers. Trigger (either hand) = press/drag through the shared PointerInteractor (same commands as
    /// the mouse). Station floor pads = point + trigger to teleport (default); left stick = walk where you look only if smooth walking is enabled (with a comfort vignette); right stick = 30° snap turn; right grip + right stick = spin the plot.
    /// </summary>
    public sealed class XRRig : MonoBehaviour
    {
        public PointerInteractor Pointer;
        public Transform PlotRoot;
        public AccessSettings Access;
        public System.Action ActivityNoted;
        public System.Action TalkPressed, TalkReleased;   // push-to-talk: hold the LEFT grip
        public System.Action PauseToggled;                // left controller menu button
        InputAction menuL;

        Transform right, left;
        LineRenderer laserR, laserL;
        Transform reticle;
        InputAction triggerR, triggerL, stick, stickL, gripR, gripL;
        Transform head;
        bool snapArmed = true;
        public float MoveSpeed = 1.2f;      // m/s, comfortable walking pace
        // walkable area (room is x +-3.8, z -4.2..3.9): keeps the learner near the lesson instead of wandering off
        public const float WalkHalfWidth = 3.3f, WalkMinZ = -3.7f, WalkMaxZ = 3.3f;
        public float SnapDegrees = 30f;
        Transform activeHand;

        public static XRRig Build(Camera cam, Vector3 standAt, Vector3 lookAt, Material lineMat, Material reticleMat)
        {
            var origin = new GameObject("XR Origin").transform;
            origin.position = new Vector3(standAt.x, 0, standAt.z);
            var flat = lookAt - standAt; flat.y = 0;
            origin.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

            // head: the existing main camera becomes the tracked HMD camera
            cam.transform.SetParent(origin, false);
            cam.transform.localPosition = new Vector3(0, 1.6f, 0);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.02f;
            var head = cam.gameObject.AddComponent<TrackedPoseDriver>();
            head.positionInput = new InputActionProperty(Bind("HeadPos", "<XRHMD>/centerEyePosition"));
            head.rotationInput = new InputActionProperty(Bind("HeadRot", "<XRHMD>/centerEyeRotation", "Quaternion"));

            var rig = origin.gameObject.AddComponent<XRRig>();
            rig.right = Hand(origin, "Right", "RightHand");
            rig.left = Hand(origin, "Left", "LeftHand");
            rig.laserR = Laser(rig.right, lineMat);
            rig.laserL = Laser(rig.left, lineMat);
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());
            dot.GetComponent<Renderer>().sharedMaterial = reticleMat;
            dot.transform.localScale = Vector3.one * 0.015f;
            rig.reticle = dot.transform;

            rig.triggerR = Bind("TriggerR", "<XRController>{RightHand}/triggerPressed", "Button");
            rig.triggerL = Bind("TriggerL", "<XRController>{LeftHand}/triggerPressed", "Button");
            rig.stick = Bind("StickR", "<XRController>{RightHand}/primary2DAxis", "Vector2");
            rig.stickL = Bind("StickL", "<XRController>{LeftHand}/primary2DAxis", "Vector2");
            rig.gripR = Bind("GripR", "<XRController>{RightHand}/gripPressed", "Button");
            rig.gripL = Bind("GripL", "<XRController>{LeftHand}/gripPressed", "Button");
            rig.menuL = new InputAction("MenuL", InputActionType.Button);
            rig.menuL.AddBinding("<XRController>{LeftHand}/menuButton");
            rig.menuL.AddBinding("<XRController>{LeftHand}/menu");
            rig.menuL.Enable();
            rig.head = cam.transform;
            return rig;
        }

        static InputAction Bind(string name, string path, string expected = "Vector3")
        {
            var type = expected == "Button" ? InputActionType.Button : InputActionType.Value;
            var a = new InputAction(name, type, path, expectedControlType: expected);
            a.Enable();
            return a;
        }

        static Transform Hand(Transform origin, string name, string usage)
        {
            var t = new GameObject(name + " Controller").transform;
            t.SetParent(origin, false);
            var tpd = t.gameObject.AddComponent<TrackedPoseDriver>();
            // the aim ("pointer") pose is the natural ray direction on OpenXR controllers
            tpd.positionInput = new InputActionProperty(Bind(name + "Pos", $"<XRController>{{{usage}}}/pointerPosition"));
            tpd.rotationInput = new InputActionProperty(Bind(name + "Rot", $"<XRController>{{{usage}}}/pointerRotation", "Quaternion"));
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(t, false);
            body.transform.localScale = new Vector3(0.03f, 0.03f, 0.1f);
            body.transform.localPosition = new Vector3(0, 0, -0.05f);
            return t;
        }

        static LineRenderer Laser(Transform hand, Material mat)
        {
            var lr = hand.gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.sharedMaterial = mat;
            lr.startWidth = 0.004f; lr.endWidth = 0.002f;
            lr.startColor = new Color(0.2f, 0.9f, 0.85f, 0.9f); lr.endColor = new Color(0.2f, 0.9f, 0.85f, 0.2f);
            lr.positionCount = 2;
            return lr;
        }

        void Update()
        {
            if (menuL.WasPressedThisFrame()) PauseToggled?.Invoke();
            if (gripL.WasPressedThisFrame()) TalkPressed?.Invoke();
            if (gripL.WasReleasedThisFrame()) TalkReleased?.Invoke();
            if (Pointer == null) return;
            // whichever trigger was pressed last owns the pointer
            if (triggerR.WasPressedThisFrame()) Begin(right);
            else if (triggerL.WasPressedThisFrame()) Begin(left);
            if (activeHand != null)
            {
                var a = activeHand == right ? triggerR : triggerL;
                if (a.IsPressed()) Pointer.Drag(RayOf(activeHand));
                else { Pointer.Release(); activeHand = null; }
            }
            DrawLaser(right, laserR, true);
            DrawLaser(left, laserL, false);

            // left stick: walk where you look (flattened to the floor) - only if the learner opted in (comfort)
            var mv = stickL.ReadValue<Vector2>();
            bool walking = Access != null && Access.SmoothWalk && mv.sqrMagnitude > 0.04f;
            SetVignette(walking ? 1f : 0f);
            if (walking)
            {
                var fwd = head.forward; fwd.y = 0; fwd.Normalize();
                var right = head.right; right.y = 0; right.Normalize();
                transform.position += (fwd * mv.y + right * mv.x) * MoveSpeed * Time.deltaTime;
                // stay in the learning area: the room's floor, a step back from the walls, not under the lab table
                var p = transform.position;
                p.x = Mathf.Clamp(p.x, -WalkHalfWidth, WalkHalfWidth);
                p.z = Mathf.Clamp(p.z, WalkMinZ, WalkMaxZ);
                transform.position = p;
            }

            var v = stick.ReadValue<Vector2>();
            if (gripR.IsPressed())
            {
                // hold right grip + right stick: spin the hologram
                if (PlotRoot != null && Mathf.Abs(v.x) > 0.2f && activeHand == null)
                    PlotRoot.Rotate(Vector3.up, -v.x * 60f * Time.deltaTime, Space.World);
            }
            else
            {
                // right stick: snap turn around the player's head (comfortable, no smooth rotation)
                if (snapArmed && Mathf.Abs(v.x) > 0.7f)
                {
                    transform.RotateAround(head.position, Vector3.up, Mathf.Sign(v.x) * SnapDegrees);
                    snapArmed = false;
                }
                else if (Mathf.Abs(v.x) < 0.3f) snapArmed = true;
            }
        }

        // ---------- comfort: teleport fade + walking vignette (both are camera-attached quads) ----------

        Renderer fade, vignette;
        float vignetteK;
        Coroutine teleporting;

        Renderer CameraQuad(string name, Texture2D tex, float dist, float size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(head, false);
            q.transform.localPosition = new Vector3(0, 0, dist);
            q.transform.localScale = Vector3.one * size;
            var m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, renderQueue = 4000 };
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m.color = new Color(0, 0, 0, 0);
            q.SetActive(false);
            return r;
        }

        static Texture2D RadialMask()
        {
            const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                tex.SetPixel(x, y, new Color(0, 0, 0, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f, 0.95f, d))));
            }
            tex.Apply(); return tex;
        }

        void SetVignette(float target)
        {
            if (vignette == null) vignette = CameraQuad("comfort vignette", RadialMask(), 0.3f, 0.75f);
            vignetteK = Mathf.MoveTowards(vignetteK, target, Time.deltaTime * 4f);
            vignette.gameObject.SetActive(vignetteK > 0.01f);
            vignette.sharedMaterial.color = new Color(1, 1, 1, vignetteK);
        }

        /// <summary>Teleport the learner to a floor point facing a target, behind a short fade.</summary>
        public void TeleportTo(Vector3 floor, Vector3 lookAt)
        {
            if (teleporting != null) StopCoroutine(teleporting);
            teleporting = StartCoroutine(Teleport(floor, lookAt));
        }

        System.Collections.IEnumerator Teleport(Vector3 floor, Vector3 lookAt)
        {
            if (fade == null) { var white = Texture2D.whiteTexture; fade = CameraQuad("teleport fade", white, 0.25f, 2f); }
            fade.gameObject.SetActive(true);
            for (float t = 0; t < 0.15f; t += Time.deltaTime) { fade.sharedMaterial.color = new Color(0, 0, 0, t / 0.15f); yield return null; }
            fade.sharedMaterial.color = Color.black;
            // keep the head (not the rig origin) over the target spot, and face the station
            var headOffset = head.position - transform.position; headOffset.y = 0;
            transform.position = new Vector3(floor.x, 0, floor.z) - headOffset;
            var flat = lookAt - new Vector3(floor.x, lookAt.y, floor.z); flat.y = 0;
            if (flat.sqrMagnitude > 0.01f)
            {
                float yawNow = head.eulerAngles.y, yawWant = Quaternion.LookRotation(flat.normalized).eulerAngles.y;
                transform.RotateAround(head.position, Vector3.up, Mathf.DeltaAngle(yawNow, yawWant));
            }
            yield return null;
            for (float t = 0; t < 0.2f; t += Time.deltaTime) { fade.sharedMaterial.color = new Color(0, 0, 0, 1 - t / 0.2f); yield return null; }
            fade.gameObject.SetActive(false);
            teleporting = null;
        }

        void Begin(Transform hand)
        {
            ActivityNoted?.Invoke();
            if (activeHand != null) Pointer.Release();
            activeHand = hand;
            if (Pointer.Press(RayOf(hand)) == PointerInteractor.Result.Empty) activeHand = null;
        }

        static Ray RayOf(Transform hand) => new Ray(hand.position, hand.forward);

        void DrawLaser(Transform hand, LineRenderer lr, bool showReticle)
        {
            var ray = RayOf(hand);
            float len = 8f;   // long enough to reach the station pads across the room
            if (Physics.Raycast(ray, out var hit, len)) len = hit.distance;
            lr.SetPosition(0, ray.origin);
            lr.SetPosition(1, ray.GetPoint(len));
            if (showReticle) { reticle.position = ray.GetPoint(len); reticle.gameObject.SetActive(len < 8f); }
        }

        void OnDestroy()
        {
            triggerR?.Dispose(); triggerL?.Dispose(); stick?.Dispose(); stickL?.Dispose(); gripR?.Dispose(); gripL?.Dispose(); menuL?.Dispose();
        }
    }
}
