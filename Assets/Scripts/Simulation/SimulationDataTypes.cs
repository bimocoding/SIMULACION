using System;
using UnityEngine;

namespace Uniguajira.Simulation
{
    public enum AgentRole
    {
        Estudiante,
        Docente,
        Funcionario
    }

    public enum AgentDestinationType
    {
        Classroom,
        ServiceOffice
    }

    public enum OfficeType
    {
        VicerrectoriaExtension,   // Piso 1
        CienciasAplicadas,        // Piso 1
        ORI,                      // Piso 2 (Relaciones Internacionales)
        CentroGraduados,          // Piso 2
        VicerrectoriaDocencia     // Piso 2
    }

    public enum SimulationScenario
    {
        EscenarioBase,            // Puerta Principal 80%, Puerta Trasera 20%
        EscenarioAlternativo      // Acceso distribuido 50%-50% y ventanilla rápida en ORI
    }

    [Serializable]
    public class ClassroomData
    {
        public string doorName;        // e.g. "Door_GF_N1"
        public string classroomName;   // e.g. "Aula 101 Norte"
        public int floorLevel;         // 1 or 2
        public bool isNorthWing;       // true = North (Z < 100), false = South (Z > 300)
        public Vector3 doorPosition;   // World position of the door
        public Vector3 hallwayPoint;   // Position on corridor centerline in front of door
        public int currentOccupants;   // Count of agents currently inside or at door
    }

    [Serializable]
    public class AgentMetrics
    {
        public int agentId;
        public AgentRole role = AgentRole.Estudiante;
        public AgentDestinationType destinationType = AgentDestinationType.Classroom;
        public string entranceDoor;
        public string destinationName;
        public int floorLevel;
        public OfficeType officeDestination;
        public float spawnTime;
        public float reachDestinationTime;
        public float reachQueueTime
        {
            get => reachDestinationTime;
            set => reachDestinationTime = value;
        }
        public float serviceStartTime;
        public float serviceEndTime;
        public float exitTime;

        public float TravelTime => reachDestinationTime - spawnTime;
        public float WaitTime => serviceStartTime - reachDestinationTime;
        public float ServiceTime => serviceEndTime - serviceStartTime;
        public float TotalSystemTime => exitTime - spawnTime;
    }

    [Serializable]
    public class TenMinuteInterval
    {
        public string intervalName;   // e.g. "05:40 - 05:50 PM"
        public int observedArrivals;  // Datos reales recolectados
        public float durationSeconds; // 600 segundos (10 minutos) en escala real
    }

    [Serializable]
    public class ReplicaSummary
    {
        public int replicaIndex;
        public int totalArrivals;
        public int totalCompleted;
        public float avgTravelTimeMinutes;
        public float avgWaitTimeMinutes;
        public float avgServiceTimeMinutes;
        public float avgTotalSystemTimeMinutes;
        public int maxQueueDetected;
        public int maxBlockOccupancy;
    }
}
