using System;
using UnityEngine;

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
        // 1. Principal: Plaza exterior frente a la entrada viendo árbol, cilindro verde a la izq y puente
        public static readonly Vector3 PosPrincipal = new Vector3(-260.0f, 6.5f, 110.0f);
        public static readonly Vector3 RotPrincipal = new Vector3(-6.0f, 73.0f, 0.0f);

        // 2. Pasillo Piso 1: Planta baja viendo hacia el núcleo rojo central, columna izq y patio
        public static readonly Vector3 PosPasilloP1 = new Vector3(38.0f, 17.8f, 48.0f);
        public static readonly Vector3 RotPasilloP1 = new Vector3(4.0f, 52.0f, 0.0f);

        // 3. Salones Piso 1: Corredor de aulas piso 1 viendo puertas, bancas y núcleo rojo a la izq
        public static readonly Vector3 PosSalonesP1 = new Vector3(145.0f, 18.0f, 48.0f);
        public static readonly Vector3 RotSalonesP1 = new Vector3(5.0f, 90.0f, 0.0f);

        // 4. Pasillo Piso 2: Planta alta con columna izq, baranda de celosía, mostrador y luces lineales
        public static readonly Vector3 PosPasilloP2 = new Vector3(38.0f, 75.0f, 48.0f);
        public static readonly Vector3 RotPasilloP2 = new Vector3(4.0f, 52.0f, 0.0f);

        // 5. Salones Piso 2: Corredor de aulas piso 2 longitudinal con luces de techo y puertas
        public static readonly Vector3 PosSalonesP2 = new Vector3(145.0f, 75.0f, 48.0f);
        public static readonly Vector3 RotSalonesP2 = new Vector3(5.0f, 90.0f, 0.0f);

        // 6. Panorámica Aérea General 45°
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
                characterController.radius = 0.45f;
                characterController.height = 1.6f;
                characterController.center = Vector3.zero;
                characterController.minMoveDistance = 0f;
                characterController.stepOffset = 0.4f;
                characterController.slopeLimit = 55f;
            }
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
                // Iniciar yaw y pitch desde la rotación actual de la cámara
                freeCamYaw = transform.eulerAngles.y;
                freeCamPitch = transform.eulerAngles.x;
                if (freeCamPitch > 180f) freeCamPitch -= 360f;

                if (characterController != null) characterController.enabled = true;
                Debug.Log($"[SimulationCamera] Modo Cámara Libre Activado (Pos: {transform.position})");
                return;
            }

            // Para vistas fijas 1 a 6: Desactivar CharacterController para teleportar limpio
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

            // 1. Mover la cámara del juego (Game View)
            transform.position = targetPos;
            transform.rotation = targetRot;

            // 2. Sincronizar también la vista de edición (Scene View) en el Editor de Unity
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

            // 1. Rotación con el ratón (manteniendo Clic Derecho, o directo)
            if (Input.GetMouseButton(1))
            {
                float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
                float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

                freeCamYaw += mouseX;
                freeCamPitch -= mouseY;
                freeCamPitch = Mathf.Clamp(freeCamPitch, -85.0f, 85.0f);

                transform.rotation = Quaternion.Euler(freeCamPitch, freeCamYaw, 0f);
            }

            // 2. Cálculo de Velocidad y Modificadores
            float currentSpeed = freeCamSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                currentSpeed *= freeCamSprintMultiplier;
            }
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                currentSpeed *= 0.35f;
            }

            // 3. Vector de Desplazamiento Relativo a la Orientación
            Vector3 moveDirection = Vector3.zero;

            // Ejes horizontales
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) moveDirection += transform.forward;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) moveDirection -= transform.forward;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) moveDirection += transform.right;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) moveDirection -= transform.right;

            // Eje vertical (Volar / Bajar)
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E)) moveDirection += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C)) moveDirection -= Vector3.up;

            if (moveDirection.sqrMagnitude > 0.001f)
            {
                moveDirection.Normalize();

                // FÍSICAS CONTRA MUROS: characterController.Move() usa PhysX
                // Bloquea completamente el paso a través de muros, columnas, bancas y barandas
                characterController.Move(moveDirection * currentSpeed * Time.unscaledDeltaTime);
            }
        }

        private void CheckKeyboardInput()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
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
