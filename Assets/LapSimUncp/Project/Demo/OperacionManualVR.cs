using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// OPERACIÓN MANUAL (Meta Quest 3): la misma colecistectomía de la demo, pero los gestos quirúrgicos los
/// hace el usuario con los mandos, teleoperando los instrumentos como en un sistema daVinci.
///
///  - Mando derecho -> brazo derecho (disector / clipadora / tijera). Mando izquierdo -> brazo izquierdo (grasper).
///  - GATILLO (índice) mantenido = SEGUIMIENTO: el brazo sigue el movimiento de la mano. Fuera del paciente,
///    1:1 en la sala; con el instrumento dentro, escalado y referido a la imagen del laparoscopio.
///  - GRIP (dedo medio) = AGARRAR, en los dos mandos. Con la mano agarra objetos (en la mano queda un clon y
///    el objeto real va al brazo). Con el brazo libre, la pinza toma o suelta lo que tenga al alcance
///    (trocares e instrumentos). Con el instrumento dentro, es su acción: agarrar tejido, cauterizar, clipar, cortar.
///  - A / X: fija o suelta el agarre del grasper (trinquete).
///  - B / Y (o Enter): confirmar / pedir al asistente el siguiente paso (montar o cambiar instrumentos).
///
/// Es parte de <see cref="DemoAutomaticaColecistectomia"/> (clase parcial) para reutilizar su maquinaria:
/// IK de los brazos, entrega de instrumentos, fulcro, medición del conducto, clips, corte y extracción.
/// </summary>
public partial class DemoAutomaticaColecistectomia
{
    [System.Serializable]
    public class ManoEntrada
    {
        public Vector3 pos;          // posición del mando (espacio de seguimiento)
        public Quaternion rot = Quaternion.identity;   // orientación del mando (espacio de seguimiento)
        public bool mover;           // gatillo
        public float accion;         // grip 0..1
        public bool primario;        // A / X
        public bool secundario;      // B / Y
    }

    [Header("Operación manual (Quest 3)")]
    [Tooltip("Cuánto se mueve la punta por cada metro que se mueve la mano (0.5 = escala 2:1, como un daVinci).")]
    public float escalaMovimiento = 0.5f;
    [Tooltip("Velocidad máxima de la punta del instrumento (m/s).")]
    public float velocidadMaxPunta = 0.08f;
    [Tooltip("Control libre (fuera del paciente): cuánto se mueve la pinza del brazo por cada metro de la mano.")]
    public float escalaLibre = 1f;
    [Tooltip("Control libre: velocidad máxima de la pinza del brazo (m/s).")]
    public float velocidadMaxLibre = 0.35f;
    [Tooltip("Control libre: distancia máxima (m) a la que la pinza puede alejarse de su posición inicial. El límite real lo pone el alcance del brazo.")]
    public float radioLibre = 1.5f;
    [Tooltip("Al empezar, los brazos van a su pose inicial de trabajo (la misma que envían los servidores de WSL al conectar).")]
    public bool usarPoseInicial = true;
    [Tooltip("Pose inicial del brazo derecho, en grados por articulación (q_sim_deg de seguimientoR.py).")]
    public float[] poseInicialDerecha = { 35f, -55f, 110f, 125f, -125f, 0f };
    [Tooltip("Pose inicial del brazo izquierdo, en grados por articulación (q_sim_deg de seguimientoL.py).")]
    public float[] poseInicialIzquierda = { 140f, -132f, -95f, 47f, 130f, 0f };
    [Tooltip("Para pruebas sin visor: la entrada se toma de simDer / simIzq / simCabeza en vez de los mandos.")]
    public bool usarEntradaSimulada = false;
    public ManoEntrada simDer = new ManoEntrada(), simIzq = new ManoEntrada();
    public Quaternion simCabeza = Quaternion.identity;

    public bool ManualActiva { get; private set; }
    /// <summary>Resumen del resultado de la última operación manual (tiempos y errores).</summary>
    public string InformeManual { get; private set; } = "";

    private readonly ManoEntrada realDer = new ManoEntrada(), realIzq = new ManoEntrada();
    private bool secDerPrev, secIzqPrev, priDerPrev, priIzqPrev;
    private bool confirmacionPendiente, stickPrev;
    private bool cerrando;
    private Quaternion rotOrigenXR = Quaternion.identity;   // orientación del XR Origin: pasa los movimientos del mando a la sala

    // Agarre con la mano: el objeto real va al brazo y en la mano queda un clon
    private class AgarreMano { public GameObject objeto; public Transform mano; public bool izq; }
    private class ClonMano { public GameObject clon; public GameObject original; public bool izq; public float creado; }
    private readonly List<AgarreMano> agarresPendientes = new List<AgarreMano>();
    private readonly List<ClonMano> clonesEnMano = new List<ClonMano>();
    private readonly Dictionary<GameObject, float> enTransito = new Dictionary<GameObject, float>();
    private readonly List<XRGrabInteractable> agarrablesSuscritos = new List<XRGrabInteractable>();
    private readonly bool[] colocadoAntes = new bool[3];
    private bool puertosListos;

    void OnApplicationQuit() => cerrando = true;
    private GameObject pantalla;
    private TextMesh textoPantalla;
    private RenderTexture rtPantalla, rtOriginalLap;
    private Camera camPantalla;
    private Material matPrincipal;
    private TextMesh etiquetaVista;
    /// <summary>
    /// false = PRIMERA PERSONA: ves la sala con tus ojos y la imagen del laparoscopio es un panel fijo en la sala.
    /// true  = LAPAROSCOPIO: la imagen del laparoscopio va pegada a tu cabeza y ocupa tu campo de visión.
    /// </summary>
    public bool VistaLaparoscopioActiva { get; private set; }
    [Tooltip("Primera persona: el panel de guía se coloca como un monitor en la cabecera de la camilla, a esta altura sobre el abdomen (m).")]
    public float alturaMonitor = 0.80f;
    [Tooltip("Primera persona: distancia (m) del monitor hacia la cabecera, medida desde el abdomen.")]
    public float distanciaMonitor = 1.15f;
    [Tooltip("Primera persona: tamaño del monitor (1 = 72 cm de ancho).")]
    public float escalaMonitor = 1.5f;
    [Tooltip("Radio (m) alrededor de cada puerto en el que un trocar queda encajado al acercarlo.")]
    public float radioEncajePuerto = 0.13f;
    private bool panelFrente;            // true = el usuario pidió traer el panel frente a él (clic del stick)
    private GameObject fondoTexto;
    [Tooltip("Vista de laparoscopio: distancia (m) de la imagen a los ojos.")]
    public float distanciaVistaLaparoscopio = 0.55f;
    [Tooltip("Vista de laparoscopio: ancho (m) de la imagen. Con 1,0 m a 0,55 m ocupa unos 85° de tu campo de visión.")]
    public float anchoVistaLaparoscopio = 1.0f;
    private readonly List<Behaviour> interactoresApagados = new List<Behaviour>();
    private readonly List<Transform> grasa = new List<Transform>();
    private readonly List<Transform> puntosLecho = new List<Transform>();
    private readonly List<float> clipsPuestos = new List<float>();
    private readonly List<string> tiempos = new List<string>();
    private Coroutine corteManual;
    private Transform guiaTijera;          // dónde poner la punta de la tijera para que el corte atraviese el conducto
    private float tiempoCauterio;
    private int errores;
    private string avisoManual = "";
    private float avisoHasta;
    private string tituloManual = "", textoManual = "";
    private const float S_CORTE_MANUAL = 0.0305f;

    // ------------------------------------------------------------------
    // API
    // ------------------------------------------------------------------
    public void IniciarManual()
    {
        if (Ejecutando) return;
        Autocompletar();
        if (brazoDerecho == null || brazoIzquierdo == null || intercambio == null || preparacion == null)
        {
            Debug.LogError("Operación manual: faltan referencias (brazos, IntercambioHerramienta o PreparacionQuirurgica).");
            return;
        }
        rutina = StartCoroutine(SecuenciaManual());
    }

    /// <summary>Punta deseada del instrumento teleoperado de un brazo (para depuración y pruebas).</summary>
    public Vector3 PuntaManual(bool izquierdo) { var b = izquierdo ? izq : der; return b != null ? b.puntaDeseada : Vector3.zero; }
    public bool TeleopActiva(bool izquierdo) { var b = izquierdo ? izq : der; return b != null && b.teleop; }
    /// <summary>¿Qué lleva el brazo? (para depuración y pruebas)</summary>
    public string EstadoBrazo(bool izquierdo)
    {
        var b = izquierdo ? izq : der;
        if (b == null) return "-";
        string lleva = b.trocarEnPinza != null ? "trocar " + b.trocarEnPinza.trocar.name : b.instrumento != null ? b.instrumento.name : "nada";
        return $"lleva={lleva} dentro={b.dentro} ocupado={b.ocupado} teleop={b.teleop} libre={b.fulcroTr == null} pinza={ExtremoBrazo(b).ToString("F3")}";
    }

    /// <summary>Movimiento de mano (espacio de seguimiento) que produce el desplazamiento pedido de la punta o la pinza.</summary>
    public Vector3 DeltaManoPara(bool izquierdo, Vector3 deltaMundo)
    {
        var b = izquierdo ? izq : der;
        if (b != null && b.fulcroTr == null) return Quaternion.Inverse(rotOrigenXR) * deltaMundo / Mathf.Max(0.01f, escalaLibre);
        return GuinadaCabeza() * (Quaternion.Inverse(MarcoVistaManual) * deltaMundo) / Mathf.Max(0.01f, escalaMovimiento);
    }

