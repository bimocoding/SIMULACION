using System;
using System.Collections.Generic;
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
        EscenarioBase,            // 1 servidor por punto, delantera 58%, trasera 42%
        EscenarioAlternativo      // Un punto de atención adicional (c+1 en ORI)
    }

    public static class FieldDataDistributions
    {
        // 19 observaciones reales de tiempo de atención C - B (en minutos)
        public static readonly float[] ServiceTimesMinutes = new float[]
        {
            26.96f, 26.98f, 27.01f, 15.91f, 15.93f, 30.93f, 0.10f, 0.08f,
            24.11f, 26.30f, 25.58f, 39.53f, 38.05f, 3.33f, 3.30f, 3.26f,
            0.05f, 6.01f, 0.85f
        };

        // 20 observaciones reales de tiempo de permanencia D - A (en minutos)
        public static readonly float[] PermanenceTimesMinutes = new float[]
        {
            49.75f, 49.83f, 49.81f, 0.05f, 0.03f, 4.33f, 0.33f, 26.91f,
            26.51f, 25.70f, 39.55f, 38.20f, 12.96f, 12.96f, 12.91f, 0.08f,
            3.06f, 6.23f, 0.95f, 0.36f
        };

        public static float SampleServiceTimeMinutes()
        {
            // Muestreo empírico con ligera interpolación estocástica
            int idx = UnityEngine.Random.Range(0, ServiceTimesMinutes.Length);
            float baseVal = ServiceTimesMinutes[idx];
            float jitter = UnityEngine.Random.Range(0.85f, 1.15f);
            return Mathf.Max(0.05f, baseVal * jitter);
        }

        public static float SamplePermanenceTimeMinutes()
        {
            int idx = UnityEngine.Random.Range(0, PermanenceTimesMinutes.Length);
            float baseVal = PermanenceTimesMinutes[idx];
            float jitter = UnityEngine.Random.Range(0.85f, 1.15f);
            return Mathf.Max(0.05f, baseVal * jitter);
        }
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
        public bool isInitialPopulation = false;
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
        public SimulationScenario scenario;
        public int totalArrivals;
        public int totalCompleted;
        public int maxQueueDetected;
        public float avgQueueCount;
        public float avgWaitTimeSeconds;
        public float medianWaitTimeSeconds;
        public float avgServiceTimeMinutes;
        public float avgPermanenceTimeMinutes;
        public float pctFrontDoor;
        public float pctStaff;
        public float pctFloor2;
        public int[] cutArrivals = new int[6];
        public float[] cutQueues = new float[6];

        public float avgTravelTimeMinutes => avgPermanenceTimeMinutes - avgServiceTimeMinutes;
        public float avgWaitTimeMinutes => avgWaitTimeSeconds / 60f;
    }
}
