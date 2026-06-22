using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

[System.Serializable]
public class RobotData
{
    public string tipo;
    public float[] joints;
}

public class seguimiento : MonoBehaviour
{
    [Header("Conexión a Python")]
    public string host = "127.0.0.1";
    public int port = 5005;

    [Header("Tu Mano en VR")]
    public Transform handReference; 
    
    [Header("Punto de Seguimiento (Tool Center Point)")]
    [Tooltip("Crea un GameObject vacío en la punta de tu herramienta, hazlo hijo de tu mano y arrástralo aquí.")]
    public Transform puntoVinculadoAMano; 

    [Header("Motores del UR10e (Articulation Bodies)")]
    public ArticulationBody[] urJoints = new ArticulationBody[6]; 

    [Header("Corrección de Ejes (1 = Normal, -1 = Invertido)")]
    [Tooltip("Cambia a -1 si la pieza gira al lado contrario de tu mano")]
    public float[] jointDirections = new float[6] { 1f, 1f, 1f, 1f, 1f, 1f };

    private TcpClient client;
    private NetworkStream stream;
    private byte[] receiveBuffer = new byte[1024];

    // === VARIABLES DE CALIBRACIÓN ===
    private Vector3 initialTargetPosition; 
    private Quaternion initialTargetRotation; // <-- La variable que faltaba declarar
    private bool isCalibrated = false;

    private float[] targetJoints = new float[6];
    private bool hasNewData = false;
    private readonly object jointLock = new object();

    void Start()
    {
        ConnectToServer();
    }

    void Update()
    {
        // Determinamos qué punto seguir (si olvidaste asignar el punto, usa la mano por defecto para no dar error)
        Transform targetToFollow = puntoVinculadoAMano != null ? puntoVinculadoAMano : handReference;

        // 1. PRESIONA ESPACIO PARA CALIBRAR EL CENTRO Y LA ROTACIÓN
        if (Input.GetKeyDown(KeyCode.Space))
        {
            initialTargetPosition = targetToFollow.position;
            initialTargetRotation = targetToFollow.rotation; // Se guarda la inclinación inicial
            isCalibrated = true;
            Debug.Log($"Centro fijado en: {targetToFollow.name}. El UR10e seguirá este punto exacto y sus giros.");
        }

        // 2. ENVIAR EL DELTA DE MOVIMIENTO Y GIRO AL SERVIDOR
        if (isCalibrated && client != null && client.Connected)
        {
            // Movimiento posicional
            Vector3 deltaMove = targetToFollow.position - initialTargetPosition;

            // Movimiento rotacional (cuánto giró desde la calibración)
            Quaternion deltaRotation = Quaternion.Inverse(initialTargetRotation) * targetToFollow.rotation;
            Vector3 deltaEuler = deltaRotation.eulerAngles;

            // Convertimos los ángulos de 0-360 a -180 a 180 para que la matemática no colapse
            float rx = deltaEuler.x > 180 ? deltaEuler.x - 360 : deltaEuler.x;
            float ry = deltaEuler.y > 180 ? deltaEuler.y - 360 : deltaEuler.y;
            float rz = deltaEuler.z > 180 ? deltaEuler.z - 360 : deltaEuler.z;

            SendDelta(deltaMove, new Vector3(rx, ry, rz));
        }

        // 3. APLICAR ÁNGULOS A LOS MOTORES FÍSICOS
        if (hasNewData)
        {
            lock (jointLock)
            {
                string debugAngles = "Ángulos aplicados: "; 

                for (int i = 0; i < 6; i++)
                {
                    if (urJoints.Length > i && urJoints[i] != null)
                    {
                        // Convertir a grados y aplicar dirección
                        float angleDeg = (targetJoints[i] * Mathf.Rad2Deg) * jointDirections[i];
                        debugAngles += $"J{i}: {angleDeg:F1}° | ";

                        // Extraemos el motor interno
                        ArticulationDrive drive = urJoints[i].xDrive;
                        
                        // Fuerza bruta para mover el brazo virtual
                        drive.stiffness = 100000f; 
                        drive.damping = 10000f;    
                        drive.forceLimit = 3.402823e+38f; 
                        
                        // Asignamos la meta
                        drive.target = angleDeg;
                        
                        // Guardamos los cambios
                        urJoints[i].xDrive = drive;
                    }
                }
                
                // Imprimimos en la consola de Unity para confirmar que todo llega
                Debug.Log(debugAngles);
                
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
        catch (System.Exception e) { Debug.LogError("Error TCP: " + e.Message); }
    }

    void SendDelta(Vector3 deltaPos, Vector3 deltaRot)
    {
        // Forzamos el uso del punto decimal sin importar el idioma del sistema
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
                        RobotData data = JsonUtility.FromJson<RobotData>(line);
                        
                        if (data != null && (data.tipo == "target" || data.tipo == "sync") && data.joints != null)
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
            catch { break; }
        }
    }

    void OnApplicationQuit()
    {
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }
}