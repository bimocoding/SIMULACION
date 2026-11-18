using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Uniguajira.Simulation
{
    public class SimulationCameraController : MonoBehaviour
    {
        public enum CameraViewMode
        {
            Principal = 1,          // 1. Principal (Entrada Fachada)
            PasilloPiso1 = 2,       // 2. Pasillo Piso 1
            SalonesPiso1 = 3,       // 3. Salones Piso 1
            PasilloPiso2 = 4,       // 4. Pasillo Piso 2
            SalonesPiso2 = 5,       // 5. Salones Piso 2
            Panoramica = 6,         // 6. Panorámica Aérea
            CamaraLibre = 7         // 7. Cámara Libre (Navegación con Físicas)
        }

        [Header("Modo de Cámara Activo")]
        public CameraViewMode currentMode = CameraViewMode.Principal;

        [Header("Configuración de Cámara Libre")]
        public float freeCamSpeed = 22.0f;
        public float freeCamSprintMultiplier = 2.5f;
        public float mouseSensitivity = 2.5f;

        [Header("Coordenadas Exactas (Basadas en las Capturas del Modelo)")]
        public static readonly Vector3 PosPrincipal = new Vector3(-260.0f, 6.5f, 110.0f);
        public static readonly Vector3 RotPrincipal = new Vector3(-6.0f, 73.0f, 0.0f);

        public static readonly Vector3 PosPasilloP1 = new Vector3(38.0f, 17.8f, 48.0f);
        public static readonly Vector3 RotPasilloP1 = new Vector3(4.0f, 52.0f, 0.0f);

        public static readonly Vector3 PosSalonesP1 = new Vector3(145.0f, 18.0f, 48.0f);
        public static readonly Vector3 RotSalonesP1 = new Vector3(5.0f, 90.0f, 0.0f);

        public static readonly Vector3 PosPasilloP2 = new Vector3(38.0f, 75.0f, 48.0f);
        public static readonly Vector3 RotPasilloP2 = new Vector3(4.0f, 52.0f, 0.0f);

        public static readonly Vector3 PosSalonesP2 = new Vector3(145.0f, 75.0f, 48.0f);
        public static readonly Vector3 RotSalonesP2 = new Vector3(5.0f, 90.0f, 0.0f);

        public static readonly Vector3 PosPanoramica = new Vector3(180.0f, 420.0f, -220.0f);
        public static readonly Vector3 RotPanoramica = new Vector3(50.0f, 5.0f, 0.0f);

        private CharacterController characterController;
        private float freeCamYaw = 0f;
        private float freeCamPitch = 0f;

        private void Awake()
        {
            EnsureCharacterController();
            DisableOtherCameras();
            SetViewMode(currentMode, instant: true);
        }

        private void Start()
        {
            EnsureCharacterController();
            DisableOtherCameras();
            SetViewMode(currentMode, instant: true);
        }

        private void Update()
        {
            CheckKeyboardInput();

            if (currentMode == CameraViewMode.CamaraLibre)
            {
                UpdateFreeCamera();
            }
        }

        private void EnsureCharacterController()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
                if (characterController == null)
                {
                    characterController = gameObject.AddComponent<CharacterController>();
                }
            }

            // Forma esférica (height = 2 * radius) centrada en la cámara
            characterController.radius = 0.35f;
            characterController.height = 0.70f;
            characterController.center = Vector3.zero;
            characterController.minMoveDistance = 0f;
            characterController.stepOffset = 0.3f;
            characterController.slopeLimit = 65f;
            characterController.skinWidth = 0.02f;
        }

        public void DisableOtherCameras()
        {
            Camera myCam = GetComponent<Camera>();
            if (myCam != null)
            {
                myCam.enabled = true;
                myCam.depth = 100;
            }

            Camera[] allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in allCams)
            {
                if (c.gameObject != this.gameObject)
                {
                    c.enabled = false;
                }
            }
        }

        public void SetViewMode(CameraViewMode mode, bool instant = true)
        {
            DisableOtherCameras();
            EnsureCharacterController();

            currentMode = mode;

            if (mode == CameraViewMode.CamaraLibre)
            {
                // Iniciar yaw y pitch desde la orientación actual
                freeCamYaw = transform.eulerAngles.y;
                freeCamPitch = transform.eulerAngles.x;
                if (freeCamPitch > 180f) freeCamPitch -= 360f;

                if (characterController != null) characterController.enabled = true;
                Debug.Log($"[SimulationCamera] Modo Cámara Libre Activado (Pos: {transform.position})");
                return;
            }

            // Para vistas fijas 1 a 6: desactivar CharacterController para posicionamiento instantáneo
            if (characterController != null) characterController.enabled = false;

            Vector3 targetPos = PosPrincipal;
            Quaternion targetRot = Quaternion.Euler(RotPrincipal);

            switch (mode)
            {
                case CameraViewMode.Principal:
                    targetPos = PosPrincipal;
                    targetRot = Quaternion.Euler(RotPrincipal);
                    break;
                case CameraViewMode.PasilloPiso1:
                    targetPos = PosPasilloP1;
                    targetRot = Quaternion.Euler(RotPasilloP1);
                    break;
                case CameraViewMode.SalonesPiso1:
                    targetPos = PosSalonesP1;
                    targetRot = Quaternion.Euler(RotSalonesP1);
                    break;
                case CameraViewMode.PasilloPiso2:
                    targetPos = PosPasilloP2;
                    targetRot = Quaternion.Euler(RotPasilloP2);
                    break;
                case CameraViewMode.SalonesPiso2:
                    targetPos = PosSalonesP2;
                    targetRot = Quaternion.Euler(RotSalonesP2);
                    break;
                case CameraViewMode.Panoramica:
                    targetPos = PosPanoramica;
                    targetRot = Quaternion.Euler(RotPanoramica);
                    break;
            }

            transform.position = targetPos;
            transform.rotation = targetRot;

#if UNITY_EDITOR
            try
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv == null && UnityEditor.SceneView.sceneViews.Count > 0)
                {
                    sv = (UnityEditor.SceneView)UnityEditor.SceneView.sceneViews[0];
                }
                if (sv != null)
                {
                    sv.AlignViewToObject(transform);
                    sv.orthographic = false;
                    sv.Repaint();
                }
            }
            catch { }
