using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ControladorFulcroLaparoscopio : MonoBehaviour
{
    [Header("Etiqueta del Trocar")]
    public string tagTrocar = "PivoteTrocar";

    [Header("Estado en Tiempo Real (Solo lectura)")]
    public bool estaAnclado = false;
    public Transform trocarActual;
    public Transform mandoVRActual;

    [Header("Seguridad del Fulcro")]
    [Tooltip("Distancia mínima (m) entre la base del instrumento (mano/robot) y el trocar. Evita que el instrumento gire 180° si la mano cruza el punto de fulcro.")]
    public float distanciaMinimaAlFulcro = 0.02f;

    [Header("Topes del trocar (centro de movimiento remoto, como en daVinci)")]
    [Tooltip("La mano / el cabezal del robot no puede pasar del cabezal del trocar: el instrumento no entra más de lo que su tubo permite.")]
    public bool topeCabezalTrocar = true;
    [Tooltip("Holgura (m) entre la mano y el cabezal del trocar en el tope de entrada.")]
    public float margenCabezal = 0.005f;
    [Tooltip("Al retirar el instrumento sigue guiado por el eje del trocar hasta que la punta sale por el cabezal; así no atraviesa las paredes de la cánula.")]
    public bool guiarSalidaPorCanula = true;

    /// <summary>Veces que el planificador (demo) pidió más profundidad de la que permite el tope del cabezal.</summary>
    [HideInInspector] public int recortesPorTope;

    private float alturaCabezal;        // del fulcro al borde exterior del cabezal del trocar actual
    private float limiteBase;           // distancia mínima efectiva base -> fulcro
    private bool saliendoGuiado;        // la punta ya salió de la esfera del fulcro pero sigue dentro de la cánula
    private float baseAlSalir;

    /// <summary>Distancia (m) del fulcro al borde exterior del cabezal del trocar; 0 si no se puede medir.</summary>
    public static float AlturaCabezalSobreFulcro(Transform puntoEntrada)
    {
        Transform trocar = puntoEntrada != null ? puntoEntrada.parent : null;
        MeshFilter mf = trocar != null ? trocar.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return 0f;
        // El +X local del trocar apunta hacia afuera (cabezal)
        float xFulcro = trocar.InverseTransformPoint(puntoEntrada.position).x;
        return Mathf.Clamp((mf.sharedMesh.bounds.max.x - xFulcro) * Mathf.Abs(trocar.lossyScale.x), 0f, 0.3f);
    }

    // Lista compartida entre todos los instrumentos para saber qué trocares ya están ocupados
    private static readonly HashSet<Transform> trocaresOcupados = new HashSet<Transform>();

    // Si "Enter Play Mode Options" desactiva la recarga de dominio, la lista estática sobrevive entre Plays
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarTrocaresOcupados() => trocaresOcupados.Clear();

    // Última dirección válida base -> trocar (para no invertir el instrumento)
    private Vector3 direccionAnterior;

    // ---------- API de solo lectura (Demo automática / UI) ----------
    /// <summary>Centro del fulcro (esfera del trocar) o la posición propia si no está anclado.</summary>
    public Vector3 PuntoFulcro => ObtenerCentroSphereCollider();

    /// <summary>
    /// Calcula dónde debe estar la mano/punta del robot (mandoVRActual) para que <paramref name="punta"/>
    /// del instrumento llegue a <paramref name="puntaDeseada"/> respetando el fulcro. Solo vale si está anclado.
    /// </summary>
    public bool CalcularManoParaPunta(Vector3 puntaDeseada, Transform punta, out Vector3 mano)
    {
        mano = mandoVRActual != null ? mandoVRActual.position : transform.position;
        if (!estaAnclado || trocarActual == null || mandoVRActual == null || punta == null) return false;

        Vector3 fulcro = ObtenerCentroSphereCollider();
        Vector3 baseActual = mandoVRActual.position + desfaseManoAlEjeDelTubo;
        Vector3 u = fulcro - baseActual;
        if (u.sqrMagnitude < 1e-8f) return false;
        u.Normalize();
        float largoBasePunta = Vector3.Dot(punta.position - baseActual, u);   // constante (instrumento rígido)

        Vector3 uDeseado = puntaDeseada - fulcro;
        if (uDeseado.sqrMagnitude < 1e-8f) uDeseado = u;
        uDeseado.Normalize();
        // La base debe quedar fuera del trocar: mismo tope que aplica ActualizarFulcroVR
        float pedida = Vector3.Distance(puntaDeseada, fulcro);
        float maxima = largoBasePunta - Mathf.Max(distanciaMinimaAlFulcro, limiteBase);
        if (pedida > maxima + 0.001f) recortesPorTope++;
        float profundidad = Mathf.Min(pedida, maxima);
        Vector3 baseDeseada = fulcro + uDeseado * profundidad - uDeseado * largoBasePunta;
        mano = baseDeseada - desfaseManoAlEjeDelTubo;
        return true;
    }

    private XRGrabInteractable grabInteractable;
    private Rigidbody rb;

    private Quaternion rotacionInicialInstrumento;
    private Vector3 direccionInicialTubo;
    private Vector3 ejeLocalTuboHaciaAdentro;
    
    private Vector3 puntoLocalEnElTubo; 
    private Vector3 desfaseManoAlEjeDelTubo;
    private float distanciaManoAPuntoTubo;

    private void Awake()
    {
        Physics.autoSyncTransforms = true;

        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        // Al nacer (incluyendo cuando es recién clonado), empieza desanclado hasta que toque su propio trocar
        estaAnclado = false;
        trocarActual = null;
        mandoVRActual = null;

        if (grabInteractable != null)
        {
            grabInteractable.useDynamicAttach = true;
            grabInteractable.selectEntered.AddListener(AlAgarrar);
            grabInteractable.selectExited.AddListener(AlSoltar);
        }
    }

    private void OnEnable()
    {
        Application.onBeforeRender += ActualizarFulcroVR;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= ActualizarFulcroVR;

        if (estaAnclado && trocarActual != null)
        {
            trocaresOcupados.Remove(trocarActual);
        }
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(AlAgarrar);
            grabInteractable.selectExited.RemoveListener(AlSoltar);
        }
    }

    private void AlAgarrar(SelectEnterEventArgs args)
    {
        mandoVRActual = args.interactorObject.transform;

        if (estaAnclado && trocarActual != null)
        {
            CapturarReagarreSinSalto();
            BloquearControlXR(true);
        }
    }

    private void AlSoltar(SelectExitEventArgs args)
    {
        mandoVRActual = null;
        BloquearControlXR(false);
        if (saliendoGuiado) LiberarTrocar();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // Llamado por el brazo robótico (funciona idéntico a AlAgarrar de tu mano VR)
    public void TomarConBrazoRobotico(Transform puntaRobot)
    {
        mandoVRActual = puntaRobot;

        if (estaAnclado && trocarActual != null)
        {
            CapturarReagarreSinSalto();
            BloquearControlXR(true);
        }
    }

    // Llamado por el brazo robótico al soltar (funciona idéntico a AlSoltar de tu mano VR)
    public void SoltarDeBrazoRobotico()
    {
        mandoVRActual = null;
        BloquearControlXR(false);
        if (saliendoGuiado) LiberarTrocar();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Volvió a entrar mientras salía guiado: sigue anclado con normalidad
        if (estaAnclado && saliendoGuiado && other.transform == trocarActual) saliendoGuiado = false;
        VerificarAnclajeTrocar(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!estaAnclado)
        {
            VerificarAnclajeTrocar(other);
        }
    }

    private void VerificarAnclajeTrocar(Collider other)
    {
        if (!estaAnclado && other.CompareTag(tagTrocar))
        {
            if (trocaresOcupados.Contains(other.transform))
            {
                return;
            }

            estaAnclado = true;
            saliendoGuiado = false;
            trocarActual = other.transform;
            trocaresOcupados.Add(trocarActual);
            alturaCabezal = AlturaCabezalSobreFulcro(trocarActual);
            limiteBase = distanciaMinimaAlFulcro;

            if (mandoVRActual != null)
            {
                CapturarEntradaAlCentro(other);
                BloquearControlXR(true);
            }

            Debug.Log($"🔧 [Fulcro Universal] '{gameObject.name}' anclado y trocar reservado: {other.name}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (estaAnclado && other.CompareTag(tagTrocar) && other.transform == trocarActual)
        {
            // Con alguien sujetándolo, el instrumento sigue guiado por el eje del trocar hasta que la
            // punta sale por el cabezal (ActualizarFulcroVR lo libera entonces).
            if (guiarSalidaPorCanula && mandoVRActual != null && alturaCabezal > 0f && !saliendoGuiado)
            {
                saliendoGuiado = true;
                baseAlSalir = Vector3.Dot(ObtenerCentroSphereCollider() - (mandoVRActual.position + desfaseManoAlEjeDelTubo), direccionAnterior);
                return;
            }
            if (!saliendoGuiado) LiberarTrocar();
        }
    }

    private void LiberarTrocar()
    {
        if (trocarActual != null) trocaresOcupados.Remove(trocarActual);
        estaAnclado = false;
        saliendoGuiado = false;
        trocarActual = null;
        BloquearControlXR(false);
        Debug.Log($"🔧 [Fulcro Universal] '{gameObject.name}' retirado. Trocar libre.");
    }

    /// <summary>
    /// Fija el tope de entrada al (re)agarrar. Nunca es más restrictivo que la posición actual de la mano,
    /// para que el instrumento no dé un salto hacia atrás si se agarró más adelante del cabezal.
    /// </summary>
    private void FijarLimiteBase(Vector3 baseDelTubo, Vector3 direccionHaciaAdentro)
    {
        float deseado = topeCabezalTrocar && alturaCabezal > 0f ? alturaCabezal + margenCabezal : distanciaMinimaAlFulcro;
        float actual = Vector3.Dot(ObtenerCentroSphereCollider() - baseDelTubo, direccionHaciaAdentro);
        limiteBase = Mathf.Max(distanciaMinimaAlFulcro, Mathf.Min(deseado, actual));
    }

    private Collider ObtenerColliderPrincipalDelTubo()
    {
        // Toma el collider físico del tubo ignorando el SphereCollider de PuntaActiva
        Collider[] todos = GetComponentsInChildren<Collider>(true);
        foreach (var c in todos)
        {
            if (!c.name.ToLower().Contains("puntaactiva"))
                return c;
        }
        return GetComponentInChildren<Collider>();
    }

    private void CapturarEntradaAlCentro(Collider triggerTrocar)
    {
        Vector3 centroEsfera = ObtenerCentroSphereCollider();

        rotacionInicialInstrumento = transform.rotation;

        Collider miCollider = ObtenerColliderPrincipalDelTubo();
        Vector3 puntoTuboEnMundo = transform.position;

        if (miCollider != null)
        {
            puntoTuboEnMundo = Physics.ClosestPoint(centroEsfera, miCollider, miCollider.transform.position, miCollider.transform.rotation);
        }

        puntoLocalEnElTubo = transform.InverseTransformPoint(puntoTuboEnMundo);

        Vector3 direccionMundoDelTubo = DetectarDireccionLargaDelTubo(centroEsfera - mandoVRActual.position);
        direccionInicialTubo = direccionMundoDelTubo;
        ejeLocalTuboHaciaAdentro = transform.InverseTransformDirection(direccionMundoDelTubo).normalized;

        Vector3 vectorPuntaAMano = mandoVRActual.position - puntoTuboEnMundo;
        distanciaManoAPuntoTubo = Vector3.Dot(vectorPuntaAMano, -direccionMundoDelTubo);
        
        Vector3 baseVirtualDelTubo = puntoTuboEnMundo - (direccionMundoDelTubo * distanciaManoAPuntoTubo);
        desfaseManoAlEjeDelTubo = baseVirtualDelTubo - mandoVRActual.position;
        direccionAnterior = direccionMundoDelTubo;
        FijarLimiteBase(baseVirtualDelTubo, direccionMundoDelTubo);
    }

    private void CapturarReagarreSinSalto()
    {
        rotacionInicialInstrumento = transform.rotation;

        Vector3 dirActualHaciaAdentro = transform.TransformDirection(ejeLocalTuboHaciaAdentro).normalized;
        direccionInicialTubo = dirActualHaciaAdentro;

        Vector3 puntoTuboEnMundo = transform.TransformPoint(puntoLocalEnElTubo);

        Vector3 vectorPuntaAMano = mandoVRActual.position - puntoTuboEnMundo;
        distanciaManoAPuntoTubo = Vector3.Dot(vectorPuntaAMano, -dirActualHaciaAdentro);

        Vector3 baseVirtualSinSalto = puntoTuboEnMundo - (dirActualHaciaAdentro * distanciaManoAPuntoTubo);
        desfaseManoAlEjeDelTubo = baseVirtualSinSalto - mandoVRActual.position;
        direccionAnterior = dirActualHaciaAdentro;
        FijarLimiteBase(baseVirtualSinSalto, dirActualHaciaAdentro);
    }

    private Vector3 DetectarDireccionLargaDelTubo(Vector3 direccionAproximadaHaciaTrocar)
    {
        Vector3[] ejesLocales = { transform.right, transform.up, transform.forward, -transform.right, -transform.up, -transform.forward };
        Vector3 mejorEje = direccionAproximadaHaciaTrocar.normalized;
        float mayorAlineacion = -1f;

        foreach (Vector3 eje in ejesLocales)
        {
            float dot = Vector3.Dot(eje.normalized, direccionAproximadaHaciaTrocar.normalized);
            if (dot > mayorAlineacion)
            {
                mayorAlineacion = dot;
                mejorEje = eje.normalized;
            }
        }
        return mejorEje;
    }

    private Vector3 ObtenerCentroSphereCollider()
    {
        if (trocarActual == null) return transform.position;

        SphereCollider sphere = trocarActual.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            return trocarActual.TransformPoint(sphere.center);
        }
        return trocarActual.position;
    }

    private void BloquearControlXR(bool bloquear)
    {
        if (grabInteractable != null)
        {
            grabInteractable.trackRotation = !bloquear;
            grabInteractable.trackPosition = !bloquear;
        }
    }

    private void LateUpdate()
    {
        ActualizarFulcroVR();
    }

    private void ActualizarFulcroVR()
    {
        if (!estaAnclado || trocarActual == null || mandoVRActual == null) return;

        Vector3 centroEsfera = ObtenerCentroSphereCollider();
        Vector3 baseDelTubo = mandoVRActual.position + desfaseManoAlEjeDelTubo;

        Vector3 vectorBaseAEsfera = centroEsfera - baseDelTubo;

        // 🛡️ Si la base se acerca o cruza el fulcro, se mantiene del lado de afuera (sin volteo de 180°)
        if (direccionAnterior.sqrMagnitude < 0.5f) direccionAnterior = direccionInicialTubo;
        float limite = Mathf.Max(distanciaMinimaAlFulcro, limiteBase);
        float distanciaBase = Vector3.Dot(vectorBaseAEsfera, direccionAnterior);

        // Salida guiada: cuando la punta ya pasó el cabezal del trocar, el instrumento queda libre
        if (saliendoGuiado)
        {
            float radio = 0f;
            SphereCollider esfera = trocarActual.GetComponent<SphereCollider>();
            if (esfera != null) radio = esfera.radius * Mathf.Abs(trocarActual.lossyScale.x);
            if (distanciaBase - baseAlSalir > Mathf.Max(0.01f, alturaCabezal - radio) + 0.01f)
            {
                LiberarTrocar();
                return;
            }
        }

        if (distanciaBase < limite)
        {
            Vector3 lateral = Vector3.ProjectOnPlane(vectorBaseAEsfera, direccionAnterior);
            vectorBaseAEsfera = direccionAnterior * limite + lateral;
            baseDelTubo = centroEsfera - vectorBaseAEsfera;
        }
        if (vectorBaseAEsfera.sqrMagnitude < 0.0001f) return;

        Vector3 direccionActualAlCentro = vectorBaseAEsfera.normalized;
        direccionAnterior = direccionActualAlCentro;

        Quaternion rotacionPalanca = Quaternion.FromToRotation(direccionInicialTubo, direccionActualAlCentro);
        transform.rotation = rotacionPalanca * rotacionInicialInstrumento;

        Vector3 posicionObjetivoPuntoTubo = baseDelTubo + (direccionActualAlCentro * distanciaManoAPuntoTubo);

        Vector3 desfasePadreAPuntoTubo = transform.TransformPoint(puntoLocalEnElTubo) - transform.position;
        transform.position = posicionObjetivoPuntoTubo - desfasePadreAPuntoTubo;
    }
}