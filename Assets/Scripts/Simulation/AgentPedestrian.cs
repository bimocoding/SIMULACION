using System.Collections;
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
            WalkingToDestination,
            InClass,
            InQueue,
            BeingServed,
            Exiting,
            Done
        }

        [Header("Estado y Perfil")]
        public AgentState currentState = AgentState.Spawning;
        public AgentRole role = AgentRole.Estudiante;
        public AgentDestinationType destinationType = AgentDestinationType.Classroom;
        public AgentMetrics metrics = new AgentMetrics();

        [Header("Configuración de Movimiento y Físicas")]
        public float walkSpeed = 6.5f;
        public float angularSpeed = 400f;
        public float stoppingDistance = 1.3f;

        private CharacterController characterController;
        private NavMeshAgent navAgent;
        private ClassroomData targetClassroom;
        private ServiceOffice targetOffice;
        private Transform exitPoint;
        private Vector3 currentTargetPosition;

        // Cola de waypoints para navegación precisa por pasillos y escaleras
        private readonly Queue<Vector3> waypointQueue = new Queue<Vector3>();
        private bool hasWaypoints = false;
        private float timeBlockedOnTarget = 0f;
        private Vector3 lastStuckPos;
        private float stuckTimer = 0f;

        // Visuales
        private Transform visualTransform;
        private Renderer bodyRenderer;
        private TextMesh overheadLabel;
        private float bobbingTimer = 0f;
        private Color baseRoleColor = new Color(0.2f, 0.65f, 1.0f);

        private void Awake()
        {
            EnsurePhysicsComponents();
            SetupVisualComponents();
            lastStuckPos = transform.position;
        }

        private void EnsurePhysicsComponents()
        {
            // Remover cualquier collider previo en el root que no sea CharacterController
            var colliders = GetComponents<Collider>();
            foreach (var c in colliders)
            {
                if (!(c is CharacterController))
                {
                    Destroy(c);
                }
            }

            // Capa 8 para ignorar colisiones físicas entre agentes pero respetar rigurosamente muros y bancas
            gameObject.layer = 8;
            Physics.IgnoreLayerCollision(8, 8, true);

            characterController = GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
            }
            characterController.radius = 0.4f;
            characterController.height = 1.8f;
            characterController.center = new Vector3(0, 0.9f, 0);
            characterController.stepOffset = 0.5f;
            characterController.slopeLimit = 60f;
            characterController.minMoveDistance = 0f;

            navAgent = GetComponent<NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.speed = walkSpeed;
                navAgent.stoppingDistance = stoppingDistance;
                navAgent.autoBraking = true;
            }
        }

        private void SetupVisualComponents()
        {
            MeshRenderer rootRend = GetComponent<MeshRenderer>();
            MeshFilter rootMf = GetComponent<MeshFilter>();
            if (rootRend != null) Destroy(rootRend);
            if (rootMf != null) Destroy(rootMf);

            Transform existingVisual = transform.Find("AgentVisual");
            if (existingVisual == null)
            {
                GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "AgentVisual";
                capsule.transform.SetParent(transform);
                capsule.transform.localPosition = new Vector3(0, 1.25f, 0);
                capsule.transform.localScale = new Vector3(1.3f, 1.25f, 1.3f);
                visualTransform = capsule.transform;
                bodyRenderer = capsule.GetComponent<Renderer>();

                Collider col = capsule.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            else
            {
                visualTransform = existingVisual;
                bodyRenderer = existingVisual.GetComponent<Renderer>();
            }

            if (bodyRenderer != null)
            {
                bodyRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            }

            // Etiqueta flotante 3D
            Transform existingLabel = transform.Find("AgentLabel");
            if (existingLabel == null)
            {
                GameObject labelObj = new GameObject("AgentLabel");
                labelObj.transform.SetParent(transform);
                labelObj.transform.localPosition = new Vector3(0, 3.4f, 0);

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

        public void InitializeClassroomAgent(int id, AgentRole agentRole, string doorName, ClassroomData classroom, List<Vector3> pathWaypoints, Transform exit)
        {
            role = agentRole;
            destinationType = AgentDestinationType.Classroom;
            targetClassroom = classroom;
            targetOffice = null;
            exitPoint = exit;

            metrics.agentId = id;
            metrics.role = agentRole;
            metrics.destinationType = AgentDestinationType.Classroom;
            metrics.entranceDoor = doorName;
            metrics.destinationName = classroom.classroomName;
            metrics.floorLevel = classroom.floorLevel;
            metrics.spawnTime = Time.time;

            if (role == AgentRole.Estudiante)
            {
                baseRoleColor = new Color(0.2f, 0.65f, 1.0f); // Azul estudiante
            }
            else if (role == AgentRole.Docente)
            {
                baseRoleColor = new Color(0.1f, 0.85f, 0.55f); // Verde azulado docente
            }
            else
            {
                baseRoleColor = new Color(1.0f, 0.55f, 0.1f); // Naranja funcionario
            }
            SetColor(baseRoleColor);

            if (overheadLabel != null)
            {
                string roleTag = role == AgentRole.Estudiante ? "Est." : (role == AgentRole.Docente ? "Prof." : "Func.");
                overheadLabel.text = $"[{roleTag} #{id}]\n► {classroom.classroomName}";
            }

            LoadWaypoints(pathWaypoints);
            currentState = AgentState.WalkingToDestination;
        }

        public void InitializeInitialOccupant(int id, AgentRole agentRole, ClassroomData classroom, ServiceOffice office, Transform exit, float remainingStaySeconds)
        {
            role = agentRole;
            exitPoint = exit;
            targetClassroom = classroom;
            targetOffice = office;
            destinationType = (classroom != null) ? AgentDestinationType.Classroom : AgentDestinationType.ServiceOffice;

            metrics.agentId = id;
            metrics.role = agentRole;
            metrics.destinationType = destinationType;
            metrics.entranceDoor = "Ya Estaba (Población Inicial)";
            metrics.destinationName = (classroom != null) ? classroom.classroomName : office.officeName;
            metrics.floorLevel = (classroom != null) ? classroom.floorLevel : office.floorLevel;
            metrics.isInitialPopulation = true;
            metrics.spawnTime = Time.time;
            metrics.reachDestinationTime = Time.time;
            metrics.serviceStartTime = Time.time;

            if (role == AgentRole.Estudiante) baseRoleColor = new Color(0.2f, 0.65f, 1.0f);
            else if (role == AgentRole.Docente) baseRoleColor = new Color(0.1f, 0.85f, 0.55f);
            else baseRoleColor = new Color(1.0f, 0.55f, 0.1f);
            SetColor(baseRoleColor);

            if (overheadLabel != null)
            {
                string tag = (classroom != null) ? "[En Aula]" : "[En Oficina]";
                overheadLabel.text = $"{tag} (Inicial #{id})\n{metrics.destinationName}";
            }

            StartCoroutine(InitialOccupantStayRoutine(remainingStaySeconds));
        }

        private IEnumerator InitialOccupantStayRoutine(float staySeconds)
        {
            currentState = (targetClassroom != null) ? AgentState.InClass : AgentState.InQueue;
            SetColor(new Color(1.0f, 0.85f, 0.2f));

            if (targetClassroom != null) targetClassroom.currentOccupants++;

            // Duración calibrada para la rotación de horarios visual (4 a 14 segundos)
            float visualStay = Mathf.Clamp(staySeconds, 4.0f, 14.0f);
            yield return new WaitForSeconds(visualStay);

            metrics.serviceEndTime = Time.time;
            StartExiting();
        }

        public void InitializeOfficeAgent(int id, AgentRole agentRole, string doorName, ServiceOffice office, List<Vector3> pathWaypoints, Transform exit)
        {
            role = agentRole;
            destinationType = AgentDestinationType.ServiceOffice;
            targetOffice = office;
            targetClassroom = null;
            exitPoint = exit;

            metrics.agentId = id;
            metrics.role = agentRole;
            metrics.destinationType = AgentDestinationType.ServiceOffice;
            metrics.entranceDoor = doorName;
            metrics.destinationName = office.officeName;
            metrics.officeDestination = office.officeType;
            metrics.floorLevel = office.floorLevel;
            metrics.spawnTime = Time.time;

            baseRoleColor = (role == AgentRole.Funcionario)
                ? new Color(1.0f, 0.55f, 0.1f)
                : new Color(0.2f, 0.65f, 1.0f);
            SetColor(baseRoleColor);

            if (overheadLabel != null)
            {
                string roleTag = (role == AgentRole.Funcionario) ? "Func." : "Est.";
                overheadLabel.text = $"[{roleTag} #{id}]\n► {office.officeName}";
            }

            LoadWaypoints(pathWaypoints);
            currentState = AgentState.WalkingToDestination;
        }

        private void LoadWaypoints(List<Vector3> waypoints)
        {
            waypointQueue.Clear();
            if (waypoints != null)
            {
                foreach (var wp in waypoints)
                {
                    waypointQueue.Enqueue(wp);
                }
            }
            hasWaypoints = (waypointQueue.Count > 0);
            timeBlockedOnTarget = 0f;
            AdvanceNextWaypoint();
        }

        private void AdvanceNextWaypoint()
        {
            timeBlockedOnTarget = 0f;
            stuckTimer = 0f;
            lastStuckPos = transform.position;
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
            HandleMovement();

            // Detección directa de llegada a la puerta de salida exterior
            if (currentState == AgentState.Exiting)
            {
                Vector3 exitPos = (exitPoint != null) ? exitPoint.position : (SimulationManager.Instance != null ? SimulationManager.Instance.GetDefaultExitPosition() : new Vector3(-230f, 4.71f, 115f));
                float distToExit = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(exitPos.x, exitPos.z));
                if (distToExit <= 4.5f)
                {
                    currentState = AgentState.Done;
                    metrics.exitTime = Time.time;
                    if (SimulationManager.Instance != null) SimulationManager.Instance.OnAgentExit(this);
                    Destroy(gameObject);
                    return;
                }
            }

            // Watchdog anti-atascamiento: si no se desplaza durante 2.5s, avanza al siguiente waypoint
            if (currentState == AgentState.WalkingToDestination || currentState == AgentState.Exiting)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= 2.5f)
                {
                    float distMoved = Vector3.Distance(transform.position, lastStuckPos);
                    lastStuckPos = transform.position;
                    stuckTimer = 0f;

                    if (distMoved < 0.2f)
                    {
                        if (waypointQueue.Count > 0)
                        {
                            AdvanceNextWaypoint();
                        }
                        else if (currentState == AgentState.Exiting)
                        {
                            // En la puerta de salida exterior y detenido -> completar salida
                            currentState = AgentState.Done;
                            metrics.exitTime = Time.time;
                            if (SimulationManager.Instance != null) SimulationManager.Instance.OnAgentExit(this);
                            Destroy(gameObject);
                            return;
                        }
                    }
                }
            }

            // Lógica de transición de estados
            if (currentState == AgentState.WalkingToDestination)
            {
                if (HasReachedTarget())
                {
                    if (waypointQueue.Count > 0)
                    {
                        AdvanceNextWaypoint();
                    }
                    else
                    {
                        // Ha llegado a la puerta del salón o a la oficina
                        metrics.reachDestinationTime = Time.time;

                        if (destinationType == AgentDestinationType.Classroom)
                        {
                            StartCoroutine(AttendClassRoutine());
                        }
                        else
                        {
                            currentState = AgentState.InQueue;
                            SetColor(new Color(1f, 0.75f, 0.1f)); // Amarillo en cola
                            if (targetOffice != null)
                            {
                                targetOffice.EnqueueAgent(this);
                            }
                        }
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
                        // Llegó a la puerta de salida exterior -> salir del bloque y desaparecer
                        currentState = AgentState.Done;
                        metrics.exitTime = Time.time;
                        if (SimulationManager.Instance != null) SimulationManager.Instance.OnAgentExit(this);
                        Destroy(gameObject);
                    }
                }
            }
        }

        private IEnumerator AttendClassRoutine()
        {
            currentState = AgentState.InClass;
            SetColor(new Color(1.0f, 0.85f, 0.2f)); // Amarillo/Dorado en clase

            if (targetClassroom != null)
            {
                targetClassroom.currentOccupants++;
            }

            if (overheadLabel != null && targetClassroom != null)
            {
                string tag = (role == AgentRole.Docente) ? "[Dictando Clase]" : "[En Clase]";
                overheadLabel.text = $"{tag}\n{targetClassroom.classroomName}";
            }

            metrics.serviceStartTime = Time.time;

            // Duración de la clase calibrada para rotación visual: entre 10 y 16 segundos
            float classDuration = (role == AgentRole.Docente)
                ? Random.Range(14.0f, 20.0f)
                : Random.Range(10.0f, 16.0f);

            yield return new WaitForSeconds(classDuration);

            metrics.serviceEndTime = Time.time;

            // Iniciar salida del salón hacia el exterior
            StartExiting();
        }

        public void StartExiting()
        {
            currentState = AgentState.Exiting;
            SetColor(new Color(0.6f, 0.65f, 0.7f)); // Gris saliendo

            if (targetClassroom != null)
            {
                targetClassroom.currentOccupants = Mathf.Max(0, targetClassroom.currentOccupants - 1);
            }

            if (overheadLabel != null)
            {
                overheadLabel.text = "[Saliendo]\nSalida";
            }

            Vector3 exitPos = (exitPoint != null) ? exitPoint.position : SimulationManager.Instance.GetDefaultExitPosition();
            List<Vector3> exitWaypoints = SimulationManager.Instance.CalculateExitPath(transform.position, targetClassroom, targetOffice, exitPos);

            LoadWaypoints(exitWaypoints);
        }

        private void HandleMovement()
        {
            bool useNavMesh = (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh);

            if (!useNavMesh && (currentState == AgentState.WalkingToDestination || currentState == AgentState.Exiting || currentState == AgentState.InQueue))
            {
                Vector3 toTarget = currentTargetPosition - transform.position;
                Vector3 horizontalDir = new Vector3(toTarget.x, 0, toTarget.z);
                float dist = horizontalDir.magnitude;

                if (dist > 0.15f)
                {
                    // Rotación suave hacia el waypoint
                    Quaternion targetRot = Quaternion.LookRotation(horizontalDir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, angularSpeed * Time.deltaTime);

                    // Velocidad horizontal
                    Vector3 velocity = horizontalDir.normalized * Mathf.Min(walkSpeed, dist / Time.deltaTime);

                    // Desplazamiento vertical con gravedad continua para grounding y escalones
                    float yDiff = currentTargetPosition.y - transform.position.y;
                    velocity.y = (Mathf.Abs(yDiff) > 1.0f) ? Mathf.Clamp(yDiff * 5.0f, -15.0f, 15.0f) : -9.81f;

                    // MOVIMIENTO CON FÍSICAS: CharacterController.Move() usa PhysX
                    // Respeta rigurosamente los MeshColliders de paredes, columnas, puertas y bancas
                    if (characterController != null && characterController.enabled)
                    {
                        CollisionFlags flags = characterController.Move(velocity * Time.deltaTime);
                        if ((flags & CollisionFlags.Sides) != 0 && dist < 3.5f)
                        {
                            timeBlockedOnTarget += Time.deltaTime;
                        }
                    }
                    else
                    {
                        transform.position += velocity * Time.deltaTime;
                    }

                    // Efecto de caminado (bobbing natural)
                    bobbingTimer += Time.deltaTime * 8.0f;
                    float bob = Mathf.Sin(bobbingTimer) * 0.1f;
                    if (visualTransform != null)
                    {
                        visualTransform.localPosition = new Vector3(0, 1.25f + bob, 0);
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
                float xzDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(currentTargetPosition.x, currentTargetPosition.z));
                float yDist = Mathf.Abs(transform.position.y - currentTargetPosition.y);

                // Si está dentro de la distancia de parada
                if (xzDist <= (stoppingDistance * 1.6f) && yDist <= 6.0f)
                {
                    return true;
                }

                // O si la física del muro/puerta lo detuvo cerca de la meta
                if (timeBlockedOnTarget > 0.35f && xzDist <= 3.5f && yDist <= 6.0f)
                {
                    return true;
                }

                return false;
            }
        }

        private void OnDestroy()
        {
            if (targetClassroom != null && (currentState == AgentState.InClass || currentState == AgentState.WalkingToDestination))
            {
                targetClassroom.currentOccupants = Mathf.Max(0, targetClassroom.currentOccupants - 1);
            }
            if (SimulationManager.Instance != null && SimulationManager.Instance.activeAgents != null)
            {
                SimulationManager.Instance.activeAgents.Remove(this);
            }
        }

        public void SetBeingServedState()
        {
            currentState = AgentState.BeingServed;
            SetColor(new Color(0.15f, 0.95f, 0.35f)); // Verde siendo atendido
            if (overheadLabel != null && targetOffice != null)
            {
                overheadLabel.text = $"[En Atención]\n{targetOffice.officeName}";
            }
        }

        public void OnServiceCompleted()
        {
            StartExiting();
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
                Vector3 camPos = Camera.main.transform.position;
                Vector3 dir = overheadLabel.transform.position - camPos;
                if (dir.sqrMagnitude > 0.01f)
                {
                    overheadLabel.transform.rotation = Quaternion.LookRotation(dir);
                }
            }
        }
    }
}