#endif

            Debug.Log($"[SimulationCamera] Vista cambiada con éxito a: {mode} (Pos: {targetPos})");
        }

        private void UpdateFreeCamera()
        {
            EnsureCharacterController();
            if (!characterController.enabled) characterController.enabled = true;

            bool isLooking = false;
            float mouseDeltaX = 0f;
            float mouseDeltaY = 0f;

            bool moveForward = false;
            bool moveBack = false;
            bool moveLeft = false;
            bool moveRight = false;
            bool moveUp = false;
            bool moveDown = false;
            bool isSprint = false;
            bool isSlow = false;

            // 1. Lectura segura mediante el Nuevo Input System
#if ENABLE_INPUT_SYSTEM
            try
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    isLooking = mouse.rightButton.isPressed || mouse.leftButton.isPressed;
                    Vector2 d = mouse.delta.ReadValue();
                    mouseDeltaX = d.x * (mouseSensitivity * 0.08f);
                    mouseDeltaY = d.y * (mouseSensitivity * 0.08f);
                }

                var kb = Keyboard.current;
                if (kb != null)
                {
                    moveForward = kb.wKey.isPressed || kb.upArrowKey.isPressed;
                    moveBack = kb.sKey.isPressed || kb.downArrowKey.isPressed;
                    moveRight = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                    moveLeft = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                    moveUp = kb.spaceKey.isPressed || kb.eKey.isPressed;
                    moveDown = kb.leftCtrlKey.isPressed || kb.qKey.isPressed || kb.cKey.isPressed;
                    isSprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                    isSlow = kb.leftAltKey.isPressed;
                }
            }
            catch { }
