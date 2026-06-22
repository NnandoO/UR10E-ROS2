using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Globalization;

public class UR10eBridge : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("Usa 127.0.0.1 si estás en WSL Mirrored, o la IP de tu PC si falla")]
    public string wslIP = "127.0.0.1"; 
    public int port = 5005;

    [Header("Partes del Robot 3D (Arrastrar aquí)")]
    public Transform baseJoint;
    public Transform shoulderJoint;
    public Transform elbowJoint;
    public Transform wrist1Joint;
    public Transform wrist2Joint;
    public Transform wrist3Joint;

    [Header("Joint Data (Radianes)")]
    public float[] targetJoints = new float[6];  

    private TcpClient client;
    private StreamReader reader;
    private StreamWriter writer;
    private Thread networkThread;
    private bool isRunning = false;
    private bool isSynced = false; // Bloquea el envío hasta que nos igualemos al robot real
    private Quaternion[] initialRotations;
    private Vector3[] jointAxes; // Ejes locales de giro de cada articulación

    void Start()
{
    // Guardar las rotaciones iniciales y los ejes de giro (según tu mapeo comprobado)
    initialRotations = new Quaternion[6];
    jointAxes = new Vector3[6];
    
    // Base: gira alrededor de Y (Vector3.up)
    initialRotations[0] = baseJoint.localRotation;
    jointAxes[0] = Vector3.up;
    
    // Shoulder: gira alrededor de X (Vector3.right)
    initialRotations[1] = shoulderJoint.localRotation;
    jointAxes[1] = Vector3.right;
    
    // Elbow: gira alrededor de Y
    initialRotations[2] = elbowJoint.localRotation;
    jointAxes[2] = Vector3.up;
    
    // Wrist1: gira alrededor de Y
    initialRotations[3] = wrist1Joint.localRotation;
    jointAxes[3] = Vector3.up;
    
    // Wrist2: gira alrededor de X
    initialRotations[4] = wrist2Joint.localRotation;
    jointAxes[4] = Vector3.right;
    
    // Wrist3: gira alrededor de X
    initialRotations[5] = wrist3Joint.localRotation;
    jointAxes[5] = Vector3.right;
    
    ConnectToPython();
}

    void ConnectToPython()
    {
        try
        {
            client = new TcpClient(wslIP, port);
            NetworkStream stream = client.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };

            // 1. Leer el mensaje de sincronización desde Python
            string syncMsg = reader.ReadLine();
            SyncVirtualRobot(syncMsg);

            isRunning = true;
            // 2. Iniciar hilo de red
            networkThread = new Thread(NetworkLoop);
            networkThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("<color=red>Error conectando a WSL:</color> Asegúrate de ejecutar el script de Python PRIMERO. Detalles: " + e.Message);
        }
    }

    // Función que teletransporta el robot virtual a la posición del real
    void SyncVirtualRobot(string jsonStr)
{
    try
    {
        SyncData data = JsonUtility.FromJson<SyncData>(jsonStr);
        
        if (data != null && data.tipo == "sync" && data.joints.Length == 6)
        {
            // Aplicar cada ángulo a la articulación correspondiente
            Transform[] joints = { baseJoint, shoulderJoint, elbowJoint, wrist1Joint, wrist2Joint, wrist3Joint };
            
            for (int i = 0; i < 6; i++)
            {
                // Convertir de radianes a grados
                float angleDeg = data.joints[i] * Mathf.Rad2Deg;
                
                // Resetear a la rotación inicial y luego aplicar la rotación del ángulo
                joints[i].localRotation = initialRotations[i] * Quaternion.AngleAxis(angleDeg, jointAxes[i]);
            }

            isSynced = true;
            Debug.Log("<color=green>Sincronización inicial completada.</color> El brazo virtual saltó a la posición real.");
        }
    }
    catch (Exception e)
    {
        Debug.LogError("Error al procesar el JSON de sincronización: " + e.Message);
    }
}

    void Update()
    {
        // NO hacer nada hasta que Unity se haya igualado con la realidad
        if (!isSynced) return; 

        if (baseJoint == null || shoulderJoint == null || elbowJoint == null || 
            wrist1Joint == null || wrist2Joint == null || wrist3Joint == null)
        {
            Debug.LogWarning("Faltan asignar piezas del robot en el Inspector.");
            return;
        }

        // Leer tus rotaciones con los ejes que descubriste
        targetJoints[0] = baseJoint.localEulerAngles.y * Mathf.Deg2Rad;
        targetJoints[1] = shoulderJoint.localEulerAngles.x * Mathf.Deg2Rad;
        targetJoints[2] = elbowJoint.localEulerAngles.y * Mathf.Deg2Rad;
        targetJoints[3] = wrist1Joint.localEulerAngles.y * Mathf.Deg2Rad;
        targetJoints[4] = wrist2Joint.localEulerAngles.x * Mathf.Deg2Rad;
        targetJoints[5] = wrist3Joint.localEulerAngles.x * Mathf.Deg2Rad;
        
        // Corrección rápida: Unity usa ángulos de 0 a 360, el robot usa de -180 a 180 (-PI a PI)
        for (int i = 0; i < 6; i++)
        {
            if (targetJoints[i] > Mathf.PI)
            {
                targetJoints[i] -= 2 * Mathf.PI;
            }
        }
    }

    void NetworkLoop()
    {
        while (isRunning)
        {
            string msg = string.Format(CultureInfo.InvariantCulture, 
                "{{\"tipo\":\"target\", \"joints\":[{0:F4}, {1:F4}, {2:F4}, {3:F4}, {4:F4}, {5:F4}]}}", 
                targetJoints[0], targetJoints[1], targetJoints[2], targetJoints[3], targetJoints[4], targetJoints[5]);
            
            try
            {
                writer.WriteLine(msg);
                Thread.Sleep(20); 
            }
            catch
            {
                isRunning = false;
                Debug.LogWarning("Conexión con Python perdida.");
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (networkThread != null) networkThread.Abort();
        if (writer != null) writer.Close();
        if (reader != null) reader.Close();
        if (client != null) client.Close();
    }

    // Clase auxiliar para que Unity pueda leer el JSON de Python
    [Serializable]
    public class SyncData
    {
        public string tipo;
        public float[] joints;
    }
}