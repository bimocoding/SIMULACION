using System.IO;
using UnityEditor;
using UnityEngine;

namespace Uniguajira.Simulation.Editor
{
    [InitializeOnLoad]
    public static class PrintModelInfo
    {
        static PrintModelInfo()
        {
            EditorApplication.delayCall += PrintBounds;
        }

        [MenuItem("Uniguajira/Imprimir Coordenadas Reales del Modelo")]
        public static void PrintBounds()
        {
            string outLog = "";
            GameObject block = GameObject.Find("Bloque_Administrativo_A");
            if (block == null)
            {
                outLog += "Bloque_Administrativo_A NO encontrado en la escena.\n";
            }
            else
            {
                outLog += $"Bloque Transform -> Pos: {block.transform.position}, Rot: {block.transform.rotation.eulerAngles}, Scale: {block.transform.lossyScale}\n";

                Renderer[] renderers = block.GetComponentsInChildren<Renderer>();
                outLog += $"Total Renderers: {renderers.Length}\n";

                Bounds totalBounds = new Bounds(block.transform.position, Vector3.zero);
                bool hasBounds = false;

                foreach (var r in renderers)
                {
                    if (!hasBounds)
                    {
                        totalBounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        totalBounds.Encapsulate(r.bounds);
                    }

                    // Imprimir objetos clave
                    string n = r.gameObject.name;
                    if (n.Contains("Celosia") || n.Contains("Stair") || n.Contains("Door") || n.Contains("Lab") || n.Contains("Cilindro") || n.Contains("Column"))
                    {
                        outLog += $"  Objeto: {n} -> Center: {r.bounds.center}, Size: {r.bounds.size}\n";
                    }
                }

                outLog += $"\nBOUNDS TOTALES DEL EDIFICIO:\n";
                outLog += $"  Min: {totalBounds.min}\n";
                outLog += $"  Max: {totalBounds.max}\n";
                outLog += $"  Center: {totalBounds.center}\n";
                outLog += $"  Size: {totalBounds.size}\n";
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                outLog += $"\nCAMERA.MAIN:\n";
                outLog += $"  Pos: {cam.transform.position}, Rot: {cam.transform.rotation.eulerAngles}, Depth: {cam.depth}, Enabled: {cam.enabled}\n";
            }

            Camera[] allCams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            outLog += $"\nTOTAL CAMARAS EN LA ESCENA: {allCams.Length}\n";
            foreach (var c in allCams)
            {
                outLog += $"  Camera: {c.gameObject.name} -> Depth: {c.depth}, Enabled: {c.enabled}, Pos: {c.transform.position}\n";
            }

            string dest = @"C:\Users\bimok\.gemini\antigravity\brain\edd049ae-d620-444e-a9ae-e0a1ee42744d\model_bounds_report.txt";
            File.WriteAllText(dest, outLog);
            Debug.Log(outLog);
        }
    }
}
