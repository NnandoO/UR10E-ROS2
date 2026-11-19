using System;
using System.Collections;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Hardware con el que trabaja la sesión. Debe coincidir con el modo del servidor Python
/// (python3 seguimientoR.py --modo sim|fisico). Si no coincide, el brazo NO se mueve.
/// </summary>
public enum ModoHardware
{
    [Tooltip("Solo lentes Meta Quest: servidor Python en --modo sim, sin UR10e encendido.")]
    SoloLentesVR,
    [Tooltip("UR10e real: servidor Python en --modo fisico (ur_rtde). Parte de la pose real del robot.")]
    UR10eFisico
}

/// <summary>Mensaje del servidor Python ("sync" o "target").</summary>
[Serializable]
public class RobotData
{
    public string tipo;
    public float[] joints;
    public string modo;   // "sim" | "fisico" (solo en "sync"; servidores antiguos no lo envían)
    public string brazo;  // "R" | "L"
}

/// <summary>
/// Lógica común de seguimiento mano/mando -> UR10e vía TCP con el servidor Python en WSL2.
/// `seguimiento` (brazo derecho, puerto 5005) y `seguimientoL` (brazo izquierdo, puerto 5006)
/// heredan de aquí, así que los valores ya configurados en el Inspector se conservan.
///
/// Controles:
///   Espacio            : iniciar el seguimiento (y liberar la profundidad Z).
///   R / clic stick der.: recalibrar la rotación de la muñeca.
///   A / B (mando der.) : girar Wrist 3.
///   Shift izq. / X (mando izq.) : CLUTCH (mantener). Congela el brazo; al soltar continúa desde donde quedó.
///   Joystick           : mientras se usa, actúa como clutch (evita saltos tras desplazarse).
/// </summary>
public abstract class SeguimientoBrazoBase : MonoBehaviour
{
    [Header("Conexión a Python")]
    public string host = "127.0.0.1";
    public int port = 5005;

    [Header("🖥️ Selector de hardware")]
    [Tooltip("Debe coincidir con el --modo del servidor Python. Si no coincide, el brazo no se mueve.")]
    public ModoHardware modoHardware = ModoHardware.SoloLentesVR;

    [Header("Referencias Vitales")]
    public Transform handReference;
    public Transform baseDelRobot;

    [Header("🎮 Bloqueo por joystick")]
    public float joystickDeadzone = 0.3f;
    public bool bloquearJoystickIzquierdo = true;
    public bool bloquearJoystickDerecho = true;
    [Tooltip("Al soltar el joystick, el brazo continúa desde donde quedó en vez de saltar a la mano.")]
    public bool joystickActuaComoClutch = true;

    [Header("🎮 Recalibración")]
    [Tooltip("Tecla para recalibrar (editor)")]
    public KeyCode teclaRecalibrar = KeyCode.R;

    [Header("🦶 Clutch (embrague quirúrgico)")]
    [Tooltip("Mantener para congelar el brazo. No uses C/V: activan el láser de corte de Tool_cut (GameObjectController).")]
    public KeyCode teclaClutch = KeyCode.LeftShift;
    [Tooltip("Usar el botón primario (X) del mando indicado como clutch.")]
    public bool clutchEnMando = true;
    public XRNode manoClutch = XRNode.LeftHand;

    [Header("〰️ Suavizado (filtro paso bajo)")]
    public bool usarSuavizado = true;
    [Tooltip("Constante de tiempo de la posición (s). Más alto = más suave pero más retraso.")]
    [Range(0f, 0.3f)] public float suavizadoPosicion = 0.06f;
    [Tooltip("Constante de tiempo de la rotación (s).")]
    [Range(0f, 0.3f)] public float suavizadoRotacion = 0.08f;

    [Header("🔌 Reconexión")]
    public float intervaloReconexion = 1f;
    public float timeoutConexion = 2f;

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

    // ---------- Estado público (para UI / GestorModosSimulador) ----------
    public bool EstaConectado => client != null && client.Connected && !conexionPerdida;
    public bool EstaCalibrado => isCalibrated;
    public bool SeguimientoActivo => seguimientoActivadoPorEspacio;
    public bool ClutchActivo => clutchActivo;
    public bool ModoIncompatible => modoIncompatible;
    public string ModoServidor => modoServidor;
    public bool ControlExterno => controlExterno;

