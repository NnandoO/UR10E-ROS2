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

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
    private Rigidbody rb;
    private ConfigurableJoint pivoteQuirurgico;
    private UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socketApagado;

    void Start()
    {
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.VelocityTracking;
        grab.selectEntered.AddListener(AlSerAtrapado);
    }

    private void AlSerAtrapado(SelectEnterEventArgs args)
    {
        if (args.interactorObject is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socket)
        {
            StartCoroutine(SecuenciaDeAnclaje(socket));
        }
    }

    private IEnumerator SecuenciaDeAnclaje(UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socket)
    {
        socketApagado = socket;
        Rigidbody rbTrocar = socket.GetComponent<Rigidbody>();

        socket.socketActive = false;
        
        yield return new WaitForFixedUpdate();

        // ==========================================
        // SOLUCIÓN AL SALTO DE LA SILUETA AZUL
        // ==========================================
        // Identificamos los puntos exactos que Unity usó para la silueta azul
        Transform receptor = socket.attachTransform != null ? socket.attachTransform : socket.transform;
        Transform miAnclaje = grab.attachTransform != null ? grab.attachTransform : transform;
        
        // 1. Alineamos la rotación
        transform.rotation = receptor.rotation;
        
        // 2. Lo movemos EXACTAMENTE a la posición de la silueta azul
        Vector3 diferencia = receptor.position - miAnclaje.position;
        transform.position += diferencia;

        // 3. Clavamos el pivote en ese mismo lugar
        if (pivoteQuirurgico != null) Destroy(pivoteQuirurgico);
        
        pivoteQuirurgico = gameObject.AddComponent<ConfigurableJoint>();
        pivoteQuirurgico.connectedBody = rbTrocar;
        
        pivoteQuirurgico.autoConfigureConnectedAnchor = false;
        pivoteQuirurgico.connectedAnchor = rbTrocar.transform.InverseTransformPoint(receptor.position);
        pivoteQuirurgico.anchor = transform.InverseTransformPoint(miAnclaje.position);

        pivoteQuirurgico.projectionMode = JointProjectionMode.PositionAndRotation;
        pivoteQuirurgico.projectionDistance = 0.001f;
        pivoteQuirurgico.projectionAngle = 0.001f;

        // 4. MOVIMIENTO
        pivoteQuirurgico.xMotion = ejeDeDeslizamiento == Eje.X ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
        pivoteQuirurgico.yMotion = ejeDeDeslizamiento == Eje.Y ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
        pivoteQuirurgico.zMotion = ejeDeDeslizamiento == Eje.Z ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;

        // 5. ROTACIÓN
        pivoteQuirurgico.angularXMotion = ConfigurableJointMotion.Free;
        pivoteQuirurgico.angularYMotion = ConfigurableJointMotion.Free;
        pivoteQuirurgico.angularZMotion = ConfigurableJointMotion.Free;

        // 6. FRICCIÓN
        rb.useGravity = false;
        rb.linearDamping = 5f;
        rb.angularDamping = 5f;
    }

    void Update()
    {
        if (pivoteQuirurgico != null && socketApagado != null)
        {
            float distancia = Vector3.Distance(transform.position, socketApagado.transform.position);
            
            // Distancia para sacarlo por completo del paciente
            if (distancia > 0.4f) 
            {
                Destroy(pivoteQuirurgico);
                socketApagado.socketActive = true; 
                socketApagado = null;
                
                rb.useGravity = true;
                rb.linearDamping = 0f;
                rb.angularDamping = 0.05f;
            }
        }
    }
}