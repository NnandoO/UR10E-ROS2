using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Abre las piernas del paciente (abducción de cadera) al iniciar.
///
/// El modelo del paciente no tiene esqueleto: es un atlas de miles de mallas estáticas (piel por regiones,
/// músculos, huesos, vasos, nervios). Aquí cada estructura que pertenece a una pierna se gira rígidamente
/// alrededor de su articulación de la cadera. No modifica la escena ni los assets: se aplica al entrar en
/// Play y se deshace al desactivar el componente.
/// </summary>
public class PosturaPaciente : MonoBehaviour
{
    [Tooltip("Raíz del modelo del paciente. Vacío = se busca 'HumanBody'.")]
    public Transform paciente;
    [Tooltip("Ángulo total entre las dos piernas, en grados (cada pierna se abre la mitad).")]
    [Range(0f, 80f)] public float aperturaTotal = 30f;

    [Header("Anatomía (en coordenadas del mundo; cabeza hacia +Z, cara anterior hacia +Y)")]
    [Tooltip("Altura (Z) de las articulaciones de la cadera.")]
    public float zCadera = 0.03f;
    [Tooltip("Separación lateral de cada cadera respecto a la línea media (m).")]
    public float separacionCadera = 0.085f;
    [Tooltip("Una estructura es de la pierna si su centro queda por debajo de esta Z.")]
    public float zLimitePierna = -0.04f;
    [Tooltip("Semiancho (m) de la banda que ocupan las piernas a cada lado de la línea media; más afuera están los brazos y las manos.")]
    public float semianchoPiernas = 0.20f;
    [Tooltip("Las estructuras a menos de esta distancia de la línea media no se mueven (pelvis, genitales).")]
    public float margenLineaMedia = 0.015f;

    private struct Original { public Transform t; public Vector3 pos; public Quaternion rot; }
    private readonly List<Original> originales = new List<Original>();
    private bool aplicada;

    void Start() => Aplicar();
    void OnDisable() => Restaurar();

    public void Aplicar()
    {
        if (aplicada) Restaurar();
        if (paciente == null) { var go = GameObject.Find("HumanBody"); if (go != null) paciente = go.transform; }
        if (paciente == null || aperturaTotal <= 0.01f) return;

        // Línea media: el centro del conjunto del cuerpo
        var renderers = paciente.GetComponentsInChildren<MeshRenderer>(true);   // padres antes que hijos
        if (renderers.Length == 0) return;
        // El atlas nombra cada estructura par con sufijo ".r" (derecha) o ".l" (izquierda): la línea media
        // es el punto medio entre los dos conjuntos, y el sufijo dice de qué pierna es cada estructura.
        double sumaR = 0, sumaL = 0; int nR = 0, nL = 0;
        foreach (var r in renderers)
        {
            if (r.name.EndsWith(".r")) { sumaR += r.bounds.center.x; nR++; }
            else if (r.name.EndsWith(".l")) { sumaL += r.bounds.center.x; nL++; }
        }
        float xMedio;
        int signoDerecha = -1;       // lado (signo en X) en el que están las estructuras ".r"
        if (nR > 0 && nL > 0)
        {
            xMedio = (float)((sumaR / nR + sumaL / nL) * 0.5);
            signoDerecha = sumaR / nR < sumaL / nL ? -1 : 1;
        }
        else
        {
            Bounds total = renderers[0].bounds;
            foreach (var r in renderers) total.Encapsulate(r.bounds);
            xMedio = total.center.x;
        }

        // 1) Se guarda la pose de TODAS las mallas y se decide cuáles son de cada pierna (con sus límites originales)
        originales.Clear();
        var lado = new int[renderers.Length];   // -1 pierna del lado x menor, +1 del lado x mayor, 0 no se mueve
        for (int i = 0; i < renderers.Length; i++)
        {
            Transform t = renderers[i].transform;
            originales.Add(new Original { t = t, pos = t.position, rot = t.rotation });
            Vector3 c = renderers[i].bounds.center;
            float dx = c.x - xMedio;
            if (c.z >= zLimitePierna || Mathf.Abs(dx) >= semianchoPiernas) continue;
            string nombre = renderers[i].name;
            if (nombre.EndsWith(".r")) lado[i] = signoDerecha;
            else if (nombre.EndsWith(".l")) lado[i] = -signoDerecha;
            else if (Mathf.Abs(dx) > margenLineaMedia) lado[i] = dx < 0f ? -1 : 1;   // sin sufijo: por posición
        }

        // 2) Se asigna la pose final a cada malla, de padres a hijos: así una malla que no es de la pierna
        //    pero cuelga de una que sí lo es conserva su sitio.
        float medio = aperturaTotal * 0.5f;
        for (int i = 0; i < renderers.Length; i++)
        {
            var o = originales[i];
            if (lado[i] == 0) { o.t.SetPositionAndRotation(o.pos, o.rot); continue; }
            Vector3 cadera = new Vector3(xMedio + lado[i] * separacionCadera, o.pos.y, zCadera);
            // Girar alrededor del eje anteroposterior (Y) separa el pie de la línea media.
            // (Los pies están hacia -Z: un giro positivo en Y los lleva hacia -X, de ahí el signo.)
            Quaternion giro = Quaternion.AngleAxis(-lado[i] * medio, Vector3.up);
            o.t.SetPositionAndRotation(cadera + giro * (o.pos - cadera), giro * o.rot);
        }
        aplicada = true;
    }

    public void Restaurar()
    {
        if (!aplicada) return;
        foreach (var o in originales) if (o.t != null) o.t.SetPositionAndRotation(o.pos, o.rot);
        originales.Clear();
        aplicada = false;
    }
}