    /// <summary>
    /// Mientras es TRUE este componente no mueve los motores ni envía nada a Python: la Demo automática
    /// controla las articulaciones directamente. La conexión se mantiene.
    /// </summary>
    public bool PausadoPorDemo { get; set; }

    // ---------- Control externo (Demo automática) ----------
    private bool controlExterno = false;
    private Transform manoOriginal;

    /// <summary>
    /// La Demo automática mueve el brazo con una "mano virtual": el robot la sigue igual que a tu mando
    /// (mismo servidor Python, mismos límites de seguridad). Se ignoran Espacio, clutch, joystick y botones.
    /// </summary>
    public void IniciarControlExterno(Transform manoVirtual)
    {
        if (manoVirtual == null) return;
        if (!controlExterno) manoOriginal = handReference;
        handReference = manoVirtual;
        controlExterno = true;
        seguimientoActivadoPorEspacio = false; // se activa en Update en cuanto esté calibrado
        filtroInicializado = false;
        clutchActivo = false;
        offsetClutch = Vector3.zero;
        hayPosEnviada = false;
        handRotationCaptured = false;
        wrist3Offset = 0f;
    }

    /// <summary>Devuelve el brazo a tu mando. Para volver a seguirte hay que pulsar Espacio de nuevo.</summary>
    public void FinalizarControlExterno()
    {
        if (!controlExterno) return;
        controlExterno = false;
        handReference = manoOriginal;
        seguimientoActivadoPorEspacio = false;
        zLiberado = false;
        filtroInicializado = false;
        clutchActivo = false;
        offsetClutch = Vector3.zero;
        hayPosEnviada = false;
        handRotationCaptured = false;
    }

    /// <summary>Puerto que se asigna al añadir el componente (Reset). No cambia escenas existentes.</summary>
    protected virtual int PuertoPorDefecto => 5005;
    protected virtual string NombreBrazo => "Brazo";
    /// <summary>False = brazo solo simulado (no hay UR10e real asignado).</summary>
    protected virtual bool PermiteHardwareFisico => true;

    protected virtual void OnValidate()
    {
        if (!PermiteHardwareFisico && modoHardware == ModoHardware.UR10eFisico)
        {
            modoHardware = ModoHardware.SoloLentesVR;
            Debug.LogWarning($"[{NombreBrazo}] Este brazo es solo simulado: 'Modo Hardware' se mantiene en SoloLentesVR.");
        }
    }

    // ---------- Red ----------
    private float sendInterval;
    private float lastSendTime = 0f;
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private volatile bool conexionPerdida = false;
    private volatile int generacion = 0;
    private Coroutine rutinaConexion;

    // ---------- Sincronización ----------
    private volatile bool hasReceivedSync = false;
    private volatile bool hasNewData = false;
    private volatile string modoServidor = null;
    private bool syncAckSent = false;
    private bool isCalibrated = false;
    private bool modoIncompatible = false;
    private readonly float[] targetJoints = new float[6];
    private readonly object jointLock = new object();

    // ---------- Seguimiento ----------
    private bool zLiberado = false;
    private bool seguimientoActivadoPorEspacio = false;
    private Quaternion initialHandRotation;
    private bool handRotationCaptured = false;
    private float wrist3Offset = 0f;

    // Filtro
    private bool filtroInicializado = false;
    private Vector3 posFiltrada;
    private Quaternion rotFiltrada = Quaternion.identity;

    // Clutch
    private bool clutchActivo = false;
    private Vector3 offsetClutch = Vector3.zero;
    private Vector3 ultimaPosEnviada;
    private bool hayPosEnviada = false;
    private Quaternion ultimoDeltaRot = Quaternion.identity;

    // Input
    private InputDevice leftDevice;
    private InputDevice rightDevice;
    private float deviceRefreshTimer = 1f;
    private bool thumbstickAnterior = false;

    // =====================================================================
    // Ciclo de vida
    // =====================================================================
    protected virtual void Reset()
    {
        port = PuertoPorDefecto;
    }

