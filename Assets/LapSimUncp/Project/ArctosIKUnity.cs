using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using System;

[System.Serializable]
public class ArctosData
{
    public string tipo;
    public float[] joints;
}

public class ArctosIKUnity : MonoBehaviour
{
    [Header("Conexión a Ubuntu (Python)")]
    public string host = "127.0.0.1"; 
    public int port = 5005;

    [Header("Referencias VR")]
    public Transform handReference; 
    public Transform puntoVinculadoAMano; 

    [Header("=== MOTORES (Transforms Puros) ===")]
    [Tooltip("Arrastra aquí los GameObjects link_1 al link_6")]
    public Transform[] arctosJoints = new Transform[6]; 

    [Header("Corrección de Dirección (1 o -1)")]
    public float[] jointDirections = new float[6] { 1f, 1f, 1f, 1f, 1f, 1f };

    [Header("Ejes Locales de Giro")]
    [Tooltip("Define sobre qué eje gira la pieza (X=1,0,0 | Y=0,1,0 | Z=0,0,1)")]
    public Vector3[] rotationAxes = new Vector3[6] {
        new Vector3(0, 1, 0), // J1 (Base) suele girar en Y
        new Vector3(1, 0, 0), // J2 (Hombro) suele girar en X
        new Vector3(1, 0, 0), // J3 (Codo) suele girar en X
        new Vector3(1, 0, 0), // J4 (Muñeca 1) suele girar en X
        new Vector3(0, 1, 0), // J5 (Muñeca 2) suele girar en Y
        new Vector3(1, 0, 0)  // J6 (Muñeca 3) suele girar en X
    };

    private TcpClient client;
    private NetworkStream stream;
    private byte[] receiveBuffer = new byte[1024];

    private Vector3 initialTargetPosition; 
    private Quaternion initialTargetRotation; 
    private bool isCalibrated = false;

    // Guardamos la postura original en la que armaste el robot en Unity
    private Quaternion[] initialJointRotations = new Quaternion[6];

    private float[] targetJoints = new float[6];
    private bool hasNewData = false;
    private readonly object jointLock = new object();

    void Start()
    {
        // Guardamos cómo están rotadas las piezas al inicio para no deformar el brazo
        for (int i = 0; i < arctosJoints.Length; i++)
        {
            if (arctosJoints[i] != null)
            {
                initialJointRotations[i] = arctosJoints[i].localRotation;
            }
        }
        
        ConnectToServer();
    }

    void Update()
    {
        Transform targetToFollow = puntoVinculadoAMano != null ? puntoVinculadoAMano : handReference;

        // CALIBRACIÓN
        if (Input.GetKeyDown(KeyCode.Space))
        {
            initialTargetPosition = targetToFollow.position;
            initialTargetRotation = targetToFollow.rotation; 
            isCalibrated = true;
            Debug.Log("¡Calibrado! El Arctos está enviando datos al servidor.");
        }

        // ENVÍO DE DATOS A PYTHON
        if (isCalibrated && client != null && client.Connected)
        {
            Vector3 deltaMove = targetToFollow.position - initialTargetPosition;
            Quaternion deltaRotation = Quaternion.Inverse(initialTargetRotation) * targetToFollow.rotation;
            Vector3 deltaEuler = deltaRotation.eulerAngles;

            float rx = deltaEuler.x > 180 ? deltaEuler.x - 360 : deltaEuler.x;
            float ry = deltaEuler.y > 180 ? deltaEuler.y - 360 : deltaEuler.y;
            float rz = deltaEuler.z > 180 ? deltaEuler.z - 360 : deltaEuler.z;

            SendDelta(deltaMove, new Vector3(rx, ry, rz));
        }

        // 🚀 MOVIMIENTO BRUTO DE GAMEOBJECTS (Cero Físicas)
        if (hasNewData)
        {
            lock (jointLock)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (arctosJoints.Length > i && arctosJoints[i] != null)
                    {
                        // Convertimos los radianes de Python a grados
                        float angleDeg = (targetJoints[i] * Mathf.Rad2Deg) * jointDirections[i];
                        
                        // Obligamos al GameObject a girar sobre su eje partiendo de su pose inicial
                        arctosJoints[i].localRotation = initialJointRotations[i] * Quaternion.AngleAxis(angleDeg, rotationAxes[i]);
                    }
                }
                hasNewData = false; 
            }
        }
    }

    void ConnectToServer()
    {
        try 
        {
            client = new TcpClient(host, port);
            stream = client.GetStream();
            Thread receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        } 
        catch (Exception e) { Debug.LogError("Error TCP: " + e.Message); }
    }

    void SendDelta(Vector3 deltaPos, Vector3 deltaRot)
    {
        string x = deltaPos.x.ToString(CultureInfo.InvariantCulture);
        string y = deltaPos.y.ToString(CultureInfo.InvariantCulture);
        string z = deltaPos.z.ToString(CultureInfo.InvariantCulture);

        string rx = deltaRot.x.ToString(CultureInfo.InvariantCulture);
        string ry = deltaRot.y.ToString(CultureInfo.InvariantCulture);
        string rz = deltaRot.z.ToString(CultureInfo.InvariantCulture);

        string json = $"{{\"tipo\": \"ik_delta\", \"delta\": [{x}, {y}, {z}], \"rot\": [{rx}, {ry}, {rz}]}}\n";
        byte[] data = Encoding.UTF8.GetBytes(json);
        try { stream.Write(data, 0, data.Length); } catch { }
    }

    void ReceiveData()
    {
        while (client != null && client.Connected)
        {
            try
            {
                int bytesRead = stream.Read(receiveBuffer, 0, receiveBuffer.Length);
                if (bytesRead > 0)
                {
                    string message = Encoding.UTF8.GetString(receiveBuffer, 0, bytesRead);
                    string[] lines = message.Split('\n'); 
                    
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        ArctosData data = JsonUtility.FromJson<ArctosData>(line);
                        
                        if (data != null && (data.tipo == "target" || data.tipo == "sync"))
                        {
                            if (data.joints != null && data.joints.Length >= 6)
                            {
                                lock (jointLock)
                                {
                                    for (int i = 0; i < 6; i++) { targetJoints[i] = data.joints[i]; }
                                    hasNewData = true;
                                }
                            }
                        }
                    }
                }
            }
            catch { break; }
        }
    }

    void OnApplicationQuit()
    {
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }
}