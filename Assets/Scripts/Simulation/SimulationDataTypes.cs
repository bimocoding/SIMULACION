using System;
using UnityEngine;

namespace Uniguajira.Simulation
{
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
    public class AgentMetrics
    {
        public int agentId;
        public string entranceDoor;
        public OfficeType destination;
        public float spawnTime;
        public float reachQueueTime;
        public float serviceStartTime;
        public float serviceEndTime;
        public float exitTime;

        public float TravelTime => reachQueueTime - spawnTime;
        public float WaitTime => serviceStartTime - reachQueueTime;
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
