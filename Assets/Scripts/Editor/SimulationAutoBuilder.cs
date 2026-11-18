using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Uniguajira.Simulation;

namespace Uniguajira.Simulation.Editor
{
    [InitializeOnLoad]
    public static class SimulationAutoBuilder
    {
        static SimulationAutoBuilder()
        {
            EditorApplication.delayCall += AutoConfigureBridgeAndScene;
        }

        private static void AutoConfigureBridgeAndScene()
        {
            // 1. Configurar EditorPrefs para el puente MCP
            try
            {
                EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
                EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
                EditorPrefs.SetString("MCPForUnity.HttpUrl", "http://127.0.0.1:8080");
                EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "local");
                EditorPrefs.SetBool("MCPForUnity.HttpServerLaunchConfirmed", true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Configuración MCP EditorPrefs: " + ex.Message);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 2. Comprobar si el modelo 3D del Bloque está en la escena
            GameObject blockInstance = GameObject.Find("Bloque_Administrativo_A");
            if (blockInstance == null)
            {
                BuildSimulationScene(true);
            }
        }

        [MenuItem("Uniguajira/1. Construir Escena Completa de Simulación", false, 1)]
        public static void BuildSceneManual()
        {
            BuildSimulationScene(true);
        }

        [MenuItem("Uniguajira/2. Iniciar Simulación (Play)", false, 2)]
        public static void PlaySimulation()
        {
            EditorApplication.isPlaying = true;
        }

        public static void BuildSimulationScene(bool forceRebuild)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            Debug.Log($"Configurando simulación en escena: {activeScene.name}...");

            // 1. Instanciar Modelo 3D del Bloque Administrativo
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bloque_Administrativo_A.fbx");
            GameObject blockInstance = GameObject.Find("Bloque_Administrativo_A");
            if (blockInstance == null && modelPrefab != null)
            {
                blockInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                blockInstance.name = "Bloque_Administrativo_A";
                blockInstance.transform.position = Vector3.zero;
                blockInstance.transform.rotation = Quaternion.identity;
                Debug.Log("Modelo Bloque Administrativo A instanciado en la escena.");
            }

            // 2. Configurar Iluminación de Atardecer (05:40 PM)
            Light dirLight = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (dirLight == null)
            {
                GameObject lightObj = new GameObject("Directional Light");
                dirLight = lightObj.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }
            dirLight.transform.rotation = Quaternion.Euler(30f, -40f, 0f);
            dirLight.color = new Color(1.0f, 0.92f, 0.82f); // Luz cálida de atardecer
            dirLight.intensity = 1.3f;

            // 3. Configurar Cámara Principal y Controlador Multi-Ángulo
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                cam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
            }
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 2000f;

            SimulationCameraController camCtrl = cam.GetComponent<SimulationCameraController>();
            if (camCtrl == null) camCtrl = cam.gameObject.AddComponent<SimulationCameraController>();
            camCtrl.SetViewMode(SimulationCameraController.CameraViewMode.Principal, true);

            // 4. Crear o Configurar Puntos de Acceso (Spawners)
            GameObject accessContainer = GameObject.Find("Puntos_Acceso");
            if (accessContainer == null) accessContainer = new GameObject("Puntos_Acceso");

            Transform mainDoor = accessContainer.transform.Find("Puerta_Principal");
            if (mainDoor == null)
            {
                GameObject md = new GameObject("Puerta_Principal");
                md.transform.SetParent(accessContainer.transform);
                mainDoor = md.transform;
            }
            mainDoor.position = new Vector3(-230.0f, 5.0f, 115.0f);
            mainDoor.rotation = Quaternion.Euler(0, 73f, 0);

            Transform backDoor = accessContainer.transform.Find("Puerta_Trasera");
            if (backDoor == null)
            {
                GameObject bd = new GameObject("Puerta_Trasera");
                bd.transform.SetParent(accessContainer.transform);
                backDoor = bd.transform;
            }
            backDoor.position = new Vector3(460.0f, 5.0f, 200.0f);
            backDoor.rotation = Quaternion.Euler(0, -90f, 0);

            // 5. Configurar SimulationManager
            GameObject mgrObj = GameObject.Find("SimulationManager");
            if (mgrObj == null) mgrObj = new GameObject("SimulationManager");

            SimulationManager simMgr = mgrObj.GetComponent<SimulationManager>();
            if (simMgr == null) simMgr = mgrObj.AddComponent<SimulationManager>();

            simMgr.mainEntranceDoor = mainDoor;
            simMgr.backEntranceDoor = backDoor;
            simMgr.registeredOffices.Clear();

            // 6. Configurar Oficinas de Servicio
            GameObject officesContainer = GameObject.Find("Oficinas_Servicio");
            if (officesContainer == null) officesContainer = new GameObject("Oficinas_Servicio");

            // Piso 1: Vicerrectoría Extensión (3.5 min, 2 servidores)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.VicerrectoriaExtension,
                "Vicerrectoria_Extension", new Vector3(150.0f, 4.71f, -15.0f), 1, 2, 3.5f, 0.8f);

