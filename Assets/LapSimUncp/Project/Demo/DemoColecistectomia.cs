using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

/// <summary>Cómo se pasa al siguiente paso de la demo.</summary>
public enum CondicionAvance
{
    [Tooltip("El usuario avanza con N / botón Y del mando izquierdo.")]
    Manual,
    [Tooltip("Avanza solo al cumplirse 'tiempoPaso'.")]
    Tiempo,
    [Tooltip("Avanza cuando 'herramientaReal' permanece dentro de la zona anatómica. Si falta alguna referencia se comporta como Manual.")]
    HerramientaEnZona
}

/// <summary>Un paso del procedimiento guiado.</summary>
[Serializable]
public class PasoDemo
{
    public string titulo = "Paso";
    [TextArea(2, 5)] public string descripcion = "";
    public AudioClip audio;

    [Header("Zona anatómica (marcador)")]
    [Tooltip("Transform en la anatomía (p. ej. un hijo vacío colocado sobre la vesícula de SOFA). Solo se lee su posición.")]
    public Transform zonaAnatomica;
    public float radioZona = 0.03f;

    [Header("Holograma de la herramienta")]
    [Tooltip("Herramienta cuyo aspecto se copia para el holograma. Solo se copian mallas: la herramienta real no se toca.")]
    public GameObject modeloFantasma;
    [Tooltip("Puntos que recorre el holograma en bucle. Vacío = el holograma se queda en la zona anatómica.")]
    public Transform[] trayectoriaFantasma = new Transform[0];
    public float duracionTrayectoria = 4f;

    [Header("Avance")]
    public CondicionAvance condicion = CondicionAvance.Manual;
    [Tooltip("Punta de la herramienta real (p. ej. su PuntaActiva). Se usa para HerramientaEnZona y para orientar el holograma por su punta. Solo se lee su posición.")]
    public Transform herramientaReal;
    public float tiempoPaso = 6f;
    [Tooltip("Segundos que la herramienta debe mantenerse en la zona.")]
    public float tiempoEnZona = 1f;
}

/// <summary>
/// Demo / procedimiento guiado de colecistectomía laparoscópica.
///
/// NO destructivo: nunca modifica herramientas, prefabs, SOFA ni los scripts de seguimiento.
/// Solo lee posiciones y crea sus propios objetos bajo "DemoColecistectomia_Runtime"
/// (panel de texto que sigue la cabeza, marcadores pulsantes y hologramas translúcidos),
/// que se destruyen al detener la demo.
///
/// La controla <see cref="GestorModosSimulador"/>. Controles en modo demo:
///   N / botón Y (mando izq.) : siguiente paso      B : paso anterior      Z : reiniciar
/// </summary>
public class DemoColecistectomia : MonoBehaviour
{
    [Header("Pasos del procedimiento")]
    public List<PasoDemo> pasos = new List<PasoDemo>();

    [Header("Apariencia")]
    [Tooltip("Material translúcido para hologramas y marcadores. Vacío = se crea uno automáticamente.")]
    public Material materialHolograma;
    public Color colorHolograma = new Color(0.2f, 0.8f, 1f, 0.35f);
    public Color colorZona = new Color(1f, 0.85f, 0.1f, 0.45f);
    public Color colorZonaCumplida = new Color(0.2f, 1f, 0.3f, 0.55f);

    [Header("Panel de texto (sigue la cabeza)")]
    public float distanciaPanel = 1.2f;
    public float alturaPanel = -0.25f;
    public float suavizadoPanel = 3f;
    public float escalaTexto = 0.012f;

    [Header("Controles")]
    public KeyCode teclaSiguiente = KeyCode.N;
    public KeyCode teclaAnterior = KeyCode.B;
    public KeyCode teclaReiniciar = KeyCode.Z;
    public bool usarBotonYMandoIzquierdo = true;

    public bool Activa { get; private set; }
    public int PasoActual { get; private set; } = -1;

    private Transform raiz;
    private Transform panel;
    private TextMesh textoPanel;
    private AudioSource audioSource;
    private GameObject marcador;
    private Renderer rendMarcador;
    private GameObject fantasma;
    private Material matHolo, matZona;
    private float tiempoInicioPaso;
    private float tiempoAcumuladoEnZona;
    private bool completado;
    private bool botonYAnterior;
    private InputDevice mandoIzq;
    private float refrescoMando = 1f;

