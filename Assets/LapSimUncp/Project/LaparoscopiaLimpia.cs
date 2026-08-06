using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class LaparoscopiaLimpia : MonoBehaviour
{
    public enum Eje { X, Y, Z }

    [Header("Configuración del Tubo")]
    [Tooltip("¿Hacia qué eje apunta lo largo de tu tubo? (Normalmente Z o Y)")]
    public Eje ejeDeDeslizamiento = Eje.Z;

    [Header("Configuración de Desconexión")]
    [Tooltip("Distancia a la que debes sacar el tubo para que se suelte del trocar")]
    public float distanciaDeDesconexion = 0.8f; // Aumentado para evitar que se desconecte accidentalmente

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
    private Rigidbody rb;
    private ConfigurableJoint pivoteQuirurgico;
    private UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socketApagado;
    
    // Candado de seguridad para evitar que 2 sockets peleen por el mismo objeto
    private bool enProcesoDeAnclaje = false;

    void Start()
    {
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.VelocityTracking;
        grab.selectEntered.AddListener(AlSerAtrapado);
    }

    private void AlSerAtrapado(SelectEnterEventArgs args)
    {
        // BUGS SOLUCIONADOS: Si ya estamos en proceso o anclados, ignoramos otros roces de sockets accidentales
        if (enProcesoDeAnclaje || pivoteQuirurgico != null) return;

        if (args.interactorObject is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socket)
        {
            StartCoroutine(SecuenciaDeAnclaje(socket));
        }
    }

    private IEnumerator SecuenciaDeAnclaje(UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socket)
    {
        enProcesoDeAnclaje = true;
        socketApagado = socket;
        Rigidbody rbTrocar = socket.GetComponentInParent<Rigidbody>();

        // Apagamos el socket
        socket.socketActive = false;
        
        yield return new WaitForFixedUpdate();
        Physics.SyncTransforms();

        // Verificación extra en caso de que el socket haya sido destruido en ese frame
        if (socket == null)
        {
            enProcesoDeAnclaje = false;
            yield break;
        }

        Transform receptor = socket.attachTransform != null ? socket.attachTransform : socket.transform;
        Transform miAnclaje = grab.attachTransform != null ? grab.attachTransform : transform;
        
        // Respetar la rotación del Punto Guía
        Quaternion diferenciaRotacion = Quaternion.Inverse(miAnclaje.rotation) * transform.rotation;
        transform.rotation = receptor.rotation * diferenciaRotacion;
        
        // Posición exacta
        Vector3 diferenciaPos = receptor.position - miAnclaje.position;
        transform.position += diferenciaPos;

        if (pivoteQuirurgico != null) Destroy(pivoteQuirurgico);
        
        pivoteQuirurgico = gameObject.AddComponent<ConfigurableJoint>();
        pivoteQuirurgico.connectedBody = rbTrocar;
        
        pivoteQuirurgico.autoConfigureConnectedAnchor = false;
        pivoteQuirurgico.connectedAnchor = rbTrocar.transform.InverseTransformPoint(receptor.position);
        pivoteQuirurgico.anchor = transform.InverseTransformPoint(miAnclaje.position);

        pivoteQuirurgico.projectionMode = JointProjectionMode.PositionAndRotation;
        pivoteQuirurgico.projectionDistance = 0.001f;
        pivoteQuirurgico.projectionAngle = 0.001f;

        pivoteQuirurgico.xMotion = ejeDeDeslizamiento == Eje.X ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
        pivoteQuirurgico.yMotion = ejeDeDeslizamiento == Eje.Y ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
        pivoteQuirurgico.zMotion = ejeDeDeslizamiento == Eje.Z ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;

        pivoteQuirurgico.angularXMotion = ConfigurableJointMotion.Free;
        pivoteQuirurgico.angularYMotion = ConfigurableJointMotion.Free;
        pivoteQuirurgico.angularZMotion = ConfigurableJointMotion.Free;

        rb.useGravity = false;
        rb.linearDamping = 5f;
        rb.angularDamping = 5f;

        enProcesoDeAnclaje = false;
    }

    void Update()
    {
        if (pivoteQuirurgico != null && socketApagado != null)
        {
            // Verificamos si la herramienta fue extraída más allá del límite
            float distancia = Vector3.Distance(transform.position, socketApagado.transform.position);
            
            if (distancia > distanciaDeDesconexion) 
            {
                DesconectarDelTrocar();
            }
        }
    }

    // Función limpia para asegurar que los Sockets jamás se queden bloqueados
    private void DesconectarDelTrocar()
    {
        if (pivoteQuirurgico != null) Destroy(pivoteQuirurgico);
        
        if (socketApagado != null)
        {
            socketApagado.socketActive = true; // Reiniciamos el socket de forma segura
            socketApagado = null;
        }
        
        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        
        enProcesoDeAnclaje = false;
    }
}