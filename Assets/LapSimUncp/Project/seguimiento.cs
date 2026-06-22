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

    [Header("Motores del UR10e (Articulation Bodies)")]
    public ArticulationBody[] urJoints = new ArticulationBody[6]; 

    [Header("Corrección de Ejes (1 = Normal, -1 = Invertido)")]
    [Tooltip("Cambia a -1 si la pieza gira al lado contrario de tu mano")]
    public float[] jointDirections = new float[6] { 1f, 1f, 1f, 1f, 1f, 1f };

    private TcpClient client;
    private NetworkStream stream;
    private byte[] receiveBuffer = new byte[1024];

    private Vector3 initialHandPosition;
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
        // 1. PRESIONA ESPACIO PARA CALIBRAR EL CENTRO
        if (Input.GetKeyDown(KeyCode.Space))
        {
            initialHandPosition = handReference.position;
            isCalibrated = true;
            Debug.Log("Centro fijado. El brazo obedecerá tus movimientos (Deltas).");
        }

        // 2. ENVIAR EL DELTA DE MOVIMIENTO
        if (isCalibrated && client != null && client.Connected)
        {
            Vector3 deltaMove = handReference.position - initialHandPosition;
            SendDelta(deltaMove);
        }

        // 3. APLICAR ÁNGULOS A LOS MOTORES FÍSICOS
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
                        
                        // EL SECRETO: Le damos fuerza bruta al motor desde el código
                        drive.stiffness = 100000f; 
                        drive.damping = 10000f;    
                        drive.forceLimit = 3.402823e+38f; // Límite de fuerza infinito
                        
                        // Le asignamos la meta
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

void SendDelta(Vector3 delta)
{
    // Forzamos el uso del punto decimal sin importar el idioma de tu PC
    string x = delta.x.ToString(CultureInfo.InvariantCulture);
    string y = delta.y.ToString(CultureInfo.InvariantCulture);
    string z = delta.z.ToString(CultureInfo.InvariantCulture);

    string json = $"{{\"tipo\": \"ik_delta\", \"delta\": [{x}, {y}, {z}]}}\n";
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