    // =====================================================================
    // Pasos por defecto (se rellenan al añadir el componente o con "Reset")
    // =====================================================================
    void Reset()
    {
        pasos = new List<PasoDemo>
        {
            new PasoDemo
            {
                titulo = "Paso 1 · Inserción de instrumentos",
                descripcion = "Toma el Grasper y la Tijera de la mesa con el GATILLO (índice) e introdúcelos por los trocares. " +
                              "Observa el efecto fulcro: el instrumento pivota en el punto de entrada.",
                condicion = CondicionAvance.Manual
            },
            new PasoDemo
            {
                titulo = "Paso 2 · Tracción del fondo vesicular",
                descripcion = "Lleva el Grasper al fondo de la vesícula y pulsa el GRIP (dedo medio) para sujetarla. " +
                              "Tracciona hacia arriba y hacia el hombro derecho del paciente para exponer el hilio.",
                condicion = CondicionAvance.HerramientaEnZona
            },
            new PasoDemo
            {
                titulo = "Paso 3 · Triángulo de Calot y corte cístico",
                descripcion = "Expón el triángulo de Calot. Con la Tijera, pulsa el GRIP sobre el conducto y la arteria císticos " +
                              "(previamente clipados) para seccionarlos.",
                condicion = CondicionAvance.HerramientaEnZona
            },
            new PasoDemo
            {
                titulo = "Paso 4 · Disección, liberación y extracción",
                descripcion = "Diseca la vesícula del lecho hepático de abajo hacia arriba, libérala por completo " +
                              "y extráela por el trocar.",
                condicion = CondicionAvance.Manual
            }
        };
    }

    // =====================================================================
    // API (la llama GestorModosSimulador)
    // =====================================================================
    public void IniciarDemo()
    {
        if (Activa) return;
        if (pasos == null || pasos.Count == 0) Reset();
        Activa = true;
        CrearObjetosRuntime();
        IrAPaso(0);
    }

    // ---------- Modo "solo narración" (lo usa la Demo automática) ----------
    private bool soloNarracion;
    private Transform zonaNarracion;
    private float radioNarracion = 0.02f;

    /// <summary>Crea el panel de texto y el marcador sin pasos, hologramas ni controles.</summary>
    public void IniciarNarracion()
    {
        if (Activa) DetenerDemo();
        soloNarracion = true;
        Activa = true;
        CrearObjetosRuntime();
        PasoActual = -1;
    }

    public void MostrarMensaje(string titulo, string texto)
    {
        if (textoPanel != null) textoPanel.text = $"{titulo}\n\n{Envolver(texto, 48)}";
    }

    /// <summary>Resalta una zona anatómica (null = ocultar el marcador).</summary>
    public void MarcarZona(Transform zona, float radio = 0.02f)
    {
        zonaNarracion = zona;
        radioNarracion = radio;
        if (marcador != null) marcador.SetActive(zona != null);
    }

    public void DetenerDemo()
    {
        if (!Activa) return;
        soloNarracion = false;
        zonaNarracion = null;
        Activa = false;
        PasoActual = -1;
        if (raiz != null) Destroy(raiz.gameObject);
        raiz = null; panel = null; textoPanel = null; marcador = null; fantasma = null; audioSource = null;
        if (matHolo != null && matHolo != materialHolograma) Destroy(matHolo);
        if (matZona != null) Destroy(matZona);
        matHolo = matZona = null;
    }

    public void Siguiente()
    {
        if (!Activa) return;
        if (completado) { IrAPaso(0); return; }
        if (PasoActual + 1 < pasos.Count) IrAPaso(PasoActual + 1);
        else MostrarCompletado();
    }

    public void Anterior()
    {
        if (!Activa) return;
        IrAPaso(Mathf.Max(0, (completado ? pasos.Count : PasoActual) - 1));
    }

    public void Reiniciar() { if (Activa) IrAPaso(0); }

    void OnDisable() => DetenerDemo();

