using System;
using UnityEngine;
using UnityEngine.XR;

/// <summary>Modo de uso del simulador. (El orden se conserva para no alterar escenas guardadas.)</summary>
public enum ModoSimulador
{
    [Tooltip("SIMULAR OPERACIÓN: te pones los lentes y operas tú (seguimiento de mandos, intercambio de instrumentos, fulcro).")]
    PracticaLibre,
    [Tooltip("PROCEDIMIENTO GUIADO: tú operas y la escena te guía paso a paso (marcadores y hologramas).")]
    DemoGuiada,
    [Tooltip("EJECUTAR DEMO: los brazos realizan solos la colecistectomía; tú observas.")]
    DemoAutomatica
}

/// <summary>
/// Selector de modo del simulador:
///   • Simular operación (VR)  -> lo que ya tenías: tú controlas los brazos con los mandos.
///   • Ejecutar demo           -> <see cref="DemoAutomaticaColecistectomia"/>: los brazos operan solos.
///   • Procedimiento guiado    -> <see cref="DemoColecistectomia"/>: tú operas con guía visual.
///
/// Es NO destructivo: cada modo activa solo sus propios componentes y, al salir, todo vuelve a su estado.
/// Controles: M / botón Menú (mando izq.) cicla los modos. En el escritorio aparece un selector al iniciar.
/// </summary>
[DisallowMultipleComponent]
public class GestorModosSimulador : MonoBehaviour
{
    public static GestorModosSimulador Instancia { get; private set; }

    /// <summary>Se dispara cada vez que cambia el modo (también al iniciar).</summary>
    public static event Action<ModoSimulador> ModoCambiado;

    [Header("Modo")]
    public ModoSimulador modoInicial = ModoSimulador.PracticaLibre;
    [Tooltip("Muestra al iniciar un selector de modo en la ventana del escritorio (Game view).")]
    public bool mostrarSelectorAlInicio = true;

    [Header("Referencias (vacío = se buscan en la escena)")]
    public DemoColecistectomia demo;
    public DemoAutomaticaColecistectomia demoAutomatica;

    [Header("Objetos opcionales por modo (vacío = no se toca nada)")]
    public GameObject[] activosSoloEnDemo = new GameObject[0];
    public GameObject[] activosSoloEnPractica = new GameObject[0];

    [Header("Controles")]
    public KeyCode teclaAlternar = KeyCode.M;
    public bool usarBotonMenuMandoIzquierdo = true;

    [Header("Panel de escritorio")]
    public bool mostrarPanelEstado = true;

    public ModoSimulador ModoActual { get; private set; }

    private bool selectorPendiente;
    private bool menuAnterior;
    private InputDevice mandoIzq;
    private float refresco = 1f;
    private SeguimientoBrazoBase[] brazos = new SeguimientoBrazoBase[0];
    private float refrescoBrazos = 0f;

    public static string Nombre(ModoSimulador m) =>
        m == ModoSimulador.PracticaLibre ? "Simular operación (VR)" :
        m == ModoSimulador.DemoAutomatica ? "Ejecutar demo (automática)" : "Procedimiento guiado";

    void Awake()
    {
        if (Instancia != null && Instancia != this)
        {
            Debug.LogWarning("Hay más de un GestorModosSimulador en la escena; se desactiva el duplicado.");
            enabled = false;
            return;
        }
        Instancia = this;
        if (demo == null) demo = FindFirstObjectByType<DemoColecistectomia>(FindObjectsInactive.Include);
        if (demoAutomatica == null) demoAutomatica = FindFirstObjectByType<DemoAutomaticaColecistectomia>(FindObjectsInactive.Include);
    }

    void Start()
    {
        selectorPendiente = mostrarSelectorAlInicio;
        AplicarModo(modoInicial, forzar: true);
    }

