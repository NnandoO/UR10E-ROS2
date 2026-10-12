using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Collections;
using UnityEngine.XR;

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

    [Header("Referencias Vitales")]
    public Transform handReference;
    public Transform baseDelRobot;

    [Header("🎮 Bloqueo por joystick")]
    public float joystickDeadzone = 0.3f;
    public bool bloquearJoystickIzquierdo = true;
    public bool bloquearJoystickDerecho = true;

    [Header("🎮 Recalibración")]
    [Tooltip("Tecla para recalibrar (editor)")]
    public KeyCode teclaRecalibrar = KeyCode.R;

    [Header("Motores del UR10e")]
    public ArticulationBody[] urJoints = new ArticulationBody[6];
    public float[] jointDirections = new float[6] { 1f, 1f, 1f, 1f, 1f, 1f };

    [Header("🎯 Precisión de Seguimiento")]
    public float sendRateHz = 100f;

    [Header("🎮 Rotación del mando -> Wrists")]
    public float wrist1Sensitivity = 1.0f;
    public bool invertWrist1 = false;
    public float wrist2Sensitivity = 1.0f;
    public bool invertWrist2 = false;

    [Header("🎮 Botones A/B (Wrist 3 / J6)")]
    public float wrist3Speed = 60f;
    public bool invertWrist3 = false;

    private float sendInterval;
    private float lastSendTime = 0f;

    private TcpClient client;
    private NetworkStream stream;
    private byte[] receiveBuffer = new byte[4096];

    private bool isCalibrated = false;
    private bool zLiberado = false;
    private float[] targetJoints = new float[6];
    private bool hasNewData = false;
    private bool hasReceivedSync = false;
    private bool syncAckSent = false;
    private readonly object jointLock = new object();

    private Quaternion initialHandRotation;
    private bool handRotationCaptured = false;

    private float wrist3Offset = 0f;

    private InputDevice leftDevice;
    private InputDevice rightDevice;
    private float deviceRefreshTimer = 0f;

    private bool thumbstickAnterior = false;

    void Start()
    {
        for (int i = 0; i < 6; i++)
        {
            if (urJoints.Length > i && urJoints[i] != null)
            {
                ArticulationDrive drive = urJoints[i].xDrive;
                drive.stiffness = 100000f;
                drive.damping = 10000f;
                drive.forceLimit = 3.402823e+38f;
                urJoints[i].xDrive = drive;
            }
        }
        sendInterval = 1f / sendRateHz;
        StartCoroutine(ConnectWithRetry());
    }

    IEnumerator ConnectWithRetry()
    {
        yield return new WaitForSeconds(0.5f);
        while (client == null || !client.Connected)
        {
            var connectTask = System.Threading.Tasks.Task.Run(() =>
            {
                try { TcpClient tempClient = new TcpClient(); tempClient.Connect(host, port); return tempClient; }
                catch { return null; }
            });
            while (!connectTask.IsCompleted) yield return null;
            client = connectTask.Result;
            if (client != null && client.Connected)
            {
                stream = client.GetStream();
                Thread receiveThread = new Thread(ReceiveData);
                receiveThread.IsBackground = true;
                receiveThread.Start();
                Debug.Log("✅ ¡Conectado al servidor Python!");
                break;
            }
            else { yield return new WaitForSeconds(1f); }
        }
    }

    void RefrescarDevices()
    {
        deviceRefreshTimer += Time.deltaTime;
        if (deviceRefreshTimer < 1f) return;
        deviceRefreshTimer = 0f;

        if (!leftDevice.isValid)
            leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!rightDevice.isValid)
            rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    bool JoystickActivo(InputDevice device)
    {
        if (!device.isValid) return false;
        Vector2 stick = Vector2.zero;
        if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick)) return false;
        return stick.magnitude > joystickDeadzone;
    }

    bool AlgunJoystickActivo()
    {
        RefrescarDevices();
        if (bloquearJoystickIzquierdo && JoystickActivo(leftDevice)) return true;
        if (bloquearJoystickDerecho && JoystickActivo(rightDevice)) return true;
        return false;
    }

    /// <summary>
    /// Detecta el thumbstick press (agarrar el joystick del mando derecho)
    /// como botón de recalibrar.
    /// </summary>
    bool BotonRecalibrarPresionado()
    {
        bool presionadoAhora = false;

        // Tecla R (editor)
        if (Input.GetKey(teclaRecalibrar))
            presionadoAhora = true;

        // Thumbstick press del mando derecho
        RefrescarDevices();
        if (rightDevice.isValid)
        {
            bool stickPressed = false;
            rightDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out stickPressed);
            if (stickPressed) presionadoAhora = true;
        }

        bool flanco = presionadoAhora && !thumbstickAnterior;
        thumbstickAnterior = presionadoAhora;
        return flanco;
    }

    void Update()
    {
        if (hasNewData) ApplyJointsToMotors();

        if (Input.GetKeyDown(KeyCode.Space) && isCalibrated && !zLiberado)
        {
            zLiberado = true;
            Debug.Log("🔓 [Robot] ESPACIO PRESIONADO. Profundidad (Z) liberada.");
        }

        // === 🔄 RECALIBRACIÓN ===
        if (BotonRecalibrarPresionado())
        {
            initialHandRotation = handReference.rotation;
            handRotationCaptured = true;
            wrist3Offset = 0f;
            Debug.Log($"🔄 RECALIBRADO. Rotación: {initialHandRotation.eulerAngles}");
        }

        // Botones A/B -> J6
        float wrist3Input = 0f;
        RefrescarDevices();
        if (rightDevice.isValid)
        {
            bool aPressed = false, bPressed = false;
            rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out aPressed);
            rightDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bPressed);
            if (aPressed) wrist3Input -= 1f;
            if (bPressed) wrist3Input += 1f;
        }
        if (invertWrist3) wrist3Input = -wrist3Input;

        if (Mathf.Abs(wrist3Input) > 0.01f)
        {
            wrist3Offset += wrist3Input * wrist3Speed * Mathf.Deg2Rad * Time.deltaTime;
            hasNewData = true;
        }
    }

    void LateUpdate()
    {
        if (client == null || !client.Connected || !hasReceivedSync) return;
        if (!isCalibrated || handReference == null || baseDelRobot == null) return;
        if (Time.time - lastSendTime < sendInterval) return;

        if (AlgunJoystickActivo()) return;

        Vector3 posLocal = baseDelRobot.InverseTransformPoint(handReference.position);

        if (posLocal.magnitude > 1.1f)
        {
            posLocal = posLocal.normalized * 1.1f;
        }

        if (!handRotationCaptured)
        {
            initialHandRotation = handReference.rotation;
            handRotationCaptured = true;
            Debug.Log("✅ Captura inicial automática.");
        }

        Quaternion deltaRot = Quaternion.Inverse(initialHandRotation) * handReference.rotation;
        Vector3 eulerDelta = deltaRot.eulerAngles;

        float angleX = eulerDelta.x;
        if (angleX > 180f) angleX -= 360f;

        float angleY = eulerDelta.y;
        if (angleY > 180f) angleY -= 360f;

        float wrist1Angle = angleX * Mathf.Deg2Rad * wrist1Sensitivity;
        if (invertWrist1) wrist1Angle = -wrist1Angle;

        float wrist2Angle = -angleY * Mathf.Deg2Rad * wrist2Sensitivity;
        if (invertWrist2) wrist2Angle = -wrist2Angle;

        string px = posLocal.x.ToString(CultureInfo.InvariantCulture);
        string py = posLocal.y.ToString(CultureInfo.InvariantCulture);
        string pz = posLocal.z.ToString(CultureInfo.InvariantCulture);
        string libre = zLiberado ? "true" : "false";

        string w1 = wrist1Angle.ToString(CultureInfo.InvariantCulture);
        string w2 = wrist2Angle.ToString(CultureInfo.InvariantCulture);
        string w3 = wrist3Offset.ToString(CultureInfo.InvariantCulture);

        string json = $"{{\"tipo\": \"ik_abs\", \"pos\": [{px}, {py}, {pz}], \"rot\": [0, {w2}, {w1}], \"z_libre\": {libre}, \"wrist3_offset\": {w3}}}\n";
        byte[] data = Encoding.UTF8.GetBytes(json);
        try { stream.Write(data, 0, data.Length); } catch { }

        lastSendTime = Time.time;
    }

    void ApplyJointsToMotors()
    {
        lock (jointLock)
        {
            for (int i = 0; i < 6; i++)
            {
                if (urJoints.Length > i && urJoints[i] != null)
                {
                    ArticulationDrive drive = urJoints[i].xDrive;
                    drive.stiffness = 100000f;
                    drive.damping = 10000f;
                    drive.forceLimit = 3.402823e+38f;
                    drive.target = (targetJoints[i] * Mathf.Rad2Deg) * jointDirections[i];
                    urJoints[i].xDrive = drive;
                }
            }
            hasNewData = false;
        }

        if (hasReceivedSync && !syncAckSent)
        {
            SendSyncAck();
            syncAckSent = true;
            isCalibrated = true;
            Debug.Log("✅ Calibrado.");
        }
    }

    void SendSyncAck()
    {
        string json = "{\"tipo\": \"sync_ack\"}\n";
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
                        try
                        {
                            RobotData data = JsonUtility.FromJson<RobotData>(line);
                            if (data != null)
                            {
                                if (data.tipo == "sync" || data.tipo == "target")
                                {
                                    if (data.tipo == "sync") hasReceivedSync = true;
                                    if (data.joints != null)
                                    {
                                        lock (jointLock)
                                        {
                                            for (int i = 0; i < 6; i++) targetJoints[i] = data.joints[i];
                                            hasNewData = true;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
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