    // =====================================================================
    // Bucle
    // =====================================================================
    void Update()
    {
        if (!Activa) return;

        if (soloNarracion)
        {
            ActualizarPanel();
            if (marcador != null && zonaNarracion != null)
            {
                marcador.transform.position = zonaNarracion.position;
                float pulso = 1f + 0.15f * Mathf.Sin(Time.time * 4f);
                marcador.transform.localScale = Vector3.one * Mathf.Max(0.005f, radioNarracion * 2f) * pulso;
            }
            return;
        }

        if (Input.GetKeyDown(teclaSiguiente)) Siguiente();
        if (Input.GetKeyDown(teclaAnterior)) Anterior();
        if (Input.GetKeyDown(teclaReiniciar)) Reiniciar();
        if (usarBotonYMandoIzquierdo)
        {
            refrescoMando += Time.unscaledDeltaTime;
            if (!mandoIzq.isValid && refrescoMando >= 1f) { refrescoMando = 0f; mandoIzq = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand); }
            bool y = false;
            if (mandoIzq.isValid) mandoIzq.TryGetFeatureValue(CommonUsages.secondaryButton, out y);
            if (y && !botonYAnterior) Siguiente();
            botonYAnterior = y;
        }

        ActualizarPanel();
        if (completado || PasoActual < 0 || PasoActual >= pasos.Count) return;