#endif

            // 2. Fallback compatible con Legacy Input Manager (sin lanzar excepción si no está activo)
            try
            {
                if (!isLooking) isLooking = Input.GetMouseButton(1) || Input.GetMouseButton(0);
                if (isLooking && Mathf.Abs(mouseDeltaX) < 0.001f && Mathf.Abs(mouseDeltaY) < 0.001f)
                {
                    mouseDeltaX = Input.GetAxis("Mouse X") * mouseSensitivity;
                    mouseDeltaY = Input.GetAxis("Mouse Y") * mouseSensitivity;
                }

                if (!moveForward) moveForward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                if (!moveBack) moveBack = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
                if (!moveRight) moveRight = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                if (!moveLeft) moveLeft = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
                if (!moveUp) moveUp = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E);
                if (!moveDown) moveDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C);
                if (!isSprint) isSprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!isSlow) isSlow = Input.GetKey(KeyCode.LeftAlt);
            }
            catch { }

            // 3. Rotación de Cámara (Mouse Look)
            if (isLooking)
            {
                freeCamYaw += mouseDeltaX;
                freeCamPitch -= mouseDeltaY;
                freeCamPitch = Mathf.Clamp(freeCamPitch, -85.0f, 85.0f);

                transform.rotation = Quaternion.Euler(freeCamPitch, freeCamYaw, 0f);
            }

            // 4. Velocidad de Movimiento
            float currentSpeed = freeCamSpeed;
            if (isSprint) currentSpeed *= freeCamSprintMultiplier;
            if (isSlow) currentSpeed *= 0.35f;

            // 5. Dirección de Desplazamiento
            Vector3 moveDirection = Vector3.zero;
            if (moveForward) moveDirection += transform.forward;
            if (moveBack) moveDirection -= transform.forward;
            if (moveRight) moveDirection += transform.right;
            if (moveLeft) moveDirection -= transform.right;
            if (moveUp) moveDirection += Vector3.up;
            if (moveDown) moveDirection -= Vector3.up;

            if (moveDirection.sqrMagnitude > 0.001f)
            {
                moveDirection.Normalize();
                Vector3 displacement = moveDirection * currentSpeed * Time.unscaledDeltaTime;

                // FÍSICAS DE MUROS CON PHYSX
                if (characterController != null && characterController.enabled)
                {
                    Vector3 posBefore = transform.position;
                    characterController.Move(displacement);

                    // Si queda atascado por overlap inicial con un collider no-convexo
                    if ((transform.position - posBefore).sqrMagnitude < 0.00001f)
                    {
                        RaycastHit hit;
                        if (!Physics.SphereCast(posBefore, 0.25f, moveDirection, out hit, displacement.magnitude, ~0, QueryTriggerInteraction.Ignore))
                        {
                            transform.position = posBefore + displacement;
                        }
                        else if (hit.distance > 0.05f)
                        {
                            transform.position = posBefore + moveDirection * (hit.distance - 0.02f);
                        }
                    }
                }
                else
                {
                    // Fallback con SphereCast para respetar paredes aun sin CharacterController
                    RaycastHit hit;
                    if (Physics.SphereCast(transform.position, 0.30f, moveDirection, out hit, displacement.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    {
                        float allowed = Mathf.Max(0, hit.distance - 0.02f);
                        transform.position += moveDirection * allowed;
                    }
                    else
                    {
                        transform.position += displacement;
                    }
                }
            }
        }

        private void CheckKeyboardInput()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SetViewMode(CameraViewMode.Principal, true);
                    if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SetViewMode(CameraViewMode.PasilloPiso1, true);
                    if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SetViewMode(CameraViewMode.SalonesPiso1, true);
                    if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) SetViewMode(CameraViewMode.PasilloPiso2, true);
                    if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) SetViewMode(CameraViewMode.SalonesPiso2, true);
                    if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) SetViewMode(CameraViewMode.Panoramica, true);
                    if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) SetViewMode(CameraViewMode.CamaraLibre, true);
                    return;
                }
            }
            catch { }
#endif

            try
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetViewMode(CameraViewMode.Principal, true);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetViewMode(CameraViewMode.PasilloPiso1, true);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetViewMode(CameraViewMode.SalonesPiso1, true);
                else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SetViewMode(CameraViewMode.PasilloPiso2, true);
                else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) SetViewMode(CameraViewMode.SalonesPiso2, true);
                else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) SetViewMode(CameraViewMode.Panoramica, true);
                else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) SetViewMode(CameraViewMode.CamaraLibre, true);
            }
            catch { }
        }
    }
}