            // Piso 1: Facultad Ciencias Aplicadas (4.0 min, 2 servidores)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.CienciasAplicadas,
                "Facultad_Ciencias_Aplicadas", new Vector3(200.0f, 4.71f, -15.0f), 1, 2, 4.0f, 1.0f);

            // Piso 2: Oficina de Relaciones Internacionales ORI (3.0 min, 1 servidor en Base)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.ORI,
                "Oficina_ORI", new Vector3(190.0f, 61.91f, 75.0f), 2, 1, 3.0f, 0.6f);

            // Piso 2: Centro de Graduados (3.8 min, 2 servidores)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.CentroGraduados,
                "Centro_Graduados", new Vector3(250.0f, 61.91f, 75.0f), 2, 2, 3.8f, 0.9f);

            // Piso 2: Vicerrectoría de Docencia (4.5 min, 1 servidor)
            CreateOffice(officesContainer.transform, simMgr, OfficeType.VicerrectoriaDocencia,
                "Vicerrectoria_Docencia", new Vector3(310.0f, 61.91f, 75.0f), 2, 1, 4.5f, 1.2f);

            // 7. Configurar Colisionadores del Edificio y Orientación de Bancas
            ConfigureBuildingColliders(blockInstance);
            ConfigureBenchOrientations();

            // 8. Generar Prefab del Agente si no existe
            GameObject agentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Agent_Pedestrian.prefab");
            if (agentPrefab == null)
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/Prefabs");
                GameObject tempAgent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                tempAgent.name = "Agent_Pedestrian";
                tempAgent.transform.localScale = new Vector3(1.4f, 1.3f, 1.4f);
                tempAgent.AddComponent<CharacterController>();
                tempAgent.AddComponent<AgentPedestrian>();

                agentPrefab = PrefabUtility.SaveAsPrefabAsset(tempAgent, "Assets/Prefabs/Agent_Pedestrian.prefab");
                UnityEngine.Object.DestroyImmediate(tempAgent);
            }
            simMgr.agentPrefab = agentPrefab;

            // 9. Guardar Escena
            EditorUtility.SetDirty(mgrObj);
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            AssetDatabase.SaveAssets();

            Debug.Log("¡Simulación del Bloque Administrativo A configurada y guardada exitosamente!");
        }

        private static void ConfigureBuildingColliders(GameObject blockInstance)
        {
            if (blockInstance == null) blockInstance = GameObject.Find("Bloque_Administrativo_A");
            if (blockInstance == null) return;

            var renderers = blockInstance.GetComponentsInChildren<Renderer>(true);
            int addedCount = 0;
            foreach (var r in renderers)
            {
                string n = r.gameObject.name.ToLower();
                if (n.Contains("tree") || n.Contains("foliage") || n.Contains("leaf") || n.Contains("hoja") ||
                    n.Contains("arbol") || n.Contains("bush") || n.Contains("vegetation") || n.Contains("branch"))
                {
                    continue;
                }
                var col = r.GetComponent<Collider>();
                if (col == null)
                {
                    var mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.convex = false;
                    addedCount++;
                }
            }

            var allBenches = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allBenches)
            {
                if (t.name.StartsWith("Bench_") && t.GetComponent<Collider>() == null)
                {
                    t.gameObject.AddComponent<MeshCollider>();
                }
            }
            Debug.Log($"Colisionadores de muros configurados en el Bloque ({addedCount} MeshColliders nuevos añadidos).");
        }

        private static void ConfigureBenchOrientations()
        {
            var allBenches = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            int flipped = 0;
            foreach (var t in allBenches)
            {
                if (!t.name.StartsWith("Bench_")) continue;
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                float topZSum = 0, seatZSum = 0;
                int topCount = 0, seatCount = 0;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = t.TransformPoint(v);
                    if (w.y > (t.position.y + 4.5f)) { topZSum += w.z; topCount++; }
                    if (w.y > (t.position.y + 1.0f) && w.y < (t.position.y + 3.0f)) { seatZSum += w.z; seatCount++; }
                }
                float topZ = topCount > 0 ? (topZSum / topCount) : t.position.z;
                float seatZ = seatCount > 0 ? (seatZSum / seatCount) : t.position.z;

                bool isNorth = t.position.z < 200f;
                bool needsFlip = isNorth ? (topZ > seatZ) : (topZ < seatZ);

                if (needsFlip)
                {
                    var rend = t.GetComponent<Renderer>();
                    Vector3 center = rend != null ? rend.bounds.center : t.position;
                    t.RotateAround(center, Vector3.up, 180f);
                    EditorUtility.SetDirty(t);
                    flipped++;
                }
            }
            Debug.Log($"Orientación de bancas verificada ({flipped} rotadas a espaldar contra la pared).");
        }

        private static void CreateOffice(Transform parent, SimulationManager mgr, OfficeType type, string name,
            Vector3 pos, int floor, int servers, float meanMin, float stdDevMin)
        {
            Transform existing = parent.Find(name);
            GameObject offObj = existing != null ? existing.gameObject : new GameObject(name);
            offObj.transform.SetParent(parent);
            offObj.transform.position = pos;

            ServiceOffice office = offObj.GetComponent<ServiceOffice>();
            if (office == null) office = offObj.AddComponent<ServiceOffice>();

            office.officeType = type;
            office.officeName = name.Replace("_", " ");
            office.floorLevel = floor;
            office.serverCount = servers;
            office.meanServiceTimeMinutes = meanMin;
            office.stdDevServiceTimeMinutes = stdDevMin;

            Transform desk = offObj.transform.Find("DeskPoint");
            if (desk == null)
            {
                GameObject d = new GameObject("DeskPoint");
                d.transform.SetParent(offObj.transform);
                d.transform.localPosition = new Vector3(0, 0, 1.5f);
                desk = d.transform;
            }
            office.serviceDeskPoint = desk;

            Transform queue = offObj.transform.Find("QueuePoint");
            if (queue == null)
            {
                GameObject q = new GameObject("QueuePoint");
                q.transform.SetParent(offObj.transform);
                q.transform.localPosition = new Vector3(0, 0, -2.5f);
                queue = q.transform;
            }
            office.queueStartPoint = queue;

            mgr.registeredOffices.Add(office);
        }
    }
}