    /// <summary>Equivale a agarrar un objeto con la mano (lo usan las pruebas sin visor).</summary>
    public void SimularAgarreConMano(GameObject objeto, bool manoIzquierda)
    {
        if (ManualActiva && objeto != null) agarresPendientes.Add(new AgarreMano { objeto = objeto, mano = null, izq = manoIzquierda });
    }

    /// <summary>Marco de la imagen del laparoscopio: a él se refieren los movimientos de la mano.</summary>
    public Quaternion MarcoVistaManual => camaraLap != null ? camaraLap.transform.rotation : Quaternion.identity;

    // ------------------------------------------------------------------
    // Entrada
    // ------------------------------------------------------------------
    ManoEntrada LeerMano(bool izquierda)
    {
        if (usarEntradaSimulada) return izquierda ? simIzq : simDer;
        var m = izquierda ? realIzq : realDer;
        var d = InputDevices.GetDeviceAtXRNode(izquierda ? XRNode.LeftHand : XRNode.RightHand);
        if (!d.isValid) { m.mover = false; m.accion = 0f; m.primario = false; m.secundario = false; return m; }
        d.TryGetFeatureValue(CommonUsages.devicePosition, out m.pos);
        if (!d.TryGetFeatureValue(CommonUsages.deviceRotation, out m.rot)) m.rot = Quaternion.identity;
        d.TryGetFeatureValue(CommonUsages.trigger, out float gatillo);
        m.mover = gatillo > 0.5f;
        d.TryGetFeatureValue(CommonUsages.grip, out m.accion);
        d.TryGetFeatureValue(CommonUsages.primaryButton, out m.primario);
        d.TryGetFeatureValue(CommonUsages.secondaryButton, out m.secundario);
        return m;
    }

