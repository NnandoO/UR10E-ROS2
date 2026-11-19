using System.Collections;
using System.Collections.Generic;
using SofaUnity;
using UnityEngine;

/// <summary>
/// DEMO AUTOMÁTICA: los dos brazos UR10e realizan solos una colecistectomía laparoscópica siguiendo el
/// procedimiento estándar:
///
///  0. Preparación: trocares en sus puertos, laparoscopio por el puerto umbilical, pared translúcida.
///  1. Entrega de instrumentos (como cuando agarras uno con el mando y salta a la punta del robot).
///  2. Inserción por los trocares (el fulcro hace la palanca en la pared abdominal).
///  3. Tracción del fondo vesicular hacia arriba y hacia la cabeza (grasper, agarre SOFA).
///  4. Disección del triángulo de Calot con el hook -> visión crítica: el conducto cístico (la cola real
///     de la malla SOFA de la vesícula) queda expuesto.
///  5. Clipado: 2 clips del lado del paciente y 1 del lado de la vesícula, sobre el conducto real.
///  6. Sección del conducto con la tijera usando el CORTE SOFA real (rayo acortado a ~1 cm).
///  7. Disección de la vesícula de su lecho hepático con el hook (electrocauterio), del cuello al fondo.
///  8. Extracción: la vesícula sale por el trocar con el grasper y se deposita en la bandeja.
///  9. Revisión de los muñones clipados, retiro de instrumentos y brazos a su posición.
///
/// Los brazos se mueven con IK en Unity (<see cref="CinematicaBrazoUR"/>): la demo NO necesita los servidores de
/// WSL. El conducto cístico ES parte de la malla SOFA de la vesícula (su cola fina), así que el clipado y el
/// corte actúan sobre tejido real; no se añade ninguna estructura que no exista en el modelo. Al salir del modo
/// todo vuelve a como estaba (los cortes SOFA permanecen hasta reiniciar Play).
/// </summary>
public class DemoAutomaticaColecistectomia : MonoBehaviour
{
    [Header("Brazos")]
    public seguimiento brazoDerecho;
    public seguimientoL brazoIzquierdo;
    public IntercambioHerramienta intercambio;

    [Header("Instrumentos")]
    public GameObject grasper;
    public GameObject tijera;
    public GameObject hook;
    public GameObject laparoscopio;

    [Tooltip("Los UR10e operan con su instrumental OpenRST montado (mordazas intercambiables del repositorio jcolan/OpenRST). El instrumento laparoscópico sigue haciendo la física SOFA, pero invisible y con su punta en la mordaza.")]
    public bool usarInstrumentalOpenRST = true;

    [Header("Puertos (orden de PreparacionQuirurgica: 0 = brazo derecho, 1 = brazo izquierdo, 2 = cámara)")]
    public PreparacionQuirurgica preparacion;

    [Header("Anatomía")]
    public Transform zonaFondo;
    public Transform zonaCalot;
    public Transform zonaLecho;
    [Tooltip("Zona del pedículo cístico (el conducto de la vesícula, por encima del duodeno), visible tras la tracción.")]
    public Transform zonaCistica;
    [Tooltip("Malla visual SOFA de la vesícula (OglModel - Visual_ves). Vacío = se busca.")]
    public MeshFilter vesiculaVisual;

    [Header("Narración y vista")]
    public DemoColecistectomia narrador;
    public VistaAbdominal vista;

    [Header("Ritmo")]
    [Tooltip("Velocidad del punto de agarre del robot fuera del paciente (m/s).")]
    public float velocidadFuera = 0.25f;
    [Tooltip("Velocidad de la punta del instrumento dentro del abdomen (m/s).")]
    public float velocidadDentro = 0.03f;
    [Tooltip("Velocidad articular máxima de los brazos (grados/s).")]
    public float velocidadArticular = 45f;
    public float pausaEntreFases = 1.2f;

    public bool Ejecutando { get; private set; }
    public string Fase { get; private set; } = "";

    // ------------------------------------------------------------------
    private class Brazo
    {
        public bool izq;
        public SeguimientoBrazoBase seg;
        public CinematicaBrazoUR ik;
        public Vector3 objetivo;
        public Vector3 eje;
        public bool activo;
        public float[] qInicio;
        public GameObject instrumento;
        public float errorIK;
        public InstrumentalOpenRST openRST;
        public readonly List<Renderer> ocultosOpenRST = new List<Renderer>();
    }

    private class Pose { public Vector3 p; public Quaternion r; public bool kin; public bool grav; }

    private Brazo der, izq;
    private readonly Dictionary<GameObject, Pose> posesMesa = new Dictionary<GameObject, Pose>();
    private readonly List<Behaviour> desactivados = new List<Behaviour>();
    private Coroutine rutina;
    private ModoParedAbdominal vistaPrevia;

    // Objetos propios de la demo (se destruyen al salir)
    private Transform raizDemo;
    private GameObject clipadora;
    private GameObject vesiculaExtraida;
    private GameObject munonExtra;          // muñón del conducto que queda clipado en el paciente
    private Transform bandeja;
    private MeshRenderer rendVesiculaSofa;

    // Conducto cístico REAL: la cola fina de la malla SOFA de la vesícula
    private Vector3 ductoOrigen;            // centroide de la vesícula al medirlo
    private Vector3 ductoDir;               // dirección fondo -> punta del conducto
    private float sCorte;                   // distancia del corte a lo largo del conducto
    private readonly List<Transform> clipsPaciente = new List<Transform>();
    private Transform clipVesicula;

    // Fase 0: los brazos colocan los trocares (datos por puerto)
    private class PuertoDemo
    {
        public int indice;
        public Transform trocar;
        public Rigidbody rb;
        public Collider[] solidos;
        public Vector3 posDock; public Quaternion rotDock;   // pose exacta que da el socket
        public Vector3 fulcroLocal, cabezalLocal;            // sobre el eje funcional del trocar
        public float xPuntaLocal;                            // extremo de la cánula (lado del paciente)
        public bool estabaColocado, kin;
        public Brazo portador;                               // brazo que lo lleva en la pinza (o null)
    }
    private readonly List<PuertoDemo> puertosDemo = new List<PuertoDemo>();

    // ==================================================================
    // API
    // ==================================================================
    public void Iniciar()
    {
        if (Ejecutando) return;
        Autocompletar();
        if (brazoDerecho == null || brazoIzquierdo == null || intercambio == null || preparacion == null)
        {
            Debug.LogError("DemoAutomatica: faltan referencias (brazos, IntercambioHerramienta o PreparacionQuirurgica).");
            return;
        }
        rutina = StartCoroutine(Secuencia());
    }

    public void Detener()
    {
        if (rutina != null) StopCoroutine(rutina);
        rutina = null;
        if (Ejecutando) Restaurar(inmediato: true);
        if (narracionActiva && narrador != null) narrador.DetenerDemo();
        if (narracionActiva && vista != null) vista.AplicarModo(vistaPrevia);
        narracionActiva = false;
        RestaurarCamaraLaparoscopio();
        LimpiarObjetosDemo();
    }

    private bool narracionActiva;
    private Camera camaraLap;
    private Quaternion camaraLapRotLocal;
    private float camaraLapFov;
    private bool camaraLapGuardada;

    void RestaurarCamaraLaparoscopio()
    {
        if (camaraLapGuardada && camaraLap != null) { camaraLap.transform.localRotation = camaraLapRotLocal; camaraLap.fieldOfView = camaraLapFov; }
        camaraLapGuardada = false;
    }

    void OnDisable() => Detener();

