using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Uniguajira.Simulation
{
    public class AgentPedestrian : MonoBehaviour
    {
        public enum AgentState
        {
            Spawning,
            WalkingToOffice,
            InQueue,
            BeingServed,
            Exiting,
            Done
        }

        [Header("Estado y Métricas")]
        public AgentState currentState = AgentState.Spawning;
        public AgentMetrics metrics = new AgentMetrics();

        [Header("Configuración de Movimiento")]
        public float walkSpeed = 6.0f; // Escala proporcional a dimensiones del modelo
        public float angularSpeed = 360f;
        public float stoppingDistance = 1.0f;

        private NavMeshAgent navAgent;
        private ServiceOffice targetOffice;
        private Transform exitPoint;
        private Vector3 currentTargetPosition;

        // Rutas y Waypoints para cambio de piso
        private readonly Queue<Vector3> waypointQueue = new Queue<Vector3>();
        private bool hasWaypoints = false;

        // Visuales
        private Renderer bodyRenderer;
        private TextMesh overheadLabel;
        private float bobbingTimer = 0f;
        private Vector3 baseVisualPosition;

        // Waypoints de la escalera monumental
        private static readonly Vector3 StairBottom = new Vector3(-385.0f, 1.1f, -200.0f);
        private static readonly Vector3 StairLanding = new Vector3(-400.0f, 29.7f, -170.0f);
        private static readonly Vector3 StairTop = new Vector3(-415.0f, 58.3f, -200.0f);

        private void Awake()
        {
            navAgent = GetComponent<NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.speed = walkSpeed;
                navAgent.stoppingDistance = stoppingDistance;
                navAgent.autoBraking = true;
            }

            SetupVisualComponents();
        }

        private void SetupVisualComponents()
        {
            bodyRenderer = GetComponentInChildren<Renderer>();
            if (bodyRenderer == null)
            {
                // Crear cápsula visual si no existe
                GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.transform.SetParent(transform);
                capsule.transform.localPosition = new Vector3(0, 1.25f, 0);
                capsule.transform.localScale = new Vector3(1.4f, 1.25f, 1.4f);
                bodyRenderer = capsule.GetComponent<Renderer>();

                Collider col = capsule.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            // Material independiente por agente
            if (bodyRenderer != null)
            {
                bodyRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                SetColor(new Color(0.2f, 0.6f, 1.0f)); // Azul estudiante inicial
            }

            // Etiqueta flotante
            Transform existingLabel = transform.Find("AgentLabel");
            if (existingLabel == null)
            {
                GameObject labelObj = new GameObject("AgentLabel");
                labelObj.transform.SetParent(transform);
                labelObj.transform.localPosition = new Vector3(0, 3.2f, 0);

                overheadLabel = labelObj.AddComponent<TextMesh>();
                overheadLabel.fontSize = 20;
                overheadLabel.characterSize = 0.10f;
                overheadLabel.alignment = TextAlignment.Center;
                overheadLabel.anchor = TextAnchor.MiddleCenter;
                overheadLabel.color = Color.white;
            }
            else
            {
                overheadLabel = existingLabel.GetComponent<TextMesh>();
            }
        }

        public void Initialize(int id, string doorName, ServiceOffice office, Transform exit)
        {
            metrics.agentId = id;
            metrics.entranceDoor = doorName;
            metrics.destination = office.officeType;
            metrics.spawnTime = Time.time;

            targetOffice = office;
            exitPoint = exit;

            if (overheadLabel != null)
            {
                overheadLabel.text = $"#{id} [{office.officeName.Replace("_", " ")}]";
            }

            currentState = AgentState.WalkingToOffice;
            SetColor(new Color(0.2f, 0.6f, 1.0f)); // Azul de camino a oficina

            Vector3 finalDest = targetOffice.queueStartPoint != null ? targetOffice.queueStartPoint.position : targetOffice.transform.position;
            NavigateTo(finalDest, office.floorLevel);
        }

        private void NavigateTo(Vector3 destination, int destinationFloor)
        {
            waypointQueue.Clear();

            int currentFloor = transform.position.y > 30.0f ? 2 : 1;

            // Si hay cambio de piso, planear camino por la escalera
            if (currentFloor == 1 && destinationFloor == 2)
            {
                waypointQueue.Enqueue(StairBottom);
                waypointQueue.Enqueue(StairLanding);
                waypointQueue.Enqueue(StairTop);
            }
            else if (currentFloor == 2 && destinationFloor == 1)
            {
                waypointQueue.Enqueue(StairTop);
                waypointQueue.Enqueue(StairLanding);
                waypointQueue.Enqueue(StairBottom);
            }

            waypointQueue.Enqueue(destination);
            hasWaypoints = true;
            AdvanceNextWaypoint();
        }

        private void AdvanceNextWaypoint()
        {
            if (waypointQueue.Count > 0)
            {
                currentTargetPosition = waypointQueue.Dequeue();
                SetDestinationInternal(currentTargetPosition);
            }
            else
            {
                hasWaypoints = false;
            }
        }

        public void SetDestination(Vector3 target)
        {
            currentTargetPosition = target;
            SetDestinationInternal(target);
        }

        private void SetDestinationInternal(Vector3 target)
        {
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                navAgent.SetDestination(target);
            }
        }

        private void Update()
        {
            UpdateOverheadRotation();
            HandleKinematicMovement();

            // Comportamiento según estado
            if (currentState == AgentState.WalkingToOffice)
            {
                if (HasReachedTarget())
                {
                    if (waypointQueue.Count > 0)
                    {
                        AdvanceNextWaypoint();
                    }
                    else
                    {
                        currentState = AgentState.InQueue;
                        SetColor(new Color(1f, 0.75f, 0.1f)); // Amarillo en cola
                        targetOffice.EnqueueAgent(this);
                    }
                }
            }
            else if (currentState == AgentState.Exiting)
            {
                if (HasReachedTarget())
                {
                    if (waypointQueue.Count > 0)
                    {
                        AdvanceNextWaypoint();
                    }
                    else
                    {
                        currentState = AgentState.Done;
                        metrics.exitTime = Time.time;
                        SimulationManager.Instance.OnAgentExit(this);
                        Destroy(gameObject);
                    }
                }
            }
        }

        private void HandleKinematicMovement()
        {
            // Movimiento híbrido cuando NavMesh no está activo o se cruzan desniveles
            bool useNavMesh = (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh);

            if (!useNavMesh && (currentState == AgentState.WalkingToOffice || currentState == AgentState.Exiting || currentState == AgentState.InQueue))
            {
                Vector3 toTarget = currentTargetPosition - transform.position;
                float dist = toTarget.magnitude;

                if (dist > 0.2f)
                {
                    // Rotar hacia el destino
                    Vector3 forwardDir = new Vector3(toTarget.x, 0, toTarget.z);
                    if (forwardDir.sqrMagnitude > 0.001f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(forwardDir);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, angularSpeed * Time.deltaTime);
                    }

                    // Avanzar hacia el destino
                    transform.position = Vector3.MoveTowards(transform.position, currentTargetPosition, walkSpeed * Time.deltaTime);

                    // Efecto de caminado (bobbing)
                    bobbingTimer += Time.deltaTime * 8.0f;
                    float bob = Mathf.Sin(bobbingTimer) * 0.1f;
                    if (bodyRenderer != null)
                    {
                        Vector3 lp = bodyRenderer.transform.localPosition;
                        bodyRenderer.transform.localPosition = new Vector3(lp.x, 1.25f + bob, lp.z);
                    }
                }
            }
        }

        private bool HasReachedTarget()
        {
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                return !navAgent.pathPending && navAgent.remainingDistance <= stoppingDistance;
            }
            else
            {
                return Vector3.Distance(transform.position, currentTargetPosition) <= (stoppingDistance * 1.5f);
            }
        }

        public void SetBeingServedState()
        {
            currentState = AgentState.BeingServed;
            SetColor(new Color(0.1f, 0.9f, 0.2f)); // Verde siendo atendido
        }

        public void OnServiceCompleted()
        {
            currentState = AgentState.Exiting;
            SetColor(new Color(0.6f, 0.6f, 0.6f)); // Gris saliendo

            Vector3 exitPos = exitPoint != null ? exitPoint.position : StairBottom;
            int exitFloor = (exitPoint != null && exitPoint.position.y > 30f) ? 2 : 1;
            NavigateTo(exitPos, exitFloor);
        }

        private void SetColor(Color col)
        {
            if (bodyRenderer != null && bodyRenderer.material != null)
            {
                bodyRenderer.material.color = col;
            }
        }

        private void UpdateOverheadRotation()
        {
            if (overheadLabel != null && Camera.main != null)
            {
                overheadLabel.transform.rotation = Quaternion.LookRotation(overheadLabel.transform.position - Camera.main.transform.position);
            }
        }
    }
}
