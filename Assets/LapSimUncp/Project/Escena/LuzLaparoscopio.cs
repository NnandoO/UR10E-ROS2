using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Luz fría en la punta del laparoscopio. Dentro del abdomen no llega la luz de la sala, así que sin
/// ella la imagen del laparoscopio (monitor y visor completo) se ve casi negra, como sin fuente de luz.
/// Crea un foco hijo de la cámara del laparoscopio en Play; no modifica assets.
/// </summary>
public class LuzLaparoscopio : MonoBehaviour
{
    [Tooltip("Cámara del laparoscopio. Vacío = la cámara hija de 'instrumento_laparoscopio'.")]
    public Camera camaraLaparoscopio;
    public Color color = new Color(1f, 0.97f, 0.92f);
    public float intensidad = 0.4f;
    public float alcance = 0.6f;
    [Range(20f, 120f)] public float angulo = 40f;
    [Tooltip("El foco se coloca detrás de la óptica, a lo largo del eje. Un foco en la punta quema la imagen: el tejido queda a 1-3 cm y la luz cae con 1/d². Desde atrás ilumina el campo de forma pareja (no proyecta sombras, atraviesa la pared).")]
    public float distanciaDetras = 0.25f;

    private Light luz;

    void Start()
    {
        if (camaraLaparoscopio == null)
        {
            var lap = GameObject.Find("instrumento_laparoscopio");
            if (lap != null) camaraLaparoscopio = lap.GetComponentInChildren<Camera>(true);
        }
        if (camaraLaparoscopio == null) { Debug.LogWarning("LuzLaparoscopio: no se encontró la cámara del laparoscopio."); return; }

        var go = new GameObject("LuzLaparoscopio (runtime)");
        go.transform.SetParent(camaraLaparoscopio.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -distanciaDetras);
        luz = go.AddComponent<Light>();
        luz.type = LightType.Spot;
        luz.color = color;
        luz.intensity = intensidad;
        luz.range = alcance;
        luz.spotAngle = angulo;
        luz.innerSpotAngle = angulo * 0.65f;
        luz.shadows = LightShadows.None;
        luz.renderMode = LightRenderMode.ForcePixel;
        luz.enabled = false;
        // La luz solo existe para la cámara del laparoscopio: desde la sala/casco no debe iluminar nada
        // (con la pared translúcida aparecería como una mancha brillante sobre los órganos).
        RenderPipelineManager.beginCameraRendering += AntesDeRenderizar;
    }

    void AntesDeRenderizar(ScriptableRenderContext ctx, Camera cam)
    {
        if (luz != null) luz.enabled = cam == camaraLaparoscopio;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= AntesDeRenderizar;
        if (luz != null) Destroy(luz.gameObject);
    }
}