    void Autocompletar()
    {
        if (brazoDerecho == null) brazoDerecho = FindFirstObjectByType<seguimiento>();
        if (brazoIzquierdo == null) brazoIzquierdo = FindFirstObjectByType<seguimientoL>();
        if (intercambio == null) intercambio = FindFirstObjectByType<IntercambioHerramienta>();
        if (preparacion == null) preparacion = FindFirstObjectByType<PreparacionQuirurgica>();
        if (narrador == null) narrador = FindFirstObjectByType<DemoColecistectomia>(FindObjectsInactive.Include);
        if (vista == null) vista = FindFirstObjectByType<VistaAbdominal>();
        if (grasper == null) grasper = GameObject.Find("instrumento_grasper");
        if (tijera == null) tijera = GameObject.Find("instrumento_tijera");
        if (hook == null) hook = GameObject.Find("instrumento_hook");
        if (laparoscopio == null) laparoscopio = GameObject.Find("instrumento_laparoscopio");
        if (zonaFondo == null) zonaFondo = GameObject.Find("Zona_FondoVesicular")?.transform;
        if (zonaCalot == null) zonaCalot = GameObject.Find("Zona_TrianguloCalot")?.transform;
        if (zonaLecho == null) zonaLecho = GameObject.Find("Zona_LechoHepatico")?.transform;
        if (zonaCistica == null) zonaCistica = GameObject.Find("Zona_ConductoCistico")?.transform;
        if (zonaCistica == null) zonaCistica = zonaCalot;
        if (vesiculaVisual == null)
            foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mf.name.EndsWith("Visual_ves")) { vesiculaVisual = mf; break; }
    }

    // ==================================================================
    // Bucle de control de los brazos (IK cada frame)
    // ==================================================================
    void Update()
    {
        if (!Ejecutando) return;
        foreach (var b in new[] { der, izq })
        {
            if (b == null || !b.activo) continue;
            b.errorIK = b.ik.Resolver(b.objetivo, b.eje);
            b.ik.AplicarObjetivos(velocidadArticular, Time.deltaTime);
        }
    }

    // ==================================================================
    // Secuencia quirúrgica
    // ==================================================================
    IEnumerator Secuencia()
    {
        Ejecutando = true;
        Preparar();
        Narrar("Preparación del quirófano",
               "Paciente en posición y neumoperitoneo establecido. Los trocares y el instrumental esperan en la mesa: los brazos colocarán los puertos.");
        yield return PrepararPuertos();
        CrearClipadora();
        CrearBandeja();
        yield return new WaitForSeconds(pausaEntreFases);

        // ---------- 0. Incisiones, puertos y docking (flujo daVinci) ----------
        Narrar("0 · Incisiones y colocación de puertos", "Como en el docking de un sistema daVinci: cada brazo hace la incisión, introduce su trocar por la pared abdominal y queda acoplado a su puerto. Primero el puerto óptico (umbilical) y el axilar derecho.");
        var c1 = StartCoroutine(ColocarPuerto(der, 2));
        var c2 = StartCoroutine(ColocarPuerto(izq, 1));
        yield return c1; yield return c2;
        yield return IntroducirLaparoscopio();
        Narrar("0 · Incisiones y colocación de puertos", "Con la óptica dentro, el último puerto de trabajo (cuadrante superior izquierdo) se coloca bajo visión directa. El fulcro de cada trocar será el centro de movimiento remoto de su instrumento.");
        yield return ColocarPuerto(der, 0);
        float t0 = Time.time;
        while (!preparacion.TodosColocados() && Time.time - t0 < 3f) yield return null;
        if (!preparacion.TodosColocados()) { Debug.LogWarning("🤖 Demo: algún trocar no quedó encajado; se encaja directamente."); preparacion.Colocar(); yield return null; }
        yield return new WaitForSeconds(pausaEntreFases);

        var fD = preparacion.Fulcro(0); // brazo derecho
        var fI = preparacion.Fulcro(1); // brazo izquierdo

        // ---------- 1. Entrega e inserción ----------
        Narrar("1 · Entrega e inserción de instrumentos", "Cada UR10e opera con su instrumento OpenRST: el brazo izquierdo monta la mordaza fenestrada (grasper) y el derecho el disector Maryland. Ambos entran por sus trocares y pivotan en el fulcro.");
        var e1 = StartCoroutine(Entregar(izq, grasper));
        var e2 = StartCoroutine(Entregar(der, hook));
        yield return e1; yield return e2;
        var i1 = StartCoroutine(Insertar(izq, 1, 0.06f));
        var i2 = StartCoroutine(Insertar(der, 0, 0.06f));
        yield return i1; yield return i2;
        yield return new WaitForSeconds(pausaEntreFases * 0.5f);

        // ---------- 2. Tracción del fondo ----------
        Narrar("2 · Tracción del fondo vesicular", "El Grasper sujeta el fondo de la vesícula y lo tracciona hacia arriba y hacia el hombro derecho, exponiendo el infundíbulo y el triángulo de Calot.");
        MarcarZona(zonaFondo, 0.012f);
        yield return MoverPunta(izq, fI, Acercamiento(fI, zonaFondo.position, 0.012f));
        Accion(grasper, true);
        yield return new WaitForSeconds(0.8f);
        Vector3 traccion = (Vector3.forward * 0.6f + Vector3.up * 0.8f).normalized * 0.035f;
        yield return MoverPunta(izq, fI, PuntaActual(grasper) + traccion);
        yield return new WaitForSeconds(pausaEntreFases * 0.5f);

        // ---------- 3. Disección del triángulo de Calot ----------
        Narrar("3 · Disección del triángulo de Calot", "El Hook abre el peritoneo y libera la grasa alrededor del conducto cístico, la única estructura que entra en la vesícula en este modelo (visión crítica de seguridad).");
        MarcarZona(zonaCistica, 0.014f);
        if (!CalcularDucto()) Debug.LogWarning("🤖 Demo: no se pudo medir el conducto cístico en la malla SOFA.");
        float[] barridoDiseccion = { 0.030f, 0.026f, 0.034f, 0.030f };
        foreach (float sD in barridoDiseccion)
        {
            yield return MoverPunta(der, fD, Acercamiento(fD, PuntoDucto(sD), 0.004f));
            Cauterizar(hook, 0.4f);
            yield return new WaitForSeconds(0.4f);
        }
        Narrar("Visión crítica de seguridad", "El conducto cístico queda disecado y expuesto en todo su contorno, con su arteria incluida en el pedículo. Ya se puede clipar.");
        yield return new WaitForSeconds(pausaEntreFases * 1.5f);

        // ---------- 4. Clipado ----------
        Narrar("4 · Clipado del conducto cístico", "Cambio de instrumento: disector por aplicador automático de clips de polímero (técnica Hem-o-lok del video guía). Dos clips del lado del paciente y uno del lado de la vesícula, sobre el conducto cístico real.");
        MarcarZona(null);
        yield return CambiarInstrumento(der, 0, clipadora);
        sCorte = 0.0305f;
        float[] posicionesClips = { 0.0265f, 0.0345f, 0.0380f };   // [0] lado vesícula; [1][2] lado paciente
        for (int k = 0; k < posicionesClips.Length; k++)
        {
            Vector3 p = PuntoDucto(posicionesClips[k]);
            yield return MoverPunta(der, fD, Acercamiento(fD, p, 0.002f), velocidadDentro * 0.8f);
            var clip = CrearClip(posicionesClips[k]);
            if (k == 0) clipVesicula = clip; else clipsPaciente.Add(clip);
            CargarClipEnClipadora(false);
            yield return new WaitForSeconds(0.35f);
            CargarClipEnClipadora(true);
            // pequeño retroceso entre disparos
            yield return MoverPunta(der, fD, Acercamiento(fD, p, 0.010f), velocidadDentro);
        }
        yield return new WaitForSeconds(pausaEntreFases * 0.5f);

        // ---------- 5. Sección entre clips ----------
        Narrar("5 · Sección del conducto entre clips", "Cambio de instrumento: Clipadora por Tijera. La tijera secciona el conducto cístico con el corte SOFA real, entre los clips: el muñón del paciente queda cerrado con dos clips.");
        yield return CambiarInstrumento(der, 0, tijera);
        // Barrido en rejilla con la PUNTA dentro del conducto: el rayo de corte parte del propio tejido,
        // así que talla los tetraedros del conducto apunte donde apunte. Hasta 3 intentos, verificando
        // después de cada uno que la malla quedó realmente en dos piezas.
        for (int intento = 0; intento < 3 && !VesiculaSeccionada(); intento++)
        {
            float sPasada = sCorte + intento * 0.0012f;   // cada intento, 1.2 mm más hacia el paciente
            Vector3 pCorte = PuntoDucto(sPasada);
            Vector3 tangCorte = (PuntoDucto(sPasada + 0.003f) - PuntoDucto(sPasada - 0.003f)).normalized;
            Vector3 latCorte = Vector3.Cross(tangCorte, Vector3.up);
            if (latCorte.sqrMagnitude < 0.25f) latCorte = Vector3.Cross(tangCorte, Vector3.right);
            latCorte.Normalize();
            Vector3 vertCorte = Vector3.Cross(tangCorte, latCorte).normalized;
            // El carving de SOFA talla donde el RAYO GOLPEA la superficie, así que el corte se hace en
            // abanico: la punta se queda 9 mm a un lado del conducto y el rayo (dirigido con apuntarHacia)
            // barre todo el disco de la sección, cruzándolo hacia el lado libre.
            Vector3 cuerpoDir = (ductoOrigen - pCorte).normalized;
            if (Vector3.Dot(latCorte, cuerpoDir) > 0f) latCorte = -latCorte;   // el rayo sale ALEJÁNDOSE del cuerpo
            Vector3 origenCorte = pCorte - latCorte * 0.009f;
            var objetivoGO = new GameObject("ObjetivoCorte");
            objetivoGO.transform.SetParent(raizDemo, true);
            var puntaT = Punta(tijera);
            yield return MoverPunta(der, fD, origenCorte, velocidadDentro * 0.8f);
            if (puntaT != null) puntaT.apuntarHacia = objetivoGO.transform;
            Accion(tijera, true);
            for (float h = 0.004f; h >= -0.00405f && !VesiculaSeccionada(); h -= 0.0013f)
                for (float a = -0.0035f; a <= 0.00355f; a += 0.0014f)
                {
                    objetivoGO.transform.position = pCorte + latCorte * 0.02f + vertCorte * h + tangCorte * a;
                    yield return new WaitForSeconds(0.22f);
                    if (VesiculaSeccionada()) break;
                }
            Accion(tijera, false);
            if (puntaT != null) puntaT.apuntarHacia = null;
            Destroy(objetivoGO);
        }
        Debug.Log(VesiculaSeccionada()
            ? "🩺 Demo: conducto cístico seccionado (la malla quedó en dos piezas)."
            : "⚠️ Demo: el conducto no quedó seccionado del todo tras 3 pasadas.");
        Vector3 pFueraCorte = PuntoDucto(sCorte);
        yield return MoverPunta(der, fD, Acercamiento(fD, pFueraCorte, 0.015f), velocidadDentro);
        yield return new WaitForSeconds(pausaEntreFases * 0.5f);

        // ---------- 6. Disección del lecho hepático ----------
        Narrar("6 · Disección del lecho hepático", "Cambio de instrumento: Tijera por Hook. La vesícula se separa del hígado con el electrobisturí, desde el cuello hasta el fondo, siguiendo el plano del lecho hepático.");
        yield return CambiarInstrumento(der, 0, hook);
        MarcarZona(zonaLecho, 0.016f);
        Vector3 centroVes = (zonaCalot.position + zonaFondo.position) * 0.5f;
        Vector3 haciaLecho = zonaLecho.position - centroVes;
        Vector3 desde = zonaCalot.position + haciaLecho, hasta = zonaFondo.position + haciaLecho;
        yield return MoverPunta(der, fD, Acercamiento(fD, desde, 0.004f));
        const int pasos = 6;
        for (int k = 1; k <= pasos; k++)
        {
            // El hook cauteriza el peritoneo del lecho (no corta la vesícula): efecto visual, sin corte SOFA
            var mover = StartCoroutine(MoverPunta(der, fD, Acercamiento(fD, Vector3.Lerp(desde, hasta, k / (float)pasos), 0.004f), velocidadDentro * 0.5f));
            Cauterizar(hook, 0.9f);
            yield return mover;
            yield return new WaitForSeconds(0.2f);
        }
        MarcarZona(null);
        yield return Retirar(der, 0);
        yield return Devolver(der);
        yield return new WaitForSeconds(pausaEntreFases * 0.5f);

        // ---------- 7. Extracción ----------
        Narrar("7 · Extracción de la vesícula", "La vesícula, ya libre del lecho hepático, sale por el trocar sujeta por el Grasper y se entrega en la bandeja para estudio anatomopatológico.");
        Accion(grasper, false);
        SepararVesicula();
        yield return new WaitForSeconds(0.6f);
        yield return Retirar(izq, 1);
        yield return DepositarEnBandeja();
        yield return Devolver(izq);

        // ---------- 8. Revisión y cierre ----------
        Narrar("8 · Revisión del lecho y del muñón", "El muñón del conducto cístico queda cerrado con dos clips, sin sangrado ni fuga biliar. Se retiran los instrumentos y los brazos vuelven a su posición.");
        MarcarZona(zonaCistica, 0.02f);
        var h1 = StartCoroutine(VolverAPoseInicial(izq));
        var h2 = StartCoroutine(VolverAPoseInicial(der));
        yield return h1; yield return h2;
        yield return new WaitForSeconds(pausaEntreFases);
        MarcarZona(null);

        Narrar("Colecistectomía completada", "Tracción, disección de Calot, clipado, sección entre clips, disección del lecho y extracción. Pulsa M (o Menú) para volver a Simular operación.");
        Restaurar(inmediato: false);
        rutina = null;
    }

    // ==================================================================
    // Preparación y restauración
    // ==================================================================
    void Preparar()
    {
        posesMesa.Clear();
        foreach (var go in new[] { grasper, tijera, hook, laparoscopio })
        {
            if (go == null) continue;
            var rb = go.GetComponent<Rigidbody>();
            posesMesa[go] = new Pose { p = go.transform.position, r = go.transform.rotation, kin = rb != null && rb.isKinematic, grav = rb != null && rb.useGravity };
            // Los instrumentos son Rigidbody dinámicos sin gravedad: cualquier roce los deja flotando a la deriva.
            // Durante la demo (y al devolverlos) quedan cinemáticos, igual que los deja IntercambioHerramienta al soltarlos.
            if (rb != null) { if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } rb.isKinematic = true; }
        }
        intercambio.entradaMandosHabilitada = false;
        der = CrearBrazo(brazoDerecho, false);
        izq = CrearBrazo(brazoIzquierdo, true);

        desactivados.Clear();
        if (laparoscopio != null)
            foreach (var v in laparoscopio.GetComponentsInChildren<ControladorVisorLaparoscopio>())
                if (v.enabled) { v.enabled = false; desactivados.Add(v); }

        if (vista != null) { vistaPrevia = vista.ModoActual; vista.AplicarModo(ModoParedAbdominal.Translucida); }
        if (narrador != null) narrador.IniciarNarracion();
        narracionActiva = true;

        LimpiarObjetosDemo();
        raizDemo = new GameObject("DemoAutomatica_Runtime").transform;
    }

    Brazo CrearBrazo(SeguimientoBrazoBase seg, bool esIzq)
    {
        seg.PausadoPorDemo = true;
        var ik = new CinematicaBrazoUR(seg.urJoints, intercambio.PosicionPivote(esIzq), intercambio.RotacionPivote(esIzq));
        ik.LeerAngulosActuales();
        var b = new Brazo { izq = esIzq, seg = seg, ik = ik, qInicio = (float[])ik.q.Clone() };
        if (usarInstrumentalOpenRST) b.openRST = InstrumentalOpenRST.Buscar(intercambio.PuntaBrazo(esIzq));
        var m = ik.FKPivote(ik.q);
        b.objetivo = CinematicaBrazoUR.Pos(m);
        b.eje = ik.Eje(m);
        b.activo = true;
        return b;
    }

    void Restaurar(bool inmediato)
    {
        foreach (var go in new[] { grasper, tijera, hook })
        {
            var p = Punta(go); if (p != null) { p.ForzarAccion(false); p.LiberarAccionForzada(); }
        }
        foreach (var b in new[] { der, izq })
        {
            if (b == null) continue;
            if (intercambio.InstrumentoEnBrazo(b.izq) != null) intercambio.SoltarDelBrazo(b.izq);
            if (inmediato)
                for (int i = 0; i < 6; i++) { var d = b.ik.juntas[i].xDrive; d.target = b.qInicio[i]; b.ik.juntas[i].xDrive = d; }
            RestaurarOpenRST(b, completo: true);
            b.activo = false;
            b.seg.PausadoPorDemo = false;
        }
        RestaurarPuertos();
        if (inmediato)
            foreach (var kv in posesMesa) ColocarEnMesa(kv.Key, kv.Value);
        foreach (var v in desactivados) if (v != null) v.enabled = true;
        desactivados.Clear();
        intercambio.entradaMandosHabilitada = true;
        MarcarZona(null);
        if (inmediato)
        {
            if (narrador != null) narrador.DetenerDemo();
            if (vista != null) vista.AplicarModo(vistaPrevia);
            narracionActiva = false;
            RestaurarCamaraLaparoscopio();
            LimpiarObjetosDemo();
        }
        Ejecutando = false;
    }

    void LimpiarObjetosDemo()
    {
        if (raizDemo != null) Destroy(raizDemo.gameObject);
        raizDemo = null;
        if (clipadora != null && intercambio != null)
            foreach (bool lado in new[] { false, true })
                if (intercambio.InstrumentoEnBrazo(lado) == clipadora) intercambio.SoltarDelBrazo(lado);
        clipadora = null; efectoCauterio = null; vesiculaExtraida = null; munonExtra = null; bandeja = null;
        clipsPaciente.Clear(); clipVesicula = null;
        if (rendVesiculaSofa != null) rendVesiculaSofa.enabled = true; // la vesícula SOFA vuelve a verse
        rendVesiculaSofa = null;
    }

    void ColocarEnMesa(GameObject go, Pose pose)
    {
        if (go == null) return;
        go.transform.SetPositionAndRotation(pose.p, pose.r);
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;          // queda quieto en la mesa hasta que lo agarres
            rb.useGravity = pose.grav;
            rb.position = pose.p; rb.rotation = pose.r;
        }
    }

    // ==================================================================
    // Fase 0: incisiones, colocación de trocares y docking
    // ==================================================================
    PuertoDemo PuertoDemoDe(int indice) => puertosDemo.Find(x => x.indice == indice);

    static Vector3 EnDock(PuertoDemo pd, Vector3 local) =>
        pd.posDock + pd.rotDock * Vector3.Scale(local, pd.trocar.lossyScale);

    Vector3 ExtremoBrazo(Brazo b) => b.openRST != null ? b.openRST.eeLink.position : intercambio.PosicionPivote(b.izq);
    float LargoHerramienta(Brazo b) => Vector3.Distance(intercambio.PosicionPivote(b.izq), ExtremoBrazo(b));

    /// <summary>
    /// Encaja un instante los trocares para leer la pose EXACTA que les da cada socket, y los devuelve
    /// a la mesa con el socket desactivado: a partir de aquí los colocan los brazos.
    /// </summary>
    IEnumerator PrepararPuertos()
    {
        puertosDemo.Clear();
        var antes = new bool[preparacion.puertos.Count];
        for (int i = 0; i < antes.Length; i++) antes[i] = preparacion.EstaColocado(i);
        preparacion.Colocar();
        float t0 = Time.time;
        while (!preparacion.TodosColocados() && Time.time - t0 < 3f) yield return null;
        yield return null; yield return null;   // el socket termina de llevar el trocar a su sitio

        for (int i = 0; i < preparacion.puertos.Count; i++)
        {
            var p = preparacion.puertos[i];
            if (p.trocar == null || p.socket == null || !p.mesaValida || !preparacion.EstaColocado(i)) continue;
            Transform t = p.trocar.transform, fTr = t.Find("PuntoEntrada");
            var mf = t.GetComponent<MeshFilter>();
            if (fTr == null || mf == null || mf.sharedMesh == null) continue;
            var pd = new PuertoDemo { indice = i, trocar = t, rb = t.GetComponent<Rigidbody>(), posDock = t.position, rotDock = t.rotation, estabaColocado = antes[i] };
            pd.fulcroLocal = t.InverseTransformPoint(CentroFulcro(fTr));
            // +X local del trocar = hacia el cabezal (afuera); -X = extremo de la cánula
            pd.cabezalLocal = new Vector3(mf.sharedMesh.bounds.max.x, pd.fulcroLocal.y, pd.fulcroLocal.z);
            pd.xPuntaLocal = mf.sharedMesh.bounds.min.x;
            var solidos = new List<Collider>();
            foreach (var c in t.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger && c.enabled) solidos.Add(c);
            pd.solidos = solidos.ToArray();
            puertosDemo.Add(pd);
        }

        foreach (var pd in puertosDemo) preparacion.Liberar(pd.indice, desactivarSocket: true);
        yield return null;   // XRGrabInteractable restaura el Rigidbody al salir del socket
        foreach (var pd in puertosDemo)
        {
            var p = preparacion.puertos[pd.indice];
            pd.kin = pd.rb != null && pd.rb.isKinematic;
            PonerTrocar(pd, p.posMesa, p.rotMesa, cinematico: true);
        }
    }

    static void PonerTrocar(PuertoDemo pd, Vector3 pos, Quaternion rot, bool cinematico)
    {
        if (pd.rb != null)
        {
            if (!pd.rb.isKinematic) { pd.rb.linearVelocity = Vector3.zero; pd.rb.angularVelocity = Vector3.zero; }
            pd.rb.isKinematic = cinematico;
        }
        pd.trocar.SetPositionAndRotation(pos, rot);
        if (pd.rb != null) { pd.rb.position = pos; pd.rb.rotation = rot; }
    }

    /// <summary>Pose del trocar sujeto por el cabezal en la pinza del brazo, coaxial con su herramienta.</summary>
    void PoseTrocarEnBrazo(Brazo b, PuertoDemo pd, out Vector3 pos, out Quaternion rot)
    {
        Vector3 piv = intercambio.PosicionPivote(b.izq), ee = ExtremoBrazo(b);
        Vector3 eje = ee - piv;
        eje = eje.sqrMagnitude > 1e-6f ? eje.normalized : (intercambio.RotacionPivote(b.izq) * b.ik.ejeHerramientaLocal).normalized;
        rot = Quaternion.FromToRotation(pd.rotDock * Vector3.left, eje) * pd.rotDock;
        pos = ee - rot * Vector3.Scale(pd.cabezalLocal, pd.trocar.lossyScale);
    }

    void LateUpdate()
    {
        if (!Ejecutando) return;
        foreach (var pd in puertosDemo)
        {
            if (pd.portador == null || pd.trocar == null) continue;
            PoseTrocarEnBrazo(pd.portador, pd, out var pos, out var rot);
            PonerTrocar(pd, pos, rot, cinematico: true);
        }
    }

    /// <summary>
    /// El brazo hace la incisión en la piel, recibe el trocar, lo introduce por la pared abdominal a lo
    /// largo del eje del puerto y lo deja encajado en su socket (mismo estado final que PreparacionQuirurgica).
    /// </summary>
    IEnumerator ColocarPuerto(Brazo b, int indice)
    {
        var pd = PuertoDemoDe(indice);
        if (pd == null) { preparacion.ColocarUno(indice); yield break; }

        Vector3 d = pd.rotDock * Vector3.left;                        // eje del puerto, hacia dentro
        Vector3 fulcro = EnDock(pd, pd.fulcroLocal);
        Vector3 piel = fulcro - d * 0.01f;                            // el fulcro queda 1 cm bajo la piel
        Vector3 cabezal = EnDock(pd, pd.cabezalLocal);
        Vector3 puntaCanula = EnDock(pd, new Vector3(pd.xPuntaLocal, pd.fulcroLocal.y, pd.fulcroLocal.z));
        float L = LargoHerramienta(b);
        if (b.openRST != null) { b.openRST.Mostrar(); b.openRST.CambiarMordaza(InstrumentalOpenRST.Mordaza.Maryland); b.openRST.Apertura(0f); }

        // 1) Incisión: la punta baja alineada con el puerto, toca la piel y abre unos 14 mm
        Vector3 sobre = piel - d * 0.06f;
        yield return MoverPivote(b, sobre - d * L + Vector3.up * 0.05f, d, velocidadFuera);
        yield return MoverPivote(b, sobre - d * L, d, velocidadFuera * 0.4f);
        Vector3 toque = piel + d * 0.003f;
        yield return MoverPivote(b, toque - d * L, d, velocidadDentro * 1.5f);
        Transform marca = CrearMarcaIncision(piel, d);
        Vector3 lat = marca.right;
        StartCoroutine(CrecerMarca(marca, 1.4f));
        yield return MoverPivote(b, toque + lat * 0.006f - d * L, d, velocidadDentro);
        yield return MoverPivote(b, toque - lat * 0.006f - d * L, d, velocidadDentro);

        // 2) Retrocede a la pose de pre-inserción: con el trocar en la pinza, su cánula quedará a 3 cm de la piel
        float retroceso = Vector3.Dot(puntaCanula - piel, d) + 0.03f;
        yield return MoverPivote(b, cabezal - d * (retroceso + L), d, velocidadFuera * 0.5f);

        // 3) El trocar llega de la mesa a la pinza (sujeto por el cabezal)
        foreach (var c in pd.solidos) if (c != null) c.enabled = false;   // en la pinza no debe empujar al brazo
        Vector3 p0 = pd.trocar.position; Quaternion r0 = pd.trocar.rotation;
        const float vuelo = 1.6f;
        for (float t = 0; t < vuelo; t += Time.deltaTime)
        {
            PoseTrocarEnBrazo(b, pd, out var pf, out var rf);
            float u = Mathf.SmoothStep(0f, 1f, t / vuelo);
            PonerTrocar(pd, Vector3.Lerp(p0, pf, u), Quaternion.Slerp(r0, rf, u), cinematico: true);
            yield return null;
        }
        pd.portador = b;
        if (b.openRST != null) b.openRST.Apertura(1.5f);
        yield return new WaitForSeconds(0.4f);

        // 4) Inserción lenta por el eje del puerto hasta la pose del socket
        yield return MoverPivote(b, cabezal - d * L, d, velocidadDentro * 0.8f);

        // 5) Encaje: el socket toma el trocar y el brazo lo suelta y se aparta
        pd.portador = null;
        p0 = pd.trocar.position; r0 = pd.trocar.rotation;
        for (float t = 0; t < 0.25f; t += Time.deltaTime)
        {
            float u = t / 0.25f;
            PonerTrocar(pd, Vector3.Lerp(p0, pd.posDock, u), Quaternion.Slerp(r0, pd.rotDock, u), cinematico: true);
            yield return null;
        }
        PonerTrocar(pd, pd.posDock, pd.rotDock, cinematico: pd.kin);
        preparacion.ColocarUno(indice);
        if (b.openRST != null) b.openRST.Apertura(9f);
        yield return new WaitForSeconds(0.4f);
        yield return MoverPivote(b, cabezal - d * (0.10f + L), d, velocidadDentro * 2f);
        foreach (var c in pd.solidos) if (c != null) c.enabled = true;
    }

    Transform CrearMarcaIncision(Vector3 piel, Vector3 d)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Incision";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(raizDemo, true);
        Vector3 arriba = Mathf.Abs(Vector3.Dot(d, Vector3.forward)) > 0.9f ? Vector3.right : Vector3.forward;
        go.transform.SetPositionAndRotation(piel - d * 0.001f, Quaternion.LookRotation(d, arriba));   // la cara del quad mira hacia afuera
        go.transform.localScale = new Vector3(0.0005f, 0.004f, 1f);
        var m = EstructuraTubular.CrearMaterial(new Color(0.42f, 0.02f, 0.03f), false);
        m.SetFloat("_Cull", 0f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    IEnumerator CrecerMarca(Transform marca, float dur)
    {
        for (float t = 0; t < dur && marca != null; t += Time.deltaTime)
        {
            marca.localScale = new Vector3(Mathf.Lerp(0.0005f, 0.014f, t / dur), 0.004f, 1f);
            yield return null;
        }
        if (marca != null) marca.localScale = new Vector3(0.014f, 0.004f, 1f);
    }

    /// <summary>El laparoscopio llega de la mesa y entra por el puerto umbilical hasta su posición de trabajo.</summary>
    IEnumerator IntroducirLaparoscopio()
    {
        if (laparoscopio == null || preparacion.Fulcro(2) == null) yield break;
        Vector3 p0 = laparoscopio.transform.position; Quaternion r0 = laparoscopio.transform.rotation;
        ColocarLaparoscopio();                                   // calcula la pose final (y orienta la óptica)
        Vector3 pf = laparoscopio.transform.position; Quaternion rf = laparoscopio.transform.rotation;
        Geometria(laparoscopio, out var ejeLocal, out _, out _);
        Vector3 fuera = pf - (rf * ejeLocal).normalized * 0.30f;  // alineado con el puerto, aún fuera del trocar
        var rb = laparoscopio.GetComponent<Rigidbody>();
        System.Action<Vector3, Quaternion> poner = (p, r) =>
        {
            laparoscopio.transform.SetPositionAndRotation(p, r);
            if (rb != null) { rb.position = p; rb.rotation = r; }
        };
        poner(p0, r0);
        for (float t = 0; t < 1.6f; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / 1.6f);
            poner(Vector3.Lerp(p0, fuera, u), Quaternion.Slerp(r0, rf, u));
            yield return null;
        }
        for (float t = 0; t < 2.2f; t += Time.deltaTime)
        {
            poner(Vector3.Lerp(fuera, pf, Mathf.SmoothStep(0f, 1f, t / 2.2f)), rf);
            yield return null;
        }
        poner(pf, rf);
    }

    /// <summary>Deja los puertos en un estado coherente al terminar o cancelar la demo.</summary>
    void RestaurarPuertos()
    {
        foreach (var pd in puertosDemo)
        {
            if (pd.trocar == null) continue;
            pd.portador = null;
            foreach (var c in pd.solidos) if (c != null) c.enabled = true;
            var p = preparacion != null && pd.indice < preparacion.puertos.Count ? preparacion.puertos[pd.indice] : null;
            if (p == null || p.socket == null) continue;
            p.socket.socketActive = true;
            if (preparacion.EstaColocado(pd.indice)) continue;
            if (pd.estabaColocado)
            {
                PonerTrocar(pd, pd.posDock, pd.rotDock, cinematico: pd.kin);
                preparacion.ColocarUno(pd.indice);
            }
            else PonerTrocar(pd, p.posMesa, p.rotMesa, cinematico: pd.kin);
        }
        puertosDemo.Clear();
    }

    // ==================================================================
    // Anatomía cística, clipadora, bandeja y extracción
    // ==================================================================
    // ==================================================================
    // Conducto cístico real (la cola fina de la malla SOFA de la vesícula)
    // ==================================================================
    /// <summary>Mide el conducto sobre la malla deformada actual: centroide y dirección hacia la punta.</summary>
    bool CalcularDucto()
    {
        if (vesiculaVisual == null || vesiculaVisual.sharedMesh == null) return false;
        Transform t = vesiculaVisual.transform;
        var vs = vesiculaVisual.sharedMesh.vertices;
        Vector3 c = Vector3.zero;
        foreach (var v in vs) c += t.TransformPoint(v);
        c /= vs.Length;
        // El conducto sale del cuello (lado de zonaCalot) y se aleja del fondo
        Vector3 dir = (zonaCalot != null && zonaFondo != null) ? (zonaCalot.position - zonaFondo.position).normalized : -Vector3.forward;
        Vector3 lejos = Vector3.zero; int n = 0;
        foreach (var v in vs)
        {
            Vector3 w = t.TransformPoint(v);
            if (Vector3.Dot(w - c, dir) > 0.024f) { lejos += w; n++; }
        }
        if (n < 8) return false;
        ductoOrigen = c;
        ductoDir = (lejos / n - c).normalized;
        return true;
    }

    /// <summary>Centro del conducto a distancia s del centroide (media de los vértices de esa rodaja).</summary>
    Vector3 PuntoDucto(float s)
    {
        Transform t = vesiculaExtraida != null ? vesiculaExtraida.transform : vesiculaVisual.transform;
        Mesh m = vesiculaExtraida != null ? vesiculaExtraida.GetComponent<MeshFilter>().sharedMesh : vesiculaVisual.sharedMesh;
        Vector3 acc = Vector3.zero; int n = 0;
        foreach (var v in m.vertices)
        {
            Vector3 w = t.TransformPoint(v);
            if (Mathf.Abs(Vector3.Dot(w - ductoOrigen, ductoDir) - s) < 0.0025f) { acc += w; n++; }
        }
        return n > 0 ? acc / n : ductoOrigen + ductoDir * s;
    }

    /// <summary>
    /// ¿El corte separó de verdad la malla de la vesícula en dos piezas (cuerpo y muñón)?
    /// Cuenta las componentes conexas de la malla visual SOFA, ignorando restos de menos de 10 vértices.
    /// </summary>
    bool VesiculaSeccionada()
    {
        if (vesiculaVisual == null || vesiculaVisual.sharedMesh == null) return false;
        var m = vesiculaVisual.sharedMesh;
        var tris = m.triangles; int nV = m.vertexCount;
        var padre = new int[nV];
        for (int i = 0; i < nV; i++) padre[i] = i;
        int Raiz(int x) { while (padre[x] != x) { padre[x] = padre[padre[x]]; x = padre[x]; } return x; }
        for (int k = 0; k < tris.Length; k += 3)
        {
            padre[Raiz(tris[k + 1])] = Raiz(tris[k]);
            padre[Raiz(tris[k + 2])] = Raiz(tris[k]);
        }
        var cuenta = new Dictionary<int, int>();
        for (int k = 0; k < tris.Length; k++)
        {
            int r = Raiz(tris[k]);
            cuenta[r] = cuenta.TryGetValue(r, out int c) ? c + 1 : 1;
        }
        int grandes = 0;
        foreach (var c in cuenta.Values) if (c >= 30) grandes++;   // 30 índices ≈ 10 triángulos
        return grandes >= 2;
    }

    /// <summary>Clip de titanio atravesado sobre el conducto real en la distancia s.</summary>
    Transform CrearClip(float s)
    {
        Vector3 p = PuntoDucto(s);
        Vector3 tang = (PuntoDucto(s + 0.003f) - PuntoDucto(s - 0.003f)).normalized;
        var clip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(clip.GetComponent<Collider>());
        clip.name = $"Clip_{s * 1000f:F0}mm";
        clip.transform.SetParent(raizDemo, true);
        clip.transform.localScale = new Vector3(0.0024f, 0.012f, 0.0019f);   // clip de polímero tipo Hem-o-lok
        Vector3 perp = Vector3.Cross(tang, Vector3.up);
        if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(tang, Vector3.right);
        clip.transform.SetPositionAndRotation(p, Quaternion.LookRotation(tang, perp.normalized));
        var mc = EstructuraTubular.CrearMaterial(new Color(0.46f, 0.22f, 0.58f), false);   // polímero violeta (Hem-o-lok)
        mc.SetFloat("_Metallic", 0.05f); mc.SetFloat("_Smoothness", 0.45f);
        var r = clip.GetComponent<MeshRenderer>();
        r.sharedMaterial = mc;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return clip.transform;
    }

    /// <summary>Clipadora de la demo: copia visual de la Tijera (sin SOFA ni XR) con un clip cargado en las mandíbulas.</summary>
    void CrearClipadora()
    {
        if (tijera == null) return;
        var origen = tijera;
        clipadora = new GameObject("instrumento_clipadora (demo)");
        clipadora.transform.SetParent(raizDemo, false);
        Vector3 offsetMesa = new Vector3(0.12f, 0f, 0f);
        clipadora.transform.SetPositionAndRotation(posesMesa.TryGetValue(origen, out var pose) ? pose.p + offsetMesa : origen.transform.position + offsetMesa,
                                                   posesMesa.TryGetValue(origen, out var pose2) ? pose2.r : origen.transform.rotation);
        clipadora.transform.localScale = origen.transform.lossyScale;
        var matAcero = EstructuraTubular.CrearMaterial(new Color(0.35f, 0.42f, 0.55f), false);
        matAcero.SetFloat("_Metallic", 0.7f);
        foreach (var mf in origen.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null || !mr.enabled) continue;
            GameObject destino = mf.gameObject == origen ? clipadora : new GameObject(mf.name);
            if (destino != clipadora)
            {
                destino.transform.SetParent(clipadora.transform, false);
                destino.transform.localPosition = origen.transform.InverseTransformPoint(mf.transform.position);
                destino.transform.localRotation = Quaternion.Inverse(origen.transform.rotation) * mf.transform.rotation;
                destino.transform.localScale = mf.transform.lossyScale.x / Mathf.Max(1e-4f, origen.transform.lossyScale.x) * Vector3.one;
            }
            destino.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = destino.AddComponent<MeshRenderer>();
            var mats = new Material[mr.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = matAcero;
            r.sharedMaterials = mats;
        }
        // Punta: la misma posición local que la punta de la tijera
        var puntaTijera = PuntaTr(tijera);
        var punta = new GameObject("PuntaDemo").transform;
        punta.SetParent(clipadora.transform, false);
        punta.localPosition = puntaTijera != null ? tijera.transform.InverseTransformPoint(puntaTijera.position) : Vector3.zero;
        // Clip cargado (visible entre disparos)
        var clip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(clip.GetComponent<Collider>());
        clip.name = "ClipCargado";
        clip.transform.SetParent(punta, false);
        clip.transform.localScale = new Vector3(0.0022f, 0.009f, 0.0016f) / Mathf.Max(1e-4f, clipadora.transform.lossyScale.x);
        var mc = EstructuraTubular.CrearMaterial(new Color(0.78f, 0.80f, 0.84f), false); mc.SetFloat("_Metallic", 0.9f);
        clip.GetComponent<MeshRenderer>().sharedMaterial = mc;
        posesMesa[clipadora] = new Pose { p = clipadora.transform.position, r = clipadora.transform.rotation, kin = true };
    }

    void CargarClipEnClipadora(bool visible)
    {
        if (clipadora == null) return;
        var c = clipadora.transform.Find("PuntaDemo/ClipCargado");
        if (c != null) c.gameObject.SetActive(visible);
    }

    /// <summary>Bandeja (riñonera) en la mesa de instrumental para recibir la pieza.</summary>
    void CrearBandeja()
    {
        Vector3 pos = posesMesa.TryGetValue(hook ?? tijera, out var p) ? p.p + new Vector3(-0.25f, 0f, 0.25f) : new Vector3(-0.35f, 0.8f, -1.5f);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "BandejaMuestra";
        go.transform.SetParent(raizDemo, false);
        go.transform.position = pos + Vector3.down * 0.005f;
        go.transform.localScale = new Vector3(0.20f, 0.006f, 0.13f);
        var m = EstructuraTubular.CrearMaterial(new Color(0.80f, 0.82f, 0.85f), false); m.SetFloat("_Metallic", 0.85f); m.SetFloat("_Smoothness", 0.8f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        bandeja = go.transform;
    }

    /// <summary>
    /// La vesícula ya está cortada y disecada: la malla SOFA (anclada por resortes al lecho) se sustituye por
    /// dos copias de su forma actual: el cuerpo, que el Grasper se lleva con su clip, y el muñón del conducto,
    /// que se queda en el paciente con sus dos clips.
    /// </summary>
    void SepararVesicula()
    {
        if (vesiculaVisual == null || vesiculaVisual.sharedMesh == null || grasper == null) return;
        rendVesiculaSofa = vesiculaVisual.GetComponent<MeshRenderer>();
        Transform tv = vesiculaVisual.transform;
        Mesh origen = vesiculaVisual.sharedMesh;
        var vs = origen.vertices;
        var ladoPaciente = new bool[vs.Length];
        for (int i = 0; i < vs.Length; i++)
            ladoPaciente[i] = Vector3.Dot(tv.TransformPoint(vs[i]) - ductoOrigen, ductoDir) > sCorte;
        vesiculaExtraida = CrearParte("VesiculaExtraida", origen, tv, i => !ladoPaciente[i]);
        munonExtra = CrearParte("MunonCistico", origen, tv, i => ladoPaciente[i]);
        rendVesiculaSofa.enabled = false;
        if (vesiculaExtraida != null)
        {
            // Viaja con el Grasper, junto con su clip
            vesiculaExtraida.transform.SetParent(grasper.transform, true);
            if (clipVesicula != null) clipVesicula.SetParent(vesiculaExtraida.transform, true);
        }
    }

    GameObject CrearParte(string nombre, Mesh origen, Transform marco, System.Func<int, bool> incluir)
    {
        var vs = origen.vertices; var tris = origen.triangles;
        var mapa = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nt = new List<int>();
        for (int k = 0; k < tris.Length; k += 3)
        {
            int a = tris[k], b = tris[k + 1], c = tris[k + 2];
            if (!(incluir(a) && incluir(b) && incluir(c))) continue;
            foreach (int idx in new[] { a, b, c })
            {
                if (!mapa.TryGetValue(idx, out int ni)) { ni = nv.Count; mapa[idx] = ni; nv.Add(vs[idx]); }
                nt.Add(mapa[idx]);
            }
        }
        if (nt.Count == 0) return null;
        var go = new GameObject(nombre);
        go.transform.SetParent(raizDemo, true);
        go.transform.SetPositionAndRotation(marco.position, marco.rotation);
        go.transform.localScale = marco.lossyScale;
        var m = new Mesh { name = nombre };
        m.SetVertices(nv); m.SetTriangles(nt, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterials = rendVesiculaSofa.sharedMaterials;
        return go;
    }

    IEnumerator DepositarEnBandeja()
    {
        if (vesiculaExtraida == null || bandeja == null) yield break;
        var t = vesiculaExtraida.transform;
        t.SetParent(raizDemo, true);
        var b = vesiculaExtraida.GetComponent<MeshRenderer>().bounds;
        Vector3 desdeCentro = b.center;
        Vector3 destinoCentro = bandeja.position + Vector3.up * (b.extents.y * 0.6f + 0.01f);
        Vector3 p0 = t.position; Quaternion r0 = t.rotation;
        const float dur = 1.8f;
        for (float k = 0; k < dur; k += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, k / dur);
            Vector3 centro = Vector3.Lerp(desdeCentro, destinoCentro, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.15f;
            t.position = p0 + (centro - desdeCentro);
            yield return null;
        }
        t.position = p0 + (destinoCentro - desdeCentro);
    }

    // ==================================================================
    // Instrumentos: geometría, entrega, cambio y devolución
    // ==================================================================
    SofaInstrumentoQuirurgico Punta(GameObject inst)
    {
        if (inst == null) return null;
        foreach (var s in FindObjectsByType<SofaInstrumentoQuirurgico>(FindObjectsSortMode.None))
            if (s.instrumentoRaiz == inst || s.transform.IsChildOf(inst.transform)) return s;
        return null;
    }

    /// <summary>Transform de la punta: la PuntaActiva SOFA o, en la clipadora, su "PuntaDemo".</summary>
    Transform PuntaTr(GameObject inst)
    {
        if (inst == null) return null;
        var s = Punta(inst);
        if (s != null) return s.transform;
        return inst.transform.Find("PuntaDemo");
    }

    Vector3 PuntaActual(GameObject inst)
    {
        var p = PuntaTr(inst);
        if (p != null) return p.position;
        Geometria(inst, out var eje, out var atras, out var largo);
        return inst.transform.TransformPoint(atras + eje * largo);
    }

    /// <summary>Eje local (de atrás hacia la punta), punto trasero local y largo del instrumento.</summary>
    void Geometria(GameObject inst, out Vector3 ejeLocal, out Vector3 atrasLocal, out float largo)
    {
        var mf = inst.GetComponent<MeshFilter>();
        Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.3f);
        Vector3 s = b.size; int k = s.x >= s.y && s.x >= s.z ? 0 : (s.y >= s.z ? 1 : 2);
        Vector3 unidad = k == 0 ? Vector3.right : (k == 1 ? Vector3.up : Vector3.forward);
        var p = PuntaTr(inst);
        float signo = 1f;
        if (p != null)
        {
            Vector3 tl = inst.transform.InverseTransformPoint(p.position);
            signo = Mathf.Sign(tl[k] - b.center[k]);
            if (signo == 0f) signo = 1f;
        }
        ejeLocal = unidad * signo;
        atrasLocal = b.center - ejeLocal * b.extents[k];
        largo = s[k];
        if (p != null)
        {
            Vector3 tl = inst.transform.InverseTransformPoint(p.position);
            largo = Mathf.Max(0.05f, Vector3.Dot(tl - atrasLocal, ejeLocal));
        }
    }

    /// <summary>El instrumento vuela de la mesa a la pinza del robot y el robot lo toma (coaxial con su herramienta).</summary>
    IEnumerator Entregar(Brazo b, GameObject inst)
    {
        if (inst == null) yield break;
        Geometria(inst, out var ejeLocal, out var atrasLocal, out _);
        var rb = inst.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        Vector3 p0 = inst.transform.position; Quaternion r0 = inst.transform.rotation;
        const float dur = 1.6f;
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            ObjetivoAgarre(b, inst, ejeLocal, atrasLocal, out var pf, out var rf);
            float u = Mathf.SmoothStep(0f, 1f, t / dur);
            inst.transform.SetPositionAndRotation(Vector3.Lerp(p0, pf, u), Quaternion.Slerp(r0, rf, u));
            if (rb != null) { rb.position = inst.transform.position; rb.rotation = inst.transform.rotation; }
            yield return null;
        }
        ObjetivoAgarre(b, inst, ejeLocal, atrasLocal, out var pFin, out var rFin);
        inst.transform.SetPositionAndRotation(pFin, rFin);
        intercambio.TomarConRobot(inst, b.izq);
        b.instrumento = inst;
        if (usarInstrumentalOpenRST && b.openRST != null)
        {
            b.openRST.Mostrar();                 // TomarConRobot oculta la punta: se reactiva
            b.openRST.CambiarMordaza(MordazaPara(inst));
            b.openRST.Apertura(9f);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; b.ocultosOpenRST.Add(r); }
        }
    }

    InstrumentalOpenRST.Mordaza MordazaPara(GameObject inst)
    {
        if (inst == grasper) return InstrumentalOpenRST.Mordaza.Fenestrada;
        if (inst == hook || inst == tijera) return InstrumentalOpenRST.Mordaza.Maryland;
        if (inst == clipadora) return InstrumentalOpenRST.Mordaza.PortaAgujas;
        return InstrumentalOpenRST.Mordaza.Original;
    }

    void RestaurarOpenRST(Brazo b, bool completo)
    {
        if (b == null) return;
        foreach (var r in b.ocultosOpenRST) if (r != null) r.enabled = true;
        b.ocultosOpenRST.Clear();
        if (b.openRST != null)
        {
            b.openRST.Apertura(0f);
            if (completo) b.openRST.Restaurar();
        }
    }

    void ObjetivoAgarre(Brazo b, GameObject inst, Vector3 ejeLocal, Vector3 atrasLocal, out Vector3 pos, out Quaternion rot)
    {
        Vector3 piv = intercambio.PosicionPivote(b.izq);
        Vector3 ejeRobot = intercambio.RotacionPivote(b.izq) * b.ik.ejeHerramientaLocal;
        Quaternion r = inst.transform.rotation;
        rot = Quaternion.FromToRotation(r * ejeLocal, ejeRobot) * r;
        if (usarInstrumentalOpenRST && b.openRST != null)
        {
            Transform ptr = PuntaTr(inst);
            if (ptr != null)
            {
                Vector3 local = inst.transform.InverseTransformPoint(ptr.position);
                pos = b.openRST.eeLink.position - rot * Vector3.Scale(local, inst.transform.lossyScale);
                return;
            }
        }
        pos = piv - rot * Vector3.Scale(atrasLocal, inst.transform.lossyScale);
    }

    IEnumerator Devolver(Brazo b)
    {
        var inst = b.instrumento;
        if (inst == null) yield break;
        RestaurarOpenRST(b, completo: false);
        intercambio.SoltarDelBrazo(b.izq);
        b.instrumento = null;
        if (!posesMesa.TryGetValue(inst, out var pose)) yield break;
        var rb = inst.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        Vector3 p0 = inst.transform.position; Quaternion r0 = inst.transform.rotation;
        const float dur = 1.6f;
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / dur);
            inst.transform.SetPositionAndRotation(Vector3.Lerp(p0, pose.p, u), Quaternion.Slerp(r0, pose.r, u));
            if (rb != null) { rb.position = inst.transform.position; rb.rotation = inst.transform.rotation; }
            yield return null;
        }
        ColocarEnMesa(inst, pose);
    }

    /// <summary>Retira el instrumento actual por su trocar, lo devuelve a la mesa, recibe el nuevo y lo inserta.</summary>
    IEnumerator CambiarInstrumento(Brazo b, int puerto, GameObject nuevo)
    {
        yield return Retirar(b, puerto);
        yield return Devolver(b);
        yield return Entregar(b, nuevo);
        yield return Insertar(b, puerto, 0.06f);
    }

    void ColocarLaparoscopio()
    {
        if (laparoscopio == null) return;
        var f = preparacion.Fulcro(2);
        if (f == null) return;
        var cam = laparoscopio.GetComponentInChildren<Camera>();
        Vector3 objetivo = zonaCistica != null ? zonaCistica.position : (zonaCalot != null ? zonaCalot.position : f.position + Vector3.down);
        Vector3 d = (objetivo - f.position).normalized;
        Geometria(laparoscopio, out var ejeLocal, out var atrasLocal, out var largo);
        Quaternion r = laparoscopio.transform.rotation;
        Quaternion rot = Quaternion.FromToRotation(r * ejeLocal, d) * r;
        // Óptica 4,5 cm más allá del fulcro (justo fuera de la cánula del trocar), sobre la línea trocar -> vesícula
        Vector3 puntaLocal = cam != null ? laparoscopio.transform.InverseTransformPoint(cam.transform.position) : atrasLocal + ejeLocal * largo;
        Vector3 puntaDeseada = CentroFulcro(f) + d * 0.045f;
        Vector3 pos = puntaDeseada - rot * Vector3.Scale(puntaLocal, laparoscopio.transform.lossyScale);
        var rb = laparoscopio.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        laparoscopio.transform.SetPositionAndRotation(pos, rot);
        if (rb != null) { rb.position = pos; rb.rotation = rot; }
        if (cam != null)
        {
            if (!camaraLapGuardada) { camaraLap = cam; camaraLapRotLocal = cam.transform.localRotation; camaraLapFov = cam.fieldOfView; camaraLapGuardada = true; }
            cam.transform.LookAt(objetivo); // la óptica mira al campo operatorio (se restaura al salir)
            cam.fieldOfView = Mathf.Max(cam.fieldOfView, 65f);
        }
    }

    // ==================================================================
    // Movimientos
    // ==================================================================
    IEnumerator MoverPivote(Brazo b, Vector3 destino, Vector3 eje, float velocidad)
    {
        Vector3 inicio = b.objetivo; Vector3 eje0 = b.eje;
        float dist = Vector3.Distance(inicio, destino);
        float dur = Mathf.Max(0.3f, dist / Mathf.Max(0.01f, velocidad));
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / dur);
            b.objetivo = Vector3.Lerp(inicio, destino, u);
            b.eje = Vector3.Slerp(eje0, eje, u).normalized;
            yield return null;
        }
        b.objetivo = destino; b.eje = eje.normalized;
        float limite = Time.time + 4f;
        while (Time.time < limite && !(b.ik.EnObjetivo(0.5f) && b.errorIK < 0.01f)) yield return null;
        if (b.errorIK >= 0.01f) Debug.LogWarning($"🤖 Demo: el brazo {(b.izq ? "izquierdo" : "derecho")} no alcanza el objetivo (error {b.errorIK * 100f:F1} cm).");
    }

    /// <summary>Lleva la PUNTA del instrumento a un punto respetando el fulcro del trocar.</summary>
    IEnumerator MoverPunta(Brazo b, Transform fulcroTr, Vector3 destino, float velocidad = -1f)
    {
        if (velocidad <= 0f) velocidad = velocidadDentro;
        var inst = b.instrumento; if (inst == null || fulcroTr == null) yield break;
        var punta = PuntaTr(inst); if (punta == null) yield break;
        var fulcro = inst.GetComponent<ControladorFulcroLaparoscopio>();
        Vector3 F = CentroFulcro(fulcroTr);
        float largo = Vector3.Distance(intercambio.PosicionPivote(b.izq), punta.position);
        Vector3 inicio = punta.position;
        float dur = Mathf.Max(0.3f, Vector3.Distance(inicio, destino) / velocidad);
        for (float t = 0; t <= dur; t += Time.deltaTime)
        {
            Vector3 T = Vector3.Lerp(inicio, destino, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur)));
            Vector3 u = (T - F).normalized;
            Vector3 mano;
            if (fulcro == null || !fulcro.CalcularManoParaPunta(T, punta, out mano))
                mano = T - u * largo; // sin anclaje (o clipadora): línea recta coaxial por el trocar
            b.objetivo = mano;
            b.eje = u;
            yield return null;
        }
        float limite = Time.time + 3f;
        while (Time.time < limite && !b.ik.EnObjetivo(0.5f)) yield return null;
    }

    IEnumerator Insertar(Brazo b, int puerto, float profundidad)
    {
        var inst = b.instrumento; var fTr = preparacion.Fulcro(puerto);
        if (inst == null || fTr == null) yield break;
        Transform puntaIns = PuntaTr(inst);
        float largo = puntaIns != null ? Vector3.Distance(intercambio.PosicionPivote(b.izq), puntaIns.position) : 0.33f;
        Vector3 F = CentroFulcro(fTr);
        Vector3 d = -fTr.parent.right; // el +X local del trocar apunta hacia afuera (cabeza)
        // Aproximación en dos tiempos para que el brazo no caiga de golpe sobre el trocar:
        // 1º a un punto de espera 6 cm por encima de la línea de inserción, 2º descenso lento ya alineado.
        // La punta espera FUERA del trocar (3 cm por encima de su cabezal) y entra por su eje:
        // así el instrumento recorre toda la cánula por dentro, sin atravesar sus paredes.
        float fuera = FueraDelTrocar(fTr);
        Vector3 preInsercion = F - d * fuera - d * largo;
        yield return MoverPivote(b, preInsercion + Vector3.up * 0.06f, d, velocidadFuera);
        yield return MoverPivote(b, preInsercion, d, velocidadFuera * 0.4f);
        float asentar = Time.time + 2f;
        while (Time.time < asentar && !(b.ik.EnObjetivo(0.4f) && b.errorIK < 0.005f)) yield return null;
        Vector3 desde = F - d * fuera, hasta = F + d * profundidad;
        float dur = Vector3.Distance(desde, hasta) / 0.045f;
        for (float t = 0; t <= dur; t += Time.deltaTime)
        {
            Vector3 T = Vector3.Lerp(desde, hasta, t / dur);
            b.objetivo = T - d * largo; b.eje = d;
            yield return null;
        }
        b.objetivo = hasta - d * largo;
        float limite = Time.time + 3f;
        while (Time.time < limite && !b.ik.EnObjetivo(0.5f)) yield return null;
        var fulcro = inst.GetComponent<ControladorFulcroLaparoscopio>();
        if (fulcro != null && !fulcro.estaAnclado) Debug.LogWarning($"🤖 Demo: '{inst.name}' no quedó anclado en el trocar.");
    }

    IEnumerator Retirar(Brazo b, int puerto)
    {
        var inst = b.instrumento; var fTr = preparacion.Fulcro(puerto);
        if (inst == null || fTr == null) yield break;
        var punta = PuntaTr(inst); if (punta == null) yield break;
        float largo = Vector3.Distance(intercambio.PosicionPivote(b.izq), punta.position);
        Vector3 F = CentroFulcro(fTr);
        Vector3 u = (punta.position - F).normalized;
        // Sale por el eje hasta que la punta rebasa el cabezal del trocar (antes se quedaba dentro)
        Vector3 desde = punta.position, hasta = F - u * FueraDelTrocar(fTr);
        float dur = Vector3.Distance(desde, hasta) / 0.06f;
        for (float t = 0; t <= dur; t += Time.deltaTime)
        {
            Vector3 T = Vector3.Lerp(desde, hasta, t / dur);
            b.objetivo = T - u * largo; b.eje = u;
            yield return null;
        }
        float limite = Time.time + 3f;
        while (Time.time < limite && !b.ik.EnObjetivo(0.5f)) yield return null;
    }

    IEnumerator VolverAPoseInicial(Brazo b)
    {
        b.activo = false;
        float limite = Time.time + 8f;
        while (Time.time < limite)
        {
            bool listo = true;
            for (int i = 0; i < 6; i++)
            {
                var d = b.ik.juntas[i].xDrive;
                d.target = Mathf.MoveTowards(d.target, b.qInicio[i], velocidadArticular * Time.deltaTime);
                b.ik.juntas[i].xDrive = d;
                if (Mathf.Abs(d.target - b.qInicio[i]) > 0.1f) listo = false;
            }
            if (listo) break;
            yield return null;
        }
    }

    // ==================================================================
    // Utilidades
    // ==================================================================
    static Vector3 CentroFulcro(Transform fTr)
    {
        var sc = fTr.GetComponent<SphereCollider>();
        return sc != null ? fTr.TransformPoint(sc.center) : fTr.position;
    }

    /// <summary>Distancia desde el fulcro, hacia afuera, a la que la punta ya está 3 cm fuera del cabezal del trocar.</summary>
    static float FueraDelTrocar(Transform fTr)
    {
        float altura = ControladorFulcroLaparoscopio.AlturaCabezalSobreFulcro(fTr);
        return altura > 0.03f ? altura + 0.03f : 0.12f;
    }

    static Vector3 Acercamiento(Transform fTr, Vector3 zona, float antes)
    {
        Vector3 F = CentroFulcro(fTr);
        Vector3 u = (zona - F).normalized;
        return zona - u * antes;
    }

    void Accion(GameObject inst, bool activa)
    {
        var p = Punta(inst);
        if (p != null) p.ForzarAccion(activa);
        if (usarInstrumentalOpenRST)
            foreach (var b in new[] { der, izq })
                if (b != null && b.instrumento == inst && b.openRST != null)
                    b.openRST.Apertura(activa ? 1.5f : 9f);
    }

    /// <summary>
    /// Electrocauterio del Hook: humo y chispas en la punta. NO activa el corte SOFA: el rayo de corte mide 8 cm
    /// (fijo al crearse en C++) y dañaría duodeno o estómago detrás del plano de disección.
    /// </summary>
    void Cauterizar(GameObject inst, float duracion)
    {
        var punta = PuntaTr(inst);
        if (punta == null || raizDemo == null) return;
        if (efectoCauterio == null) efectoCauterio = CrearEfectoCauterio();
        efectoCauterio.transform.position = punta.position;
        efectoCauterio.Emit(12);
        StartCoroutine(SeguirCauterio(punta, duracion));
    }

    private ParticleSystem efectoCauterio;

    IEnumerator SeguirCauterio(Transform punta, float duracion)
    {
        for (float t = 0; t < duracion; t += Time.deltaTime)
        {
            if (efectoCauterio == null || punta == null) yield break;
            efectoCauterio.transform.position = punta.position;
            if (Random.value < 0.5f) efectoCauterio.Emit(2);
            yield return null;
        }
    }

    ParticleSystem CrearEfectoCauterio()
    {
        var go = new GameObject("Cauterio");
        go.transform.SetParent(raizDemo, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.02f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.009f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0.55f), new Color(1f, 0.6f, 0.2f, 0.9f));
        main.gravityModifier = -0.02f; // el humo sube
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.0015f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.gray, 1f) },
                  new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        var sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh2 != null)
        {
            var m = new Material(sh2);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            r.sharedMaterial = m;
        }
        return ps;
    }

    void Narrar(string titulo, string texto)
    {
        Fase = titulo;
        Debug.Log($"🩺 Demo automática: {titulo}");
        if (narrador != null) narrador.MostrarMensaje(titulo, texto);
    }

    void MarcarZona(Transform zona, float radio = 0.02f)
    {
        if (narrador != null) narrador.MarcarZona(zona, radio);
    }
}
