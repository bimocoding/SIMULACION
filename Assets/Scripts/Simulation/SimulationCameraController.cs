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
            SeguirEstudiante = 7    // 7. Seguir Estudiante
        }

        [Header("Modo de Cámara Activo")]
        public CameraViewMode currentMode = CameraViewMode.Principal;
        public Transform followTarget;

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

        private void Awake()
        {
            DisableOtherCameras();
            SetViewMode(currentMode, instant: true);
        }

        private void Start()
        {
            DisableOtherCameras();
            SetViewMode(currentMode, instant: true);
        }

        private void Update()
        {
            CheckKeyboardInput();
        }

        private void LateUpdate()
        {
            if (currentMode == CameraViewMode.SeguirEstudiante)
            {
                UpdateFollowAgentCamera();
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
            currentMode = mode;
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
                case CameraViewMode.SeguirEstudiante:
                    FindAgentToFollow();
                    if (followTarget != null)
                    {
                        targetPos = followTarget.position - followTarget.forward * 8.5f + Vector3.up * 4.0f;
                        Vector3 lookDir = (followTarget.position + Vector3.up * 1.5f) - targetPos;
                        targetRot = Quaternion.LookRotation(lookDir);
                    }
                    else
                    {
                        targetPos = PosPrincipal;
                        targetRot = Quaternion.Euler(RotPrincipal);
                    }
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

        public void FindAgentToFollow()
        {
            AgentPedestrian[] agents = FindObjectsByType<AgentPedestrian>(FindObjectsSortMode.None);
            if (agents != null && agents.Length > 0)
            {
                followTarget = agents[agents.Length - 1].transform;
            }
        }

        private void UpdateFollowAgentCamera()
        {
            if (followTarget == null)
            {
                FindAgentToFollow();
                if (followTarget == null) return;
            }

            Vector3 desiredPos = followTarget.position - followTarget.forward * 8.5f + Vector3.up * 4.0f;
            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.unscaledDeltaTime * 8.0f);

            Vector3 lookTarget = followTarget.position + Vector3.up * 1.5f;
            Vector3 dir = lookTarget - transform.position;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.unscaledDeltaTime * 8.0f);
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
                    if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) SetViewMode(CameraViewMode.SeguirEstudiante, true);
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
                else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) SetViewMode(CameraViewMode.SeguirEstudiante, true);
            }
            catch { }
        }
    }
}
