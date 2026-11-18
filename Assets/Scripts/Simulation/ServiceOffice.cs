using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Uniguajira.Simulation
{
    public class ServiceOffice : MonoBehaviour
    {
        [Header("Identificación")]
        public OfficeType officeType;
        public string officeName = "Oficina";
        public int floorLevel = 1;

        [Header("Capacidad de Servidores")]
        [Tooltip("Número de ventanillas o puestos de atención simultáneos (c)")]
        public int serverCount = 1;

        [Header("Distribución de Tiempos de Atención (Minutos)")]
        public float meanServiceTimeMinutes = 3.5f;
        public float stdDevServiceTimeMinutes = 0.8f;

        [Header("Puntos Espaciales")]
        public Transform serviceDeskPoint;
        public Transform queueStartPoint;
        public float queueSpacing = 2.0f;

        // Estado interno
        private readonly List<AgentPedestrian> waitingQueue = new List<AgentPedestrian>();
        private int activeServersBusy = 0;
        private TextMesh statusBillboard;

        public int CurrentQueueCount => waitingQueue.Count;
        public int BusyServers => activeServersBusy;

        private void Start()
        {
            CreateVisualDecorations();
        }

        private void CreateVisualDecorations()
        {
            // Crear cartel 3D flotante con el nombre de la oficina
            GameObject billboardObj = new GameObject("Billboard_" + officeName);
            billboardObj.transform.SetParent(transform);
            billboardObj.transform.localPosition = new Vector3(0, 3.5f, 0);

            statusBillboard = billboardObj.AddComponent<TextMesh>();
            statusBillboard.text = $"{officeName}\n[Piso {floorLevel}]";
            statusBillboard.fontSize = 24;
            statusBillboard.characterSize = 0.12f;
            statusBillboard.alignment = TextAlignment.Center;
            statusBillboard.anchor = TextAnchor.MiddleCenter;
            statusBillboard.color = floorLevel == 1 ? new Color(0.2f, 0.8f, 1f) : new Color(1f, 0.7f, 0.2f);

            // Crear mostrador 3D si no existe
            Transform deskVisual = transform.Find("DeskVisual");
            if (deskVisual == null)
            {
                GameObject desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                desk.name = "DeskVisual";
                desk.transform.SetParent(transform);
                desk.transform.localPosition = new Vector3(0, 0.6f, 0);
                desk.transform.localScale = new Vector3(3.0f, 1.2f, 1.0f);

                // Material marrón madera o gris oficina
                Renderer r = desk.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    r.material.color = new Color(0.35f, 0.25f, 0.15f);
                }

                Collider c = desk.GetComponent<Collider>();
                if (c != null) Destroy(c); // Evitar bloqueos de física
            }

            // Crear marcas visuales de cola en el suelo (discos amarillos)
            Transform queueParent = transform.Find("QueueMarkers");
            if (queueParent == null)
            {
                GameObject qp = new GameObject("QueueMarkers");
                qp.transform.SetParent(transform);
                qp.transform.localPosition = Vector3.zero;

                Vector3 queueDir = (queueStartPoint != null) ? -queueStartPoint.forward : -Vector3.forward;
                Vector3 origin = (queueStartPoint != null) ? queueStartPoint.localPosition : new Vector3(0, 0, -2f);

                for (int i = 0; i < 6; i++)
                {
                    GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    mark.name = $"Mark_{i + 1}";
                    mark.transform.SetParent(qp.transform);
                    mark.transform.localPosition = origin + queueDir * (i * queueSpacing);
                    mark.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);

                    Renderer mr = mark.GetComponent<Renderer>();
                    if (mr != null)
                    {
                        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                        mr.material.color = new Color(1f, 0.85f, 0.1f, 0.7f);
                    }

                    Collider mc = mark.GetComponent<Collider>();
                    if (mc != null) Destroy(mc);
                }
            }
        }

        private void Update()
        {
            if (statusBillboard != null)
            {
                statusBillboard.text = $"{officeName} (P{floorLevel})\n" +
                                       $"Ventanillas: {activeServersBusy}/{serverCount} | En fila: {waitingQueue.Count}";

                // Rotar cartel hacia la cámara principal
                if (Camera.main != null)
                {
                    statusBillboard.transform.rotation = Quaternion.LookRotation(statusBillboard.transform.position - Camera.main.transform.position);
                }
            }
        }

        public void EnqueueAgent(AgentPedestrian agent)
        {
            agent.metrics.reachQueueTime = Time.time;
            waitingQueue.Add(agent);
            UpdateQueueVisualPositions();
            TryServeNext();
        }

        private void TryServeNext()
        {
            while (activeServersBusy < serverCount && waitingQueue.Count > 0)
            {
                AgentPedestrian nextAgent = waitingQueue[0];
                waitingQueue.RemoveAt(0);
                activeServersBusy++;
                UpdateQueueVisualPositions();
                StartCoroutine(ServeAgentRoutine(nextAgent));
            }
        }

        private IEnumerator ServeAgentRoutine(AgentPedestrian agent)
        {
            if (agent == null)
            {
                activeServersBusy = Mathf.Max(0, activeServersBusy - 1);
                TryServeNext();
                yield break;
            }

            agent.metrics.serviceStartTime = Time.time;
            agent.SetBeingServedState();

            // Mover al mostrador de atención
            Vector3 deskPos = serviceDeskPoint != null ? serviceDeskPoint.position : transform.position + new Vector3(0, 0, 1.2f);
            agent.SetDestination(deskPos);

            // Muestreo estocástico del tiempo de atención (Distribución Normal con Box-Muller)
            float serviceDurationSec = SampleServiceTimeSeconds();
            yield return new WaitForSeconds(serviceDurationSec);

            activeServersBusy = Mathf.Max(0, activeServersBusy - 1);

            if (agent != null)
            {
                agent.metrics.serviceEndTime = Time.time;
                agent.OnServiceCompleted();
            }

            // Atender al siguiente de la cola
            TryServeNext();
        }

        private float SampleServiceTimeSeconds()
        {
            float u1 = Mathf.Max(0.0001f, Random.value);
            float u2 = Mathf.Max(0.0001f, Random.value);
            float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            float durationMin = meanServiceTimeMinutes + stdDevServiceTimeMinutes * randStdNormal;
            durationMin = Mathf.Max(0.5f, durationMin); // mínimo 30 segundos
            return durationMin * 60f;
        }

        private void UpdateQueueVisualPositions()
        {
            Vector3 queueOrigin = queueStartPoint != null ? queueStartPoint.position : transform.position - new Vector3(0, 0, 2f);
            Vector3 queueDir = queueStartPoint != null ? -queueStartPoint.forward : -Vector3.forward;

            for (int i = 0; i < waitingQueue.Count; i++)
            {
                Vector3 targetPos = queueOrigin + queueDir * (i * queueSpacing);
                waitingQueue[i].SetDestination(targetPos);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = floorLevel == 1 ? Color.cyan : Color.magenta;
            Gizmos.DrawWireCube(transform.position, new Vector3(4f, 2.5f, 3f));
            if (serviceDeskPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(serviceDeskPoint.position, 0.5f);
            }
            if (queueStartPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(queueStartPoint.position, 0.5f);
            }
        }
    }
}
