using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace Uniguajira.Simulation.Editor
{
    public class SimulationSetupWindow : EditorWindow
    {
        [MenuItem("Uniguajira/Configurar Simulación Bloque A")]
        public static void ShowWindow()
        {
            var window = GetWindow<SimulationSetupWindow>("Simulación Uniguajira");
            window.minSize = new Vector2(450, 480);
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("UNIGUAJIRA · BLOQUE ADMINISTRATIVO A", EditorStyles.boldLabel);
            GUILayout.Label("Configurador Automático de Escena y Simulación", EditorStyles.miniLabel);
            GUILayout.Space(15);

            EditorGUILayout.HelpBox(
                "Esta herramienta configura automáticamente la escena con el modelo 3D del Bloque Administrativo A, " +
                "los puntos de acceso (Puerta Principal y Trasera), las 5 oficinas de servicio, " +
                "el gestor de simulación estocástica y el prefab de agentes.",
                MessageType.Info);

            GUILayout.Space(15);

            if (GUILayout.Button("1. Configurar Todo en la Escena Actual", GUILayout.Height(40)))
            {
                SimulationAutoBuilder.BuildSimulationScene(true);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("2. Hornear NavMesh (Bake Navigation)", GUILayout.Height(35)))
            {
                BakeNavMeshSurface();
            }

            GUILayout.Space(20);
            GUILayout.Label("Parámetros del Bloque Administrativo:", EditorStyles.boldLabel);
            GUILayout.Label("• Horario Pico Observado: 05:40 PM - 06:40 PM");
            GUILayout.Label("• Aforo promedio: 66 personas / hora");
            GUILayout.Label("• Piso 1: Vicerrectoría de Extensión, Facultad de Ciencias Aplicadas");
            GUILayout.Label("• Piso 2: ORI, Centro de Graduados, Vicerrectoría de Docencia");
            GUILayout.Label("• Disciplina: FIFO | Servidores: Ventanillas individuales");
        }

        private static void SetupFullSimulationScene()
        {
            // 1. Instanciar Modelo 3D del Bloque
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bloque_Administrativo_A.fbx");
            GameObject blockInstance = GameObject.Find("Bloque_Administrativo_A");
            if (blockInstance == null && modelPrefab != null)
            {
                blockInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                blockInstance.name = "Bloque_Administrativo_A";
                blockInstance.transform.position = Vector3.zero;
                // Marcar como static para navmesh
                GameObjectUtility.SetStaticEditorFlags(blockInstance, StaticEditorFlags.NavigationStatic);
            }

            // 2. Crear Manager
            GameObject mgrObj = GameObject.Find("SimulationManager");
            if (mgrObj == null)
            {
                mgrObj = new GameObject("SimulationManager");
            }
            SimulationManager simMgr = mgrObj.GetComponent<SimulationManager>();
            if (simMgr == null) simMgr = mgrObj.AddComponent<SimulationManager>();

            // 3. Crear Puntos de Acceso
            GameObject spawnContainer = GameObject.Find("Puntos_Acceso");
            if (spawnContainer == null) spawnContainer = new GameObject("Puntos_Acceso");

            Transform mainDoor = spawnContainer.transform.Find("Puerta_Principal");
            if (mainDoor == null)
            {
                GameObject md = new GameObject("Puerta_Principal");
                md.transform.SetParent(spawnContainer.transform);
                md.transform.position = new Vector3(-11.7f, 1.1f, -195.0f); // Frente fachada principal
                mainDoor = md.transform;
            }

            Transform backDoor = spawnContainer.transform.Find("Puerta_Trasera");
            if (backDoor == null)
            {
                GameObject bd = new GameObject("Puerta_Trasera");
                bd.transform.SetParent(spawnContainer.transform);
                bd.transform.position = new Vector3(-470.0f, 1.1f, -239.0f); // Acceso posterior
                backDoor = bd.transform;
            }

            simMgr.mainEntranceDoor = mainDoor;
            simMgr.backEntranceDoor = backDoor;

            // 4. Crear Oficinas de Servicio
            GameObject officesContainer = GameObject.Find("Oficinas_Servicio");
            if (officesContainer == null) officesContainer = new GameObject("Oficinas_Servicio");

            simMgr.registeredOffices.Clear();

            // Oficina 1: Vicerrectoría Extensión (Piso 1)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.VicerrectoriaExtension, "Vicerrectoria_Extension",
                new Vector3(-150.0f, 1.1f, -25.0f), 1, 2, 3.5f, 0.8f);

            // Oficina 2: Facultad Ciencias Aplicadas (Piso 1)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.CienciasAplicadas, "Facultad_Ciencias_Aplicadas",
                new Vector3(-220.0f, 1.1f, -25.0f), 1, 2, 4.0f, 1.0f);

            // Oficina 3: ORI (Piso 2)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.ORI, "Oficina_ORI",
                new Vector3(-150.0f, 58.3f, -25.0f), 2, 1, 3.0f, 0.6f);

            // Oficina 4: Centro de Graduados (Piso 2)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.CentroGraduados, "Centro_Graduados",
                new Vector3(-220.0f, 58.3f, -25.0f), 2, 2, 3.8f, 0.9f);

            // Oficina 5: Vicerrectoría Docencia (Piso 2)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.VicerrectoriaDocencia, "Vicerrectoria_Docencia",
                new Vector3(-290.0f, 58.3f, -25.0f), 2, 1, 4.5f, 1.2f);

            // 5. Crear Prefab de Agente si no existe
            GameObject agentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agent_Pedestrian.prefab");
            if (agentPrefab == null)
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/Prefabs");
                GameObject tempAgent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                tempAgent.name = "Agent_Pedestrian";
                tempAgent.transform.localScale = new Vector3(1.5f, 2.5f, 1.5f);
                tempAgent.AddComponent<NavMeshAgent>();
                tempAgent.AddComponent<AgentPedestrian>();

                agentPrefab = PrefabUtility.SaveAsPrefabAsset(tempAgent, "Assets/Prefabs/Agent_Pedestrian.prefab");
                DestroyImmediate(tempAgent);
            }
            simMgr.agentPrefab = agentPrefab;

            // 6. Configurar NavMeshSurface si está disponible
            NavMeshSurface surface = blockInstance != null ? blockInstance.GetComponent<NavMeshSurface>() : null;
            if (surface == null && blockInstance != null)
            {
                surface = blockInstance.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
            }

            EditorUtility.SetDirty(mgrObj);
            AssetDatabase.SaveAssets();
            Debug.Log("Simulación de Bloque Administrativo A configurada con éxito.");
        }

        private static void CreateOffice(Transform parent, SimulationManager mgr, OfficeType type, string name,
            Vector3 pos, int floor, int servers, float meanServiceMin, float stdDevMin)
        {
            Transform existing = parent.Find(name);
            GameObject offObj = existing != null ? existing.gameObject : new GameObject(name);
            offObj.transform.SetParent(parent);
            offObj.transform.position = pos;

            ServiceOffice office = offObj.GetComponent<ServiceOffice>();
            if (office == null) office = offObj.AddComponent<ServiceOffice>();

            office.officeType = type;
            office.officeName = name;
            office.floorLevel = floor;
            office.serverCount = servers;
            office.meanServiceTimeMinutes = meanServiceMin;
            office.stdDevServiceTimeMinutes = stdDevMin;

            // Crear puntos de fila y mostrador si no existen
            Transform desk = offObj.transform.Find("DeskPoint");
            if (desk == null)
            {
                GameObject d = new GameObject("DeskPoint");
                d.transform.SetParent(offObj.transform);
                d.transform.localPosition = new Vector3(0, 0, 2f);
                desk = d.transform;
            }
            office.serviceDeskPoint = desk;

            Transform queue = offObj.transform.Find("QueuePoint");
            if (queue == null)
            {
                GameObject q = new GameObject("QueuePoint");
                q.transform.SetParent(offObj.transform);
                q.transform.localPosition = new Vector3(0, 0, -2f);
                queue = q.transform;
            }
            office.queueStartPoint = queue;

            mgr.registeredOffices.Add(office);
        }

        private static void BakeNavMeshSurface()
        {
            NavMeshSurface[] surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            if (surfaces.Length > 0)
            {
                foreach (var s in surfaces)
                {
                    s.BuildNavMesh();
                }
                Debug.Log("NavMesh horneado exitosamente con NavMeshSurface.");
            }
            else
            {
                Debug.LogWarning("No se encontró NavMeshSurface. Configura primero la escena.");
            }
        }
    }
}
