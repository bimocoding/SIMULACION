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

        [Header("Directorio de Salones (Aulas)")]
        public List<ClassroomData> registeredClassrooms = new List<ClassroomData>();

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

        // Waypoints fijos de referencia de la arquitectura del Bloque
        public static readonly Vector3 PosPlazaEntrance = new Vector3(-140.0f, 4.71f, 170.0f);
        public static readonly Vector3 PosCourtyardWest = new Vector3(10.0f, 4.71f, 180.0f);
        public static readonly Vector3 PosCourtyardEast = new Vector3(350.0f, 4.71f, 200.0f);

        // Escalera monumental (Piso 1 -> Piso 2)
        public static readonly Vector3 StairBottom = new Vector3(410.3f, 4.71f, 222.9f);
        public static readonly Vector3 StairMid1 = new Vector3(410.3f, 20.0f, 200.0f);
        public static readonly Vector3 StairLanding1 = new Vector3(410.3f, 40.0f, 182.0f);
        public static readonly Vector3 StairLanding2 = new Vector3(440.3f, 41.0f, 182.0f);
        public static readonly Vector3 StairMid2 = new Vector3(440.3f, 59.0f, 218.0f);
        public static readonly Vector3 StairTop = new Vector3(440.3f, 62.0f, 222.9f);
        public static readonly Vector3 StairExitFloor2 = new Vector3(410.0f, 62.0f, 222.9f);

        private void Awake()
        {
            Application.runInBackground = true;

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
            InitializeClassroomDirectory();
            EnsureAccessPoints();

            cameraController = FindAnyObjectByType<SimulationCameraController>();
            if (cameraController == null && Camera.main != null)
            {
                cameraController = Camera.main.gameObject.AddComponent<SimulationCameraController>();
            }

            Time.timeScale = timeScale;
            ApplyScenarioSettings();
            StartSimulation();

            SwitchCamera(SimulationCameraController.CameraViewMode.Principal);
        }

        private void EnsureAccessPoints()
        {
            GameObject accessContainer = GameObject.Find("Puntos_Acceso");
            if (accessContainer == null) accessContainer = new GameObject("Puntos_Acceso");

            if (mainEntranceDoor == null)
            {
                Transform md = accessContainer.transform.Find("Puerta_Principal");
                if (md == null)
                {
                    GameObject mdObj = new GameObject("Puerta_Principal");
                    mdObj.transform.SetParent(accessContainer.transform);
                    md = mdObj.transform;
                }
                mainEntranceDoor = md;
            }
            mainEntranceDoor.position = new Vector3(-230.0f, 4.71f, 115.0f);
            mainEntranceDoor.rotation = Quaternion.Euler(0, 73f, 0);

            if (backEntranceDoor == null)
            {
                Transform bd = accessContainer.transform.Find("Puerta_Trasera");
                if (bd == null)
                {
                    GameObject bdObj = new GameObject("Puerta_Trasera");
                    bdObj.transform.SetParent(accessContainer.transform);
                    bd = bdObj.transform;
                }
                backEntranceDoor = bd;
            }
            backEntranceDoor.position = new Vector3(460.0f, 4.71f, 200.0f);
            backEntranceDoor.rotation = Quaternion.Euler(0, -90f, 0);
        }

        public void InitializeClassroomDirectory()
        {
            registeredClassrooms.Clear();

            // Definición de las 18 puertas reales de salones del Bloque
            // PISO 1 (Ground Floor - GF, Y=4.71)
            AddClassroom("Door_GF_N1", "Aula 101 Norte", 1, true, 160.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N2", "Aula 102 Norte", 1, true, 230.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N3", "Aula 103 Norte", 1, true, 300.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N4", "Aula 104 Norte", 1, true, 370.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N5", "Aula 105 Norte", 1, true, 440.3f, 4.71f, 34.4f);

            AddClassroom("Door_GF_S1", "Aula 106 Sur", 1, false, 160.3f, 4.71f, 438.9f);
            AddClassroom("Door_GF_S2", "Aula 107 Sur", 1, false, 230.3f, 4.71f, 438.9f);
            AddClassroom("Door_GF_S3", "Aula 108 Sur", 1, false, 300.3f, 4.71f, 438.9f);
            AddClassroom("Door_GF_S4", "Aula 109 Sur", 1, false, 370.3f, 4.71f, 438.9f);

            // PISO 2 (Second Floor - 2F, Y=61.91)
            AddClassroom("Door_2F_N1", "Aula 201 Norte", 2, true, 160.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N2", "Aula 202 Norte", 2, true, 230.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N3", "Aula 203 Norte", 2, true, 300.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N4", "Aula 204 Norte", 2, true, 370.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N5", "Aula 205 Norte", 2, true, 440.3f, 61.91f, 34.4f);

            AddClassroom("Door_2F_S1", "Aula 206 Sur", 2, false, 160.3f, 61.91f, 438.9f);
            AddClassroom("Door_2F_S2", "Aula 207 Sur", 2, false, 230.3f, 61.91f, 438.9f);
            AddClassroom("Door_2F_S3", "Aula 208 Sur", 2, false, 300.3f, 61.91f, 438.9f);
            AddClassroom("Door_2F_S4", "Aula 209 Sur", 2, false, 370.3f, 61.91f, 438.9f);

            // Crear placas 3D de identificación sobre las puertas para mejorar la visualización
            CreateDoorSignboards();
        }

        private void AddClassroom(string door, string name, int floor, bool isNorth, float x, float y, float z)
        {
            float hallwayZ = isNorth ? 52.0f : 418.0f;
            ClassroomData cr = new ClassroomData
            {
                doorName = door,
                classroomName = name,
                floorLevel = floor,
                isNorthWing = isNorth,
                doorPosition = new Vector3(x, y, z),
                hallwayPoint = new Vector3(x, y, hallwayZ),
                currentOccupants = 0
            };
            registeredClassrooms.Add(cr);
        }

        private void CreateDoorSignboards()
        {
            foreach (var cr in registeredClassrooms)
            {
                string signName = "Sign_" + cr.doorName;
                if (GameObject.Find(signName) != null) continue;

                GameObject signObj = new GameObject(signName);
                Vector3 signPos = cr.doorPosition + Vector3.up * 18.0f; // Arriba del vano de la puerta
                signPos.z += cr.isNorthWing ? 2.0f : -2.0f;
                signObj.transform.position = signPos;

                TextMesh tm = signObj.AddComponent<TextMesh>();
                tm.text = cr.classroomName.Replace("Aula ", "SALÓN ");
                tm.fontSize = 22;
                tm.characterSize = 0.12f;
                tm.alignment = TextAlignment.Center;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = cr.floorLevel == 1 ? new Color(0.2f, 0.85f, 1f) : new Color(1f, 0.75f, 0.2f);

                // Rotar para ver hacia el pasillo
                signObj.transform.rotation = Quaternion.Euler(0, cr.isNorthWing ? 180f : 0f, 0);
            }
        }

        private void EnsureModelAndEnvironmentInScene()
        {
            // Desactivar cámaras secundarias de importación
            Camera[] allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in allCams)
            {
                if (c != Camera.main && c.gameObject.name != "Main Camera")
                {
                    c.enabled = false;
                }
            }
            if (Camera.main != null)
            {
                Camera.main.depth = 100;
                Camera.main.enabled = true;
            }

            // Asegurar Bloque 3D
            GameObject block = GameObject.Find("Bloque_Administrativo_A");
            if (block == null)
            {
#if UNITY_EDITOR
                GameObject modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bloque_Administrativo_A.fbx");
                if (modelAsset != null)
                {
                    block = Instantiate(modelAsset, Vector3.zero, Quaternion.identity);
                    block.name = "Bloque_Administrativo_A";
                }
#endif
            }

            // Suelo exterior de plaza
            GameObject ground = GameObject.Find("Simulation_Ground_Plaza");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Simulation_Ground_Plaza";
                ground.transform.position = new Vector3(-200.0f, 4.60f, 200.0f);
                ground.transform.localScale = new Vector3(80f, 1f, 80f);

                Renderer gr = ground.GetComponent<Renderer>();
                if (gr != null)
                {
                    gr.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    gr.material.color = new Color(0.82f, 0.80f, 0.76f);
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

            foreach (var cr in registeredClassrooms)
            {
                cr.currentOccupants = 0;
            }

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
            currentAgentIdCounter++;

            // Selección de acceso según escenario
            float mainDoorProb = (activeScenario == SimulationScenario.EscenarioBase) ? 0.80f : 0.50f;
            bool spawnAtMain = UnityEngine.Random.value <= mainDoorProb;
            Transform spawnPoint = spawnAtMain ? mainEntranceDoor : (backEntranceDoor != null ? backEntranceDoor : mainEntranceDoor);
            string doorName = spawnAtMain ? "Puerta Principal" : "Puerta Trasera";

            Vector3 spawnPos = (spawnPoint != null) ? spawnPoint.position : new Vector3(-230.0f, 5.0f, 115.0f);
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
                agentObj.transform.localScale = new Vector3(1.3f, 1.25f, 1.3f);
            }

            AgentPedestrian agent = agentObj.GetComponent<AgentPedestrian>();
            if (agent == null) agent = agentObj.AddComponent<AgentPedestrian>();

            // Asignación de Rol
            // ~70% Estudiantes, ~15% Docentes, ~15% Funcionarios
            float rollRole = UnityEngine.Random.value;
            AgentRole role = rollRole < 0.70f ? AgentRole.Estudiante : (rollRole < 0.85f ? AgentRole.Docente : AgentRole.Funcionario);

            // Determinación de destino:
            // Estudiantes: 85% van a salones de clase (Piso 1 o 2), 15% a trámites en oficinas
            // Docentes: 100% van a salones de clase
            // Funcionarios: 100% van a oficinas administrativas
            bool goToClassroom = false;
            if (role == AgentRole.Estudiante)
            {
                goToClassroom = (registeredClassrooms.Count > 0) && (UnityEngine.Random.value < 0.85f || registeredOffices.Count == 0);
            }
            else if (role == AgentRole.Docente)
            {
                goToClassroom = (registeredClassrooms.Count > 0);
            }
            else // Funcionario
            {
                goToClassroom = (registeredOffices.Count == 0);
            }

            Transform exitPoint = spawnPoint;

            if (goToClassroom && registeredClassrooms.Count > 0)
            {
                // Elegir un salón aleatorio o con menor ocupación
                ClassroomData classroom = registeredClassrooms[UnityEngine.Random.Range(0, registeredClassrooms.Count)];
                agentObj.name = $"{role}_{currentAgentIdCounter}_{classroom.classroomName.Replace(" ", "_")}";

                List<Vector3> path = CalculatePath(spawnPos, classroom, null);
                agent.InitializeClassroomAgent(currentAgentIdCounter, role, doorName, classroom, path, exitPoint);
            }
            else
            {
                ServiceOffice office = SelectDestinationOffice();
                agentObj.name = $"{role}_{currentAgentIdCounter}_{office.officeName.Replace(" ", "_")}";

                List<Vector3> path = CalculatePath(spawnPos, null, office);
                agent.InitializeOfficeAgent(currentAgentIdCounter, role, doorName, office, path, exitPoint);
            }

            activeAgents.Add(agent);
        }

        public List<Vector3> CalculatePath(Vector3 startPos, ClassroomData classroom, ServiceOffice office)
        {
            List<Vector3> path = new List<Vector3>();
            int destFloor = (classroom != null) ? classroom.floorLevel : (office != null ? office.floorLevel : 1);
            bool isNorth = (classroom != null) ? classroom.isNorthWing : (office == null || office.transform.position.z < 200f);
            Vector3 finalDest = (classroom != null) ? classroom.doorPosition : (office.queueStartPoint != null ? office.queueStartPoint.position : office.transform.position);
            Vector3 finalHallway = (classroom != null) ? classroom.hallwayPoint : (isNorth ? new Vector3(finalDest.x, (destFloor == 1 ? 4.71f : 61.91f), 52.0f) : new Vector3(finalDest.x, (destFloor == 1 ? 4.71f : 61.91f), 418.0f));

            bool isSpawningAtMain = (startPos.x < 0);

            if (isSpawningAtMain)
            {
                // Viene desde la plaza principal exterior
                path.Add(PosPlazaEntrance + RandomJitter());
                path.Add(PosCourtyardWest + RandomJitter());

                if (destFloor == 1)
                {
                    // Piso 1: Dirigirse al pasillo Norte o Sur
                    float corridorZ = isNorth ? 52.0f : 418.0f;
                    path.Add(new Vector3(50.0f, 4.71f, corridorZ) + RandomJitter());
                    path.Add(finalHallway + RandomJitter());
                    // Punto final frente a la puerta del salón
                    Vector3 stepIn = classroom != null ? (isNorth ? classroom.doorPosition + Vector3.forward * 4.0f : classroom.doorPosition - Vector3.forward * 4.0f) : finalDest;
                    path.Add(stepIn);
                }
                else
                {
                    // Piso 2: Cruzar patio y subir la escalera monumental
                    path.Add(PosCourtyardEast + RandomJitter());
                    AddStaircaseAscent(path);

                    // Pasillo de piso 2
                    float corridorZ = isNorth ? 52.0f : 418.0f;
                    path.Add(new Vector3(410.0f, 61.91f, corridorZ) + RandomJitter());
                    path.Add(finalHallway + RandomJitter());

                    Vector3 stepIn = classroom != null ? (isNorth ? classroom.doorPosition + Vector3.forward * 4.0f : classroom.doorPosition - Vector3.forward * 4.0f) : finalDest;
                    path.Add(stepIn);
                }
            }
            else
            {
                // Viene desde Puerta Trasera (este del bloque)
                path.Add(new Vector3(430.0f, 4.71f, 200.0f) + RandomJitter());

                if (destFloor == 1)
                {
                    float corridorZ = isNorth ? 52.0f : 418.0f;
                    path.Add(new Vector3(430.0f, 4.71f, corridorZ) + RandomJitter());
                    path.Add(finalHallway + RandomJitter());
                    Vector3 stepIn = classroom != null ? (isNorth ? classroom.doorPosition + Vector3.forward * 4.0f : classroom.doorPosition - Vector3.forward * 4.0f) : finalDest;
                    path.Add(stepIn);
                }
                else
                {
                    AddStaircaseAscent(path);
                    float corridorZ = isNorth ? 52.0f : 418.0f;
                    path.Add(new Vector3(410.0f, 61.91f, corridorZ) + RandomJitter());
                    path.Add(finalHallway + RandomJitter());
                    Vector3 stepIn = classroom != null ? (isNorth ? classroom.doorPosition + Vector3.forward * 4.0f : classroom.doorPosition - Vector3.forward * 4.0f) : finalDest;
                    path.Add(stepIn);
                }
            }

            return path;
        }

        public List<Vector3> CalculateExitPath(Vector3 currentPos, ClassroomData classroom, ServiceOffice office, Vector3 exitTarget)
        {
            List<Vector3> path = new List<Vector3>();
            int currentFloor = currentPos.y > 30.0f ? 2 : 1;
            bool isNorth = (classroom != null) ? classroom.isNorthWing : (office == null || currentPos.z < 200f);
            bool exitViaMain = (exitTarget.x < 100);

            float corridorZ = isNorth ? 52.0f : 418.0f;
            float floorY = (currentFloor == 1) ? 4.71f : 61.91f;

            // 1. Salir del salón hacia el centro del pasillo
            path.Add(new Vector3(currentPos.x, floorY, corridorZ) + RandomJitter());

            if (currentFloor == 2)
            {
                // Caminar hacia la escalera de bajada
                path.Add(new Vector3(410.0f, 61.91f, corridorZ) + RandomJitter());
                path.Add(StairExitFloor2);

                // Bajar la escalera
                AddStaircaseDescent(path);

                path.Add(PosCourtyardEast + RandomJitter());
            }

            if (exitViaMain)
            {
                if (currentFloor == 1)
                {
                    path.Add(new Vector3(50.0f, 4.71f, corridorZ) + RandomJitter());
                }
                path.Add(PosCourtyardWest + RandomJitter());
                path.Add(PosPlazaEntrance + RandomJitter());
                path.Add(exitTarget);
            }
            else
            {
                // Salida por Puerta Trasera
                path.Add(new Vector3(430.0f, 4.71f, 200.0f) + RandomJitter());
                path.Add(exitTarget);
            }

            return path;
        }

        private void AddStaircaseAscent(List<Vector3> path)
        {
            path.Add(StairBottom);
            path.Add(StairMid1);
            path.Add(StairLanding1);
            path.Add(StairLanding2);
            path.Add(StairMid2);
            path.Add(StairTop);
            path.Add(StairExitFloor2);
        }

        private void AddStaircaseDescent(List<Vector3> path)
        {
            path.Add(StairTop);
            path.Add(StairMid2);
            path.Add(StairLanding2);
            path.Add(StairLanding1);
            path.Add(StairMid1);
            path.Add(StairBottom);
        }

        private Vector3 RandomJitter()
        {
            return new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0, UnityEngine.Random.Range(-0.8f, 0.8f));
        }

        public Vector3 GetDefaultExitPosition()
        {
            return mainEntranceDoor != null ? mainEntranceDoor.position : new Vector3(-230.0f, 5.0f, 115.0f);
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

            // Panel Izquierdo: Simulación y Estadísticas de Salones
            float leftPanelW = Mathf.Min(470f, Screen.width * 0.45f);
            GUI.Box(new Rect(10, 10, leftPanelW, 510), "");
            GUILayout.BeginArea(new Rect(20, 15, leftPanelW - 20, 500));

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            GUILayout.Label("UNIGUAJIRA · BLOQUE ADMINISTRATIVO A", titleStyle);
            GUILayout.Label("Simulación de Clases y Trámites · Semana 14", GUI.skin.label);
            GUILayout.Space(2);

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
            GUILayout.Label($"<b>Franja:</b> {currentFranja} ({generatedInCurrentSlice}/{expectedSlice} agentes)");

            int inClassCount = 0;
            int exitingCount = 0;
            int walkingCount = 0;
            foreach (var a in activeAgents)
            {
                if (a == null) continue;
                if (a.currentState == AgentPedestrian.AgentState.InClass) inClassCount++;
                else if (a.currentState == AgentPedestrian.AgentState.Exiting) exitingCount++;
                else walkingCount++;
            }

            GUILayout.Label($"<b>Activos:</b> {activeAgents.Count} (Clase: <color=yellow>{inClassCount}</color> | Tránsito: {walkingCount} | Saliendo: <color=cyan>{exitingCount}</color>)");
            GUILayout.Label($"<b>Llegadas:</b> {currentAgentIdCounter}/66 | <b>Completados:</b> {completedMetrics.Count}");
            GUILayout.Space(3);

            // Resumen de Ocupación por Pisos y Salones
            int occF1N = 0, occF1S = 0, occF2N = 0, occF2S = 0;
            foreach (var cr in registeredClassrooms)
            {
                if (cr.floorLevel == 1)
                {
                    if (cr.isNorthWing) occF1N += cr.currentOccupants;
                    else occF1S += cr.currentOccupants;
                }
                else
                {
                    if (cr.isNorthWing) occF2N += cr.currentOccupants;
                    else occF2S += cr.currentOccupants;
                }
            }

            GUILayout.Label("<b>OCUPACIÓN DE AULAS POR ALA Y PISO:</b>");
            GUILayout.Label($"• <b>Piso 1:</b> Norte (101-105): <b>{occF1N}</b> est. | Sur (106-109): <b>{occF1S}</b> est.");
            GUILayout.Label($"• <b>Piso 2:</b> Norte (201-205): <b>{occF2N}</b> est. | Sur (206-209): <b>{occF2S}</b> est.");
            GUILayout.Space(3);

            // Oficinas y Trámites
            GUILayout.Label("<b>OFICINAS DE SERVICIO:</b>");
            foreach (var off in registeredOffices)
            {
                if (off != null)
                {
                    string colorTag = off.CurrentQueueCount > 3 ? "<color=red>" : "<color=white>";
                    GUILayout.Label($"• <b>{off.officeName}</b> (P{off.floorLevel}): {off.BusyServers}/{off.serverCount} vent. | {colorTag}Fila: {off.CurrentQueueCount}</color>");
                }
            }
            GUILayout.Space(4);

            string btnScenarioText = (activeScenario == SimulationScenario.EscenarioBase)
                ? "Cambiar a Escenario Alternativo (+1 ORI)"
                : "Cambiar a Escenario Base (1 Servidor ORI)";

            if (GUILayout.Button(btnScenarioText, GUILayout.Height(24)))
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
            if (GUILayout.Button("Reiniciar Simulación")) StartSimulation();
            if (GUILayout.Button("Exportar CSV")) ExportResultsToCSV();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            // Panel Derecho: Selector de Cámaras (Diseño Compacto)
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
    }
}
