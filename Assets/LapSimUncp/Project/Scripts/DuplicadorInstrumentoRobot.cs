using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using SofaUnity;

public class DuplicadorInstrumentoRobot : MonoBehaviour
{
    [Header("1. Destino en el Brazo Robótico")]
    [Tooltip("Arrastra aquí el Transform de la punta/efector final de tu brazo robótico (UR10e o Arctos) donde aparecerá el clon")]
    public Transform puntoMontajeRobot;

    [Tooltip("Si tienes dos brazos robóticos (Izquierdo y Derecho), pon aquí el del brazo izquierdo")]
    public Transform puntoMontajeRobotIzquierdo;

    [Header("2. Opciones de Sincronización")]
    [Tooltip("Si está activo, el clon del robot rotará y se moverá igual que el instrumento de tu mano")]
    public bool copiarRotacionDeMano = true;

    [Tooltip("Ajuste de rotación por si el efector del robot tiene los ejes distintos (ej: 0, 90, 0)")]
    public Vector3 offsetRotacionRobot = Vector3.zero;
    public Vector3 offsetPosicionRobot = Vector3.zero;

    [Tooltip("Destruir el duplicado del robot cuando sueltes el instrumento del todo")]
    public bool destruirAlSoltar = false;

    [Header("3. Estado en Vivo (Solo lectura)")]
    public bool esUnClonEnElRobot = false;
    public GameObject instanciaClonRobot;

    private XRGrabInteractable grabInteractable;
    private SofaInstrumentoQuirurgico miScriptSofa;
    private SofaInstrumentoQuirurgico scriptSofaDelClon;

    void Awake()
    {
        // Si este objeto ya es el clon creado en el robot, no vuelve a escuchar eventos de agarre
        if (esUnClonEnElRobot) return;

        grabInteractable = GetComponent<XRGrabInteractable>();
        miScriptSofa = GetComponentInChildren<SofaInstrumentoQuirurgico>();

        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(AlAgarrarInstrumento);
            grabInteractable.selectExited.AddListener(AlSoltarInstrumento);
        }
    }

    void OnDestroy()
    {
        if (grabInteractable != null && !esUnClonEnElRobot)
        {
            grabInteractable.selectEntered.RemoveListener(AlAgarrarInstrumento);
            grabInteractable.selectExited.RemoveListener(AlSoltarInstrumento);
        }
    }

    private void AlAgarrarInstrumento(SelectEnterEventArgs args)
    {
        if (esUnClonEnElRobot) return;

        // Si ya tiene un clon creado en el robot, no creamos otro encima
        if (instanciaClonRobot != null) return;

        // Elegimos en qué brazo robótico montarlo según la mano (Derecha o Izquierda)
        Transform destinoFinal = ObtenerPuntoMontajeSegunMano(args.interactorObject.transform);
        if (destinoFinal == null)
        {
            Debug.LogWarning($"⚠️ [DuplicadorRobot] Falta asignar 'Punto Montaje Robot' en {gameObject.name}");
            return;
        }

        CrearClonEnRobot(destinoFinal);
    }

    private void AlSoltarInstrumento(SelectExitEventArgs args)
    {
        if (esUnClonEnElRobot) return;

        if (destruirAlSoltar && instanciaClonRobot != null)
        {
            Destroy(instanciaClonRobot);
            instanciaClonRobot = null;
            scriptSofaDelClon = null;
        }
    }

    private Transform ObtenerPuntoMontajeSegunMano(Transform interactor)
    {
        if (puntoMontajeRobotIzquierdo != null && interactor != null)
        {
            string ruta = "";
            Transform t = interactor;
            while (t != null)
            {
                ruta += t.name.ToLower() + "/";
                t = t.parent;
            }

            if (ruta.Contains("seguimientol") || ruta.Contains("left") || ruta.Contains("izq"))
                return puntoMontajeRobotIzquierdo;
        }

        return puntoMontajeRobot;
    }

    private void CrearClonEnRobot(Transform socketRobot)
    {
        // 1. Instanciamos una copia exacta del instrumento que acabas de agarrar
        instanciaClonRobot = Instantiate(this.gameObject, socketRobot.position, socketRobot.rotation, socketRobot);
        instanciaClonRobot.name = $"{this.gameObject.name}_ClonRobot";

        // 2. Marcamos el clon como pasivo para que no cree más clones en bucle
        DuplicadorInstrumentoRobot dupClon = instanciaClonRobot.GetComponent<DuplicadorInstrumentoRobot>();
        if (dupClon != null)
        {
            dupClon.esUnClonEnElRobot = true;
            dupClon.enabled = false;
        }

        // 3. Desactivamos físicas y XRGrabInteractable en el clon del robot para que no se caiga ni choque con el robot
        XRGrabInteractable grabClon = instanciaClonRobot.GetComponent<XRGrabInteractable>();
        if (grabClon != null) Destroy(grabClon);

        ControladorFulcroLaparoscopio fulcroClon = instanciaClonRobot.GetComponent<ControladorFulcroLaparoscopio>();
        if (fulcroClon != null) Destroy(fulcroClon);

        Rigidbody rbClon = instanciaClonRobot.GetComponent<Rigidbody>();
        if (rbClon != null)
        {
            rbClon.isKinematic = true;
            rbClon.useGravity = false;
        }

        // 4. Ajustamos su posición local en la punta del brazo robótico
        instanciaClonRobot.transform.localPosition = offsetPosicionRobot;
        instanciaClonRobot.transform.localRotation = Quaternion.Euler(offsetRotacionRobot);

        // 5. Guardamos referencia al SofaInstrumentoQuirurgico del clon por si el robot también toca órganos SOFA
        scriptSofaDelClon = instanciaClonRobot.GetComponentInChildren<SofaInstrumentoQuirurgico>();

        Debug.Log($"🤖 [DuplicadorRobot] Se duplicó '{gameObject.name}' en el brazo robótico '{socketRobot.name}'.");
    }

    void LateUpdate()
    {
        if (esUnClonEnElRobot || instanciaClonRobot == null) return;

        // Sincronizar orientación si quieres que el instrumento en el robot gire igual que tu mano
        if (copiarRotacionDeMano)
        {
            instanciaClonRobot.transform.rotation = this.transform.rotation * Quaternion.Euler(offsetRotacionRobot);
        }

        // Sincronizar la acción del dedo medio (Grip) entre tu instrumento y el clon del robot
        if (miScriptSofa != null && scriptSofaDelClon != null)
        {
            scriptSofaDelClon.instrumentoTomado = miScriptSofa.instrumentoTomado;
            scriptSofaDelClon.manoQueLoSostiene = miScriptSofa.manoQueLoSostiene;
        }
    }
}