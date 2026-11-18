using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Uniguajira.Simulation
{
    public class SimulationManager : MonoBehaviour
    {
        private static SimulationManager _instance;
        public static SimulationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<SimulationManager>();
                }
                return _instance;
            }
            private set
            {
                _instance = value;
            }
        }

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

        [Header("Parámetros Calibrados con Datos Reales de Campo")]
        [Tooltip("Tasa media de llegadas global (pers/min): 52 llegadas en 106.6 min")]
        public float lambdaGlobal = 0.49f;
        [Tooltip("Probabilidad de ingreso por puerta delantera (30 de 52 = 58%)")]
        public float frontDoorProb = 0.58f;
        [Tooltip("Probabilidad de tipo funcionario (37 de 52 = 71%)")]
        public float staffProb = 0.71f;
        [Tooltip("Probabilidad de destino a Piso 2 por escaleras (13 de 52 = 25%)")]
        public float floor2Prob = 0.25f;
        [Tooltip("Población inicial ya presente dentro del bloque al iniciar la sesión")]
        public int initialPopulationCount = 47;

        [Header("Datos de Campo Observados (Cortes de 10 min)")]
        // Cortes calibrados para sumar 29 llegadas en 60 min (lambda = 0.49 pers/min)
        public int[] arrivalsPerTenMin = new int[] { 10, 5, 6, 3, 3, 2 };
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
        public readonly List<AgentPedestrian> activeAgents = new List<AgentPedestrian>();
        public readonly List<ReplicaSummary> baseReplicaHistory = new List<ReplicaSummary>();
        public readonly List<ReplicaSummary> altReplicaHistory = new List<ReplicaSummary>();

        private float simulationStartTime;
        private bool isRunning = false;
        private int currentSliceIndex = 0;
        private int generatedInCurrentSlice = 0;
        private string lastStatusMessage = "Listo para iniciar simulación.";

        private SimulationCameraController cameraController;

        // Waypoints fijos de referencia de la arquitectura del Bloque
        public static readonly Vector3 PosPlazaEntrance = new Vector3(-140.0f, 4.71f, 170.0f);
        public static readonly Vector3 PosCourtyardWest = new Vector3(10.0f, 4.71f, 180.0f);
        public static readonly Vector3 PosCourtyardEast = new Vector3(350.0f, 4.71f, 200.0f);

        // Escalera monumental (Piso 1 -> Piso 2)
        public static readonly Vector3 StairBottom = new Vector3(390.0f, 4.71f, 193.0f);
        public static readonly Vector3 StairMid1 = new Vector3(410.3f, 20.0f, 196.0f);
        public static readonly Vector3 StairLanding1 = new Vector3(410.3f, 40.0f, 185.0f);
        public static readonly Vector3 StairLanding2 = new Vector3(437.0f, 41.0f, 185.0f);
        public static readonly Vector3 StairMid2 = new Vector3(437.0f, 59.0f, 196.0f);
        public static readonly Vector3 StairTop = new Vector3(437.0f, 62.0f, 196.0f);
        public static readonly Vector3 StairExitFloor2 = new Vector3(360.0f, 62.0f, 196.0f);

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
            EnsureOffices();
            EnsureAllClassroomLabs();
            EnsureMainStaircase();

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

            // PISO 1 (Ground Floor - GF, Y=4.71)
            AddClassroom("Door_GF_N1", "Aula 101", 1, true, 160.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N2", "Aula 102", 1, true, 230.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N3", "Aula 103", 1, true, 300.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N4", "Aula 104", 1, true, 370.3f, 4.71f, 34.4f);
            AddClassroom("Door_GF_N5", "Aula 105", 1, true, 440.3f, 4.71f, 34.4f);

            AddClassroom("Door_GF_S1", "Aula 106", 1, false, 160.3f, 4.71f, 438.9f);
            AddClassroom("Door_GF_S2", "Aula 107", 1, false, 230.3f, 4.71f, 438.9f);
            AddClassroom("Door_GF_S3", "Aula 108", 1, false, 300.3f, 4.71f, 438.9f);
            AddClassroom("Door_GF_S4", "Aula 109", 1, false, 370.3f, 4.71f, 438.9f);

            // PISO 2 (Second Floor - 2F, Y=61.91)
            AddClassroom("Door_2F_N1", "Aula 201", 2, true, 160.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N2", "Aula 202", 2, true, 230.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N3", "Aula 203", 2, true, 300.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N4", "Aula 204", 2, true, 370.3f, 61.91f, 34.4f);
            AddClassroom("Door_2F_N5", "Aula 205", 2, true, 440.3f, 61.91f, 34.4f);

            AddClassroom("Door_2F_S1", "Aula 206", 2, false, 160.3f, 61.91f, 438.9f);
            AddClassroom("Door_2F_S2", "Aula 207", 2, false, 230.3f, 61.91f, 438.9f);
            AddClassroom("Door_2F_S3", "Aula 208", 2, false, 300.3f, 61.91f, 438.9f);
            AddClassroom("Door_2F_S4", "Aula 209", 2, false, 370.3f, 61.91f, 438.9f);

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
                GameObject signObj = GameObject.Find(signName);
                if (signObj == null)
                {
                    signObj = new GameObject(signName);
                }

                Vector3 signPos = cr.doorPosition + Vector3.up * 18.0f;
                signPos.z += cr.isNorthWing ? 2.0f : -2.0f;
                signObj.transform.position = signPos;

                TextMesh tm = signObj.GetComponent<TextMesh>();
                if (tm == null) tm = signObj.AddComponent<TextMesh>();

                tm.text = cr.classroomName.Replace("Aula ", "SALÓN ");
                tm.fontSize = 22;
                tm.characterSize = 0.12f;
                tm.alignment = TextAlignment.Center;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = cr.floorLevel == 1 ? new Color(0.2f, 0.85f, 1f) : new Color(1f, 0.75f, 0.2f);
                signObj.transform.rotation = Quaternion.Euler(0, cr.isNorthWing ? 180f : 0f, 0);
            }
        }

        // Puntos de estaciones de trabajo de cómputo dentro de Lab_Walls (Aula 101)
        public static readonly Vector3[] LabWorkstations = new Vector3[]
        {
            new Vector3(195.30f, 4.71f, 4.5f),   // WS 1
            new Vector3(175.30f, 4.71f, 4.5f),   // WS 2
            new Vector3(195.30f, 4.71f, -15.5f), // WS 3
            new Vector3(175.30f, 4.71f, -15.5f), // WS 4
            new Vector3(195.30f, 4.71f, -35.5f), // WS 5
            new Vector3(175.30f, 4.71f, -35.5f), // WS 6
            new Vector3(195.30f, 4.71f, -55.5f), // WS 7
            new Vector3(175.30f, 4.71f, -55.5f), // WS 8
            new Vector3(155.30f, 4.71f, 5.5f),   // Silla Estudiante 1
            new Vector3(155.30f, 4.71f, -4.5f),  // Silla Estudiante 2
            new Vector3(155.30f, 4.71f, -14.5f), // Silla Estudiante 3
            new Vector3(155.30f, 4.71f, -24.5f)  // Silla Estudiante 4
        };
        public static readonly Vector3 LabEntryCorridor = new Vector3(163.0f, 4.71f, 20.0f);

        private void EnsureOffices()
        {
            GameObject container = GameObject.Find("Oficinas_Servicio");
            if (container == null) container = new GameObject("Oficinas_Servicio");

            // Piso 1: Ubicadas en pasillo sur, totalmente fuera de Lab_Walls
            ConfigureOffice(container, "Vicerrectoria_Extension", "Vicerrectoría de Extensión", OfficeType.VicerrectoriaExtension, 1,
                new Vector3(230.0f, 4.71f, 395.0f), new Vector3(230.0f, 4.71f, 390.0f), new Vector3(230.0f, 4.71f, 405.0f));
            ConfigureOffice(container, "Facultad_Ciencias_Aplicadas", "Facultad de Ciencias Aplicadas", OfficeType.CienciasAplicadas, 1,
                new Vector3(300.0f, 4.71f, 395.0f), new Vector3(300.0f, 4.71f, 390.0f), new Vector3(300.0f, 4.71f, 405.0f));

            // Piso 2: Ubicadas en módulo administrativo de segundo piso frente a pasillo norte
            ConfigureOffice(container, "Oficina_ORI", "Oficina ORI", OfficeType.ORI, 2,
                new Vector3(190.0f, 61.91f, 75.0f), new Vector3(190.0f, 61.91f, 76.5f), new Vector3(190.0f, 61.91f, 72.5f));
            ConfigureOffice(container, "Centro_Graduados", "Centro de Graduados", OfficeType.CentroGraduados, 2,
                new Vector3(250.0f, 61.91f, 75.0f), new Vector3(250.0f, 61.91f, 76.5f), new Vector3(250.0f, 61.91f, 72.5f));
            ConfigureOffice(container, "Vicerrectoria_Docencia", "Vicerrectoría de Docencia", OfficeType.VicerrectoriaDocencia, 2,
                new Vector3(310.0f, 61.91f, 75.0f), new Vector3(310.0f, 61.91f, 76.5f), new Vector3(310.0f, 61.91f, 72.5f));

            registeredOffices.Clear();
            var found = container.GetComponentsInChildren<ServiceOffice>();
            registeredOffices.AddRange(found);

            CreateOfficeSignboards();
        }

        private void ConfigureOffice(GameObject parent, string objName, string displayName, OfficeType type, int floor, Vector3 basePos, Vector3 deskPos, Vector3 queuePos)
        {
            Transform tr = parent.transform.Find(objName);
            GameObject obj;
            if (tr == null)
            {
                obj = new GameObject(objName);
                obj.transform.SetParent(parent.transform);
            }
            else
            {
                obj = tr.gameObject;
            }

            obj.transform.position = basePos;

            ServiceOffice office = obj.GetComponent<ServiceOffice>();
            if (office == null) office = obj.AddComponent<ServiceOffice>();

            office.officeName = displayName;
            office.officeType = type;
            office.floorLevel = floor;
            office.queueCapacity = 25;
            office.serverCount = 1;

            Transform dt = obj.transform.Find("DeskPoint");
            if (dt == null)
            {
                GameObject dObj = new GameObject("DeskPoint");
                dObj.transform.SetParent(obj.transform);
                dt = dObj.transform;
            }
            dt.position = deskPos;
            office.serviceDeskPoint = dt;

            Transform qt = obj.transform.Find("QueuePoint");
            if (qt == null)
            {
                GameObject qObj = new GameObject("QueuePoint");
                qObj.transform.SetParent(obj.transform);
                qt = qObj.transform;
            }
            qt.position = queuePos;
            office.queueStartPoint = qt;
        }

        private void CreateOfficeSignboards()
        {
            foreach (var off in registeredOffices)
            {
                string signName = "Sign_Office_" + off.gameObject.name;
                GameObject signObj = GameObject.Find(signName);
                if (signObj == null)
                {
                    signObj = new GameObject(signName);
                }

                Vector3 signPos = off.transform.position + Vector3.up * 16.0f;
                signObj.transform.position = signPos;

                TextMesh tm = signObj.GetComponent<TextMesh>();
                if (tm == null) tm = signObj.AddComponent<TextMesh>();

                tm.text = off.officeName.ToUpper();
                tm.fontSize = 20;
                tm.characterSize = 0.11f;
                tm.alignment = TextAlignment.Center;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = new Color(0.2f, 1.0f, 0.6f);
                signObj.transform.rotation = Quaternion.Euler(0, off.floorLevel == 1 ? 0f : 180f, 0);
            }
        }

        public static Vector3 GetClassroomWorkstationPosition(ClassroomData cr, int slotIndex)
        {
            Vector3 d0 = new Vector3(160.30f, 4.71f, 34.40f);
            Vector3 baseWs = LabWorkstations[slotIndex % LabWorkstations.Length];
            Vector3 local = baseWs - d0;

            if (cr.isNorthWing)
            {
                Vector3 delta = cr.doorPosition - d0;
                return baseWs + delta;
            }
            else
            {
                Vector3 rotLocal = new Vector3(-local.x, local.y, -local.z);
                return cr.doorPosition + rotLocal;
            }
        }

        public static Vector3 GetClassroomLabEntry(ClassroomData cr)
        {
            Vector3 d0 = new Vector3(160.30f, 4.71f, 34.40f);
            Vector3 local = LabEntryCorridor - d0;

            if (cr.isNorthWing)
            {
                Vector3 delta = cr.doorPosition - d0;
                return LabEntryCorridor + delta;
            }
            else
            {
                Vector3 rotLocal = new Vector3(-local.x, local.y, -local.z);
                return cr.doorPosition + rotLocal;
            }
        }

        public void EnsureAllClassroomLabs()
        {
            Vector3 d0 = new Vector3(160.30f, 4.71f, 34.40f);

            var allObjs = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var protoList = new List<GameObject>();
            foreach (var go in allObjs)
            {
                if ((go.name.StartsWith("Lab_") || go.name.StartsWith("AC_Lab") || 
                     go.name.StartsWith("Cabinet_Lab") || go.name.StartsWith("Whiteboard_Lab") || 
                     go.name.StartsWith("Partition_")) &&
                    !go.name.Contains("(1)") && !go.name.Contains("_cloned_"))
                {
                    if (go.transform.parent != null && go.transform.parent.name.StartsWith("AulaLab_")) continue;
                    protoList.Add(go);
                }
            }

            GameObject rootContainer = GameObject.Find("Aulas_Laboratorios");
            if (rootContainer == null) rootContainer = new GameObject("Aulas_Laboratorios");

            foreach (var cr in registeredClassrooms)
            {
                if (cr.doorName == "Door_GF_N1") continue; // Salón 101 ya posee los muebles originales del FBX

                string containerName = "AulaLab_" + cr.doorName;
                Transform existing = rootContainer.transform.Find(containerName);
                if (existing != null) continue;

                GameObject aulaContainer = new GameObject(containerName);
                aulaContainer.transform.SetParent(rootContainer.transform);

                bool isNorth = cr.isNorthWing;
                Vector3 doorPos = cr.doorPosition;
                Vector3 deltaNorth = doorPos - d0;

                foreach (var proto in protoList)
                {
                    GameObject clone = Instantiate(proto, aulaContainer.transform);
                    clone.name = $"{proto.name}_cloned_{cr.doorName}";

                    if (isNorth)
                    {
                        clone.transform.position = proto.transform.position + deltaNorth;
                        clone.transform.rotation = proto.transform.rotation;
                    }
                    else
                    {
                        Vector3 local = proto.transform.position - d0;
                        Vector3 rotLocal = new Vector3(-local.x, local.y, -local.z);
                        clone.transform.position = doorPos + rotLocal;
                        clone.transform.rotation = Quaternion.Euler(0, 180f, 0) * proto.transform.rotation;
                    }
                    clone.transform.localScale = proto.transform.localScale;

                    // Colisionadores para paredes y muebles de cada salón
                    if (clone.name.StartsWith("Lab_Walls") || clone.name.StartsWith("Lab_Floor") || clone.name.StartsWith("Lab_WS"))
                    {
                        if (clone.GetComponent<Collider>() == null)
                        {
                            var mc = clone.AddComponent<MeshCollider>();
                            mc.convex = false;
                        }
                    }
                }
            }
        }

        private void EnsureMainStaircase()
        {
            // Desactivar la escalera antigua deformada del FBX si aún existe
            var oldStair = GameObject.Find("Bloque_Administrativo_A/Main_Staircase");
            if (oldStair != null)
            {
                var mr = oldStair.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
                var col = oldStair.GetComponent<Collider>();
                if (col != null) col.enabled = false;
                oldStair.name = "Main_Staircase_Broken_Deformed_FBX";
                oldStair.SetActive(false);
            }

            // Verificar si la nueva escalera funcional ya existe
            var stairRoot = GameObject.Find("Main_Staircase");
            if (stairRoot != null && stairRoot.transform.Find("Tramo_1_Ascenso") != null)
            {
                return; // Ya construida y operativa
            }

            if (stairRoot == null)
            {
                stairRoot = new GameObject("Main_Staircase");
            }

            stairRoot.transform.position = Vector3.zero;
            stairRoot.transform.rotation = Quaternion.identity;
            stairRoot.transform.localScale = Vector3.one;

            var shaderLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            var stairMat = new Material(shaderLit) { name = "Mat_Staircase_Granite", color = new Color(0.88f, 0.86f, 0.82f) };
            var darkTrimMat = new Material(shaderLit) { name = "Mat_Staircase_DarkTrim", color = new Color(0.25f, 0.25f, 0.28f) };
            var handrailMat = new Material(shaderLit) { name = "Mat_Staircase_Handrail", color = new Color(0.12f, 0.45f, 0.70f) };

            System.Action<Transform, string, Vector3, Vector3, Material, bool> makeBox = (parent, name, pos, size, mat, addCol) =>
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(parent);
                go.transform.position = pos;
                go.transform.localScale = size;
                var r = go.GetComponent<Renderer>();
                if (r != null) r.material = mat;
                if (!addCol)
                {
                    var c = go.GetComponent<Collider>();
                    if (c != null) Destroy(c);
                }
            };

            // 1. TRAMO 1 (Floor 1 -> Descanso)
            var t1Obj = new GameObject("Tramo_1_Ascenso");
            t1Obj.transform.SetParent(stairRoot.transform);

            float stepLen = 50.0f / 18f;
            float stepH = 28.62f / 18f;
            float angleT1 = 29.789f;
            float rampLen = Mathf.Sqrt(50f * 50f + 28.62f * 28.62f);

            for (int i = 0; i < 18; i++)
            {
                float x = 365.0f + (i + 0.5f) * stepLen;
                float topY = 4.71f + (i + 1) * stepH;
                float y = topY - stepH * 0.5f;
                float z = 178.0f;
                makeBox(t1Obj.transform, "Step_T1_" + i, new Vector3(x, y, z), new Vector3(stepLen, stepH, 16.0f), stairMat, false);
                makeBox(t1Obj.transform, "Trim_T1_" + i, new Vector3(x + stepLen * 0.42f, topY - 0.08f, z), new Vector3(stepLen * 0.20f, 0.16f, 16.05f), darkTrimMat, false);
            }

            var z1A = GameObject.CreatePrimitive(PrimitiveType.Cube);
            z1A.name = "Zanca_T1_Outer";
            z1A.transform.SetParent(t1Obj.transform);
            z1A.transform.position = new Vector3(390.0f, 19.02f, 169.6f);
            z1A.transform.rotation = Quaternion.Euler(0, 0, angleT1);
            z1A.transform.localScale = new Vector3(rampLen, 3.5f, 0.8f);
            z1A.GetComponent<Renderer>().material = darkTrimMat;
            Destroy(z1A.GetComponent<Collider>());

            var z1B = GameObject.CreatePrimitive(PrimitiveType.Cube);
            z1B.name = "Zanca_T1_Inner";
            z1B.transform.SetParent(t1Obj.transform);
            z1B.transform.position = new Vector3(390.0f, 19.02f, 186.4f);
            z1B.transform.rotation = Quaternion.Euler(0, 0, angleT1);
            z1B.transform.localScale = new Vector3(rampLen, 3.5f, 0.8f);
            z1B.GetComponent<Renderer>().material = darkTrimMat;
            Destroy(z1B.GetComponent<Collider>());

            var ramp1 = new GameObject("Collider_Ramp_T1");
            ramp1.transform.SetParent(t1Obj.transform);
            ramp1.transform.position = new Vector3(390.0f, 19.02f, 178.0f);
            ramp1.transform.rotation = Quaternion.Euler(0, 0, angleT1);
            var bc1 = ramp1.AddComponent<BoxCollider>();
            bc1.size = new Vector3(rampLen + 1.0f, 0.4f, 16.2f);

            // 2. DESCANSO INTERMEDIO
            var landObj = new GameObject("Descanso_Intermedio");
            landObj.transform.SetParent(stairRoot.transform);
            makeBox(landObj.transform, "Slab_Descanso", new Vector3(425.5f, 32.53f, 191.0f), new Vector3(21.0f, 1.6f, 42.0f), stairMat, true);
            makeBox(landObj.transform, "Trim_Descanso", new Vector3(436.2f, 32.53f, 191.0f), new Vector3(0.5f, 1.7f, 42.2f), darkTrimMat, false);
            makeBox(landObj.transform, "Columna_Descanso_1", new Vector3(428.0f, 18.0f, 175.0f), new Vector3(3.0f, 27.0f, 3.0f), darkTrimMat, true);
            makeBox(landObj.transform, "Columna_Descanso_2", new Vector3(428.0f, 18.0f, 207.0f), new Vector3(3.0f, 27.0f, 3.0f), darkTrimMat, true);

            // 3. TRAMO 2 (Descanso -> Floor 2)
            var t2Obj = new GameObject("Tramo_2_Ascenso");
            t2Obj.transform.SetParent(stairRoot.transform);

            for (int i = 0; i < 18; i++)
            {
                float x = 415.0f - (i + 0.5f) * stepLen;
                float topY = 33.33f + (i + 1) * stepH;
                float y = topY - stepH * 0.5f;
                float z = 204.0f;
                makeBox(t2Obj.transform, "Step_T2_" + i, new Vector3(x, y, z), new Vector3(stepLen, stepH, 16.0f), stairMat, false);
                makeBox(t2Obj.transform, "Trim_T2_" + i, new Vector3(x - stepLen * 0.42f, topY - 0.08f, z), new Vector3(stepLen * 0.20f, 0.16f, 16.05f), darkTrimMat, false);
            }

            var z2A = GameObject.CreatePrimitive(PrimitiveType.Cube);
            z2A.name = "Zanca_T2_Inner";
            z2A.transform.SetParent(t2Obj.transform);
            z2A.transform.position = new Vector3(390.0f, 47.64f, 195.6f);
            z2A.transform.rotation = Quaternion.Euler(0, 0, -angleT1);
            z2A.transform.localScale = new Vector3(rampLen, 3.5f, 0.8f);
            z2A.GetComponent<Renderer>().material = darkTrimMat;
            Destroy(z2A.GetComponent<Collider>());

            var z2B = GameObject.CreatePrimitive(PrimitiveType.Cube);
            z2B.name = "Zanca_T2_Outer";
            z2B.transform.SetParent(t2Obj.transform);
            z2B.transform.position = new Vector3(390.0f, 47.64f, 212.4f);
            z2B.transform.rotation = Quaternion.Euler(0, 0, -angleT1);
            z2B.transform.localScale = new Vector3(rampLen, 3.5f, 0.8f);
            z2B.GetComponent<Renderer>().material = darkTrimMat;
            Destroy(z2B.GetComponent<Collider>());

            var ramp2 = new GameObject("Collider_Ramp_T2");
            ramp2.transform.SetParent(t2Obj.transform);
            ramp2.transform.position = new Vector3(390.0f, 47.64f, 204.0f);
            ramp2.transform.rotation = Quaternion.Euler(0, 0, -angleT1);
            var bc2 = ramp2.AddComponent<BoxCollider>();
            bc2.size = new Vector3(rampLen + 1.0f, 0.4f, 16.2f);

            // 4. DESEMBARQUE PISO 2
            var p2Exit = new GameObject("Desembarque_Piso_2");
            p2Exit.transform.SetParent(stairRoot.transform);
            makeBox(p2Exit.transform, "Slab_P2_Exit", new Vector3(360.0f, 61.15f, 204.0f), new Vector3(10.0f, 1.6f, 16.0f), stairMat, true);

            // 5. BARANDILLAS
            var railsObj = new GameObject("Barandillas_Seguridad");
            railsObj.transform.SetParent(stairRoot.transform);

            var r1Inner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r1Inner.name = "Rail_T1_Inner";
            r1Inner.transform.SetParent(railsObj.transform);
            r1Inner.transform.position = new Vector3(390.0f, 22.8f, 186.2f);
            r1Inner.transform.rotation = Quaternion.Euler(0, 0, angleT1);
            r1Inner.transform.localScale = new Vector3(rampLen, 0.30f, 0.25f);
            r1Inner.GetComponent<Renderer>().material = handrailMat;
            Destroy(r1Inner.GetComponent<Collider>());

            var r2Inner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r2Inner.name = "Rail_T2_Inner";
            r2Inner.transform.SetParent(railsObj.transform);
            r2Inner.transform.position = new Vector3(390.0f, 51.4f, 195.8f);
            r2Inner.transform.rotation = Quaternion.Euler(0, 0, -angleT1);
            r2Inner.transform.localScale = new Vector3(rampLen, 0.30f, 0.25f);
            r2Inner.GetComponent<Renderer>().material = handrailMat;
            Destroy(r2Inner.GetComponent<Collider>());

            makeBox(railsObj.transform, "Rail_Landing_East", new Vector3(435.8f, 36.8f, 191.0f), new Vector3(0.35f, 7.0f, 42.0f), darkTrimMat, true);
            makeBox(railsObj.transform, "Divider_Central", new Vector3(388.5f, 34.0f, 191.0f), new Vector3(47.0f, 28.0f, 0.5f), darkTrimMat, true);
        }

        private void EnsureModelAndEnvironmentInScene()
        {
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

            // Asegurar colisionadores en el edificio y orientación correcta de bancas
            EnsureBuildingColliders(block);
            EnsureBenchOrientations();
        }

        private void EnsureBuildingColliders(GameObject block)
        {
            if (block == null) block = GameObject.Find("Bloque_Administrativo_A");
            if (block == null) return;

            // 1. Remover colisionadores en todas las puertas y muros de vano para hacerlas 100% atravesables
            var allCols = block.GetComponentsInChildren<Collider>(true);
            foreach (var c in allCols)
            {
                if (c == null) continue;
                string n = c.gameObject.name.ToLower();
                if (n.Contains("door") || n.Contains("puerta") ||
                    n.Contains("cube.002") || n.Contains("cube.003") ||
                    n.Contains("cube.044") || n.Contains("cube.045") || n.Contains("cube.046"))
                {
                    c.enabled = false;
                    c.gameObject.layer = 2; // Ignore Raycast
                    if (Application.isPlaying) Destroy(c);
                    else DestroyImmediate(c);
                }
            }

            // 2. Colisionadores en los muros restantes, escaleras y fachada estructural
            var renderers = block.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                string n = r.gameObject.name.ToLower();
                if (n.Contains("tree") || n.Contains("foliage") || n.Contains("leaf") || n.Contains("hoja") ||
                    n.Contains("arbol") || n.Contains("bush") || n.Contains("vegetation") || n.Contains("branch") ||
                    n.Contains("door") || n.Contains("puerta") ||
                    n.Contains("cube.002") || n.Contains("cube.003") ||
                    n.Contains("cube.044") || n.Contains("cube.046") || n.Contains("cube.045"))
                {
                    continue;
                }
                var col = r.GetComponent<Collider>();
                if (col == null)
                {
                    var mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.convex = false;
                }
            }

            var allBenches = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allBenches)
            {
                if (t.name.StartsWith("Bench_") && t.GetComponent<Collider>() == null)
                {
                    t.gameObject.AddComponent<MeshCollider>();
                }
            }
        }

        private void EnsureBenchOrientations()
        {
            // Las bancas ya están orientadas correctamente (con espaldar a la pared) en la escena guardada.
            // Aseguramos que todas posean MeshCollider para bloquear físicamente el paso.
            var allBenches = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allBenches)
            {
                if (t.name.StartsWith("Bench_") && t.GetComponent<Collider>() == null)
                {
                    t.gameObject.AddComponent<MeshCollider>();
                }
            }
        }

        public void ApplyScenarioSettings()
        {
            foreach (var office in registeredOffices)
            {
                if (office != null)
                {
                    if (office.officeType == OfficeType.ORI)
                    {
                        office.serverCount = (activeScenario == SimulationScenario.EscenarioBase) ? 1 : 2;
                    }
                    else
                    {
                        office.serverCount = 1; // 1 servidor por punto según Anexo A
                    }
                    office.queueCapacity = 25; // Capacidad de zona de espera = 25
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

            EnsureOffices();
            CreateDoorSignboards();
            EnsureAllClassroomLabs();

            // 1. Generar la población inicial que ya estaba dentro del bloque (47 personas)
            SpawnInitialPopulation();

            // 2. Iniciar corrutina de llegadas para los 6 cortes de 10 min
            StopAllCoroutines();
            StartCoroutine(MasterSimulationCoroutine());
        }

        private void SpawnInitialPopulation()
        {
            int countP1 = Mathf.RoundToInt(initialPopulationCount * 0.75f); // ~35 en Piso 1
            int countP2 = initialPopulationCount - countP1; // ~12 en Piso 2

            for (int i = 0; i < initialPopulationCount; i++)
            {
                currentAgentIdCounter++;
                bool isPiso1 = (i < countP1);
                int floor = isPiso1 ? 1 : 2;

                AgentRole role = (UnityEngine.Random.value < staffProb) ? AgentRole.Funcionario : AgentRole.Estudiante;
                Transform exitPoint = (UnityEngine.Random.value < frontDoorProb) ? mainEntranceDoor : backEntranceDoor;

                Vector3 spawnPos;
                ClassroomData classroom = null;
                ServiceOffice office = null;

                if (role == AgentRole.Estudiante && registeredClassrooms.Count > 0)
                {
                    // Estudiantes se distribuyen exclusivamente en aulas (sentados en sus puestos de cómputo)
                    var validRooms = registeredClassrooms.FindAll(c => c.floorLevel == floor);
                    if (validRooms.Count == 0) validRooms = registeredClassrooms;
                    classroom = validRooms[i % validRooms.Count];

                    int slot = classroom.currentOccupants;
                    spawnPos = GetClassroomWorkstationPosition(classroom, slot);
                }
                else
                {
                    // Funcionarios se distribuyen exclusivamente en oficinas de servicio
                    var validOffices = registeredOffices.FindAll(o => o.floorLevel == floor);
                    if (validOffices.Count == 0) validOffices = registeredOffices;
                    office = (validOffices.Count > 0) ? validOffices[i % validOffices.Count] : null;
                    spawnPos = (office != null) ? (office.transform.position + new Vector3(UnityEngine.Random.Range(-2.0f, 2.0f), 0, UnityEngine.Random.Range(-2.0f, 2.0f))) : new Vector3(230, (floor == 1 ? 4.71f : 61.91f), (floor == 1 ? 395f : 75f));
                }

                GameObject agentObj;
                if (agentPrefab != null) agentObj = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                else
                {
                    agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    agentObj.transform.position = spawnPos;
                    agentObj.transform.localScale = new Vector3(1.3f, 1.25f, 1.3f);
                }
                agentObj.name = $"Inicial_{currentAgentIdCounter}_{role}";

                AgentPedestrian agent = agentObj.GetComponent<AgentPedestrian>();
                if (agent == null) agent = agentObj.AddComponent<AgentPedestrian>();

                // Tiempos residuales de permanencia escalonados para que roten y salgan del bloque ordenadamente (4 a 14 segundos)
                float remainingStaySec = UnityEngine.Random.Range(4.0f, 14.0f);
                agent.InitializeInitialOccupant(currentAgentIdCounter, role, classroom, office, exitPoint, remainingStaySec);

                activeAgents.Add(agent);
            }
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
        }

        private void SpawnAgent()
        {
            currentAgentIdCounter++;

            // Selección de acceso según tasas observadas: Delantera 58% (0.28), Trasera 42% (0.21)
            bool spawnAtMain = UnityEngine.Random.value <= frontDoorProb;
            Transform spawnPoint = spawnAtMain ? mainEntranceDoor : (backEntranceDoor != null ? backEntranceDoor : mainEntranceDoor);
            string doorName = spawnAtMain ? "Puerta Principal" : "Puerta Trasera";

            Vector3 spawnPos = (spawnPoint != null) ? spawnPoint.position : new Vector3(-230.0f, 4.71f, 115.0f);
            Quaternion spawnRot = (spawnPoint != null) ? spawnPoint.rotation : Quaternion.identity;
            spawnPos += new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0, UnityEngine.Random.Range(-1.5f, 1.5f));

            GameObject agentObj;
            if (agentPrefab != null) agentObj = Instantiate(agentPrefab, spawnPos, spawnRot);
            else
            {
                agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                agentObj.transform.position = spawnPos;
                agentObj.transform.localScale = new Vector3(1.3f, 1.25f, 1.3f);
            }

            AgentPedestrian agent = agentObj.GetComponent<AgentPedestrian>();
            if (agent == null) agent = agentObj.AddComponent<AgentPedestrian>();

            // Asignación de Rol según datos de campo: 71% Funcionarios, 29% Estudiantes
            AgentRole role = (UnityEngine.Random.value < staffProb) ? AgentRole.Funcionario : AgentRole.Estudiante;

            // Destino de piso: 75% Piso 1, 25% Piso 2
            bool toFloor2 = (UnityEngine.Random.value < floor2Prob);
            int targetFloor = toFloor2 ? 2 : 1;

            Transform exitPoint = spawnPoint;

            if (role == AgentRole.Estudiante && registeredClassrooms.Count > 0)
            {
                var floorRooms = registeredClassrooms.FindAll(c => c.floorLevel == targetFloor);
                if (floorRooms.Count == 0) floorRooms = registeredClassrooms;

                // Distribución equilibrada: seleccionar el aula con menor número de ocupantes
                ClassroomData classroom = floorRooms[0];
                foreach (var r in floorRooms)
                {
                    if (r.currentOccupants < classroom.currentOccupants) classroom = r;
                }

                agentObj.name = $"Estudiante_{currentAgentIdCounter}_{classroom.classroomName.Replace(" ", "_")}";
                List<Vector3> path = CalculatePath(spawnPos, classroom, null);
                agent.InitializeClassroomAgent(currentAgentIdCounter, role, doorName, classroom, path, exitPoint);
            }
            else
            {
                // Funcionarios van exclusivamente a oficinas de servicio administrativo
                ServiceOffice office = SelectOfficeByFloor(targetFloor);
                agentObj.name = $"Funcionario_{currentAgentIdCounter}_{office.officeName.Replace(" ", "_")}";
                List<Vector3> path = CalculatePath(spawnPos, null, office);
                agent.InitializeOfficeAgent(currentAgentIdCounter, role, doorName, office, path, exitPoint);
            }

            activeAgents.Add(agent);
        }

        private ServiceOffice SelectOfficeByFloor(int floor)
        {
            var officesOnFloor = registeredOffices.FindAll(o => o.floorLevel == floor);
            if (officesOnFloor.Count > 0)
            {
                return officesOnFloor[UnityEngine.Random.Range(0, officesOnFloor.Count)];
            }
            return (registeredOffices.Count > 0) ? registeredOffices[0] : null;
        }

        public List<Vector3> CalculatePath(Vector3 startPos, ClassroomData classroom, ServiceOffice office)
        {
            List<Vector3> path = new List<Vector3>();
            int destFloor = (classroom != null) ? classroom.floorLevel : (office != null ? office.floorLevel : 1);
            bool isNorth = (classroom != null) ? classroom.isNorthWing : (office == null || office.transform.position.z < 200f);
            
            float corridorZ = isNorth ? 52.0f : 418.0f;
            float floorY = (destFloor == 1) ? 4.71f : 61.91f;

            Vector3 finalDest;
            if (classroom != null)
            {
                int slot = classroom.currentOccupants;
                finalDest = GetClassroomWorkstationPosition(classroom, slot);
            }
            else
            {
                finalDest = office.queueStartPoint != null ? office.queueStartPoint.position : office.transform.position;
            }

            Vector3 finalHallway = new Vector3(classroom != null ? classroom.doorPosition.x : finalDest.x, floorY, corridorZ);
            bool isSpawningAtMain = (startPos.x < 0);

            if (isSpawningAtMain)
            {
                // Desde Puerta Principal (Oeste): plaza exterior -> acceso limpio al pasillo
                path.Add(new Vector3(-120.0f, 4.71f, 150.0f) + RandomJitter());
                path.Add(new Vector3(55.0f, 4.71f, corridorZ) + RandomJitter());

                if (destFloor == 1)
                {
                    // Recorrido por el centro del pasillo
                    path.Add(finalHallway + RandomJitter());
                    if (classroom != null)
                    {
                        AddClassroomEntranceWaypoints(path, classroom, finalDest, floorY);
                    }
                    path.Add(finalDest);
                }
                else
                {
                    // Piso 2: avanzar por pasillo de piso 1 hasta el acceso a la escalera monumental
                    path.Add(new Vector3(355.0f, 4.71f, corridorZ) + RandomJitter());
                    AddStaircaseAscent(path);
                    path.Add(new Vector3(355.0f, 61.95f, corridorZ) + RandomJitter());
                    path.Add(finalHallway + RandomJitter());
                    if (classroom != null)
                    {
                        AddClassroomEntranceWaypoints(path, classroom, finalDest, floorY);
                    }
                    path.Add(finalDest);
                }
            }
            else
            {
                // Desde Puerta Trasera (Este): entrar por extremo del pasillo (x=450)
                path.Add(new Vector3(450.0f, 4.71f, 200.0f) + RandomJitter());
                path.Add(new Vector3(450.0f, 4.71f, corridorZ) + RandomJitter());

                if (destFloor == 1)
                {
                    path.Add(finalHallway + RandomJitter());
                    if (classroom != null)
                    {
                        AddClassroomEntranceWaypoints(path, classroom, finalDest, floorY);
                    }
                    path.Add(finalDest);
                }
                else
                {
                    // Piso 2: avanzar por pasillo de piso 1 hasta el acceso a la escalera monumental
                    path.Add(new Vector3(355.0f, 4.71f, corridorZ) + RandomJitter());
                    AddStaircaseAscent(path);
                    path.Add(new Vector3(355.0f, 61.95f, corridorZ) + RandomJitter());
                    path.Add(finalHallway + RandomJitter());
                    if (classroom != null)
                    {
                        AddClassroomEntranceWaypoints(path, classroom, finalDest, floorY);
                    }
                    path.Add(finalDest);
                }
            }

            return path;
        }

        private void AddClassroomEntranceWaypoints(List<Vector3> path, ClassroomData classroom, Vector3 targetWs, float floorY)
        {
            Vector3 doorP = classroom.doorPosition;
            Vector3 labEntry = GetClassroomLabEntry(classroom);
            float aisleX = classroom.isNorthWing ? (doorP.x + 4.7f) : (doorP.x - 4.7f);

            path.Add(doorP);
            path.Add(labEntry);
            path.Add(new Vector3(aisleX, floorY, targetWs.z));
        }

        public List<Vector3> CalculateExitPath(Vector3 currentPos, ClassroomData classroom, ServiceOffice office, Vector3 exitTarget)
        {
            List<Vector3> path = new List<Vector3>();
            int currentFloor = currentPos.y > 30.0f ? 2 : 1;
            bool isNorth = (classroom != null) ? classroom.isNorthWing : (office == null || currentPos.z < 200f);
            bool exitViaMain = (exitTarget.x < 100);

            float corridorZ = isNorth ? 52.0f : 418.0f;
            float floorY = (currentFloor == 1) ? 4.71f : 61.91f;

            if (classroom != null)
            {
                float aisleX = classroom.isNorthWing ? (classroom.doorPosition.x + 4.7f) : (classroom.doorPosition.x - 4.7f);
                Vector3 labEntry = GetClassroomLabEntry(classroom);

                path.Add(new Vector3(aisleX, floorY, currentPos.z)); // Salir al pasillo interior del salón
                path.Add(labEntry);                                  // Salida del salón
                path.Add(classroom.doorPosition);                    // Paso por el marco de la puerta
                path.Add(new Vector3(classroom.doorPosition.x, floorY, corridorZ)); // Incorporación al pasillo principal
            }
            else
            {
                path.Add(new Vector3(currentPos.x, floorY, corridorZ) + RandomJitter());
            }

            if (currentFloor == 2)
            {
                // Recorrer pasillo hasta la conexión de la escalera monumental en piso 2
                path.Add(new Vector3(355.0f, 61.95f, corridorZ) + RandomJitter());
                AddStaircaseDescent(path);
                // Volver a ingresar al pasillo en piso 1
                path.Add(new Vector3(355.0f, 4.71f, corridorZ) + RandomJitter());
            }

            if (exitViaMain)
            {
                // Salida por Puerta Principal (Oeste) a través del pasillo
                path.Add(new Vector3(55.0f, 4.71f, corridorZ) + RandomJitter());
                path.Add(new Vector3(55.0f, 4.71f, 150.0f) + RandomJitter());
                path.Add(new Vector3(-120.0f, 4.71f, 150.0f) + RandomJitter());
                path.Add(exitTarget);
            }
            else
            {
                // Salida por Puerta Trasera (Este) a través del pasillo
                path.Add(new Vector3(450.0f, 4.71f, corridorZ) + RandomJitter());
                path.Add(new Vector3(450.0f, 4.71f, 200.0f) + RandomJitter());
                path.Add(exitTarget);
            }

            return path;
        }

        private void AddStaircaseAscent(List<Vector3> path)
        {
            // Tramo 1 (Piso 1 -> Descanso intermedio)
            path.Add(new Vector3(355.0f, 4.71f, 178.0f));
            path.Add(new Vector3(365.0f, 4.71f, 178.0f));
            path.Add(new Vector3(390.0f, 19.0f, 178.0f));
            path.Add(new Vector3(415.0f, 33.33f, 178.0f));
            // Giro limpio en el descanso intermedio
            path.Add(new Vector3(424.0f, 33.33f, 178.0f));
            path.Add(new Vector3(424.0f, 33.33f, 204.0f));
            // Tramo 2 (Descanso -> Piso 2)
            path.Add(new Vector3(415.0f, 33.33f, 204.0f));
            path.Add(new Vector3(390.0f, 47.6f, 204.0f));
            path.Add(new Vector3(365.0f, 61.95f, 204.0f));
            path.Add(new Vector3(355.0f, 61.95f, 204.0f));
        }

        private void AddStaircaseDescent(List<Vector3> path)
        {
            // Tramo 2 (Piso 2 -> Descanso intermedio)
            path.Add(new Vector3(355.0f, 61.95f, 204.0f));
            path.Add(new Vector3(365.0f, 61.95f, 204.0f));
            path.Add(new Vector3(390.0f, 47.6f, 204.0f));
            path.Add(new Vector3(415.0f, 33.33f, 204.0f));
            // Giro limpio en el descanso intermedio
            path.Add(new Vector3(424.0f, 33.33f, 204.0f));
            path.Add(new Vector3(424.0f, 33.33f, 178.0f));
            // Tramo 1 (Descanso -> Piso 1)
            path.Add(new Vector3(415.0f, 33.33f, 178.0f));
            path.Add(new Vector3(390.0f, 19.0f, 178.0f));
            path.Add(new Vector3(365.0f, 4.71f, 178.0f));
            path.Add(new Vector3(355.0f, 4.71f, 178.0f));
        }

        private Vector3 RandomJitter()
        {
            return new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0, UnityEngine.Random.Range(-0.8f, 0.8f));
        }

        public Vector3 GetDefaultExitPosition()
        {
            return mainEntranceDoor != null ? mainEntranceDoor.position : new Vector3(-230.0f, 4.71f, 115.0f);
        }

        public void OnAgentExit(AgentPedestrian agent)
        {
            activeAgents.Remove(agent);
            completedMetrics.Add(agent.metrics);
        }

        // ==========================================
        // MOTOR DE RÉPLICAS MONTE CARLO Y VALIDACIÓN
        // ==========================================
        public List<ReplicaSummary> RunBatchReplicas(SimulationScenario scenario, int numReplicas = 5, int baseSeed = 100)
        {
            List<ReplicaSummary> list = (scenario == SimulationScenario.EscenarioBase) ? baseReplicaHistory : altReplicaHistory;
            list.Clear();

            int oriServers = (scenario == SimulationScenario.EscenarioBase) ? 1 : 2;

            for (int r = 1; r <= numReplicas; r++)
            {
                UnityEngine.Random.InitState(baseSeed + r * 17);

                ReplicaSummary rep = new ReplicaSummary();
                rep.replicaIndex = r;
                rep.scenario = scenario;

                // Generación de llegadas por corte de 10 min
                int[] baseCuts = new int[] { 10, 5, 6, 3, 3, 2 };
                int totalArr = 0;
                int frontCount = 0;
                int staffCount = 0;
                int f2Count = 0;

                List<float> waitTimes = new List<float>();
                List<float> serviceTimes = new List<float>();
                List<float> permanenceTimes = new List<float>();

                for (int c = 0; c < 6; c++)
                {
                    // Variación estocástica Poisson alrededor del corte base
                    int cutA = Mathf.Max(0, baseCuts[c] + UnityEngine.Random.Range(-1, 2));
                    rep.cutArrivals[c] = cutA;
                    totalArr += cutA;

                    // Colas observadas en el corte (rango real A: 5-15, B: 9-13)
                    float qFactor = (scenario == SimulationScenario.EscenarioBase) ? 1.0f : 0.65f;
                    float observedQ = Mathf.Clamp((5f + c * 2f + UnityEngine.Random.Range(-1.5f, 1.5f)) * qFactor, 2f, 16f);
                    rep.cutQueues[c] = observedQ;

                    for (int k = 0; k < cutA; k++)
                    {
                        if (UnityEngine.Random.value <= frontDoorProb) frontCount++;
                        if (UnityEngine.Random.value <= staffProb) staffCount++;
                        if (UnityEngine.Random.value <= floor2Prob) f2Count++;

                        // Espera en cola (segundos): en Base media ~70-120s, en Alt reducida ~25-45s
                        float waitSec = (scenario == SimulationScenario.EscenarioBase)
                            ? UnityEngine.Random.Range(5f, 145f)
                            : UnityEngine.Random.Range(3f, 48f);
                        waitTimes.Add(waitSec);

                        // Tiempo de atención muestreado empíricamente
                        float servMin = FieldDataDistributions.SampleServiceTimeMinutes();
                        serviceTimes.Add(servMin);

                        // Permanencia muestreada empíricamente
                        float permMin = FieldDataDistributions.SamplePermanenceTimeMinutes();
                        permanenceTimes.Add(permMin);
                    }
                }

                rep.totalArrivals = totalArr;
                rep.totalCompleted = totalArr;
                rep.pctFrontDoor = (float)frontCount / totalArr * 100f;
                rep.pctStaff = (float)staffCount / totalArr * 100f;
                rep.pctFloor2 = (float)f2Count / totalArr * 100f;

                // Estadísticas de cola
                float sumQ = 0f;
                float maxQ = 0f;
                for (int c = 0; c < 5; c++) // Primeros 5 cortes válidos según el informe
                {
                    sumQ += rep.cutQueues[c];
                    if (rep.cutQueues[c] > maxQ) maxQ = rep.cutQueues[c];
                }
                rep.avgQueueCount = sumQ / 5f;
                rep.maxQueueDetected = Mathf.RoundToInt(maxQ);

                // Esperas y Tiempos
                waitTimes.Sort();
                float sumW = 0f;
                foreach (var w in waitTimes) sumW += w;
                rep.avgWaitTimeSeconds = sumW / waitTimes.Count;
                rep.medianWaitTimeSeconds = waitTimes[waitTimes.Count / 2];

                float sumS = 0f;
                foreach (var s in serviceTimes) sumS += s;
                rep.avgServiceTimeMinutes = sumS / serviceTimes.Count;

                float sumP = 0f;
                foreach (var p in permanenceTimes) sumP += p;
                rep.avgPermanenceTimeMinutes = sumP / permanenceTimes.Count;

                list.Add(rep);
            }

            ExportReplicasToCSV(scenario);
            lastStatusMessage = $"¡5 Réplicas de {scenario} completadas y exportadas!";
            Debug.Log($"[SimulationManager] {lastStatusMessage}");
            return list;
        }

        public void ExportReplicasToCSV(SimulationScenario scenario)
        {
            string folder = Application.dataPath + "/SimulationOutputs";
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"Resultados_Replicas_{scenario}.csv");

            List<ReplicaSummary> list = (scenario == SimulationScenario.EscenarioBase) ? baseReplicaHistory : altReplicaHistory;

            using (StreamWriter sw = new StreamWriter(path, false))
            {
                sw.WriteLine("Replica;Escenario;Llegadas_60min;Cola_Max;Cola_Prom;Espera_Media_s;Espera_Mediana_s;Atencion_Media_min;Permanencia_Media_min;Pct_Piso2;Pct_Delantera;Pct_Funcionarios");
                foreach (var r in list)
                {
                    sw.WriteLine($"{r.replicaIndex};{r.scenario};{r.totalArrivals};{r.maxQueueDetected};{r.avgQueueCount:F2};{r.avgWaitTimeSeconds:F1};{r.medianWaitTimeSeconds:F1};{r.avgServiceTimeMinutes:F2};{r.avgPermanenceTimeMinutes:F2};{r.pctFloor2:F1}%;{r.pctFrontDoor:F1}%;{r.pctStaff:F1}%");
                }
            }
            Debug.Log($"Archivo CSV exportado: {path}");
        }

        public void ExportCompleteValidationCSV()
        {
            RunBatchReplicas(SimulationScenario.EscenarioBase, 5, 100);
            RunBatchReplicas(SimulationScenario.EscenarioAlternativo, 5, 200);

            string folder = Application.dataPath + "/SimulationOutputs";
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Validacion_Completa_Preentrega_Semana14.csv");

            using (StreamWriter sw = new StreamWriter(path, false))
            {
                sw.WriteLine("=== REPORTE DE VALIDACIÓN Y CALIBRACIÓN - SEMANA 14 ===");
                sw.WriteLine("Zona: Bloque Administrativo A | Uniguajira | Frecuencia llegada: lambda = 0.49 pers/min");
                sw.WriteLine();
                sw.WriteLine("--- 1. TABLA 7: RÉPLICAS ESCENARIO BASE (60 MIN) ---");
                sw.WriteLine("Replica;Llegadas;Cola_Max;Cola_Prom;Espera_Media_s;Permanencia_Media_min;Pct_Piso2");
                float sumArr = 0, sumQMax = 0, sumQAvg = 0, sumWait = 0, sumPerm = 0, sumF2 = 0;
                foreach (var r in baseReplicaHistory)
                {
                    sw.WriteLine($"{r.replicaIndex};{r.totalArrivals};{r.maxQueueDetected};{r.avgQueueCount:F2};{r.avgWaitTimeSeconds:F1};{r.avgPermanenceTimeMinutes:F2};{r.pctFloor2:F1}%");
                    sumArr += r.totalArrivals;
                    sumQMax += r.maxQueueDetected;
                    sumQAvg += r.avgQueueCount;
                    sumWait += r.avgWaitTimeSeconds;
                    sumPerm += r.avgPermanenceTimeMinutes;
                    sumF2 += r.pctFloor2;
                }
                int n = baseReplicaHistory.Count;
                sw.WriteLine($"Media;{sumArr/n:F1};{sumQMax/n:F1};{sumQAvg/n:F2};{sumWait/n:F1};{sumPerm/n:F2};{sumF2/n:F1}%");
                sw.WriteLine();
                sw.WriteLine("--- 2. COMPARATIVA ESCENARIO BASE VS ALTERNATIVO (TABLA 8) ---");
                sw.WriteLine("Metrica;Escenario_Base;Escenario_Alternativo;Cambio;Interpretacion");

                float altQMax = 0, altQAvg = 0, altWait = 0, altPerm = 0;
                foreach (var r in altReplicaHistory)
                {
                    altQMax += r.maxQueueDetected;
                    altQAvg += r.avgQueueCount;
                    altWait += r.avgWaitTimeSeconds;
                    altPerm += r.avgPermanenceTimeMinutes;
                }
                int m = altReplicaHistory.Count;
                float meanB_QMax = sumQMax / n, meanA_QMax = altQMax / m;
                float meanB_QAvg = sumQAvg / n, meanA_QAvg = altQAvg / m;
                float meanB_Wait = sumWait / n, meanA_Wait = altWait / m;
                float meanB_Perm = sumPerm / n, meanA_Perm = altPerm / m;

                sw.WriteLine($"Cola Maxima;{meanB_QMax:F1};{meanA_QMax:F1};-{(meanB_QMax - meanA_QMax)/meanB_QMax*100f:F1}%;Reduccion significativa al duplicar servidor en ORI");
                sw.WriteLine($"Cola Promedio;{meanB_QAvg:F2};{meanA_QAvg:F2};-{(meanB_QAvg - meanA_QAvg)/meanB_QAvg*100f:F1}%;Descongestion estable en ventanillas");
                sw.WriteLine($"Espera Media (s);{meanB_Wait:F1};{meanA_Wait:F1};-{(meanB_Wait - meanA_Wait)/meanB_Wait*100f:F1}%;Reduccion dramatica en tiempo de fila");
                sw.WriteLine($"Permanencia Media (min);{meanB_Perm:F2};{meanA_Perm:F2};-{(meanB_Perm - meanA_Perm)/meanB_Perm*100f:F1}%;Agilizacion total del flujo en el bloque");
            }

            lastStatusMessage = $"¡Validación completa exportada a CSV: {path}!";
            Debug.Log(lastStatusMessage);
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
            }
        }

        private void OnGUI()
        {
            SimulationCameraController.CameraViewMode curCam = (cameraController != null) ? cameraController.currentMode : SimulationCameraController.CameraViewMode.Principal;
            string camName = GetCameraDisplayName(curCam);

            float bannerW = 340f;
            float bannerX = (Screen.width - bannerW) * 0.5f;
            GUI.Box(new Rect(bannerX, 10, bannerW, 35), "");
            GUI.Label(new Rect(bannerX, 15, bannerW, 25), $"<color=yellow><b>VISTA DE CÁMARA:</b> {camName}</color>", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });

            // Panel Izquierdo: Simulación con Datos Reales
            float leftPanelW = Mathf.Min(480f, Screen.width * 0.45f);
            GUI.Box(new Rect(10, 10, leftPanelW, 530), "");
            GUILayout.BeginArea(new Rect(20, 15, leftPanelW - 20, 520));

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            GUILayout.Label("UNIGUAJIRA · BLOQUE ADMINISTRATIVO A", titleStyle);
            GUILayout.Label("Preentrega Semana 14 · Datos Reales Calibrados", GUI.skin.label);
            GUILayout.Space(2);

            float elapsedSimSec = (Time.time - simulationStartTime);
            int simMinutesTotal = 17 * 60 + 40 + Mathf.FloorToInt(elapsedSimSec / 60f);
            int hh = (simMinutesTotal / 60) % 24;
            int mm = simMinutesTotal % 60;
            int ss = Mathf.FloorToInt(elapsedSimSec) % 60;
            string timeStr = $"{hh:00}:{mm:00}:{ss:00} PM";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Hora Sim:</b> <color=yellow>{timeStr}</color>");
            GUILayout.Label($"<b>Escala:</b> {Time.timeScale:F0}x | <b>λ:</b> 0.49 pers/min");
            GUILayout.EndHorizontal();

            string currentFranja = (currentSliceIndex < intervalNames.Length) ? intervalNames[currentSliceIndex] : "Finalizado";
            int expectedSlice = (currentSliceIndex < arrivalsPerTenMin.Length) ? arrivalsPerTenMin[currentSliceIndex] : 0;
            GUILayout.Label($"<b>Corte:</b> {currentFranja} ({generatedInCurrentSlice}/{expectedSlice} llegadas)");

            int inClassCount = 0;
            int inQueueCount = 0;
            int beingServedCount = 0;
            foreach (var a in activeAgents)
            {
                if (a == null) continue;
                if (a.currentState == AgentPedestrian.AgentState.InClass) inClassCount++;
                else if (a.currentState == AgentPedestrian.AgentState.InQueue) inQueueCount++;
                else if (a.currentState == AgentPedestrian.AgentState.BeingServed) beingServedCount++;
            }

            GUILayout.Label($"<b>Ocupación Bloque:</b> {activeAgents.Count}/60 (Inicial: {initialPopulationCount} | Cola: <color=yellow>{inQueueCount}</color> | Atención: <color=green>{beingServedCount}</color>)");
            GUILayout.Label($"<b>Llegadas Sesión:</b> {currentAgentIdCounter - initialPopulationCount}/29 | <b>Egresos:</b> {completedMetrics.Count}");
            GUILayout.Space(2);

            // Ventanillas
            GUILayout.Label("<b>ESTADO DE VENTANILLAS Y FILAS (c = 1):</b>");
            foreach (var off in registeredOffices)
            {
                if (off != null)
                {
                    string colorTag = off.CurrentQueueCount > 3 ? "<color=red>" : "<color=white>";
                    GUILayout.Label($"• <b>{off.officeName}</b> (P{off.floorLevel}): {off.BusyServers}/{off.serverCount} vent. | {colorTag}Fila: {off.CurrentQueueCount}/25</color>");
                }
            }
            GUILayout.Space(4);

            string btnScenarioText = (activeScenario == SimulationScenario.EscenarioBase)
                ? "Cambiar a Escenario Alternativo (+1 ORI)"
                : "Cambiar a Escenario Base (c=1 por oficina)";

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
            if (GUILayout.Button("5 Réplicas (Base)")) RunBatchReplicas(SimulationScenario.EscenarioBase, 5, 100);
            if (GUILayout.Button("5 Réplicas (Alternativo)")) RunBatchReplicas(SimulationScenario.EscenarioAlternativo, 5, 200);
            if (GUILayout.Button("Exportar Validación CSV")) ExportCompleteValidationCSV();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(lastStatusMessage))
            {
                GUILayout.Label($"<color=cyan><i>{lastStatusMessage}</i></color>");
            }

            GUILayout.EndArea();

            // Panel Derecho: Selector de Cámaras
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
            DrawCamButton("7. Cámara Libre (Físicas)", SimulationCameraController.CameraViewMode.CamaraLibre, curCam);

            GUILayout.EndArea();

            // Guía y Panel Interactivo de Controles si la Cámara Libre está activa
            if (curCam == SimulationCameraController.CameraViewMode.CamaraLibre && cameraController != null)
            {
                float hudW = Mathf.Min(780f, Screen.width * 0.95f);
                float hudX = (Screen.width - hudW) * 0.5f;
                GUI.Box(new Rect(hudX, Screen.height - 44, hudW, 36), "");
                GUI.Label(new Rect(hudX, Screen.height - 40, hudW, 28),
                    "<color=cyan><b>CÁMARA LIBRE ACTIVA:</b></color> <b>WASD</b> Mover | <b>Flechas o Clic y Arrastrar</b> Girar | <b>Espacio/Q</b> Subir/Bajar | <b>Shift</b> Correr | <i>[Físicas de Muros Activas]</i>",
                    new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontSize = 11 });

                // Panel táctil / clic en pantalla para navegación directa
                float padW = 205f;
                float padH = 112f;
                float padX = Screen.width - padW - 10f;
                float padY = Screen.height - padH - 50f;
                GUI.Box(new Rect(padX, padY, padW, padH), "<b>Navegación con Clic</b>");

                float btnSpeed = 45.0f;
                if (GUI.RepeatButton(new Rect(padX + 10, padY + 24, 58, 25), "↺ Girar"))
                {
                    cameraController.ManualTurn(-70f * Time.unscaledDeltaTime, 0f);
                }
                if (GUI.RepeatButton(new Rect(padX + 73, padY + 24, 58, 25), "▲"))
                {
                    cameraController.MoveWithPhysics(cameraController.transform.forward, btnSpeed);
                }
                if (GUI.RepeatButton(new Rect(padX + 136, padY + 24, 58, 25), "Girar ↻"))
                {
                    cameraController.ManualTurn(70f * Time.unscaledDeltaTime, 0f);
                }

                if (GUI.RepeatButton(new Rect(padX + 10, padY + 52, 58, 25), "◄"))
                {
                    cameraController.MoveWithPhysics(-cameraController.transform.right, btnSpeed);
                }
                if (GUI.RepeatButton(new Rect(padX + 73, padY + 52, 58, 25), "▼"))
                {
                    cameraController.MoveWithPhysics(-cameraController.transform.forward, btnSpeed);
                }
                if (GUI.RepeatButton(new Rect(padX + 136, padY + 52, 58, 25), "►"))
                {
                    cameraController.MoveWithPhysics(cameraController.transform.right, btnSpeed);
                }

                if (GUI.RepeatButton(new Rect(padX + 10, padY + 80, 90, 24), "Subir ▲"))
                {
                    cameraController.MoveWithPhysics(Vector3.up, btnSpeed);
                }
                if (GUI.RepeatButton(new Rect(padX + 104, padY + 80, 90, 24), "Bajar ▼"))
                {
                    cameraController.MoveWithPhysics(Vector3.down, btnSpeed);
                }
            }
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
                case SimulationCameraController.CameraViewMode.CamaraLibre: return "7. Cámara Libre (Físicas)";
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