    protected virtual void Start()
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
        sendInterval = 1f / Mathf.Max(1f, sendRateHz);
    }

    protected virtual void OnEnable()
    {
        rutinaConexion = StartCoroutine(BucleConexion());
    }

    protected virtual void OnDisable()
    {
        if (rutinaConexion != null) StopCoroutine(rutinaConexion);
        rutinaConexion = null;
        CerrarConexion();
    }

    protected virtual void OnApplicationQuit() => CerrarConexion();

    // =====================================================================
    // Conexión y reconexión automática
    // =====================================================================
    IEnumerator BucleConexion()
    {
        yield return new WaitForSeconds(0.5f);
        bool avisado = false;
        while (true)
        {
            if (client == null)
            {
                string h = host; int p = port; int timeoutMs = Mathf.RoundToInt(timeoutConexion * 1000f);
                var tarea = System.Threading.Tasks.Task.Run(() => IntentarConectar(h, p, timeoutMs));
                while (!tarea.IsCompleted) yield return null;
                TcpClient nuevo = tarea.Result;

                if (nuevo != null)
                {
                    client = nuevo;
                    stream = client.GetStream();
                    conexionPerdida = false;
                    ReiniciarEstadoSesion();
                    int gen = ++generacion;
                    NetworkStream s = stream;
                    receiveThread = new Thread(() => ReceiveData(s, gen)) { IsBackground = true };
                    receiveThread.Start();
                    avisado = false;
                    Debug.Log($"✅ [{NombreBrazo}] Conectado al servidor Python {host}:{port}");
                }
                else
                {
                    if (!avisado)
                    {
                        Debug.Log($"⏳ [{NombreBrazo}] Esperando al servidor Python en {host}:{port}...");
                        avisado = true;
                    }
                    yield return new WaitForSeconds(intervaloReconexion);
                }
            }
            else
            {
                yield return new WaitForSeconds(0.25f);
            }
        }
    }

    static TcpClient IntentarConectar(string h, int p, int timeoutMs)
    {
        TcpClient c = new TcpClient();
        try
        {
            var t = c.ConnectAsync(h, p);
            if (t.Wait(timeoutMs) && c.Connected)
            {
                c.NoDelay = true; // ⚡ sin Nagle en el lado de Unity
                return c;
            }
        }
        catch { }
        try { c.Close(); } catch { }
        return null;
    }

    void CerrarConexion()
    {
        generacion++; // invalida el hilo de recepción actual
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
        stream = null;
        client = null;
    }

    /// <summary>Estado limpio para cada conexión: el servidor vuelve a mandar la pose inicial.</summary>
    void ReiniciarEstadoSesion()
    {
        hasReceivedSync = false;
        hasNewData = false;
        modoServidor = null;
        syncAckSent = false;
        isCalibrated = false;
        modoIncompatible = false;
        seguimientoActivadoPorEspacio = false; // por seguridad, hay que volver a pulsar Espacio
        zLiberado = false;
        handRotationCaptured = false;
        wrist3Offset = 0f;
        filtroInicializado = false;
        clutchActivo = false;
        offsetClutch = Vector3.zero;
        hayPosEnviada = false;
        ultimoDeltaRot = Quaternion.identity;
    }

    void EnviarLinea(string json)
    {
        if (stream == null) return;
        byte[] data = Encoding.UTF8.GetBytes(json);
        try { stream.Write(data, 0, data.Length); }
        catch { conexionPerdida = true; }
    }

    // =====================================================================
    // Input
    // =====================================================================
    void RefrescarDevices()
    {
        deviceRefreshTimer += Time.unscaledDeltaTime;
        if (deviceRefreshTimer < 1f) return;
        deviceRefreshTimer = 0f;
        if (!leftDevice.isValid) leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!rightDevice.isValid) rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    InputDevice Dispositivo(XRNode nodo) => nodo == XRNode.LeftHand ? leftDevice : rightDevice;

    bool JoystickActivo(InputDevice device)
    {
        if (!device.isValid) return false;
        if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick)) return false;
        return stick.magnitude > joystickDeadzone;
    }

    bool AlgunJoystickActivo()
    {
        if (bloquearJoystickIzquierdo && JoystickActivo(leftDevice)) return true;
        if (bloquearJoystickDerecho && JoystickActivo(rightDevice)) return true;
        return false;
    }

    bool ClutchPresionado()
    {
        if (Input.GetKey(teclaClutch)) return true;
        if (clutchEnMando)
        {
            InputDevice d = Dispositivo(manoClutch);
            if (d.isValid && d.TryGetFeatureValue(CommonUsages.primaryButton, out bool x) && x) return true;
        }
        return false;
    }

    bool BotonRecalibrarPresionado()
    {
        bool presionadoAhora = Input.GetKey(teclaRecalibrar);
        if (rightDevice.isValid && rightDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickPressed) && stickPressed)
            presionadoAhora = true;

        bool flanco = presionadoAhora && !thumbstickAnterior;
        thumbstickAnterior = presionadoAhora;
        return flanco;
    }

    // =====================================================================
    // Update
    // =====================================================================
    /// <summary>
    /// El componente Controller del importador URDF pone stiffness/damping/forceLimit = 0 al iniciar.
    /// Sin datos de Python el brazo quedaba flácido y caía. Aquí se mantienen los motores rígidos y,
    /// hasta recibir la primera pose de Python, el brazo se sostiene en la pose en que está.
    /// </summary>
    void MantenerMotoresRigidos()
    {
        for (int i = 0; i < 6; i++)
        {
            if (urJoints.Length <= i || urJoints[i] == null) continue;
            ArticulationDrive d = urJoints[i].xDrive;
            if (d.stiffness >= 1f && d.forceLimit >= 1f) continue;
            d.stiffness = 100000f;
            d.damping = 10000f;
            d.forceLimit = 3.402823e+38f;
            if (!hasReceivedSync && !PausadoPorDemo && urJoints[i].jointPosition.dofCount > 0)
                d.target = urJoints[i].jointPosition[0] * Mathf.Rad2Deg; // sostener la pose actual
            urJoints[i].xDrive = d;
        }
    }

    protected virtual void FixedUpdate() => MantenerMotoresRigidos();

    protected virtual void Update()
    {
        RefrescarDevices();
        MantenerMotoresRigidos();

        if (conexionPerdida && client != null)
        {
            Debug.LogWarning($"⚠️ [{NombreBrazo}] Conexión perdida con Python. Reintentando...");
            CerrarConexion();
            ReiniciarEstadoSesion();
        }

        if (hasNewData) ApplyJointsToMotors();

        // Si se corrige "Modo Hardware" en el Inspector durante la ejecución, se reanuda la sincronización
        if (modoIncompatible && hasReceivedSync && !syncAckSent && !string.IsNullOrEmpty(modoServidor) &&
            modoServidor == (modoHardware == ModoHardware.UR10eFisico ? "fisico" : "sim"))
        {
            modoIncompatible = false;
            ConfirmarSyncSiProcede();
        }

        // 🤖 Control externo (Demo automática): se activa solo y se ignoran teclado y mandos
        if (controlExterno)
        {
            if (isCalibrated && !modoIncompatible && !seguimientoActivadoPorEspacio)
            {
                seguimientoActivadoPorEspacio = true;
                zLiberado = true;
                offsetClutch = Vector3.zero;
                Debug.Log($"🤖 [{NombreBrazo}] Control externo activo (Demo automática).");
            }
            return;
        }

        // ⌨️ ACTIVAR SEGUIMIENTO CON LA BARRA ESPACIADORA
        if (Input.GetKeyDown(KeyCode.Space) && isCalibrated && !modoIncompatible)
        {
            if (!seguimientoActivadoPorEspacio)
            {
                seguimientoActivadoPorEspacio = true;
                zLiberado = true; // Libera la profundidad al activar
                offsetClutch = Vector3.zero;
                Debug.Log($"🚀 [{NombreBrazo}] ¡SEGUIMIENTO ACTIVADO! Sincronizado con la mano.");
            }
            else if (!zLiberado)
            {
                zLiberado = true;
                Debug.Log($"🔓 [{NombreBrazo}] Profundidad (Z) liberada.");
            }
        }

        if (BotonRecalibrarPresionado() && handReference != null)
        {
            initialHandRotation = handReference.rotation;
            rotFiltrada = handReference.rotation;
            ultimoDeltaRot = Quaternion.identity;
            handRotationCaptured = true;
            wrist3Offset = 0f;
            Debug.Log($"🔄 [{NombreBrazo}] RECALIBRADO. Rotación: {initialHandRotation.eulerAngles}");
        }

        float wrist3Input = 0f;
        if (rightDevice.isValid)
        {
            rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed);
            rightDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bPressed);
            if (aPressed) wrist3Input -= 1f;
            if (bPressed) wrist3Input += 1f;
        }
        if (invertWrist3) wrist3Input = -wrist3Input;
        if (Mathf.Abs(wrist3Input) > 0.01f && !clutchActivo)
            wrist3Offset += wrist3Input * wrist3Speed * Mathf.Deg2Rad * Time.deltaTime;
    }

    protected virtual void LateUpdate()
    {
        if (PausadoPorDemo) return;
        // 🛑 Sin conexión, sin pose inicial, modo incorrecto o sin Espacio: no se envía nada
        if (!EstaConectado || !hasReceivedSync || !isCalibrated || modoIncompatible) return;
        if (!seguimientoActivadoPorEspacio || handReference == null || baseDelRobot == null) return;

        // 〰️ Filtro paso bajo exponencial (independiente de los FPS), se actualiza cada frame
        Vector3 posMano = baseDelRobot.InverseTransformPoint(handReference.position);
        Quaternion rotMano = handReference.rotation;
        if (!filtroInicializado || !usarSuavizado)
        {
            posFiltrada = posMano;
            rotFiltrada = rotMano;
            filtroInicializado = true;
        }
        else
        {
            float dt = Time.deltaTime;
            float aPos = suavizadoPosicion <= 0f ? 1f : 1f - Mathf.Exp(-dt / suavizadoPosicion);
            float aRot = suavizadoRotacion <= 0f ? 1f : 1f - Mathf.Exp(-dt / suavizadoRotacion);
            posFiltrada = Vector3.Lerp(posFiltrada, posMano, aPos);
            rotFiltrada = Quaternion.Slerp(rotFiltrada, rotMano, aRot);
        }

        if (!handRotationCaptured)
        {
            initialHandRotation = rotFiltrada;
            handRotationCaptured = true;
            Debug.Log($"✅ [{NombreBrazo}] Captura inicial de rotación automática.");
        }

        // 🦶 CLUTCH: mientras está pulsado (o se usa el joystick) el brazo se congela
        bool congelar = !controlExterno && (ClutchPresionado() || (AlgunJoystickActivo() && joystickActuaComoClutch));
        bool soloBloquear = !controlExterno && !joystickActuaComoClutch && AlgunJoystickActivo();
        if (congelar)
        {
            if (!clutchActivo)
            {
                clutchActivo = true;
                Debug.Log($"🦶 [{NombreBrazo}] Clutch: brazo congelado.");
            }
            return;
        }
        if (clutchActivo)
        {
            // Re-anclar: el objetivo continúa exactamente desde donde quedó
            if (hayPosEnviada) offsetClutch = ultimaPosEnviada - posFiltrada;
            initialHandRotation = rotFiltrada * Quaternion.Inverse(ultimoDeltaRot);
            clutchActivo = false;
            Debug.Log($"▶️ [{NombreBrazo}] Clutch liberado: movimiento relativo reanudado.");
        }
        if (soloBloquear) return; // comportamiento original (bloquear sin re-anclar)

        if (Time.time - lastSendTime < sendInterval) return;

        Vector3 posLocal = posFiltrada + offsetClutch;
        if (posLocal.magnitude > 1.1f) posLocal = posLocal.normalized * 1.1f;

        Quaternion deltaRot = Quaternion.Inverse(initialHandRotation) * rotFiltrada;
        Vector3 eulerDelta = deltaRot.eulerAngles;

        float angleX = eulerDelta.x;
        if (angleX > 180f) angleX -= 360f;
        float angleY = eulerDelta.y;
        if (angleY > 180f) angleY -= 360f;

        float wrist1Angle = angleX * Mathf.Deg2Rad * wrist1Sensitivity;
        if (invertWrist1) wrist1Angle = -wrist1Angle;
        float wrist2Angle = -angleY * Mathf.Deg2Rad * wrist2Sensitivity;
        if (invertWrist2) wrist2Angle = -wrist2Angle;

        CultureInfo ci = CultureInfo.InvariantCulture;
        string json = "{\"tipo\": \"ik_abs\", \"pos\": [" +
                      posLocal.x.ToString(ci) + ", " + posLocal.y.ToString(ci) + ", " + posLocal.z.ToString(ci) +
                      "], \"rot\": [0, " + wrist2Angle.ToString(ci) + ", " + wrist1Angle.ToString(ci) +
                      "], \"z_libre\": " + (zLiberado ? "true" : "false") +
                      ", \"wrist3_offset\": " + wrist3Offset.ToString(ci) + "}\n";
        EnviarLinea(json);

        ultimaPosEnviada = posLocal;
        hayPosEnviada = true;
        ultimoDeltaRot = deltaRot;
        lastSendTime = Time.time;
    }

    // =====================================================================
    // Motores y protocolo
    // =====================================================================
    void ApplyJointsToMotors()
    {
        lock (jointLock)
        {
            for (int i = 0; i < 6 && !PausadoPorDemo; i++)
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
        ConfirmarSyncSiProcede();
    }

    /// <summary>Envía sync_ack una sola vez, después de haber aplicado la pose inicial.</summary>
    void ConfirmarSyncSiProcede()
    {
        if (hasReceivedSync && !syncAckSent && !modoIncompatible)
        {
            if (!ModoCoincide(out string esperado))
            {
                // No se envía sync_ack: el servidor queda esperando y el robot no se mueve.
                modoIncompatible = true;
                Debug.LogError($"⛔ [{NombreBrazo}] Modo incompatible: Unity espera '{esperado}' pero el servidor Python está en '{modoServidor}'. " +
                               $"Cambia 'Modo Hardware' en el Inspector o arranca Python con --modo {esperado}.");
                return;
            }
            EnviarLinea("{\"tipo\": \"sync_ack\"}\n");
            syncAckSent = true;
            isCalibrated = true;
            Debug.Log($"✅ [{NombreBrazo}] Calibrado en la pose inicial (servidor: {modoServidor ?? "sin modo"}). Presiona ESPACIO para iniciar el seguimiento.");
        }
    }

    bool ModoCoincide(out string esperado)
    {
        esperado = modoHardware == ModoHardware.UR10eFisico ? "fisico" : "sim";
        if (string.IsNullOrEmpty(modoServidor))
        {
            // Servidor antiguo sin campo "modo": no se puede verificar.
            if (modoHardware == ModoHardware.UR10eFisico)
                Debug.LogWarning($"⚠️ [{NombreBrazo}] El servidor no informa su modo; no se puede verificar que el UR10e real esté conectado.");
            return true;
        }
        return modoServidor == esperado;
    }

    void ReceiveData(NetworkStream s, int gen)
    {
        byte[] buffer = new byte[4096];
        StringBuilder pendiente = new StringBuilder();
        try
        {
            while (gen == generacion)
            {
                int bytesRead = s.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break; // el servidor cerró la conexión

                pendiente.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                string texto = pendiente.ToString();
                int ultimoSalto = texto.LastIndexOf('\n');
                if (ultimoSalto < 0) continue; // línea incompleta: esperar más datos

                string completas = texto.Substring(0, ultimoSalto);
                pendiente.Clear();
                pendiente.Append(texto.Substring(ultimoSalto + 1));

                foreach (string line in completas.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    RobotData data;
                    try { data = JsonUtility.FromJson<RobotData>(line); }
                    catch { continue; }
                    if (data == null || data.joints == null || data.joints.Length < 6) continue;
                    if (data.tipo != "sync" && data.tipo != "target") continue;

                    lock (jointLock)
                    {
                        for (int i = 0; i < 6; i++) targetJoints[i] = data.joints[i];
                        if (data.tipo == "sync")
                        {
                            modoServidor = data.modo;
                            hasReceivedSync = true;
                        }
                        hasNewData = true;
                    }
                }
            }
        }
        catch { }

        if (gen == generacion) conexionPerdida = true;
    }
}