        PasoDemo p = pasos[PasoActual];
        ActualizarMarcador(p);
        ActualizarFantasma(p);
        EvaluarAvance(p);
    }

    void EvaluarAvance(PasoDemo p)
    {
        float t = Time.time - tiempoInicioPaso;
        switch (p.condicion)
        {
            case CondicionAvance.Tiempo:
                if (t >= p.tiempoPaso) Siguiente();
                break;
            case CondicionAvance.HerramientaEnZona:
                if (p.herramientaReal == null || p.zonaAnatomica == null) break; // se comporta como Manual
                if (HerramientaDentro(p))
                {
                    tiempoAcumuladoEnZona += Time.deltaTime;
                    if (tiempoAcumuladoEnZona >= p.tiempoEnZona) Siguiente();
                }
                else tiempoAcumuladoEnZona = 0f;
                break;
        }
    }

    bool HerramientaDentro(PasoDemo p) =>
        p.herramientaReal != null && p.zonaAnatomica != null &&
        Vector3.Distance(p.herramientaReal.position, p.zonaAnatomica.position) <= p.radioZona;

    // =====================================================================
    // Pasos
    // =====================================================================
    void IrAPaso(int indice)
    {
        completado = false;
        PasoActual = Mathf.Clamp(indice, 0, pasos.Count - 1);
        tiempoInicioPaso = Time.time;
        tiempoAcumuladoEnZona = 0f;

        PasoDemo p = pasos[PasoActual];

        if (fantasma != null) Destroy(fantasma);
        fantasma = p.modeloFantasma != null ? CrearHolograma(p.modeloFantasma, p.herramientaReal) : null;

        if (marcador != null)
        {
            marcador.SetActive(p.zonaAnatomica != null);
            float d = Mathf.Max(0.005f, p.radioZona * 2f);
            marcador.transform.localScale = Vector3.one * d;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            if (p.audio != null) audioSource.PlayOneShot(p.audio);
        }

        EscribirTexto(p);
        Debug.Log($"🩺 Demo: {p.titulo}");
    }

    void MostrarCompletado()
    {
        completado = true;
        if (fantasma != null) Destroy(fantasma);
        if (marcador != null) marcador.SetActive(false);
        if (textoPanel != null)
            textoPanel.text = "PROCEDIMIENTO COMPLETADO\n\n" + Envolver("Pulsa N / Y para repetir la demo o M / Menú para volver a Práctica libre.", 48);
    }

    void EscribirTexto(PasoDemo p)
    {
        if (textoPanel == null) return;
        string avance = p.condicion == CondicionAvance.Manual || (p.condicion == CondicionAvance.HerramientaEnZona && (p.herramientaReal == null || p.zonaAnatomica == null))
            ? "Pulsa N / Y para continuar"
            : p.condicion == CondicionAvance.Tiempo ? "Avanza automáticamente" : "Lleva la herramienta a la zona marcada";
        textoPanel.text = $"{p.titulo}   ({PasoActual + 1}/{pasos.Count})\n\n{Envolver(p.descripcion, 48)}\n\n<{avance}>";
    }

    static string Envolver(string texto, int ancho)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new System.Text.StringBuilder();
        int col = 0;
        foreach (string palabra in texto.Split(' '))
        {
            if (col > 0 && col + palabra.Length + 1 > ancho) { sb.Append('\n'); col = 0; }
            else if (col > 0) { sb.Append(' '); col++; }
            sb.Append(palabra);
            col += palabra.Length;
        }
        return sb.ToString();
    }

    // =====================================================================
    // Objetos visuales propios (nunca se tocan los objetos reales)
    // =====================================================================
    void CrearObjetosRuntime()
    {
        raiz = new GameObject("DemoColecistectomia_Runtime").transform;

        matHolo = materialHolograma != null ? materialHolograma : CrearMaterialTransparente(colorHolograma);
        matZona = CrearMaterialTransparente(colorZona);

        // Panel de texto
        panel = new GameObject("PanelDemo").transform;
        panel.SetParent(raiz, false);
        var fondo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(fondo.GetComponent<Collider>()); // inmediato: nunca debe existir en la física
        fondo.name = "Fondo";
        fondo.transform.SetParent(panel, false);
        fondo.transform.localPosition = new Vector3(0f, 0f, 0.005f);
        fondo.transform.localScale = new Vector3(0.75f, 0.42f, 1f);
        fondo.GetComponent<Renderer>().sharedMaterial = CrearMaterialTransparente(new Color(0f, 0f, 0f, 0.6f));

        var goTexto = new GameObject("Texto");
        goTexto.transform.SetParent(panel, false);
        textoPanel = goTexto.AddComponent<TextMesh>();
        textoPanel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        goTexto.GetComponent<MeshRenderer>().sharedMaterial = textoPanel.font.material;
        textoPanel.fontSize = 48;
        textoPanel.characterSize = escalaTexto * 0.25f;
        textoPanel.anchor = TextAnchor.MiddleCenter;
        textoPanel.alignment = TextAlignment.Center;
        textoPanel.color = Color.white;
        textoPanel.richText = false;

        audioSource = panel.gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        ColocarPanel(true);

        // Marcador de zona anatómica (esfera pulsante sin colisionador)
        marcador = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(marcador.GetComponent<Collider>()); // inmediato: no debe tocar SOFA ni los sockets XR
        marcador.name = "MarcadorZona";
        marcador.transform.SetParent(raiz, false);
        rendMarcador = marcador.GetComponent<Renderer>();
        rendMarcador.sharedMaterial = matZona;
        rendMarcador.shadowCastingMode = ShadowCastingMode.Off;
        marcador.SetActive(false);
    }

    void ActualizarPanel() => ColocarPanel(false);

    void ColocarPanel(bool inmediato)
    {
        if (panel == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 adelante = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (adelante.sqrMagnitude < 1e-4f) adelante = cam.transform.forward;
        adelante.Normalize();
        Vector3 objetivo = cam.transform.position + adelante * distanciaPanel + Vector3.up * alturaPanel;
        Quaternion rot = Quaternion.LookRotation(objetivo - cam.transform.position, Vector3.up);
        float a = inmediato ? 1f : 1f - Mathf.Exp(-suavizadoPanel * Time.deltaTime);
        panel.position = Vector3.Lerp(panel.position, objetivo, a);
        panel.rotation = Quaternion.Slerp(panel.rotation, rot, a);
    }

    void ActualizarMarcador(PasoDemo p)
    {
        if (marcador == null || p.zonaAnatomica == null) return;
        marcador.transform.position = p.zonaAnatomica.position;
        float pulso = 1f + 0.15f * Mathf.Sin(Time.time * 4f);
        marcador.transform.localScale = Vector3.one * Mathf.Max(0.005f, p.radioZona * 2f) * pulso;
        Color c = HerramientaDentro(p) ? colorZonaCumplida : colorZona;
        AsignarColor(matZona, c);
    }

    void ActualizarFantasma(PasoDemo p)
    {
        if (fantasma == null) return;
        Transform[] ruta = p.trayectoriaFantasma;
        var puntos = new List<Vector3>();
        if (ruta != null) foreach (var t in ruta) if (t != null) puntos.Add(t.position);
        if (puntos.Count == 0 && p.zonaAnatomica != null) puntos.Add(p.zonaAnatomica.position);
        if (puntos.Count == 0) return;

        Vector3 puntoPunta;
        Vector3 direccion;
        if (puntos.Count >= 2)
        {
            // Recorre la ruta en bucle; la PUNTA del holograma va sobre la ruta
            float u = Mathf.Repeat((Time.time - tiempoInicioPaso) / Mathf.Max(0.1f, p.duracionTrayectoria), 1f) * (puntos.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(u), puntos.Count - 2);
            float f = Mathf.SmoothStep(0f, 1f, u - i);
            puntoPunta = Vector3.Lerp(puntos[i], puntos[i + 1], f);
            direccion = puntos[i + 1] - puntos[i];
        }
        else
        {
            puntoPunta = puntos[0];
            direccion = Vector3.zero; // un solo punto: se mantiene la orientación original
        }
        ColocarFantasmaPorPunta(puntoPunta, direccion);
    }

    // Eje del holograma (raíz -> punta) en coordenadas locales de la raíz
    private bool fantasmaTieneEje;
    private Vector3 fantasmaPuntaLocal;
    private Quaternion fantasmaRotOriginal = Quaternion.identity;

    /// <summary>
    /// Coloca el holograma para que su punta quede en <paramref name="punta"/>. Si hay dirección,
    /// alinea el eje del instrumento (raíz -> punta) con ella, como al entrar por el trocar.
    /// </summary>
    void ColocarFantasmaPorPunta(Vector3 punta, Vector3 direccion)
    {
        Transform tf = fantasma.transform;
        Quaternion rot = fantasmaRotOriginal;
        if (fantasmaTieneEje && direccion.sqrMagnitude > 1e-8f)
        {
            Vector3 ejeMundo = fantasmaRotOriginal * Vector3.Scale(fantasmaPuntaLocal, tf.localScale);
            rot = Quaternion.FromToRotation(ejeMundo, direccion.normalized) * fantasmaRotOriginal;
        }
        tf.rotation = rot;
        Vector3 offsetPunta = fantasmaTieneEje ? rot * Vector3.Scale(fantasmaPuntaLocal, tf.localScale) : Vector3.zero;
        tf.position = punta - offsetPunta;
    }

    /// <summary>
    /// Copia SOLO las mallas visibles de la herramienta (MeshFilter + MeshRenderer) en objetos nuevos.
    /// No se usa Instantiate: así no se duplican scripts de SOFA, XR, colisionadores ni rigidbodies.
    /// </summary>
    GameObject CrearHolograma(GameObject origen, Transform punta)
    {
        var raizHolo = new GameObject("Holograma_" + origen.name);
        raizHolo.transform.SetParent(raiz, false);
        raizHolo.transform.SetPositionAndRotation(origen.transform.position, origen.transform.rotation);
        raizHolo.transform.localScale = origen.transform.lossyScale; // mismas unidades que la herramienta real

        // Eje del instrumento: de la raíz a su punta (p. ej. PuntaActiva), en coordenadas locales.
        // No se exige que la punta sea hija: en Play, SofaInstrumentoQuirurgico la mueve a SofaContext
        // (PuntaActiva_<instrumento>), pero sigue a la herramienta a distancia fija.
        fantasmaRotOriginal = origen.transform.rotation;
        fantasmaTieneEje = false;
        if (punta != null && punta != origen.transform)
        {
            float distancia = Vector3.Distance(origen.transform.position, punta.position);
            if (distancia > 1e-5f && distancia < 1f) // descarta referencias que no pertenecen al instrumento
            {
                fantasmaPuntaLocal = origen.transform.InverseTransformPoint(punta.position);
                fantasmaTieneEje = true;
            }
        }

        foreach (MeshFilter mf in origen.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled) continue;

            var copia = new GameObject(mf.name);
            copia.transform.SetParent(raizHolo.transform, false);
            // Pose relativa a la raíz de la herramienta
            Matrix4x4 rel = origen.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            copia.transform.localPosition = rel.GetColumn(3);
            copia.transform.localRotation = Quaternion.LookRotation(rel.GetColumn(2), rel.GetColumn(1));
            copia.transform.localScale = new Vector3(rel.GetColumn(0).magnitude, rel.GetColumn(1).magnitude, rel.GetColumn(2).magnitude);

            copia.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = copia.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = matHolo;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return raizHolo;
    }

    static Material CrearMaterialTransparente(Color color)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        Material m;
        if (sh != null)
        {
            m = new Material(sh);
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);     // Alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            m = new Material(Shader.Find("Sprites/Default"));
        }
        m.name = "DemoHolograma (runtime)";
        AsignarColor(m, color);
        return m;
    }

    static void AsignarColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }
}
