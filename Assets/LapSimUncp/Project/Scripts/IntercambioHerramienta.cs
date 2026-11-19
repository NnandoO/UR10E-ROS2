using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using SofaUnity;

[DefaultExecutionOrder(-50)] // Se ejecuta antes del Fulcro para que la palanca no tenga retraso de 1 frame
public class IntercambioHerramienta : MonoBehaviour
{
    public enum ModoAnclajeRobot
    {
        PuntoExactoDondeAgarreConElMando,
        CentradoEnAttachDelInstrumento
    }

    [Header("1. Puntas de los Brazos Robóticos")]
    public Transform puntaBrazoDerecho;
    public Transform puntaBrazoIzquierdo;

    [Header("2. Re-Agarre Directo con la Punta del Robot")]
    [Tooltip("Distancia en metros a la que la punta del brazo robótico puede volver a agarrar un instrumento que soltaste.")]
    public float radioAgarrePuntaRobot = 0.20f;

    [Tooltip("Umbral del botón de agarre del mando para tomar/soltar desde el brazo robótico.")]
    [Range(0.1f, 0.9f)]
    public float umbralBotonAgarre = 0.5f;

    [Header("3. Modo de Agarre Inicial")]
    public ModoAnclajeRobot modoDeAnclaje = ModoAnclajeRobot.PuntoExactoDondeAgarreConElMando;

    [Tooltip("Si está en TRUE, cuando el brazo robótico vuelva a agarrar un instrumento suelto, lo tomará en su última posición sin pegarlo de golpe.")]
    public bool mantenerUltimaPosicionAlReagarrar = true;

    [Header("4. Ajuste de Ejes de la Punta del Robot (Puedes moverlo en Play)")]
    public Vector3 offsetPosicionDerecho = Vector3.zero;
    public Vector3 ajusteRotacionDerecho = Vector3.zero;

    public Vector3 offsetPosicionIzquierdo = Vector3.zero;
    public Vector3 ajusteRotacionIzquierdo = Vector3.zero;

    [Header("5. Control")]
    [Tooltip("Si está en FALSE, los botones de los mandos no agarran ni sueltan (lo usa la Demo automática).")]
    public bool entradaMandosHabilitada = true;
    [Tooltip("Solo se trasladan al robot los objetos con ControladorFulcroLaparoscopio (instrumentos). Evita que los trocares salten a la punta del brazo.")]
    public bool soloInstrumentosConFulcro = true;

    // ---------- API pública (Demo automática) ----------
    public GameObject InstrumentoEnBrazo(bool esIzquierdo) { var e = esIzquierdo ? brazoIzq : brazoDer; return e.activoEnBrazo ? e.instrumentoActual : null; }
    public Transform PuntaBrazo(bool esIzquierdo) => esIzquierdo ? puntaBrazoIzquierdo : puntaBrazoDerecho;
    /// <summary>Posición del punto de agarre del robot (punta + ajuste del usuario), exista o no el pivote.</summary>
    public Vector3 PosicionPivote(bool esIzquierdo)
    {
        Transform p = PuntaBrazo(esIzquierdo);
        if (p == null) return Vector3.zero;
        return p.position + p.rotation * (esIzquierdo ? offsetPosicionIzquierdo : offsetPosicionDerecho);
    }
    public Quaternion RotacionPivote(bool esIzquierdo)
    {
        Transform p = PuntaBrazo(esIzquierdo);
        return p == null ? Quaternion.identity : p.rotation * Quaternion.Euler(esIzquierdo ? ajusteRotacionIzquierdo : ajusteRotacionDerecho);
    }
    /// <summary>El robot toma el instrumento tal como está respecto a su punta (sin saltos).</summary>
    public void TomarConRobot(GameObject instrumento, bool esIzquierdo) => TrasladarAlBrazo(instrumento, null, esIzquierdo, true);
    /// <summary>Cambia cómo sostiene el robot el instrumento (pose local respecto al pivote). Ignorado si está en un trocar.</summary>
    public bool AjustarAgarre(bool esIzquierdo, Vector3 posLocal, Quaternion rotLocal)
    {
        var e = esIzquierdo ? brazoIzq : brazoDer;
        if (!e.activoEnBrazo || (e.fulcroInstrumento != null && e.fulcroInstrumento.estaAnclado)) return false;
        e.offsetAgarreLocalPos = posLocal;
        e.offsetAgarreLocalRot = rotLocal;
        return true;
    }

