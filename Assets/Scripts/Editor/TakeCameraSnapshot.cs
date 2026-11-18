using System.IO;
using UnityEditor;
using UnityEngine;

namespace Uniguajira.Simulation.Editor
{
    [InitializeOnLoad]
    public static class TakeCameraSnapshot
    {
        static TakeCameraSnapshot()
        {
            EditorApplication.delayCall += Capture;
        }

        [MenuItem("Uniguajira/Tomar Captura Camara Actual")]
        public static void Capture()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("No hay Camera.main en la escena.");
                return;
            }

            RenderTexture rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            Texture2D screenShot = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            cam.Render();
            RenderTexture.active = rt;
            screenShot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            screenShot.Apply();

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);

            byte[] bytes = screenShot.EncodeToPNG();
            Object.DestroyImmediate(screenShot);

            string outPath = @"C:\Users\bimok\.gemini\antigravity\brain\edd049ae-d620-444e-a9ae-e0a1ee42744d\camera_snapshot.png";
            File.WriteAllBytes(outPath, bytes);
            Debug.Log("Captura guardada en: " + outPath + " desde cámara en pos: " + cam.transform.position + " rot: " + cam.transform.rotation.eulerAngles);
        }
    }
}