    void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
    }

    void Update()
    {
        if (Input.GetKeyDown(teclaAlternar)) Alternar();

        if (usarBotonMenuMandoIzquierdo)
        {
            refresco += Time.unscaledDeltaTime;
            if (!mandoIzq.isValid && refresco >= 1f)
            {
                refresco = 0f;
                mandoIzq = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            }
            bool menu = false;
            if (mandoIzq.isValid) mandoIzq.TryGetFeatureValue(CommonUsages.menuButton, out menu);
            if (menu && !menuAnterior) Alternar();
            menuAnterior = menu;
        }

        refrescoBrazos -= Time.unscaledDeltaTime;
        if (refrescoBrazos <= 0f)
        {
            refrescoBrazos = 2f;
            brazos = FindObjectsByType<SeguimientoBrazoBase>(FindObjectsSortMode.None);
        }
    }

    // ---------------- API pública (para botones de UI) ----------------
    public void ActivarPractica() => AplicarModo(ModoSimulador.PracticaLibre);
    public void ActivarDemo() => AplicarModo(ModoSimulador.DemoGuiada);
    public void ActivarDemoAutomatica() => AplicarModo(ModoSimulador.DemoAutomatica);

    /// <summary>Simular operación -> Ejecutar demo -> Procedimiento guiado -> Simular operación…</summary>
    public void Alternar()
    {
        var siguiente = ModoActual == ModoSimulador.PracticaLibre ? ModoSimulador.DemoAutomatica
                      : ModoActual == ModoSimulador.DemoAutomatica ? ModoSimulador.DemoGuiada
                      : ModoSimulador.PracticaLibre;
        AplicarModo(siguiente);
    }

    public void AplicarModo(ModoSimulador modo, bool forzar = false)
    {
        selectorPendiente = false;
        if (!forzar && modo == ModoActual) return;
        ModoActual = modo;
        bool esGuiada = modo == ModoSimulador.DemoGuiada;
        bool esAutomatica = modo == ModoSimulador.DemoAutomatica;
        bool esDemo = esGuiada || esAutomatica;

        foreach (var go in activosSoloEnDemo) if (go != null) go.SetActive(esDemo);
        foreach (var go in activosSoloEnPractica) if (go != null) go.SetActive(!esDemo);

        // Primero se detiene lo que no corresponde (la demo automática usa el panel de la guiada)
        if (!esAutomatica && demoAutomatica != null) demoAutomatica.Detener();
        if (!esGuiada && demo != null && (demoAutomatica == null || !demoAutomatica.Ejecutando)) demo.DetenerDemo();

        if (esGuiada)
        {
            if (demo != null) demo.IniciarDemo();
            else Debug.LogWarning("Procedimiento guiado seleccionado pero no hay DemoColecistectomia en la escena.");
        }
        if (esAutomatica)
        {
            if (demoAutomatica != null) demoAutomatica.Iniciar();
            else Debug.LogWarning("Ejecutar demo seleccionado pero no hay DemoAutomaticaColecistectomia en la escena.");
        }

        Debug.Log($"🩺 Modo del simulador: {Nombre(modo)}");
        ModoCambiado?.Invoke(modo);
    }

    // ---------------- Panel de escritorio (no visible en el visor VR) ----------------
    void OnGUI()
    {
        if (selectorPendiente)
        {
            const float w = 380f, h = 230f;
            Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUILayout.BeginArea(r, "Simulador de Colecistectomía", GUI.skin.window);
            GUILayout.Space(8);
            if (GUILayout.Button("Simular operación (te pones los lentes y operas)", GUILayout.Height(44))) ActivarPractica();
            if (GUILayout.Button("Ejecutar demo (los brazos operan solos)", GUILayout.Height(44))) ActivarDemoAutomatica();
            if (GUILayout.Button("Procedimiento guiado (operas con guía)", GUILayout.Height(36))) ActivarDemo();
            GUILayout.Label($"También: tecla {teclaAlternar} o botón Menú del mando izquierdo.");
            GUILayout.EndArea();
            return;
        }

        if (!mostrarPanelEstado) return;
        int filas = brazos.Length + (demoAutomatica != null && demoAutomatica.Ejecutando ? 1 : 0);
        GUILayout.BeginArea(new Rect(10, 10, 400, 70 + 22 * filas), GUI.skin.box);
        GUILayout.Label($"Modo: {Nombre(ModoActual)}  [{teclaAlternar}]");
        if (demoAutomatica != null && demoAutomatica.Ejecutando) GUILayout.Label($"Demo: {demoAutomatica.Fase}");
        foreach (var b in brazos)
        {
            if (b == null) continue;
            string estado = b.PausadoPorDemo ? "controlado por la demo"
                : !b.EstaConectado ? "sin conexión"
                : b.ModoIncompatible ? $"⛔ modo incompatible (servidor: {b.ModoServidor})"
                : !b.EstaCalibrado ? "esperando pose inicial"
                : b.ClutchActivo ? "clutch"
                : b.SeguimientoActivo ? "siguiendo" : "listo (Espacio)";
            GUILayout.Label($"{b.name} [{b.modoHardware}] :{b.port} → {estado}");
        }
        GUILayout.EndArea();
    }
}