    private bool EsTrasladable(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Trocar")) return false;
        return !soloInstrumentosConFulcro || go.GetComponent<ControladorFulcroLaparoscopio>() != null;
    }

    private class EstadoBrazo
    {
        public GameObject instrumentoActual;
        public XRGrabInteractable grabInstrumento;
        public Rigidbody rbInstrumento;
        public ControladorFulcroLaparoscopio fulcroInstrumento;
        public SofaInstrumentoQuirurgico[] sofasInstrumento;

        public Transform pivoteVirtualRobot;
        public bool activoEnBrazo = false;
        public bool agarradoPorProximidadRobot = false;
        public bool botonPresionadoFrameAnterior = false;

        public Vector3 offsetAgarreLocalPos;
        public Quaternion offsetAgarreLocalRot;
    }

    private EstadoBrazo brazoDer = new EstadoBrazo();
    private EstadoBrazo brazoIzq = new EstadoBrazo();

    private HashSet<GameObject> objetosTrasladados = new HashSet<GameObject>();
    private XRGrabInteractable[] todosLosInstrumentos;

    void Start()
    {
        Physics.autoSyncTransforms = true;

        todosLosInstrumentos = FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None);
        foreach (var grab in todosLosInstrumentos)
        {
            grab.selectEntered.AddListener(AlAgarrarConManoVR);
            grab.selectExited.AddListener(AlSoltarConManoVR);
        }
    }

    void OnDestroy()
    {
        if (todosLosInstrumentos != null)
        {
            foreach (var grab in todosLosInstrumentos)
            {
                if (grab != null)
                {
                    grab.selectEntered.RemoveListener(AlAgarrarConManoVR);
                    grab.selectExited.RemoveListener(AlSoltarConManoVR);
                }
            }
        }
    }

    // 1. Cuando agarras el instrumento directamente con tu mano VR en la mesa
    private void AlAgarrarConManoVR(SelectEnterEventArgs args)
    {
        if (args.interactorObject is XRSocketInteractor) return;
        if (!entradaMandosHabilitada) return;

        GameObject instrumento = args.interactableObject.transform.gameObject;
        if (!EsTrasladable(instrumento)) return; // p. ej. los trocares se colocan con la mano, no van al robot
        Transform manoVR = args.interactorObject.transform;
        bool esManoIzquierda = EsManoIzquierda(manoVR);

        EstadoBrazo estado = esManoIzquierda ? brazoIzq : brazoDer;
        estado.agarradoPorProximidadRobot = false;

        TrasladarAlBrazo(instrumento, manoVR, esManoIzquierda, false);
    }

    // 2. Cuando sueltas el botón de tu mando VR después de haberlo agarrado con la mano
    private void AlSoltarConManoVR(SelectExitEventArgs args)
    {
        if (args.interactorObject is XRSocketInteractor) return;
        if (!entradaMandosHabilitada) return;

        GameObject instrumento = args.interactableObject.transform.gameObject;

        // Si se disparó SelectExit porque el objeto se teletransportó lejos de un DirectInteractor en la mesa,
        // verificamos si realmente soltaste el botón en el mando antes de soltarlo del robot
        if (brazoDer.instrumentoActual == instrumento && brazoDer.activoEnBrazo)
        {
            if (!EstaPresionandoAgarre(XRNode.RightHand))
                SoltarDelBrazo(false);
            else
                brazoDer.agarradoPorProximidadRobot = true;
        }

        if (brazoIzq.instrumentoActual == instrumento && brazoIzq.activoEnBrazo)
        {
            if (!EstaPresionandoAgarre(XRNode.LeftHand))
                SoltarDelBrazo(true);
            else
                brazoIzq.agarradoPorProximidadRobot = true;
        }
    }

    private bool EsManoIzquierda(Transform interactor)
    {
        if (interactor == null) return false;
        string ruta = "";
        Transform t = interactor;
        while (t != null)
        {
            ruta += t.name.ToLower() + "/";
            t = t.parent;
        }
        return ruta.Contains("seguimientol") || ruta.Contains("left") || ruta.Contains("izq");
    }

    public void TrasladarAlBrazo(GameObject instrumento, Transform manoVR, bool esIzquierdo, bool esReagarreDesdePuntaRobot)
    {
        Transform puntaDestino = esIzquierdo ? puntaBrazoIzquierdo : puntaBrazoDerecho;
        if (puntaDestino == null) return;

        EstadoBrazo estado = esIzquierdo ? brazoIzq : brazoDer;
        XRNode nodoMano = esIzquierdo ? XRNode.LeftHand : XRNode.RightHand;

        // Si el otro brazo lo tenía agarrado, lo suelta primero
        EstadoBrazo otroBrazo = esIzquierdo ? brazoDer : brazoIzq;
        if (otroBrazo.activoEnBrazo && otroBrazo.instrumentoActual == instrumento)
        {
            SoltarDelBrazo(!esIzquierdo);
        }

        // Si este mismo brazo tenía otra herramienta antes, la suelta
        if (estado.activoEnBrazo && estado.instrumentoActual != null && estado.instrumentoActual != instrumento)
        {
            SoltarDelBrazo(esIzquierdo);
        }

        // Ocultamos mallas decorativas originales de la punta del robot
        foreach (Transform hijo in puntaDestino)
        {
            if (estado.pivoteVirtualRobot != null && hijo == estado.pivoteVirtualRobot) continue;
            hijo.gameObject.SetActive(false);
        }

        if (estado.pivoteVirtualRobot == null)
        {
            GameObject goPivote = new GameObject($"PivoteManoRobot_{(esIzquierdo ? "Izq" : "Der")}");
            estado.pivoteVirtualRobot = goPivote.transform;
            estado.pivoteVirtualRobot.SetParent(puntaDestino, false);
        }

        ActualizarPivoteVirtualRobot(estado, puntaDestino, esIzquierdo);

        estado.instrumentoActual = instrumento;
        estado.grabInstrumento = instrumento.GetComponent<XRGrabInteractable>();
        estado.rbInstrumento = instrumento.GetComponent<Rigidbody>();
        estado.fulcroInstrumento = instrumento.GetComponent<ControladorFulcroLaparoscopio>();

        // CORRECCIÓN 1: Encuentra los SofaInstrumentoQuirurgico aunque PuntaActiva ya se haya movido a SofaContext
        estado.sofasInstrumento = ObtenerSofasDeInstrumento(instrumento);
        estado.activoEnBrazo = true;

        if (estado.grabInstrumento != null)
        {
            estado.grabInstrumento.trackPosition = false;
            estado.grabInstrumento.trackRotation = false;
            estado.grabInstrumento.throwOnDetach = false;
        }

        if (estado.rbInstrumento != null)
        {
            estado.rbInstrumento.linearVelocity = Vector3.zero;
            estado.rbInstrumento.angularVelocity = Vector3.zero;
            estado.rbInstrumento.isKinematic = true;
            estado.rbInstrumento.useGravity = false;
        }

        bool yaFueTrasladadoAntes = objetosTrasladados.Contains(instrumento);

        // Lógica de posición:
        if (estado.fulcroInstrumento != null && estado.fulcroInstrumento.estaAnclado)
        {
            // Si está anclado en el trocar, lo retoma en esa última posición exacta sin saltar
            estado.fulcroInstrumento.TomarConBrazoRobotico(estado.pivoteVirtualRobot);
        }
        else if ((yaFueTrasladadoAntes && mantenerUltimaPosicionAlReagarrar) || esReagarreDesdePuntaRobot)
        {
            // Si lo estás re-agarrando con la punta del brazo robótico donde lo dejaste antes,
            // calcula la diferencia desde su última posición actual para no darle ningún tirón
            Quaternion rotInvPivote = Quaternion.Inverse(estado.pivoteVirtualRobot.rotation);
            estado.offsetAgarreLocalRot = rotInvPivote * instrumento.transform.rotation;
            estado.offsetAgarreLocalPos = rotInvPivote * (instrumento.transform.position - estado.pivoteVirtualRobot.position);

            if (estado.fulcroInstrumento != null)
                estado.fulcroInstrumento.TomarConBrazoRobotico(estado.pivoteVirtualRobot);
        }
        else
        {
            // Primera vez que lo agarras desde la mesa: copia el agarre de tu mano y lo traslada a la punta del robot
            CapturarOffsetDeAgarre(estado, instrumento, manoVR);
            objetosTrasladados.Add(instrumento);

            if (estado.fulcroInstrumento != null)
                estado.fulcroInstrumento.TomarConBrazoRobotico(estado.pivoteVirtualRobot);
        }

        if (estado.sofasInstrumento != null)
        {
            foreach (var sofa in estado.sofasInstrumento)
            {
                if (sofa != null) sofa.ConfigurarDesdeBrazoRobotico(true, nodoMano);
            }
        }

        MoverInstrumentoEnBrazo(estado, esIzquierdo);
    }

    private SofaInstrumentoQuirurgico[] ObtenerSofasDeInstrumento(GameObject instrumento)
    {
        List<SofaInstrumentoQuirurgico> lista = new List<SofaInstrumentoQuirurgico>();
        SofaInstrumentoQuirurgico[] todos = FindObjectsByType<SofaInstrumentoQuirurgico>(FindObjectsSortMode.None);
        foreach (var s in todos)
        {
            if (s != null && (s.instrumentoRaiz == instrumento || s.transform.IsChildOf(instrumento.transform)))
            {
                lista.Add(s);
            }
        }
        return lista.ToArray();
    }

    private void CapturarOffsetDeAgarre(EstadoBrazo estado, GameObject instrumento, Transform manoVR)
    {
        Transform puntoAttach = (estado.grabInstrumento != null && estado.grabInstrumento.attachTransform != null)
            ? estado.grabInstrumento.attachTransform
            : null;

        if (modoDeAnclaje == ModoAnclajeRobot.PuntoExactoDondeAgarreConElMando && manoVR != null)
        {
            Quaternion rotInversaMando = Quaternion.Inverse(manoVR.rotation);
            estado.offsetAgarreLocalRot = rotInversaMando * instrumento.transform.rotation;
            estado.offsetAgarreLocalPos = rotInversaMando * (instrumento.transform.position - manoVR.position);
        }
        else if (puntoAttach != null)
        {
            Quaternion rotInversaAttach = Quaternion.Inverse(puntoAttach.rotation);
            estado.offsetAgarreLocalRot = rotInversaAttach * instrumento.transform.rotation;
            estado.offsetAgarreLocalPos = rotInversaAttach * (instrumento.transform.position - puntoAttach.position);
        }
        else
        {
            estado.offsetAgarreLocalRot = Quaternion.identity;
            estado.offsetAgarreLocalPos = Vector3.zero;
        }
    }

    private void ActualizarPivoteVirtualRobot(EstadoBrazo estado, Transform puntaBrazo, bool esIzquierdo)
    {
        if (estado.pivoteVirtualRobot == null) return;

        Vector3 offsetPosUsuario = esIzquierdo ? offsetPosicionIzquierdo : offsetPosicionDerecho;
        Vector3 ajusteRotUsuario = esIzquierdo ? ajusteRotacionIzquierdo : ajusteRotacionDerecho;

        estado.pivoteVirtualRobot.position = puntaBrazo.position + (puntaBrazo.rotation * offsetPosUsuario);
        estado.pivoteVirtualRobot.rotation = puntaBrazo.rotation * Quaternion.Euler(ajusteRotUsuario);
    }

    void Update()
    {
        GestionarBrazoEnUpdate(brazoDer, puntaBrazoDerecho, XRNode.RightHand, false);
        GestionarBrazoEnUpdate(brazoIzq, puntaBrazoIzquierdo, XRNode.LeftHand, true);
    }

    private void GestionarBrazoEnUpdate(EstadoBrazo estado, Transform puntaBrazo, XRNode nodoMano, bool esIzquierdo)
    {
        if (puntaBrazo == null) return;

        bool presionandoAhora = entradaMandosHabilitada && EstaPresionandoAgarre(nodoMano);
        if (!entradaMandosHabilitada) estado.botonPresionadoFrameAnterior = false;

        // A. Si el brazo NO tiene nada agarrado y acabas de presionar el botón de agarre en tu mando VR:
        //    buscamos si hay algún instrumento cerca de la punta del brazo robótico para volver a agarrarlo
        if (!estado.activoEnBrazo && presionandoAhora && !estado.botonPresionadoFrameAnterior)
        {
            GameObject instrumentoCercano = BuscarInstrumentoCercaDePuntaRobot(puntaBrazo.position);
            if (instrumentoCercano != null)
            {
                estado.agarradoPorProximidadRobot = true;
                TrasladarAlBrazo(instrumentoCercano, null, esIzquierdo, true);
            }
        }
        // B. Si el brazo SÍ tenía el instrumento agarrado por proximidad del robot y sueltas el botón del mando: lo suelta
        else if (entradaMandosHabilitada && estado.activoEnBrazo && estado.agarradoPorProximidadRobot && !presionandoAhora)
        {
            SoltarDelBrazo(esIzquierdo);
        }

        estado.botonPresionadoFrameAnterior = presionandoAhora;

        // C. Si el instrumento está activo en el brazo, lo movemos siguiendo al brazo robótico
        if (estado.activoEnBrazo && estado.instrumentoActual != null)
        {
            MoverInstrumentoEnBrazo(estado, esIzquierdo);
        }
    }

    // CORRECCIÓN 2: Mantiene consistente la lectura del botón durante todo el agarre para que no se suelte solo al frame siguiente
    private bool EstaPresionandoAgarre(XRNode nodoMano)
    {
        InputDevice mando = InputDevices.GetDeviceAtXRNode(nodoMano);
        if (!mando.isValid) return false;

        float valorTrigger = 0f;
        float valorGrip = 0f;
        mando.TryGetFeatureValue(CommonUsages.trigger, out valorTrigger);
        mando.TryGetFeatureValue(CommonUsages.grip, out valorGrip);

        return (valorTrigger >= umbralBotonAgarre) || (valorGrip >= umbralBotonAgarre);
    }

    // CORRECCIÓN 3: Evita crash con MeshColliders no-convexos al calcular el punto más cercano
    private GameObject BuscarInstrumentoCercaDePuntaRobot(Vector3 posicionPuntaRobot)
    {
        if (todosLosInstrumentos == null) return null;

        GameObject mejorInstrumento = null;
        float menorDistancia = radioAgarrePuntaRobot;

        foreach (var grab in todosLosInstrumentos)
        {
            if (grab == null || !grab.gameObject.activeInHierarchy) continue;
            if (!EsTrasladable(grab.gameObject)) continue;

            Collider[] colliders = grab.GetComponentsInChildren<Collider>();
            float distMinimaObjeto = Vector3.Distance(posicionPuntaRobot, grab.transform.position);

            foreach (var col in colliders)
            {
                if (col == null || !col.enabled) continue;

                Vector3 puntoMasCercano;
                if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
                {
                    puntoMasCercano = col.ClosestPoint(posicionPuntaRobot);
                }
                else
                {
                    puntoMasCercano = col.bounds.ClosestPoint(posicionPuntaRobot);
                }

                float d = Vector3.Distance(posicionPuntaRobot, puntoMasCercano);
                if (d < distMinimaObjeto) distMinimaObjeto = d;
            }

            if (distMinimaObjeto <= menorDistancia)
            {
                menorDistancia = distMinimaObjeto;
                mejorInstrumento = grab.gameObject;
            }
        }

        return mejorInstrumento;
    }

    private void MoverInstrumentoEnBrazo(EstadoBrazo estado, bool esIzquierdo)
    {
        Transform puntaBrazo = esIzquierdo ? puntaBrazoIzquierdo : puntaBrazoDerecho;
        ActualizarPivoteVirtualRobot(estado, puntaBrazo, esIzquierdo);

        if (estado.fulcroInstrumento == null || !estado.fulcroInstrumento.estaAnclado)
        {
            Quaternion rotFinal = estado.pivoteVirtualRobot.rotation * estado.offsetAgarreLocalRot;
            Vector3 posFinal = estado.pivoteVirtualRobot.position + (estado.pivoteVirtualRobot.rotation * estado.offsetAgarreLocalPos);

            estado.instrumentoActual.transform.SetPositionAndRotation(posFinal, rotFinal);

            if (estado.rbInstrumento != null)
            {
                estado.rbInstrumento.position = posFinal;
                estado.rbInstrumento.rotation = rotFinal;
            }

            Physics.SyncTransforms();
        }
        else
        {
            if (estado.fulcroInstrumento.mandoVRActual != estado.pivoteVirtualRobot)
            {
                estado.fulcroInstrumento.TomarConBrazoRobotico(estado.pivoteVirtualRobot);
            }
        }

        // Mantiene sincronizada la mano activa con los componentes de SOFA del instrumento
        if (estado.sofasInstrumento != null)
        {
            XRNode nodoMano = esIzquierdo ? XRNode.LeftHand : XRNode.RightHand;
            foreach (var sofa in estado.sofasInstrumento)
            {
                if (sofa != null) sofa.ConfigurarDesdeBrazoRobotico(true, nodoMano);
            }
        }
    }

    public void SoltarDelBrazo(bool esIzquierdo)
    {
        EstadoBrazo estado = esIzquierdo ? brazoIzq : brazoDer;

        estado.activoEnBrazo = false;
        estado.agarradoPorProximidadRobot = false;

        if (estado.sofasInstrumento != null)
        {
            XRNode nodoMano = esIzquierdo ? XRNode.LeftHand : XRNode.RightHand;
            foreach (var sofa in estado.sofasInstrumento)
            {
                if (sofa != null) sofa.ConfigurarDesdeBrazoRobotico(false, nodoMano);
            }
        }

        if (estado.fulcroInstrumento != null)
        {
            estado.fulcroInstrumento.SoltarDeBrazoRobotico();
        }

        if (estado.grabInstrumento != null)
        {
            bool bloqueadoPorFulcro = (estado.fulcroInstrumento != null && estado.fulcroInstrumento.estaAnclado);
            estado.grabInstrumento.trackPosition = !bloqueadoPorFulcro;
            estado.grabInstrumento.trackRotation = !bloqueadoPorFulcro;
        }

        if (estado.rbInstrumento != null)
        {
            estado.rbInstrumento.linearVelocity = Vector3.zero;
            estado.rbInstrumento.angularVelocity = Vector3.zero;
            estado.rbInstrumento.isKinematic = true;
            estado.rbInstrumento.useGravity = false;
        }

        estado.instrumentoActual = null;
    }
}