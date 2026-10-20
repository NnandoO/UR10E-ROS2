using UnityEngine;

public class ControladorVisorLaparoscopio : MonoBehaviour
{
    [Header("Configuración de Cámaras")]
    [Tooltip("Arrastra aquí la Main Camera (la del XR Origin)")]
    public Camera cameraGeneralSala;     
    
    [Tooltip("Arrastra aquí la cámara ubicada en la punta del laparoscopio")]
    public Camera cameraLaparoscopio;    

    [Header("Etiquetas de Control")]
    [Tooltip("Tag que tiene el Box Collider del abdomen")]
    public string tagZonaAbdomen = "AbdomenPaciente";

    // Guardará tu textura 'VideoLaparoscopio' para no perderla
    private RenderTexture texturaOriginalMonitor;

    void Start()
    {
        if (cameraLaparoscopio != null)
        {
            // Guardamos la referencia a VideoLaparoscopio si la tiene puesta
            texturaOriginalMonitor = cameraLaparoscopio.targetTexture;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Si este objeto (el laparoscopio) toca el collider del abdomen
        if (other.CompareTag(tagZonaAbdomen))
        {
            CambiarAVisorCompleto(true);
            Debug.Log("📷 [Laparoscopio] Dentro del abdomen: Visor completo activado.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Si el laparoscopio sale del collider del abdomen
        if (other.CompareTag(tagZonaAbdomen))
        {
            CambiarAVisorCompleto(false);
            Debug.Log("📷 [Laparoscopio] Fuera del abdomen: Vista de sala restaurada.");
        }
    }

    void CambiarAVisorCompleto(bool activarLaparoscopio)
    {
        if (cameraGeneralSala == null || cameraLaparoscopio == null) return;

        if (activarLaparoscopio)
        {
            // 1. Apagamos la cámara general de la sala
            cameraGeneralSala.enabled = false;

            // 2. Desconectamos la textura del monitor para que renderice directo en el visor VR
            cameraLaparoscopio.targetTexture = null;
            
            // 3. Encendemos y damos máxima prioridad a la cámara del laparoscopio
            cameraLaparoscopio.enabled = true;
            cameraLaparoscopio.depth = cameraGeneralSala.depth + 10;
        }
        else
        {
            // 1. Devolvemos la textura original para que siga viéndose en el monitor de la sala
            cameraLaparoscopio.targetTexture = texturaOriginalMonitor;
            
            // 2. Si no usas monitor externo, apagamos la cámara; si usas monitor, déjala en true
            if (texturaOriginalMonitor == null)
            {
                cameraLaparoscopio.enabled = false;
            }

            cameraLaparoscopio.depth = cameraGeneralSala.depth - 1;

            // 3. Reactivamos la cámara principal del quirófano
            cameraGeneralSala.enabled = true;
        }
    }
}