using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Uniguajira.Simulation
{
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        [Header("Escenario Activo")]
        public SimulationScenario activeScenario = SimulationScenario.EscenarioBase;

        [Header("Configuración Temporal")]
        [Tooltip("Escala de aceleración de tiempo (1x a 30x)")]
        [Range(1f, 30f)]
        public float timeScale = 5f;
        public float totalSessionDurationMinutes = 60f;

        [Header("Puntos de Acceso / Spawners")]
        public Transform mainEntranceDoor;
        public Transform backEntranceDoor;

        [Header("Prefab de Agentes")]
        public GameObject agentPrefab;

        [Header("Oficinas de Servicio Registradas")]
        public List<ServiceOffice> registeredOffices = new List<ServiceOffice>();

        [Header("Datos de Campo Observados (Cortes de 10 min)")]
        public int[] arrivalsPerTenMin = new int[] { 18, 14, 11, 8, 5, 10 };
        public string[] intervalNames = new string[]
        {
            "05:40 - 05:50 PM",
            "05:50 - 06:00 PM",
            "06:00 - 06:10 PM",
            "06:10 - 06:20 PM",
            "06:20 - 06:30 PM",
            "06:30 - 06:40 PM"
        };

        // Estado interno
        private int currentAgentIdCounter = 0;
        private readonly List<AgentMetrics> completedMetrics = new List<AgentMetrics>();
        private readonly List<AgentPedestrian> activeAgents = new List<AgentPedestrian>();
        private readonly List<ReplicaSummary> replicaHistory = new List<ReplicaSummary>();

        private float simulationStartTime;
        private bool isRunning = false;
        private int currentReplicaNumber = 1;
        private int currentSliceIndex = 0;
        private int generatedInCurrentSlice = 0;

        private SimulationCameraController cameraController;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            EnsureModelAndEnvironmentInScene();

            cameraController = FindAnyObjectByType<SimulationCameraController>();
            if (cameraController == null && Camera.main != null)
            {
                cameraController = Camera.main.gameObject.AddComponent<SimulationCameraController>();
            }

            Time.timeScale = timeScale;
            ApplyScenarioSettings();
            StartSimulation();

            // Vista principal frontal inicial por defecto
            SwitchCamera(SimulationCameraController.CameraViewMode.Principal);
        }

        private void EnsureModelAndEnvironmentInScene()
        {
            // 0. Desactivar y eliminar cualquier cámara secundaria importada en el FBX
            Camera[] allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in allCams)
            {
                if (c != Camera.main && c.gameObject.name != "Main Camera")
                {
                    c.enabled = false;
                    Debug.Log($"[Simulation] Desactivando cámara secundaria de FBX: {c.gameObject.name}");
                }
            }
            if (Camera.main != null)
            {
                Camera.main.depth = 100;
                Camera.main.enabled = true;
            }

            // 1. Asegurar que el Bloque Administrativo 3D esté en la escena
            GameObject block = GameObject.Find("Bloque_Administrativo_A");
            if (block == null)
            {
#if UNITY_EDITOR
                GameObject modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bloque_Administrativo_A.fbx");
                if (modelAsset != null)
                {
                    block = Instantiate(modelAsset, Vector3.zero, Quaternion.identity);
                    block.name = "Bloque_Administrativo_A";
                    Debug.Log("[Simulation] Bloque_Administrativo_A instanciado en la escena dinámicamente.");
                }
#endif
            }

            // 2. Asegurar Plano de Suelo para contraste visual y sombras
            GameObject ground = GameObject.Find("Simulation_Ground_Plaza");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Simulation_Ground_Plaza";
                ground.transform.position = new Vector3(-200.0f, 0.5f, -200.0f);
                ground.transform.localScale = new Vector3(80f, 1f, 80f); // 800m x 800m

                Renderer gr = ground.GetComponent<Renderer>();
                if (gr != null)
                {
                    gr.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    gr.material.color = new Color(0.82f, 0.80f, 0.76f); // Tono concreto plaza Uniguajira
                }
            }
        }

        public void ApplyScenarioSettings()
        {
            foreach (var office in registeredOffices)
            {
                if (office != null && office.officeType == OfficeType.ORI)
                {
                    office.serverCount = (activeScenario == SimulationScenario.EscenarioBase) ? 1 : 2;
                }
            }
        }

        public void StartSimulation()
        {
            simulationStartTime = Time.time;
            isRunning = true;
            completedMetrics.Clear();
            currentAgentIdCounter = 0;
            currentSliceIndex = 0;
            generatedInCurrentSlice = 0;

            foreach (var a in activeAgents)
            {
                if (a != null) Destroy(a.gameObject);
            }
            activeAgents.Clear();

            StopAllCoroutines();
            StartCoroutine(MasterSimulationCoroutine());
        }

        private IEnumerator MasterSimulationCoroutine()
        {
            float sliceDurationSimMinutes = totalSessionDurationMinutes / arrivalsPerTenMin.Length;
            float sliceDurationSec = sliceDurationSimMinutes * 60f;

            for (int i = 0; i < arrivalsPerTenMin.Length; i++)
            {
                currentSliceIndex = i;
                generatedInCurrentSlice = 0;
                int targetArrivals = arrivalsPerTenMin[i];
                float intervalBetweenArrivals = sliceDurationSec / Mathf.Max(1, targetArrivals);

                for (int a = 0; a < targetArrivals; a++)
                {
                    SpawnAgent();
                    generatedInCurrentSlice++;

                    float jitter = UnityEngine.Random.Range(0.65f, 1.35f);
                    yield return new WaitForSeconds(intervalBetweenArrivals * jitter);
                }
            }

            while (activeAgents.Count > 0)
            {
                yield return new WaitForSeconds(1.0f);
            }

            isRunning = false;
            RecordReplicaSummary();
        }

        private void SpawnAgent()
        {
            if (registeredOffices.Count == 0) return;

            currentAgentIdCounter++;

            float mainDoorProb = (activeScenario == SimulationScenario.EscenarioBase) ? 0.80f : 0.50f;
            bool spawnAtMain = UnityEngine.Random.value <= mainDoorProb;
            Transform spawnPoint = spawnAtMain ? mainEntranceDoor : (backEntranceDoor != null ? backEntranceDoor : mainEntranceDoor);
            string doorName = spawnAtMain ? "Puerta Principal" : "Puerta Trasera";

            Vector3 spawnPos = (spawnPoint != null) ? spawnPoint.position : new Vector3(-11.7f, 1.1f, -195.0f);
            Quaternion spawnRot = (spawnPoint != null) ? spawnPoint.rotation : Quaternion.identity;
            spawnPos += new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0, UnityEngine.Random.Range(-1.5f, 1.5f));

            GameObject agentObj;
            if (agentPrefab != null)
            {
                agentObj = Instantiate(agentPrefab, spawnPos, spawnRot);
            }
            else
            {
                agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                agentObj.transform.position = spawnPos;
                agentObj.transform.localScale = new Vector3(1.4f, 1.3f, 1.4f);
            }

            agentObj.name = $"Estudiante_{currentAgentIdCounter}";
            AgentPedestrian agent = agentObj.GetComponent<AgentPedestrian>();
            if (agent == null) agent = agentObj.AddComponent<AgentPedestrian>();

            ServiceOffice office = SelectDestinationOffice();
            Transform exitPoint = spawnPoint;

            agent.Initialize(currentAgentIdCounter, doorName, office, exitPoint);
            activeAgents.Add(agent);
        }

        private ServiceOffice SelectDestinationOffice()
        {
            float r = UnityEngine.Random.value;
            if (r < 0.30f) return FindOffice(OfficeType.VicerrectoriaExtension);
            if (r < 0.55f) return FindOffice(OfficeType.CienciasAplicadas);
            if (r < 0.75f) return FindOffice(OfficeType.ORI);
            if (r < 0.90f) return FindOffice(OfficeType.CentroGraduados);
            return FindOffice(OfficeType.VicerrectoriaDocencia);
        }

        private ServiceOffice FindOffice(OfficeType type)
        {
            foreach (var off in registeredOffices)
            {
                if (off != null && off.officeType == type) return off;
            }
            return (registeredOffices.Count > 0) ? registeredOffices[0] : null;
        }

        public void OnAgentExit(AgentPedestrian agent)
        {
            activeAgents.Remove(agent);
            completedMetrics.Add(agent.metrics);
        }

        private void RecordReplicaSummary()
        {
            ReplicaSummary rep = new ReplicaSummary
            {
                replicaIndex = currentReplicaNumber,
                totalArrivals = currentAgentIdCounter,
                totalCompleted = completedMetrics.Count
            };

            float sumTravel = 0f, sumWait = 0f, sumService = 0f, sumTotal = 0f;
            foreach (var m in completedMetrics)
            {
                sumTravel += m.TravelTime;
                sumWait += m.WaitTime;
                sumService += m.ServiceTime;
                sumTotal += m.TotalSystemTime;
            }

            int n = Mathf.Max(1, completedMetrics.Count);
            rep.avgTravelTimeMinutes = (sumTravel / n) / 60f;
            rep.avgWaitTimeMinutes = (sumWait / n) / 60f;
            rep.avgServiceTimeMinutes = (sumService / n) / 60f;
            rep.avgTotalSystemTimeMinutes = (sumTotal / n) / 60f;

            replicaHistory.Add(rep);
            ExportResultsToCSV();
        }

        public void ExportResultsToCSV()
        {
            string folder = Application.dataPath + "/SimulationOutputs";
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"Resultados_Simulacion_{activeScenario}.csv");

            using (StreamWriter sw = new StreamWriter(path, false))
            {
                sw.WriteLine("Replica;TotalLlegadas;TotalAtendidos;TiempoRecorrido_Min;TiempoEspera_Min;TiempoAtencion_Min;TiempoTotalSistema_Min");
                foreach (var r in replicaHistory)
                {
                    sw.WriteLine($"{r.replicaIndex};{r.totalArrivals};{r.totalCompleted};{r.avgTravelTimeMinutes:F2};{r.avgWaitTimeMinutes:F2};{r.avgServiceTimeMinutes:F2};{r.avgTotalSystemTimeMinutes:F2}");
                }
            }
            Debug.Log($"Resultados de la simulación exportados a: {path}");
        }

        public void SwitchCamera(SimulationCameraController.CameraViewMode mode)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                if (cameraController == null)
                {
                    cameraController = mainCam.GetComponent<SimulationCameraController>();
                    if (cameraController == null)
                    {
                        cameraController = mainCam.gameObject.AddComponent<SimulationCameraController>();
                    }
                }
            }

            if (cameraController != null)
            {
                cameraController.SetViewMode(mode, instant: true);
                Debug.Log($"[SimulationManager] Cámara cambiada exitosamente a: {mode}");
            }
        }

        private void OnGUI()
        {
            // Banner central superior con la vista activa
            SimulationCameraController.CameraViewMode curCam = (cameraController != null) ? cameraController.currentMode : SimulationCameraController.CameraViewMode.Principal;
            string camName = GetCameraDisplayName(curCam);

            float bannerW = 340f;
            float bannerX = (Screen.width - bannerW) * 0.5f;
            GUI.Box(new Rect(bannerX, 10, bannerW, 35), "");
            GUI.Label(new Rect(bannerX, 15, bannerW, 25), $"<color=yellow><b>VISTA DE CÁMARA:</b> {camName}</color>", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });

            // Panel Izquierdo: Simulación y Estadísticas
            float leftPanelW = Mathf.Min(440f, Screen.width * 0.45f);
            GUI.Box(new Rect(10, 10, leftPanelW, 490), "");
            GUILayout.BeginArea(new Rect(20, 15, leftPanelW - 20, 480));

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            GUILayout.Label("UNIGUAJIRA · BLOQUE ADMINISTRATIVO A", titleStyle);
            GUILayout.Label("Simulación de Eventos Discretos · Semana 14", GUI.skin.label);
            GUILayout.Space(3);

            // Reloj de Simulación
            float elapsedSimSec = (Time.time - simulationStartTime);
            int simMinutesTotal = 17 * 60 + 40 + Mathf.FloorToInt(elapsedSimSec / 60f);
            int hh = (simMinutesTotal / 60) % 24;
            int mm = simMinutesTotal % 60;
            int ss = Mathf.FloorToInt(elapsedSimSec) % 60;
            string timeStr = $"{hh:00}:{mm:00}:{ss:00} PM";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Hora:</b> <color=yellow>{timeStr}</color>");
            GUILayout.Label($"<b>Velocidad:</b> {Time.timeScale:F0}x");
            GUILayout.EndHorizontal();

            string currentFranja = (currentSliceIndex < intervalNames.Length) ? intervalNames[currentSliceIndex] : "Finalizado";
            int expectedSlice = (currentSliceIndex < arrivalsPerTenMin.Length) ? arrivalsPerTenMin[currentSliceIndex] : 0;
            GUILayout.Label($"<b>Franja:</b> {currentFranja} ({generatedInCurrentSlice}/{expectedSlice} est.)");
            GUILayout.Label($"<b>Llegadas:</b> {currentAgentIdCounter}/66 | <b>En Cola:</b> {activeAgents.Count} | <b>Listos:</b> {completedMetrics.Count}");
            GUILayout.Space(4);

            GUILayout.Label("<b>OFICINAS Y FILAS DE ESPERA:</b>");
            foreach (var off in registeredOffices)
            {
                if (off != null)
                {
                    string colorTag = off.CurrentQueueCount > 3 ? "<color=red>" : "<color=white>";
                    GUILayout.Label($"• <b>{off.officeName}</b> (P{off.floorLevel}): {off.BusyServers}/{off.serverCount} vent. | {colorTag}Fila: {off.CurrentQueueCount}</color>");
                }
            }
            GUILayout.Space(4);

            float curAvgTravel = 0, curAvgWait = 0, curAvgService = 0, curAvgTotal = 0;
            if (completedMetrics.Count > 0)
            {
                foreach (var m in completedMetrics)
                {
                    curAvgTravel += m.TravelTime;
                    curAvgWait += m.WaitTime;
                    curAvgService += m.ServiceTime;
                    curAvgTotal += m.TotalSystemTime;
                }
                int cnt = completedMetrics.Count;
                curAvgTravel = (curAvgTravel / cnt) / 60f;
                curAvgWait = (curAvgWait / cnt) / 60f;
                curAvgService = (curAvgService / cnt) / 60f;
                curAvgTotal = (curAvgTotal / cnt) / 60f;
            }

            GUILayout.Label("<b>TIEMPOS PROMEDIO (MINUTOS):</b>");
            GUILayout.Label($"Recorrido: {curAvgTravel:F2}m | Espera: <b>{curAvgWait:F2}m</b> | Total: {curAvgTotal:F2}m");
            GUILayout.Space(6);

            string btnScenarioText = (activeScenario == SimulationScenario.EscenarioBase)
                ? "Cambiar a Escenario Alternativo (+1 ORI)"
                : "Cambiar a Escenario Base (1 Servidor ORI)";

            if (GUILayout.Button(btnScenarioText, GUILayout.Height(26)))
            {
                activeScenario = (activeScenario == SimulationScenario.EscenarioBase)
                    ? SimulationScenario.EscenarioAlternativo
                    : SimulationScenario.EscenarioBase;
                ApplyScenarioSettings();
                StartSimulation();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1x")) SetTimeScale(1f);
            if (GUILayout.Button("2x")) SetTimeScale(2f);
            if (GUILayout.Button("5x")) SetTimeScale(5f);
            if (GUILayout.Button("10x")) SetTimeScale(10f);
            if (GUILayout.Button("20x")) SetTimeScale(20f);
            if (GUILayout.Button(Time.timeScale == 0 ? "Reanudar" : "Pausa"))
            {
                Time.timeScale = (Time.timeScale == 0) ? timeScale : 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reiniciar")) StartSimulation();
            if (GUILayout.Button("5 Réplicas")) StartCoroutine(RunFiveReplicasRoutine());
            if (GUILayout.Button("Exportar CSV")) ExportResultsToCSV();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            // Panel Derecho: Selector de Cámaras (Diseño Compacto y Separado)
            float rightPanelW = 230f;
            float rightPanelX = Screen.width - rightPanelW - 10f;
            GUI.Box(new Rect(rightPanelX, 10, rightPanelW, 290), "");
            GUILayout.BeginArea(new Rect(rightPanelX + 10, 15, rightPanelW - 20, 280));

            GUILayout.Label("<b>VISTAS DE CÁMARA (1-7):</b>");
            GUILayout.Space(2);

            DrawCamButton("1. Principal", SimulationCameraController.CameraViewMode.Principal, curCam);
            DrawCamButton("2. Pasillo Piso 1", SimulationCameraController.CameraViewMode.PasilloPiso1, curCam);
            DrawCamButton("3. Salones Piso 1", SimulationCameraController.CameraViewMode.SalonesPiso1, curCam);
            DrawCamButton("4. Pasillo Piso 2", SimulationCameraController.CameraViewMode.PasilloPiso2, curCam);
            DrawCamButton("5. Salones Piso 2", SimulationCameraController.CameraViewMode.SalonesPiso2, curCam);
            DrawCamButton("6. Panorámica", SimulationCameraController.CameraViewMode.Panoramica, curCam);
            DrawCamButton("7. Seguir Estudiante", SimulationCameraController.CameraViewMode.SeguirEstudiante, curCam);

            GUILayout.EndArea();
        }

        private void DrawCamButton(string label, SimulationCameraController.CameraViewMode mode, SimulationCameraController.CameraViewMode activeMode)
        {
            bool isActive = (mode == activeMode);
            string btnText = isActive ? $"► {label} ◄" : label;

            if (GUILayout.Button(btnText, GUILayout.Height(26)))
            {
                SwitchCamera(mode);
            }
        }

        private string GetCameraDisplayName(SimulationCameraController.CameraViewMode mode)
        {
            switch (mode)
            {
                case SimulationCameraController.CameraViewMode.Principal: return "1. Principal";
                case SimulationCameraController.CameraViewMode.PasilloPiso1: return "2. Pasillo Piso 1";
                case SimulationCameraController.CameraViewMode.SalonesPiso1: return "3. Salones Piso 1";
                case SimulationCameraController.CameraViewMode.PasilloPiso2: return "4. Pasillo Piso 2";
                case SimulationCameraController.CameraViewMode.SalonesPiso2: return "5. Salones Piso 2";
                case SimulationCameraController.CameraViewMode.Panoramica: return "6. Panorámica Aérea";
                case SimulationCameraController.CameraViewMode.SeguirEstudiante: return "7. Seguir Estudiante";
                default: return mode.ToString();
            }
        }

        private void SetTimeScale(float ts)
        {
            timeScale = ts;
            Time.timeScale = ts;
        }

        private IEnumerator RunFiveReplicasRoutine()
        {
            replicaHistory.Clear();
            float oldScale = Time.timeScale;
            Time.timeScale = 25f;

            for (int r = 1; r <= 5; r++)
            {
                currentReplicaNumber = r;
                StartSimulation();
                while (isRunning)
                {
                    yield return new WaitForSeconds(0.2f);
                }
                yield return new WaitForSeconds(0.5f);
            }

            Time.timeScale = oldScale;
            Debug.Log("¡5 Réplicas completadas y exportadas a CSV con éxito!");
        }
    }
}