    Quaternion GuinadaCabeza()
    {
        Quaternion q = simCabeza;
        if (!usarEntradaSimulada)
        {
            var cabeza = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!cabeza.isValid || !cabeza.TryGetFeatureValue(CommonUsages.deviceRotation, out q)) q = Quaternion.identity;
        }
        Vector3 f = q * Vector3.forward; f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
    }

    /// <summary>Se llama cada frame desde Update(): lee los mandos y mueve los instrumentos teleoperados.</summary>
    void ActualizarManual()
    {
        if (!ManualActiva) return;
        ManoEntrada md = LeerMano(false), mi = LeerMano(true);
        if ((md.secundario && !secDerPrev) || (mi.secundario && !secIzqPrev) || Input.GetKeyDown(KeyCode.Return)) confirmacionPendiente = true;
        bool priDer = md.primario && !priDerPrev, priIzq = mi.primario && !priIzqPrev;
        bool stick = false;
        if (!usarEntradaSimulada)
        {
            var mandoDer = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (mandoDer.isValid) mandoDer.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out stick);
        }
        if ((stick && !stickPrev) || Input.GetKeyDown(KeyCode.P)) { panelFrente = !panelFrente; ReubicarPantalla(); }
        if (priDer || Input.GetKeyDown(KeyCode.V)) AlternarVista();
        stickPrev = stick;
        secDerPrev = md.secundario; secIzqPrev = mi.secundario; priDerPrev = md.primario; priIzqPrev = mi.primario;

        ProcesarAgarresDeMano(md, mi);
        EncajarTrocaresCercanos();
        Quaternion guinada = GuinadaCabeza();
        Teleoperar(der, md, guinada, priDer);
        Teleoperar(izq, mi, guinada, priIzq);
        for (int i = 0; i < colocadoAntes.Length; i++) colocadoAntes[i] = preparacion.EstaColocado(i);

        MantenerCamaraDePantalla();
        if (pantalla != null && textoPantalla != null)
        {
            string aviso = Time.time < avisoHasta ? "\n\n>> " + avisoManual : "";
            // Mirando por el laparoscopio solo se muestra el nombre del paso (y los avisos): la guía completa
            // está en el monitor de la cabecera.
            textoPantalla.text = VistaLaparoscopioActiva ? tituloManual + (Time.time < avisoHasta ? "\n>> " + avisoManual : "")
                                                         : tituloManual + "\n" + textoManual + aviso;
        }
    }

    bool TomarConfirmacion()
    {
        if (!confirmacionPendiente) return false;
        confirmacionPendiente = false;
        return true;
    }

    IEnumerator EsperarConfirmacion()
    {
        confirmacionPendiente = false;
        while (!TomarConfirmacion()) yield return null;
    }

    // ------------------------------------------------------------------
    // Teleoperación de un instrumento a través de su trocar
    // ------------------------------------------------------------------
    void IniciarTeleop(Brazo b, Transform fulcroTr)
    {
        b.fulcroTr = fulcroTr;
        b.puntaDeseada = ExtremoBrazo(b);
        b.pendiente = Vector3.zero;
        b.moviendo = false;
        b.profMin = 0.01f;
        b.teleop = true;
    }

    /// <summary>
    /// Lleva el brazo a su pose inicial de trabajo moviendo las articulaciones, y deja la IK partiendo de ahí.
    /// Es la pose que, en el modo con WSL, manda el servidor de Python al conectar.
    /// </summary>
    IEnumerator IrAPoseInicial(Brazo b, float[] pose)
    {
        if (b == null || pose == null || pose.Length < 6) yield break;
        b.activo = false;
        var objetivo = new float[6];
        for (int i = 0; i < 6; i++) objetivo[i] = pose[i] * (b.seg.jointDirections != null && b.seg.jointDirections.Length > i ? b.seg.jointDirections[i] : 1f);
        float limite = Time.time + 12f;
        while (Time.time < limite)
        {
            bool listo = true;
            for (int i = 0; i < 6; i++)
            {
                var d = b.ik.juntas[i].xDrive;
                d.target = Mathf.MoveTowards(d.target, objetivo[i], velocidadArticular * Time.deltaTime);
                if (d.stiffness < 1f || d.forceLimit < 1f) { d.stiffness = 100000f; d.damping = 10000f; d.forceLimit = 3.402823e+38f; }
                b.ik.juntas[i].xDrive = d;
                if (Mathf.Abs(d.target - objetivo[i]) > 0.05f) listo = false;
            }
            if (listo) break;
            yield return null;
        }
        // Espera a que el brazo real llegue (los motores siguen al objetivo con algo de retraso)
        float asentar = Time.time + 3f;
        while (Time.time < asentar)
        {
            float maxError = 0f;
            for (int i = 0; i < 6; i++)
                if (b.ik.juntas[i].jointPosition.dofCount > 0)
                    maxError = Mathf.Max(maxError, Mathf.Abs(b.ik.juntas[i].jointPosition[0] * Mathf.Rad2Deg - objetivo[i]));
            if (maxError < 0.5f) break;
            yield return null;
        }
        for (int i = 0; i < 6; i++) b.ik.q[i] = objetivo[i];
        b.qInicio = (float[])objetivo.Clone();          // al terminar la operación vuelve a esta pose
        var m = b.ik.FKPivote(b.ik.q);
        b.objetivo = CinematicaBrazoUR.Pos(m);
        b.eje = b.ik.Eje(m);
        b.activo = true;
    }

    void DetenerTeleop(Brazo b)
    {
        if (b == null) return;
        b.teleop = false;
        b.moviendo = false;
        b.accionPrev = false;
    }

    /// <summary>
    /// Control libre: el brazo sigue al mando en la sala, sin instrumento dentro de un trocar. Mientras se
    /// mantiene el gatillo, la pinza del brazo se desplaza y se orienta igual que la mano (1:1).
    /// </summary>
    void IniciarTeleopLibre(Brazo b)
    {
        if (b == null) return;
        b.fulcroTr = null;
        b.puntaDeseada = b.objetivo;        // en modo libre se guía la base del tubo (el pivote de la IK)
        b.inicioLibre = b.objetivo;
        b.pendiente = Vector3.zero;
        b.moviendo = false;
        b.activo = true;
        b.teleop = true;
    }

    void TeleoperarLibre(Brazo b, ManoEntrada mano)
    {
        if (mano.mover)
        {
            if (b.moviendo)
            {
                b.pendiente += rotOrigenXR * (mano.pos - b.manoPrev) * escalaLibre;
                b.pendiente = Vector3.ClampMagnitude(b.pendiente, 0.25f);
                // La muñeca del brazo gira lo mismo que giró la mano
                Quaternion giro = rotOrigenXR * (mano.rot * Quaternion.Inverse(b.rotManoPrev)) * Quaternion.Inverse(rotOrigenXR);
                b.eje = (giro * b.eje).normalized;
            }
            b.manoPrev = mano.pos; b.rotManoPrev = mano.rot;
            b.moviendo = true;
        }
        else b.moviendo = false;

        Vector3 avance = Vector3.ClampMagnitude(b.pendiente, velocidadMaxLibre * Time.deltaTime);
        b.pendiente -= avance;
        Vector3 destino = b.puntaDeseada + avance;
        Vector3 desdeInicio = destino - b.inicioLibre;
        if (desdeInicio.magnitude > radioLibre) destino = b.inicioLibre + desdeInicio.normalized * radioLibre;
        // Fuera del alcance del brazo la IK ya no llega: el objetivo vuelve hacia donde está el brazo de verdad
        if (b.errorIK > 0.03f)
        {
            destino = Vector3.MoveTowards(destino, Pivote(b), velocidadMaxLibre * Time.deltaTime * 1.5f);
            b.pendiente = Vector3.zero;
        }
        b.puntaDeseada = destino;
        b.objetivo = destino;
    }

    void Teleoperar(Brazo b, ManoEntrada mano, Quaternion guinada, bool flancoPrimario)
    {
        if (b == null || !b.teleop) return;
        if (b.fulcroTr == null) { TeleoperarLibre(b, mano); GripLibre(b, mano); return; }
        b.gripPrev = mano.accion > 0.5f;

        // 1) Movimiento: mientras se mantiene el gatillo, la punta sigue a la mano (relativo, escalado)
        if (mano.mover)
        {
            if (b.moviendo)
            {
                Vector3 enVista = Quaternion.Inverse(guinada) * (mano.pos - b.manoPrev);
                b.pendiente += MarcoVistaManual * enVista * escalaMovimiento;
                b.pendiente = Vector3.ClampMagnitude(b.pendiente, 0.06f);
            }
            b.manoPrev = mano.pos;
            b.moviendo = true;
        }
        else b.moviendo = false;

        Vector3 avance = Vector3.ClampMagnitude(b.pendiente, velocidadMaxPunta * Time.deltaTime);
        b.pendiente -= avance;
        b.puntaDeseada += avance;

        // 2) Restricción del trocar: la punta entra por el fulcro, entre la profundidad mínima y el tope del cabezal
        Vector3 F = CentroFulcro(b.fulcroTr);
        float L = LargoHerramienta(b);
        float altura = ControladorFulcroLaparoscopio.AlturaCabezalSobreFulcro(b.fulcroTr);
        float profMax = L - (altura > 0f ? altura + 0.005f : 0.02f);
        Vector3 v = b.puntaDeseada - F;
        float prof = v.magnitude;
        Vector3 u = prof > 1e-4f ? v / prof : b.eje;
        u = Vector3.RotateTowards(b.eje, u, 60f * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;   // giro máximo 60°/s
        prof = Mathf.Clamp(prof, b.profMin, profMax);
        b.puntaDeseada = F + u * prof;
        b.objetivo = F + u * prof - u * L;
        b.eje = u;

        // 3) Acción del instrumento
        if (flancoPrimario && b.instrumento == grasper) b.trinquete = !b.trinquete;
        bool activa = mano.accion > 0.5f || (b.instrumento == grasper && b.trinquete);
        bool flanco = activa && !b.accionPrev;
        if (b.instrumento == grasper)
        {
            if (activa != b.accionPrev) Accion(grasper, activa);
        }
        else if (b.instrumento == hook)
        {
            if (activa) CauterizarManual(b);
        }
        else if (b.instrumento == clipadora)
        {
            if (flanco) IntentarClip(b);
        }
        else if (b.instrumento == tijera)
        {
            if (flanco && corteManual == null) corteManual = StartCoroutine(CorteAsistido(b, mano));
        }
        b.accionPrev = activa;
    }


    // ------------------------------------------------------------------
    // Agarre: con la pinza del brazo (GRIP en control libre) y con la mano (clon en la mano, real al brazo)
    // ------------------------------------------------------------------
    int PuertoDe(Brazo b) => b == izq ? 1 : 0;
    /// <summary>Brazo que usa cada instrumento en la operación: el grasper va en el izquierdo; el resto, en el derecho.</summary>
    Brazo BrazoPara(GameObject inst) => inst == grasper ? izq : der;
    bool EstaMontado(GameObject inst) => (der != null && der.instrumento == inst) || (izq != null && izq.instrumento == inst);
    bool Listo(Brazo b, GameObject inst) => b.instrumento == inst && b.dentro && b.teleop && b.fulcroTr != null && !b.ocupado;

    static bool EsManoIzquierda(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("left") || n.Contains("izq")) return true;
            if (n.Contains("right") || n.Contains("der")) return false;
        }
        return false;
    }

    static float DistanciaAObjeto(GameObject go, Vector3 punto)
    {
        float mejor = float.MaxValue;
        foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            if (r.enabled) mejor = Mathf.Min(mejor, Vector3.Distance(r.bounds.ClosestPoint(punto), punto));
        return mejor;
    }

    PuertoDemo PuertoDemoDelTrocar(GameObject go)
    {
        foreach (var pd in puertosDemo) if (pd.trocar != null && pd.trocar.gameObject == go) return pd;
        return null;
    }

    void SuscribirAgarres(bool suscribir)
    {
        foreach (var g in agarrablesSuscritos) if (g != null) g.selectEntered.RemoveListener(AlAgarrarConLaMano);
        agarrablesSuscritos.Clear();
        if (!suscribir) return;
        var lista = new List<GameObject> { grasper, hook, tijera, clipadora };
        foreach (var p in preparacion.puertos) if (p.trocar != null) lista.Add(p.trocar.gameObject);
        foreach (var go in lista)
        {
            var g = go != null ? go.GetComponent<XRGrabInteractable>() : null;
            if (g == null) continue;
            g.selectEntered.AddListener(AlAgarrarConLaMano);
            agarrablesSuscritos.Add(g);
        }
    }

    void AlAgarrarConLaMano(SelectEnterEventArgs args)
    {
        if (!ManualActiva || args.interactorObject is XRSocketInteractor) return;
        var comp = args.interactableObject as Component;
        if (comp == null) return;
        GameObject go = comp.gameObject;
        // Un trocar ya colocado se puede retirar con la mano de forma normal (paso de cierre)
        var pd = PuertoDemoDelTrocar(go);
        if (pd != null && pd.indice < colocadoAntes.Length && colocadoAntes[pd.indice]) return;
        if (enTransito.ContainsKey(go)) return;
        agarresPendientes.Add(new AgarreMano { objeto = go, mano = args.interactorObject.transform, izq = EsManoIzquierda(args.interactorObject.transform) });
    }

    void ProcesarAgarresDeMano(ManoEntrada md, ManoEntrada mi)
    {
        // Mientras el objeto real viaja al brazo, la mano no puede volver a cogerlo
        if (enTransito.Count > 0)
        {
            var caducados = new List<GameObject>();
            foreach (var kv in enTransito)
            {
                if (kv.Key == null || Time.time > kv.Value) { caducados.Add(kv.Key); continue; }
                SoltarDeCualquierMano(kv.Key);
            }
            foreach (var k in caducados) enTransito.Remove(k);
        }

        foreach (var a in agarresPendientes)
        {
            if (a.objeto == null || enTransito.ContainsKey(a.objeto)) continue;
            Brazo brazoDeLaMano = a.izq ? izq : der;
            brazoDeLaMano.ignorarGrip = true;                 // ese apretón fue para la mano, no para la pinza del brazo
            SoltarDeCualquierMano(a.objeto);
            var pd = PuertoDemoDelTrocar(a.objeto);
            if (pd != null)
            {
                Brazo b = brazoDeLaMano;
                if (b.ocupado || b.trocarEnPinza != null || b.instrumento != null) { Avisar("Ese brazo ya lleva algo: suéltalo primero con el GRIP."); continue; }
                CrearClonEnMano(a);
                enTransito[a.objeto] = Time.time + 2.5f;
                StartCoroutine(LlevarTrocarAlBrazo(b, pd));
            }
            else
            {
                Brazo b = BrazoPara(a.objeto);
                if (b.ocupado) { Avisar("Ese brazo está ocupado: espera a que termine."); continue; }
                if (b.instrumento == a.objeto) continue;
                CrearClonEnMano(a);
                enTransito[a.objeto] = Time.time + 2.5f;
                Avisar(b == izq ? "El instrumento va al brazo izquierdo." : "El instrumento va al brazo derecho.", 2f);
                StartCoroutine(MontarEInsertar(b, a.objeto));
            }
        }
        agarresPendientes.Clear();

        // El clon desaparece cuando esa mano suelta el grip
        for (int i = clonesEnMano.Count - 1; i >= 0; i--)
        {
            var c = clonesEnMano[i];
            float grip = (c.izq ? mi : md).accion;
            if (c.clon == null || (grip < 0.3f && Time.time - c.creado > 0.6f))
            {
                if (c.clon != null) Destroy(c.clon);
                clonesEnMano.RemoveAt(i);
            }
        }
    }

    static void SoltarDeCualquierMano(GameObject go)
    {
        var g = go.GetComponent<XRGrabInteractable>();
        if (g == null || !g.isSelected || g.interactionManager == null) return;
        if (g.firstInteractorSelecting is XRSocketInteractor) return;
        g.interactionManager.CancelInteractableSelection((IXRSelectInteractable)g);
    }

    /// <summary>Copia solo visual del objeto, que se queda en la mano mientras el real viaja al brazo.</summary>
    void CrearClonEnMano(AgarreMano a)
    {
        var clon = new GameObject("Clon_" + a.objeto.name);
        clon.transform.SetPositionAndRotation(a.objeto.transform.position, a.objeto.transform.rotation);
        clon.transform.localScale = a.objeto.transform.lossyScale;
        foreach (var mf in a.objeto.GetComponentsInChildren<MeshFilter>(false))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null) continue;
            GameObject destino = clon;
            if (mf.gameObject != a.objeto)
            {
                destino = new GameObject(mf.name);
                destino.transform.SetParent(clon.transform, false);
                destino.transform.localPosition = a.objeto.transform.InverseTransformPoint(mf.transform.position);
                destino.transform.localRotation = Quaternion.Inverse(a.objeto.transform.rotation) * mf.transform.rotation;
            }
            destino.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            destino.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
        }
        clon.transform.SetParent(a.mano != null ? a.mano : raizDemo, true);
        clonesEnMano.Add(new ClonMano { clon = clon, original = a.objeto, izq = a.izq, creado = Time.time });
    }

    IEnumerator LlevarTrocarAlBrazo(Brazo b, PuertoDemo pd)
    {
        b.ocupado = true;
        Transform esteTrocar = pd.trocar;
        foreach (var c in pd.solidos) if (c != null) c.enabled = false;
        Vector3 p0 = pd.trocar.position; Quaternion r0 = pd.trocar.rotation;
        const float vuelo = 0.9f;
        for (float t = 0; t < vuelo; t += Time.deltaTime)
        {
            if (pd.trocar != esteTrocar || preparacion.EstaColocado(pd.indice)) { b.ocupado = false; yield break; }
            PoseTrocarEnBrazo(b, pd, out var pf, out var rf);
            float u = Mathf.SmoothStep(0f, 1f, t / vuelo);
            PonerTrocar(pd, Vector3.Lerp(p0, pf, u), Quaternion.Slerp(r0, rf, u), cinematico: true);
            yield return null;
        }
        if (pd.trocar == esteTrocar && !preparacion.EstaColocado(pd.indice)) TomarTrocarConBrazo(b, pd);
        b.ocupado = false;
    }

    void TomarTrocarConBrazo(Brazo b, PuertoDemo pd)
    {
        SoltarDeCualquierMano(pd.trocar.gameObject);
        foreach (var c in pd.solidos) if (c != null) c.enabled = false;   // en la pinza no debe empujar al brazo
        if (pd.rb != null) pd.rb.isKinematic = true;
        pd.portador = b;                 // LateUpdate lo mantiene sujeto por el cabezal, coaxial con la herramienta
        b.trocarEnPinza = pd;
        if (b.openRST != null) b.openRST.Apertura(1.5f);
    }

    /// <summary>
    /// Encaje por proximidad: en cuanto un trocar (en tu mano, en la pinza de un brazo, o el clon que llevas en
    /// la mano) entra en el radio de un puerto libre, queda colocado en él. No hace falta acertar con precisión.
    /// </summary>
    void EncajarTrocaresCercanos()
    {
        if (puertosDemo.Count == 0 || puertosListos) return;
        foreach (var pd in puertosDemo)
        {
            if (pd.trocar == null || preparacion.EstaColocado(pd.indice)) continue;
            // Solo encaja un trocar que alguien lleva: en la pinza de un brazo teleoperado o cogido con la mano.
            // (Uno suelto, volviendo a la mesa o llevado por el asistente no debe encajarse solo.)
            bool enPinza = pd.portador != null && pd.portador.trocarEnPinza == pd;
            var agarrable = pd.trocar.GetComponent<XRGrabInteractable>();
            bool enMano = pd.portador == null && agarrable != null && agarrable.isSelected && !preparacion.EstaColocado(pd.indice)
                          && !(agarrable.firstInteractorSelecting is XRSocketInteractor);
            if (!enPinza && !enMano) continue;
            if (EncajarSiEstaCerca(pd, pd.trocar.TransformPoint(pd.fulcroLocal))) return;   // uno por frame: las asignaciones cambian
        }
        for (int i = clonesEnMano.Count - 1; i >= 0; i--)
        {
            var c = clonesEnMano[i];
            if (c.clon == null || c.original == null) continue;
            var pd = PuertoDemoDelTrocar(c.original);
            if (pd == null || preparacion.EstaColocado(pd.indice)) continue;
            if (!EncajarSiEstaCerca(pd, c.clon.transform.TransformPoint(pd.fulcroLocal))) continue;
            Destroy(c.clon);
            clonesEnMano.RemoveAt(i);
            return;
        }
    }

    /// <summary>Si 'fulcro' está dentro del radio de un puerto libre, coloca en él el trocar de 'pd'.</summary>
    bool EncajarSiEstaCerca(PuertoDemo pd, Vector3 fulcro)
    {
        PuertoDemo destino = null; float mejor = radioEncajePuerto;
        foreach (var k in puertosDemo)
        {
            if (preparacion.EstaColocado(k.indice)) continue;
            float d = Vector3.Distance(fulcro, EnDock(k, k.fulcroLocal));
            if (d < mejor) { mejor = d; destino = k; }
        }
        if (destino == null) return false;
        ColocarTrocarEnPuerto(pd, destino);
        return true;
    }

    void ColocarTrocarEnPuerto(PuertoDemo pd, PuertoDemo destino)
    {
        if (pd.portador != null && pd.portador.trocarEnPinza == pd) SoltarTrocarDelBrazo(pd.portador, permitirEncaje: false);
        SoltarDeCualquierMano(pd.trocar.gameObject);
        enTransito.Remove(pd.trocar.gameObject);
        foreach (var c in pd.solidos) if (c != null) c.enabled = true;
        if (destino != pd)
        {
            preparacion.IntercambiarTrocares(pd.indice, destino.indice);
            IntercambiarTrocares(pd.indice, destino.indice);    // ahora 'destino' tiene ese trocar
        }
        PonerTrocar(destino, destino.posDock, destino.rotDock, cinematico: destino.kin);
        preparacion.ColocarUno(destino.indice);
        Avisar("Trocar colocado en su puerto.", 2f);
    }

    /// <summary>La pinza suelta el trocar. Si está junto a un puerto libre, queda encajado en él.</summary>
    void SoltarTrocarDelBrazo(Brazo b, bool permitirEncaje = true)
    {
        var pd = b.trocarEnPinza;
        if (pd == null) return;
        b.trocarEnPinza = null;
        pd.portador = null;
        foreach (var c in pd.solidos) if (c != null) c.enabled = true;
        if (b.openRST != null) b.openRST.Apertura(9f);
        if (!permitirEncaje) return;

        Vector3 fulcroAhora = pd.trocar.TransformPoint(pd.fulcroLocal);
        PuertoDemo destino = null; float mejor = radioEncajePuerto * 1.5f;
        foreach (var k in puertosDemo)
        {
            if (preparacion.EstaColocado(k.indice)) continue;
            float d = Vector3.Distance(fulcroAhora, EnDock(k, k.fulcroLocal));
            if (d < mejor) { mejor = d; destino = k; }
        }
        if (destino == null) { Avisar("Trocar soltado. Para colocarlo, acércalo a una esfera verde del abdomen."); return; }
        ColocarTrocarEnPuerto(pd, destino);
    }

    /// <summary>GRIP en control libre: la pinza del brazo toma lo que tenga al alcance, o suelta lo que lleva.</summary>
    void GripLibre(Brazo b, ManoEntrada mano)
    {
        bool apretado = mano.accion > 0.5f;
        bool flanco = apretado && !b.gripPrev;
        b.gripPrev = apretado;
        if (!apretado) b.ignorarGrip = false;
        if (b.ocupado) return;

        // Acoplamiento: con los puertos listos, un instrumento montado entra solo al acercarlo al cabezal de su trocar
        if (puertosListos && b.instrumento != null && !b.dentro)
        {
            Transform fTr = preparacion.Fulcro(PuertoDe(b));
            if (fTr != null && preparacion.EstaColocado(PuertoDe(b)))
            {
                Vector3 cabezal = CentroFulcro(fTr) + fTr.right * ControladorFulcroLaparoscopio.AlturaCabezalSobreFulcro(fTr);
                if (Vector3.Distance(ExtremoBrazo(b), cabezal) < 0.08f) { StartCoroutine(MontarEInsertar(b, b.instrumento)); return; }
            }
        }

        if (!flanco || b.ignorarGrip) return;
        if (b.trocarEnPinza != null) { SoltarTrocarDelBrazo(b); return; }
        if (b.instrumento != null) { StartCoroutine(ManiobraDevolver(b)); return; }

        Vector3 pinza = ExtremoBrazo(b);
        const float alcance = 0.09f;
        PuertoDemo mejorTrocar = null; float dTrocar = alcance;
        foreach (var pd in puertosDemo)
        {
            if (pd.trocar == null || pd.portador != null || preparacion.EstaColocado(pd.indice)) continue;
            float d = DistanciaAObjeto(pd.trocar.gameObject, pinza);
            if (d < dTrocar) { dTrocar = d; mejorTrocar = pd; }
        }
        GameObject mejorInst = null; float dInst = alcance;
        foreach (var inst in new[] { grasper, hook, tijera, clipadora })
        {
            if (inst == null || EstaMontado(inst)) continue;
            float d = DistanciaAObjeto(inst, pinza);
            if (d < dInst) { dInst = d; mejorInst = inst; }
        }
        if (mejorTrocar != null && dTrocar <= dInst) { TomarTrocarConBrazo(b, mejorTrocar); Avisar("Trocar en la pinza: llévalo a una esfera verde y aprieta el GRIP para soltarlo.", 3f); }
        else if (mejorInst != null)
        {
            if (BrazoPara(mejorInst) != b) Avisar(mejorInst == grasper ? "El grasper va en el brazo izquierdo." : "Ese instrumento va en el brazo derecho.");
            else StartCoroutine(MontarEInsertar(b, mejorInst));
        }
        else Avisar("No hay nada al alcance de la pinza (9 cm).", 2f);
    }

    /// <summary>
    /// El brazo deja lo que lleve, monta el instrumento pedido y, si los puertos ya están colocados, lo introduce
    /// por su trocar y pasa a control por el trocar. Sirve para montar y para cambiar de instrumento.
    /// </summary>
    IEnumerator MontarEInsertar(Brazo b, GameObject inst)
    {
        if (b.ocupado || inst == null) yield break;
        b.ocupado = true;
        DetenerTeleop(b);
        int puerto = PuertoDe(b);
        if (b.trocarEnPinza != null) SoltarTrocarDelBrazo(b, permitirEncaje: false);
        if (b.instrumento != inst)
        {
            if (b.instrumento != null)
            {
                if (b.instrumento == grasper) { b.trinquete = false; Accion(grasper, false); }
                if (b.dentro) yield return Retirar(b, puerto);
                b.dentro = false;
                yield return Devolver(b);
            }
            SoltarDeCualquierMano(inst);
            yield return Entregar(b, inst);
        }
        if (puertosListos && preparacion.EstaColocado(puerto))
        {
            if (!b.dentro) yield return Insertar(b, puerto, 0.06f);
            b.dentro = true;
            IniciarTeleop(b, preparacion.Fulcro(puerto));
        }
        else IniciarTeleopLibre(b);
        b.gripPrev = true;            // el grip que se esté apretando ahora no cuenta como una pulsación nueva
        b.ocupado = false;
    }

    /// <summary>El brazo devuelve su instrumento a la mesa y queda en control libre.</summary>
    IEnumerator ManiobraDevolver(Brazo b)
    {
        if (b.ocupado) yield break;
        b.ocupado = true;
        DetenerTeleop(b);
        if (b.instrumento == grasper) { b.trinquete = false; Accion(grasper, false); }
        if (b.dentro) yield return Retirar(b, PuertoDe(b));
        b.dentro = false;
        yield return Devolver(b);
        IniciarTeleopLibre(b);
        b.gripPrev = true;
        b.ocupado = false;
    }

    /// <summary>El asistente coloca un puerto con este brazo (incisión e inserción), dejando antes lo que lleve.</summary>
    IEnumerator ManiobraPuerto(Brazo b, int indice)
    {
        if (b.ocupado) yield break;
        b.ocupado = true;
        DetenerTeleop(b);
        if (b.trocarEnPinza != null) SoltarTrocarDelBrazo(b, permitirEncaje: false);
        if (b.instrumento != null) { if (b.dentro) yield return Retirar(b, PuertoDe(b)); b.dentro = false; yield return Devolver(b); }
        var pd = PuertoDemoDe(indice);
        if (pd != null && pd.portador != null) SoltarTrocarDelBrazo(pd.portador, permitirEncaje: false);   // lo llevaba el otro brazo
        preparacion.SoltarDeLaMano(indice);
        yield return null;
        if (pd != null && pd.rb != null) pd.rb.isKinematic = true;
        yield return ColocarPuerto(b, indice);
        IniciarTeleopLibre(b);
        b.gripPrev = true;
        b.ocupado = false;
    }

    /// <summary>
    /// Espera a que cada brazo tenga dentro el instrumento indicado. Se consigue cogiéndolo con la mano o con
    /// la pinza del brazo; B / Y hace que el asistente monte lo que falte.
    /// </summary>
    IEnumerator EsperarInstrumentos(GameObject paraIzquierdo, GameObject paraDerecho)
    {
        confirmacionPendiente = false;
        while (!(Listo(izq, paraIzquierdo) && Listo(der, paraDerecho)))
        {
            if (TomarConfirmacion())
            {
                if (!Listo(izq, paraIzquierdo) && !izq.ocupado) StartCoroutine(MontarEInsertar(izq, paraIzquierdo));
                if (!Listo(der, paraDerecho) && !der.ocupado) StartCoroutine(MontarEInsertar(der, paraDerecho));
            }
            yield return null;
        }
    }

    /// <summary>La clipadora la fabrica la demo: se le añade lo necesario para poder cogerla con la mano.</summary>
    void HacerAgarrable(GameObject go)
    {
        if (go == null || go.GetComponent<XRGrabInteractable>() != null) return;
        var renderers = go.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        var caja = go.AddComponent<BoxCollider>();
        caja.center = go.transform.InverseTransformPoint(b.center);
        Vector3 t = go.transform.InverseTransformVector(b.size);
        caja.size = new Vector3(Mathf.Abs(t.x), Mathf.Abs(t.y), Mathf.Abs(t.z));
        caja.isTrigger = true;                      // solo para que la mano la detecte; no empuja nada
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false;
        var g = go.AddComponent<XRGrabInteractable>();
        g.throwOnDetach = false;
    }

    void Avisar(string texto, float segundos = 3f)
    {
        avisoManual = texto;
        avisoHasta = Time.time + segundos;
        Debug.Log("🩺 Operación manual: " + texto);
    }

    // ------------------------------------------------------------------
    // Gestos
    // ------------------------------------------------------------------
    void CauterizarManual(Brazo b)
    {
        tiempoCauterio -= Time.deltaTime;
        if (tiempoCauterio <= 0f) { Cauterizar(hook, 0.15f); tiempoCauterio = 0.12f; }
        Vector3 punta = ExtremoBrazo(b);
        ConsumirCerca(grasa, punta, 0.011f);
        ConsumirCerca(puntosLecho, punta, 0.013f);
    }

    /// <summary>Lo que está a menos de <paramref name="radio"/> de la punta se encoge y desaparece al cauterizar.</summary>
    static void ConsumirCerca(List<Transform> lista, Vector3 punta, float radio)
    {
        for (int i = lista.Count - 1; i >= 0; i--)
        {
            Transform t = lista[i];
            if (t == null) { lista.RemoveAt(i); continue; }
            if (Vector3.Distance(t.position, punta) > radio) continue;
            t.localScale *= Mathf.Max(0f, 1f - 3f * Time.deltaTime);
            if (t.localScale.x < 0.0025f) { Destroy(t.gameObject); lista.RemoveAt(i); }
        }
    }

    /// <summary>Distancia a lo largo del conducto del punto más cercano a <paramref name="p"/>; devuelve la separación.</summary>
    float DuctoMasCercano(Vector3 p, out float sMejor)
    {
        float mejor = float.MaxValue; sMejor = 0f;
        for (float s = 0.020f; s <= 0.0421f; s += 0.0005f)
        {
            float d = Vector3.Distance(PuntoDucto(s), p);
            if (d < mejor) { mejor = d; sMejor = s; }
        }
        return mejor;
    }

    void IntentarClip(Brazo b)
    {
        float d = DuctoMasCercano(ExtremoBrazo(b), out float s);
        if (d > 0.010f) { Avisar("Acerca más el aplicador al conducto cístico para poner el clip."); return; }
        if (s > 0.0293f && s < 0.0317f) { errores++; Avisar("Ahí irá el corte: pon los clips a los lados de la marca roja."); return; }
        foreach (float otro in clipsPuestos) if (Mathf.Abs(otro - s) < 0.0018f) { Avisar("Ya hay un clip ahí; separa un poco el siguiente."); return; }
        Transform clip = CrearClip(s);
        clipsPuestos.Add(s);
        if (s < S_CORTE_MANUAL) { if (clipVesicula == null) clipVesicula = clip; else clip.SetParent(clipVesicula, true); }
        else clipsPaciente.Add(clip);
        CargarClipEnClipadora(false);
        Avisar(s < S_CORTE_MANUAL ? "Clip del lado de la vesícula colocado." : $"Clip del lado del paciente colocado ({clipsPaciente.Count} de 2).", 2f);
    }

    bool ClipsCompletos => clipVesicula != null && clipsPaciente.Count >= 2;

    /// <summary>
    /// Corte asistido: mientras se mantiene el grip con la tijera junto a la marca de corte, el rayo de corte
    /// SOFA barre la sección del conducto (el carving solo actúa donde el rayo golpea la superficie).
    /// Lejos de la marca la tijera no corta, para no dañar otros órganos.
    /// </summary>
    IEnumerator CorteAsistido(Brazo b, ManoEntrada mano)
    {
        if (!ClipsCompletos) { errores++; Avisar("No se corta antes de clipar: faltan clips en el conducto."); corteManual = null; yield break; }
        var puntaT = Punta(tijera);
        Vector3 pCorte = PuntoDucto(S_CORTE_MANUAL);
        Vector3 origen = ExtremoBrazo(b);
        float dist = Vector3.Distance(origen, pCorte);
        if (dist > 0.022f) { errores++; Avisar("La tijera está lejos del conducto: llévala a la esfera azul, junto a la marca roja."); corteManual = null; yield break; }
        if (guiaTijera != null && Vector3.Distance(origen, guiaTijera.position) > 0.007f)
        { Avisar("Coloca la punta de la tijera en la esfera azul: desde ahí el corte atraviesa el conducto de lado a lado."); corteManual = null; yield break; }
        Vector3 dir = (pCorte - origen).normalized;
        Vector3 tang = (PuntoDucto(S_CORTE_MANUAL + 0.003f) - PuntoDucto(S_CORTE_MANUAL - 0.003f)).normalized;
        Vector3 vert = Vector3.Cross(tang, dir).normalized;
        var objetivoGO = new GameObject("ObjetivoCorteManual");
        objetivoGO.transform.SetParent(raizDemo, true);
        if (puntaT != null) puntaT.apuntarHacia = objetivoGO.transform;
        Accion(tijera, true);
        bool seguir = true;
        while (seguir && !VesiculaSeccionada())
        {
            // El objetivo recorre la propia sección del conducto (no un punto lejano): el rayo debe barrer
            // de verdad la superficie para que SOFA talle; con un abanico estrecho no corta.
            for (float h = 0.0045f; h >= -0.00455f && seguir; h -= 0.0015f)
                for (float a = -0.002f; a <= 0.00205f && seguir; a += 0.001f)
                {
                    objetivoGO.transform.position = pCorte + vert * h + tang * a;
                    yield return new WaitForSeconds(0.15f);
                    seguir = mano.accion > 0.5f && b.teleop && b.instrumento == tijera && !VesiculaSeccionada();
                }
        }
        Accion(tijera, false);
        if (puntaT != null) puntaT.apuntarHacia = null;
        Destroy(objetivoGO);
        corteManual = null;
    }

    // ------------------------------------------------------------------
    // Pantalla del laparoscopio, marcadores y manos
    // ------------------------------------------------------------------
    void Instruir(string titulo, string texto)
    {
        Fase = titulo;
        Debug.Log($"🩺 Operación manual: {titulo}");
        tituloManual = titulo;
        textoManual = EnvolverTexto(texto, 62);
    }

    static string EnvolverTexto(string texto, int ancho)
    {
        var sb = new System.Text.StringBuilder(); int col = 0;
        foreach (string palabra in texto.Split(' '))
        {
            if (col + palabra.Length > ancho) { sb.Append('\n'); col = 0; }
            sb.Append(palabra).Append(' '); col += palabra.Length + 1;
        }
        return sb.ToString();
    }

    /// <summary>Pantalla flotante frente al usuario con la imagen del laparoscopio y las instrucciones del paso.</summary>
    /// <summary>Cámara de la cabeza del usuario (la del XR Origin), aunque no sea la cámara principal en ese momento.</summary>
    Transform CabezaUsuario()
    {
        var origen = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origen != null && origen.Camera != null) return origen.Camera.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }

    /// <summary>
    /// Coloca el panel de guía en primera persona. Por defecto es un monitor en la cabecera de la camilla, por
    /// encima del paciente: se lee desde los pies y no tapa el campo de trabajo. Con el clic del stick derecho
    /// (o la tecla P) se alterna con tenerlo delante, a un metro.
    /// </summary>
    void ReubicarPantalla()
    {
        if (pantalla == null || VistaLaparoscopioActiva) return;
        pantalla.transform.SetParent(null, true);
        if (!panelFrente)
        {
            Vector3 abdomen = Vector3.zero; int n = 0;
            foreach (var p in preparacion.puertos) if (p.socket != null) { abdomen += p.socket.transform.position; n++; }
            abdomen = n > 0 ? abdomen / n : new Vector3(0.13f, 0.92f, 0.17f);
            // El paciente tiene la cabeza hacia +Z: el monitor queda más allá de la cabeza, mirando hacia los pies
            pantalla.transform.SetPositionAndRotation(abdomen + Vector3.up * alturaMonitor + Vector3.forward * distanciaMonitor,
                                                      Quaternion.LookRotation(Vector3.forward, Vector3.up));
            pantalla.transform.localScale = Vector3.one * escalaMonitor;
            return;
        }
        pantalla.transform.localScale = Vector3.one;
        Transform cabeza = CabezaUsuario();
        if (cabeza == null) { pantalla.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -0.6f), Quaternion.identity); return; }
        Vector3 f = cabeza.forward; f.y = 0f;
        f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        pantalla.transform.SetPositionAndRotation(cabeza.position + f * 0.95f + Vector3.down * 0.05f, Quaternion.LookRotation(f, Vector3.up));
    }

    void CrearPantalla()
    {
        pantalla = new GameObject("PantallaLaparoscopio");
        ReubicarPantalla();
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Imagen";
        quad.transform.SetParent(pantalla.transform, false);
        quad.transform.localScale = new Vector3(0.72f, 0.405f, 1f);
        matPrincipal = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = matPrincipal;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var fondoEtiqueta = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(fondoEtiqueta.GetComponent<Collider>());
        fondoEtiqueta.name = "FondoEtiqueta";
        fondoEtiqueta.transform.SetParent(pantalla.transform, false);
        fondoEtiqueta.transform.localPosition = new Vector3(-0.160f, 0.186f, -0.003f);
        fondoEtiqueta.transform.localScale = new Vector3(0.40f, 0.03f, 1f);
        var rfe = fondoEtiqueta.GetComponent<MeshRenderer>();
        rfe.sharedMaterial = MaterialSinLuz(new Color(0.03f, 0.05f, 0.08f, 0.85f));
        rfe.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var etiqueta = new GameObject("EtiquetaVista");
        etiqueta.transform.SetParent(pantalla.transform, false);
        etiqueta.transform.localPosition = new Vector3(-0.352f, 0.197f, -0.004f);
        etiquetaVista = etiqueta.AddComponent<TextMesh>();
        etiquetaVista.anchor = TextAnchor.UpperLeft;
        etiquetaVista.fontSize = 64;
        etiquetaVista.characterSize = 0.0038f;
        etiquetaVista.color = new Color(0.55f, 1f, 0.6f);

        camPantalla = laparoscopio != null ? laparoscopio.GetComponentInChildren<Camera>(true) : null;
        if (camPantalla != null)
        {
            rtOriginalLap = camPantalla.targetTexture;
            rtPantalla = new RenderTexture(1920, 1080, 24) { name = "PantallaManual" };
            matPrincipal.SetTexture("_BaseMap", rtPantalla);
            MantenerCamaraDePantalla();
        }
        VistaLaparoscopioActiva = false;   // se empieza en primera persona
        AplicarVista();

        var fondo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fondoTexto = fondo;
        Destroy(fondo.GetComponent<Collider>());
        fondo.name = "FondoTexto";
        fondo.transform.SetParent(pantalla.transform, false);
        fondo.transform.localPosition = new Vector3(0f, -0.305f, 0.002f);
        fondo.transform.localScale = new Vector3(0.72f, 0.19f, 1f);
        var rf = fondo.GetComponent<MeshRenderer>();
        rf.sharedMaterial = MaterialSinLuz(new Color(0.03f, 0.05f, 0.08f, 0.82f));
        rf.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var texto = new GameObject("Instrucciones");
        texto.transform.SetParent(pantalla.transform, false);
        texto.transform.localPosition = new Vector3(0f, -0.218f, -0.002f);
        textoPantalla = texto.AddComponent<TextMesh>();
        textoPantalla.anchor = TextAnchor.UpperCenter;
        textoPantalla.alignment = TextAlignment.Center;
        textoPantalla.fontSize = 64;
        textoPantalla.characterSize = 0.0042f;
        textoPantalla.color = new Color(1f, 0.97f, 0.75f);
    }

    /// <summary>
    /// La cámara del laparoscopio dibuja siempre en la pantalla flotante y la cámara de la sala sigue encendida.
    /// (ControladorVisorLaparoscopio, al entrar la óptica en el abdomen, manda la cámara del laparoscopio a
    /// pantalla completa y apaga la de la sala: en esta modalidad el usuario necesita ver las dos cosas.)
    /// </summary>
    void MantenerCamaraDePantalla()
    {
        if (camPantalla == null || rtPantalla == null) return;
        if (camPantalla.targetTexture != rtPantalla) camPantalla.targetTexture = rtPantalla;
        if (!camPantalla.enabled) camPantalla.enabled = true;
        foreach (var visor in laparoscopio.GetComponentsInChildren<ControladorVisorLaparoscopio>(true))
            if (visor.cameraGeneralSala != null && !visor.cameraGeneralSala.enabled) visor.cameraGeneralSala.enabled = true;
    }

    /// <summary>Cambia entre la vista en primera persona y la vista del laparoscopio (botón A o tecla V).</summary>
    public void AlternarVista()
    {
        VistaLaparoscopioActiva = !VistaLaparoscopioActiva;
        AplicarVista();
    }

    void AplicarVista()
    {
        if (pantalla == null) return;
        Transform cabeza = CabezaUsuario();
        if (VistaLaparoscopioActiva && cabeza != null)
        {
            // La imagen viaja con la cabeza y llena el campo de visión: es "mirar por el laparoscopio"
            pantalla.transform.SetParent(cabeza, false);
            pantalla.transform.localPosition = new Vector3(0f, 0.03f, distanciaVistaLaparoscopio);
            pantalla.transform.localRotation = Quaternion.identity;
            pantalla.transform.localScale = Vector3.one * (anchoVistaLaparoscopio / 0.72f);
        }
        else
        {
            // Primera persona: el panel se queda fijo en la sala, frente a donde estás mirando ahora
            VistaLaparoscopioActiva = false;
            ReubicarPantalla();
        }
        if (fondoTexto != null) fondoTexto.SetActive(!VistaLaparoscopioActiva);   // mirando por el laparoscopio, sin cuadro de guía
        if (etiquetaVista != null)
            etiquetaVista.text = VistaLaparoscopioActiva ? "VISTA LAPAROSCOPIO  (A: primera persona)" : "PRIMERA PERSONA  (A: vista laparoscopio)";
    }

    void DestruirPantalla()
    {
        matPrincipal = null; etiquetaVista = null;
        if (camPantalla != null && camPantalla.targetTexture == rtPantalla) camPantalla.targetTexture = rtOriginalLap;
        camPantalla = null; rtOriginalLap = null;
        if (rtPantalla != null) { rtPantalla.Release(); Destroy(rtPantalla); rtPantalla = null; }
        if (pantalla != null) Destroy(pantalla);
        pantalla = null; textoPantalla = null;
    }

    /// <summary>
    /// Apaga los interactores de los mandos (rayo y agarre directo) mientras se teleopera, para que el grip
    /// no agarre por accidente un instrumento o un trocar de la escena.
    /// </summary>
    void ApagarInteractoresDeMano(bool apagar)
    {
        if (apagar)
        {
            foreach (var it in FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None))
                if (it.enabled) { it.enabled = false; interactoresApagados.Add(it); }
        }
        else
        {
            // Al cerrar la aplicación no se reactivan: sus objetos ya se están destruyendo
            if (!cerrando) foreach (var it in interactoresApagados) if (it != null) it.enabled = true;
            interactoresApagados.Clear();
        }
    }

    Transform Marcador(Vector3 p, Color color, float radio, string nombre)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = nombre;
        go.transform.SetParent(raizDemo, true);
        go.transform.position = p;
        go.transform.localScale = Vector3.one * radio * 2f;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = MaterialSinLuz(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    /// <summary>Material plano (no le afecta la luz del laparoscopio, que quemaría el color); translúcido si alfa menor que 1.</summary>
    static Material MaterialSinLuz(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", color);
        if (color.a < 0.99f)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return m;
    }

    void FinManual()
    {
        DetenerTeleop(der); DetenerTeleop(izq);
        if (corteManual != null) { StopCoroutine(corteManual); corteManual = null; }
        var pt = Punta(tijera); if (pt != null) pt.apuntarHacia = null;
        ApagarInteractoresDeMano(false);
        SuscribirAgarres(false);
        foreach (var c in clonesEnMano) if (c.clon != null) Destroy(c.clon);
        clonesEnMano.Clear(); agarresPendientes.Clear(); enTransito.Clear();
        puertosListos = false;
        DestruirPantalla();
        grasa.Clear(); puntosLecho.Clear(); clipsPuestos.Clear();
        ManualActiva = false;
    }

    // ------------------------------------------------------------------
    // Secuencia
    // ------------------------------------------------------------------
    IEnumerator SecuenciaManual()
    {
        Ejecutando = true;
        ManualActiva = true;
        errores = 0; tiempos.Clear(); InformeManual = "";
        confirmacionPendiente = false;
        Preparar();
        if (narrador != null) narrador.DetenerDemo();   // las instrucciones van en la pantalla flotante, no en el panel de la demo
        var origenXR = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        rotOrigenXR = origenXR != null ? origenXR.transform.rotation : Quaternion.identity;
        CrearPantalla();
        if (usarPoseInicial)
        {
            Instruir("Preparación", "Los brazos van a su posición inicial de trabajo...");
            var pd0 = StartCoroutine(IrAPoseInicial(der, poseInicialDerecha));
            var pi0 = StartCoroutine(IrAPoseInicial(izq, poseInicialIzquierda));
            yield return pd0; yield return pi0;
        }
        // Desde aquí cada brazo sigue a su mando (control libre, sin WSL)
        IniciarTeleopLibre(der);
        IniciarTeleopLibre(izq);
        float tInicio = Time.time, tPaso = Time.time;
        System.Action<string> cerrarPaso = nombre => { tiempos.Add($"{nombre}: {Time.time - tPaso:F0} s"); tPaso = Time.time; };

        Instruir("Preparación", "Los brazos ya siguen tus mandos: GATILLO mantenido = el brazo sigue a tu mano; GRIP = agarrar. Los trocares y el instrumental están en la mesa.");
        yield return PrepararPuertos();
        CrearClipadora();
        HacerAgarrable(clipadora);
        CrearBandeja();
        foreach (var pd in puertosDemo) if (pd.tq != null) pd.tq.obturadorAutomatico = true;   // al encajar el trocar, el obturador sale solo
        puertosListos = false;
        SuscribirAgarres(true);

        // ---------- 0. Puertos ----------
        Instruir("0 · Puertos", "Coloca los 3 trocares en las esferas verdes del abdomen, en el orden que quieras: coge un trocar con la mano (GRIP) y acércalo a una esfera verde; al entrar en ella queda encajado. También puedes llevarlo con la pinza del brazo (GATILLO), o pulsar B / Y para que el brazo coloque el siguiente.");
        var guiasPuerto = new Dictionary<int, Transform>();
        foreach (var pd in puertosDemo)
        {
            Vector3 dPuerto = -(pd.rotDock * pd.ejeLocal);
            guiasPuerto[pd.indice] = Marcador(EnDock(pd, pd.fulcroLocal) - dPuerto * 0.012f, new Color(0.2f, 1f, 0.3f, 0.30f), radioEncajePuerto * 0.55f, "GuiaPuerto");
        }
        if (puertosDemo.Count < preparacion.puertos.Count) preparacion.Colocar();   // sin datos de algún puerto: se colocan directamente
        bool opticaDentro = false;
        confirmacionPendiente = false;
        while (!preparacion.TodosColocados() || !opticaDentro)
        {
            for (int i = 0; i < preparacion.puertos.Count; i++)
            {
                if (!preparacion.EstaColocado(i)) continue;
                if (guiasPuerto.TryGetValue(i, out var guia)) { if (guia != null) Destroy(guia.gameObject); guiasPuerto.Remove(i); }
                var pd = PuertoDemoDe(i);
                if (pd != null && !marcasIncision.ContainsKey(i))
                {
                    Vector3 dPuerto = -(pd.rotDock * pd.ejeLocal);
                    Transform marca = CrearMarcaIncision(EnDock(pd, pd.fulcroLocal) - dPuerto * 0.01f, dPuerto);
                    marcasIncision[i] = marca;
                    StartCoroutine(CrecerMarca(marca, 0.6f));
                }
            }
            if (!opticaDentro && preparacion.EstaColocado(2))
            {
                yield return IntroducirLaparoscopio();
                if (camaraLap != null && zonaCistica != null && zonaFondo != null)
                {
                    // Campo más abierto que en la demo: tienen que verse a la vez el fondo (tracción) y el conducto
                    camaraLap.transform.LookAt(Vector3.Lerp(zonaCistica.position, zonaFondo.position, 0.4f));
                    camaraLap.fieldOfView = 80f;
                }
                opticaDentro = true;
                Avisar("Óptica dentro. Pulsa A para mirar por el laparoscopio; A otra vez para volver a primera persona.", 6f);
            }
            if (TomarConfirmacion())
            {
                int siguiente = -1;
                foreach (int i in new[] { 2, 1, 0 }) if (!preparacion.EstaColocado(i)) { siguiente = i; break; }
                Brazo quien = siguiente == 1 ? izq : der;
                if (siguiente >= 0 && !quien.ocupado) yield return ManiobraPuerto(quien, siguiente);
            }
            yield return null;
        }
        foreach (var g in guiasPuerto.Values) if (g != null) Destroy(g.gameObject);
        puertosListos = true;
        cerrarPaso("Puertos");

        var fD = preparacion.Fulcro(0);
        var fI = preparacion.Fulcro(1);

        // ---------- 1. Instrumentos ----------
        Instruir("1 · Instrumentos", "Coge de la mesa con la mano (GRIP) el grasper y el disector: en tu mano queda un clon y el instrumento real va a su brazo y entra por su trocar (grasper al brazo izquierdo, disector al derecho). O pulsa B / Y y el asistente los monta.");
        yield return EsperarInstrumentos(grasper, hook);
        cerrarPaso("Instrumentos");

        // ---------- 2. Tracción ----------
        Instruir("2 · Tracción del fondo vesicular", "MANO IZQUIERDA: mantén el GATILLO y mueve la mano para llevar el grasper al fondo de la vesícula (esfera amarilla pequeña). Aprieta el GRIP para agarrar, tira hacia arriba y pulsa X para dejar fijo el agarre.");
        Transform guiaFondo = Marcador(zonaFondo.position, new Color(1f, 0.9f, 0.1f, 0.45f), 0.006f, "GuiaFondo");
        Vector3[] reposo = VerticesVesicula();
        float sostenido = 0f;
        while (sostenido < 0.6f)
        {
            // Válida cuando el agarre está fijado (trinquete) y la vesícula está traccionada de verdad
            bool ok = izq.trinquete && DeformacionMaxima(reposo) > 0.015f;
            sostenido = ok ? sostenido + 0.2f : 0f;
            yield return new WaitForSeconds(0.2f);
        }
        if (guiaFondo != null) Destroy(guiaFondo.gameObject);
        cerrarPaso("Tracción");

        // ---------- 3. Disección y visión crítica ----------
        if (!CalcularDucto()) Debug.LogWarning("🩺 Operación manual: no se pudo medir el conducto cístico en la malla SOFA.");
        CrearGrasaSobreElDucto();
        Instruir("3 · Disección del triángulo de Calot", "MANO DERECHA: lleva el disector a las bolitas amarillas de grasa que cubren el conducto y mantén el GRIP para cauterizarlas, hasta dejarlo limpio.");
        while (grasa.Count > 0) yield return null;
        Instruir("Visión crítica de seguridad", "Triángulo despejado, tercio inferior de la vesícula separado y solo el conducto cístico entrando en la vesícula. Ahora toca el aplicador de clips: cógelo de la mesa con la mano, o pulsa B / Y.");
        cerrarPaso("Disección");
        yield return EsperarInstrumentos(grasper, clipadora);

        // ---------- 4. Clipado ----------
        var guias = new List<Transform>
        {
            Marcador(PuntoDucto(0.0265f), new Color(0.2f, 1f, 0.3f, 0.5f), 0.0035f, "GuiaClip"),
            Marcador(PuntoDucto(0.0345f), new Color(0.2f, 1f, 0.3f, 0.5f), 0.0035f, "GuiaClip"),
            Marcador(PuntoDucto(0.0380f), new Color(0.2f, 1f, 0.3f, 0.5f), 0.0035f, "GuiaClip"),
            Marcador(PuntoDucto(S_CORTE_MANUAL), new Color(1f, 0.15f, 0.1f, 0.6f), 0.003f, "GuiaCorte")
        };
        Instruir("4 · Clipado del conducto cístico", "MANO DERECHA: lleva el aplicador a cada esfera verde y aprieta el GRIP para poner un clip: uno del lado de la vesícula y dos del lado del paciente. La esfera roja es donde irá el corte.");
        while (!ClipsCompletos) yield return null;
        for (int g = 0; g < 3; g++) if (guias[g] != null) Destroy(guias[g].gameObject);
        cerrarPaso("Clipado");
        Instruir("4 · Clipado del conducto cístico", "Clips colocados. Ahora toca la tijera: cógela de la mesa con la mano, o pulsa B / Y.");
        yield return EsperarInstrumentos(grasper, tijera);

        // ---------- 5. Sección ----------
        sCorte = S_CORTE_MANUAL;
        {
            // Misma geometría que usa la demo: la tijera se pone a un lado del conducto, del lado del cuerpo
            // de la vesícula, para que el rayo de corte lo cruce y salga hacia el lado libre.
            Vector3 pC = PuntoDucto(S_CORTE_MANUAL);
            Vector3 tangC = (PuntoDucto(S_CORTE_MANUAL + 0.003f) - PuntoDucto(S_CORTE_MANUAL - 0.003f)).normalized;
            Vector3 latC = Vector3.Cross(tangC, Vector3.up);
            if (latC.sqrMagnitude < 0.25f) latC = Vector3.Cross(tangC, Vector3.right);
            latC.Normalize();
            if (Vector3.Dot(latC, (ductoOrigen - pC).normalized) > 0f) latC = -latC;
            guiaTijera = Marcador(pC - latC * 0.009f, new Color(0.2f, 0.5f, 1f, 0.7f), 0.003f, "GuiaTijera");
        }
        Instruir("5 · Sección del conducto", "MANO DERECHA: lleva la punta de la tijera a la esfera AZUL (al lado del conducto, entre los clips) y mantén el GRIP hasta que el conducto quede cortado por la marca roja.");
        while (!VesiculaSeccionada()) yield return null;
        if (guias[3] != null) Destroy(guias[3].gameObject);
        if (guiaTijera != null) Destroy(guiaTijera.gameObject);
        while (corteManual != null) yield return null;
        cerrarPaso("Sección");
        Instruir("5 · Sección del conducto", "Conducto seccionado: el muñón queda cerrado con sus clips. Ahora toca el disector: cógelo de la mesa con la mano, o pulsa B / Y.");
        yield return EsperarInstrumentos(grasper, hook);

        // ---------- 6. Lecho hepático ----------
        Vector3 centroVes = (zonaCalot.position + zonaFondo.position) * 0.5f;
        Vector3 haciaLecho = zonaLecho.position - centroVes;
        for (int k = 0; k <= 5; k++)
        {
            Vector3 p = Vector3.Lerp(zonaCalot.position, zonaFondo.position, k / 5f) + haciaLecho;
            puntosLecho.Add(Marcador(Acercamiento(fD, p, 0.004f), new Color(1f, 0.55f, 0.1f, 0.6f), 0.005f, "PuntoLecho"));
        }
        Instruir("6 · Disección del lecho hepático", "MANO DERECHA: recorre con el disector las esferas naranjas, del cuello al fondo, manteniendo el GRIP para cauterizar cada una. Así se separa la vesícula del hígado.");
        while (puntosLecho.Count > 0) yield return null;
        cerrarPaso("Lecho hepático");
        while (der.ocupado) yield return null;
        yield return ManiobraDevolver(der);

        // ---------- 7. Extracción ----------
        Instruir("7 · Extracción de la vesícula", "La vesícula está libre. Pulsa B / Y para introducirla en la bolsa de extracción.");
        yield return EsperarConfirmacion();
        izq.trinquete = false;
        Accion(grasper, false);
        SepararVesicula();
        CrearBolsaExtraccion();
        izq.profMin = 0.002f;
        Instruir("7 · Extracción de la vesícula", "MANO IZQUIERDA: mantén el GATILLO y tira del grasper hacia fuera, hacia su trocar, para sacar la bolsa con la vesícula.");
        while (Vector3.Distance(izq.puntaDeseada, CentroFulcro(fI)) > 0.02f || izq.ocupado) yield return null;
        izq.ocupado = true;
        DetenerTeleop(izq);
        yield return Retirar(izq, 1);
        izq.dentro = false;
        yield return DepositarEnBandeja();
        yield return Devolver(izq);
        IniciarTeleopLibre(izq);
        izq.ocupado = false;
        cerrarPaso("Extracción");

        // ---------- 8. Revisión ----------
        Instruir("8 · Revisión", "Comprueba en la imagen que el muñón del conducto queda cerrado con sus clips, sin sangrado. Pulsa B / Y para terminar.");
        yield return EsperarConfirmacion();

        // ---------- 9. Cierre ----------
        ApagarInteractoresDeMano(false);
        Instruir("9 · Retirada de la óptica y de los trocares", "Retira los trocares con la mano y déjalos en la mesa, o pulsa B / Y para que se retiren solos. Las incisiones se cierran.");
        confirmacionPendiente = false;
        while (!TomarConfirmacion() && (preparacion.EstaColocado(0) || preparacion.EstaColocado(1) || preparacion.EstaColocado(2))) yield return null;
        yield return RetirarLaparoscopio();
        yield return RetirarTrocar(0);
        yield return RetirarTrocar(1);
        yield return RetirarTrocar(2);
        cerrarPaso("Cierre");
        while (der.ocupado || izq.ocupado) yield return null;
        foreach (var b in new[] { der, izq })
        {
            DetenerTeleop(b);
            if (b.trocarEnPinza != null) SoltarTrocarDelBrazo(b, permitirEncaje: false);
            if (b.instrumento != null) { if (b.dentro) yield return Retirar(b, PuertoDe(b)); b.dentro = false; yield return Devolver(b); }
        }
        var h1 = StartCoroutine(VolverAPoseInicial(izq));
        var h2 = StartCoroutine(VolverAPoseInicial(der));
        yield return h1; yield return h2;

        InformeManual = $"Tiempo total: {Time.time - tInicio:F0} s. Avisos por error: {errores}.\n" + string.Join(" | ", tiempos);
        Instruir("Operación completada", InformeManual + " Pulsa Menú (o M) para cambiar de modo.");
        Debug.Log("🩺 Operación manual completada. " + InformeManual.Replace("\n", " "));
        yield return new WaitForSeconds(6f);
        FinManual();
        Restaurar(inmediato: false);
        rutina = null;
    }

    Vector3[] VerticesVesicula()
    {
        if (vesiculaVisual == null || vesiculaVisual.sharedMesh == null) return new Vector3[0];
        var vs = vesiculaVisual.sharedMesh.vertices;
        for (int i = 0; i < vs.Length; i++) vs[i] = vesiculaVisual.transform.TransformPoint(vs[i]);
        return vs;
    }

    /// <summary>Mayor desplazamiento (m) de un vértice de la vesícula respecto a una foto anterior.</summary>
    float DeformacionMaxima(Vector3[] antes)
    {
        var ahora = VerticesVesicula();
        float max = 0f;
        for (int i = 0; i < ahora.Length && i < antes.Length; i++) max = Mathf.Max(max, (ahora[i] - antes[i]).sqrMagnitude);
        return Mathf.Sqrt(max);
    }

    /// <summary>Tras encajar a mano un trocar "de otro puerto": los datos propios del trocar cambian de puerto.</summary>
    void IntercambiarTrocares(int i, int j)
    {
        PuertoDemo a = PuertoDemoDe(i), b = PuertoDemoDe(j);
        if (a == null || b == null) return;
        (a.trocar, b.trocar) = (b.trocar, a.trocar);
        (a.rb, b.rb) = (b.rb, a.rb);
        (a.solidos, b.solidos) = (b.solidos, a.solidos);
        (a.tq, b.tq) = (b.tq, a.tq);
        (a.kin, b.kin) = (b.kin, a.kin);
        (a.obturador, b.obturador) = (b.obturador, a.obturador);
    }

    int VerticeCercano(Vector3 mundo)
    {
        if (vesiculaVisual == null || vesiculaVisual.sharedMesh == null) return -1;
        var vs = vesiculaVisual.sharedMesh.vertices; int mejor = -1; float d = float.MaxValue;
        for (int i = 0; i < vs.Length; i++)
        {
            float q = (vesiculaVisual.transform.TransformPoint(vs[i]) - mundo).sqrMagnitude;
            if (q < d) { d = q; mejor = i; }
        }
        return mejor;
    }

    Vector3 VerticeMundo(int indice)
    {
        if (indice < 0 || vesiculaVisual == null || vesiculaVisual.sharedMesh == null) return Vector3.zero;
        var vs = vesiculaVisual.sharedMesh.vertices;
        return indice < vs.Length ? vesiculaVisual.transform.TransformPoint(vs[indice]) : Vector3.zero;
    }

    /// <summary>Grasa y peritoneo que cubren el conducto cístico: hay que cauterizarlos para lograr la visión crítica.</summary>
    void CrearGrasaSobreElDucto()
    {
        grasa.Clear();
        float[] tramos = { 0.0245f, 0.0285f, 0.0325f, 0.0365f };
        foreach (float s in tramos)
        {
            Vector3 p = PuntoDucto(s);
            Vector3 tang = (PuntoDucto(s + 0.003f) - PuntoDucto(s - 0.003f)).normalized;
            Vector3 lat = Vector3.Cross(tang, Vector3.up);
            if (lat.sqrMagnitude < 0.25f) lat = Vector3.Cross(tang, Vector3.right);
            lat.Normalize();
            foreach (float lado in new[] { -1f, 1f })
                grasa.Add(Marcador(p + lat * lado * 0.0035f + Vector3.up * 0.003f, new Color(0.95f, 0.82f, 0.35f, 1f), 0.0048f, "GrasaCalot"));
        }
    }
}
