using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Soporte del instrumental OpenRST (jcolan/OpenRST) para la demo automática.
///
/// Los dos UR10e de la escena ya llevan montado el instrumento OpenRST completo
/// (forceps_base → tubo interface de 31 cm → pitch → dos dedos). Esta clase:
///  - localiza esas piezas en la punta de cada brazo,
///  - carga en tiempo de ejecución las mordazas intercambiables del repositorio
///    (fenestrated / maryland / needle driver, en Models/OpenRST_EE) y las intercambia
///    en los dedos según la fase quirúrgica,
///  - anima la apertura y el cierre de la pinza,
///  - y restaura los dedos originales al terminar. No modifica assets ni la escena.
/// </summary>
public class InstrumentalOpenRST
{
    public enum Mordaza { Original, Fenestrada, Maryland, PortaAgujas }

    public Transform punta;            // interface_link: el tubo rígido del instrumento (corre por su +Y local)
    public Transform eeLink;           // ee_link del URDF (cuelga de un dedo: NO es rígido respecto al tubo)
    public Transform dedoIzq, dedoDer; // finger_left_link / finger_right_link
    private Transform pitch;
    private MeshFilter visIzq, visDer;
    private Mesh mallaIzqOriginal, mallaDerOriginal;
    private float largo;               // del origen del tubo al centro de la mordaza, sobre el eje del tubo

    // La muñeca (pitch) y los dedos son articulaciones físicas que el importador URDF deja sin rigidez:
    // sin motor cuelgan sueltas. Mientras la demo usa el instrumento se sujetan con sus drives.
    private ArticulationBody abPitch, abIzq, abDer;
    private ArticulationDrive drivePitch0, driveIzq0, driveDer0;
    private bool drivesGuardados;
    private float signoIzq = 1f, signoDer = -1f;

    /// <summary>Eje del tubo, de la base hacia la mordaza.</summary>
    public Vector3 Eje => punta.up;
    /// <summary>Centro de la mordaza, sobre el eje del tubo (rígido respecto al brazo).</summary>
    public Vector3 Extremo => punta.position + punta.up * largo;
    public float Largo => largo;

    private static readonly Dictionary<string, Mesh> cacheStl = new Dictionary<string, Mesh>();

    public static InstrumentalOpenRST Buscar(Transform puntaBrazo)
    {
        if (puntaBrazo == null) return null;
        var o = new InstrumentalOpenRST { punta = puntaBrazo };
        foreach (var t in puntaBrazo.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ee_link" && o.eeLink == null) o.eeLink = t;
            if (t.name == "finger_left_link" && o.dedoIzq == null && t.parent.name == "pitch_link") o.dedoIzq = t;
            if (t.name == "finger_right_link" && o.dedoDer == null && t.parent.name == "pitch_link") o.dedoDer = t;
            if (t.name == "finger_left_link_0" && o.visIzq == null) o.visIzq = t.GetComponent<MeshFilter>();
            if (t.name == "finger_right_link_0" && o.visDer == null) o.visDer = t.GetComponent<MeshFilter>();
        }
        if (o.eeLink == null || o.dedoIzq == null || o.dedoDer == null || o.visIzq == null || o.visDer == null)
        {
            Debug.LogWarning($"OpenRST: no se encontró el efector completo bajo '{puntaBrazo.name}'.");
            return null;
        }
        o.mallaIzqOriginal = o.visIzq.sharedMesh;
        o.mallaDerOriginal = o.visDer.sharedMesh;
        o.pitch = o.dedoIzq.parent;
        o.abPitch = o.pitch.GetComponent<ArticulationBody>();
        o.abIzq = o.dedoIzq.GetComponent<ArticulationBody>();
        o.abDer = o.dedoDer.GetComponent<ArticulationBody>();
        // Largo nominal: tubo hasta la muñeca + dedo + ee_link, todo en línea con la muñeca recta
        o.largo = Vector3.Dot(o.pitch.position - puntaBrazo.position, puntaBrazo.up)
                  + o.dedoIzq.localPosition.magnitude + o.eeLink.localPosition.magnitude;
        // Los marcos de los dos dedos están espejados: se deduce el signo que ABRE cada uno
        Vector3 ejeIzq = o.abIzq != null ? o.dedoIzq.rotation * o.abIzq.anchorRotation * Vector3.right : Vector3.zero;
        Vector3 ejeDer = o.abDer != null ? o.dedoDer.rotation * o.abDer.anchorRotation * Vector3.right : Vector3.zero;
        o.signoIzq = 1f;
        o.signoDer = Vector3.Dot(ejeIzq, ejeDer) > 0f ? -1f : 1f;
        return o;
    }

    /// <summary>Vuelve a activar la punta OpenRST (IntercambioHerramienta la oculta al tomar un instrumento).</summary>
    public void Mostrar()
    {
        for (var t = eeLink; t != null && t != punta.parent; t = t.parent) t.gameObject.SetActive(true);
        foreach (Transform h in punta) if (!h.name.StartsWith("PivoteManoRobot")) h.gameObject.SetActive(true);
        foreach (Transform h in punta.parent) h.gameObject.SetActive(true);
    }

    public void CambiarMordaza(Mordaza m)
    {
        Mesh a = null, b = null;
        switch (m)
        {
            // La misma mordaza 'a' en ambos dedos: el espejo lo hacen los transforms de los links,
            // igual que el URDF usa un único STL de dedo para los dos lados.
            case Mordaza.Fenestrada:  a = b = Stl("fenestrated_jaw_link_a");   break;
            case Mordaza.Maryland:    a = b = Stl("maryland_jaw_link_a");      break;
            case Mordaza.PortaAgujas: a = b = Stl("needle_driver_jaw_link_a"); break;
        }
        visIzq.sharedMesh = a != null ? a : mallaIzqOriginal;
        visDer.sharedMesh = b != null ? b : mallaDerOriginal;
    }

    static void Sujetar(ArticulationBody ab, float objetivoGrados)
    {
        if (ab == null || ab.jointPosition.dofCount == 0) return;
        var d = ab.xDrive;
        d.stiffness = 4000f; d.damping = 80f; d.forceLimit = float.MaxValue;
        d.target = objetivoGrados;
        ab.xDrive = d;
    }

    /// <summary>
    /// Apertura de la pinza en grados por dedo (0 = cerrada). También mantiene la muñeca recta, alineada
    /// con el tubo. Actúa sobre los motores de las articulaciones, no sobre los transforms.
    /// </summary>
    public void Apertura(float grados)
    {
        if (!drivesGuardados)
        {
            if (abPitch != null) drivePitch0 = abPitch.xDrive;
            if (abIzq != null) driveIzq0 = abIzq.xDrive;
            if (abDer != null) driveDer0 = abDer.xDrive;
            drivesGuardados = true;
        }
        Sujetar(abPitch, 0f);
        Sujetar(abIzq, signoIzq * grados);
        Sujetar(abDer, signoDer * grados);
    }

    /// <summary>Ángulo real entre las dos mordazas (grados), para comprobar la apertura.</summary>
    public float AperturaReal => Vector3.Angle(dedoIzq.right, dedoDer.right);

    public void Restaurar()
    {
        if (visIzq != null) visIzq.sharedMesh = mallaIzqOriginal;
        if (visDer != null) visDer.sharedMesh = mallaDerOriginal;
        if (drivesGuardados)
        {
            if (abPitch != null) abPitch.xDrive = drivePitch0;
            if (abIzq != null) abIzq.xDrive = driveIzq0;
            if (abDer != null) abDer.xDrive = driveDer0;
            drivesGuardados = false;
        }
    }

    /// <summary>Solo para diagnóstico desde el editor.</summary>
    public static Mesh CargarStlBinarioPublico(string nombre) => Stl(nombre);

    static Mesh Stl(string nombre)
    {
        if (cacheStl.TryGetValue(nombre, out var m)) return m;
        string ruta = Path.Combine(Application.dataPath, "LapSimUncp/Models/OpenRST_EE", nombre + ".STL");
        m = CargarStlBinario(ruta);
        cacheStl[nombre] = m;
        return m;
    }

    /// <summary>Lector mínimo de STL binario (84 bytes de cabecera + 50 por triángulo, en metros).</summary>
    static Mesh CargarStlBinario(string ruta)
    {
        try
        {
            var datos = File.ReadAllBytes(ruta);
            int n = System.BitConverter.ToInt32(datos, 80);
            if (84 + n * 50 > datos.Length) { Debug.LogWarning("STL inesperado: " + ruta); return null; }
            var verts = new List<Vector3>(n * 3);
            var norms = new List<Vector3>(n * 3);
            var tris = new int[n * 3];
            int off = 84;
            for (int i = 0; i < n; i++)
            {
                float nx = System.BitConverter.ToSingle(datos, off), ny = System.BitConverter.ToSingle(datos, off + 4), nz = System.BitConverter.ToSingle(datos, off + 8);
                var nrm = new Vector3(nx, ny, nz);
                for (int k = 0; k < 3; k++)
                {
                    int p = off + 12 + k * 12;
                    verts.Add(new Vector3(System.BitConverter.ToSingle(datos, p), System.BitConverter.ToSingle(datos, p + 4), System.BitConverter.ToSingle(datos, p + 8)));
                    norms.Add(nrm);
                    tris[i * 3 + k] = i * 3 + k;
                }
                off += 50;
            }
            // Los STL de la carpeta CAD vienen en milímetros; los del URDF, en metros.
            // Si la pieza "mide" más de 30 cm es que está en mm: se pasa a metros.
            Vector3 mn = verts[0], mx = verts[0];
            foreach (var v in verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            if ((mx - mn).magnitude > 0.3f)
            {
                // Además de mm→m, los STL del CAD usan otro marco que los dedos del URDF:
                // la mordaza corre por +Y desde 0; el dedo corre por +X con la bisagra en el origen.
                // Rotación propia + recentrado: x' = y - 3.5 mm, y' = z - 3 mm, z' = 3.5 mm - x.
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 v = verts[i] * 0.001f;
                    verts[i] = new Vector3(v.y - 0.0035f, v.z - 0.003f, 0.0035f - v.x);
                }
                for (int i = 0; i < norms.Count; i++)
                {
                    Vector3 nv = norms[i];
                    norms[i] = new Vector3(nv.y, nv.z, -nv.x);
                }
            }

            var m = new Mesh { name = Path.GetFileNameWithoutExtension(ruta) };
            m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            Debug.Log($"OpenRST STL '{m.name}': bounds c={m.bounds.center.ToString("F4")} s={m.bounds.size.ToString("F4")}");
            return m;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"No se pudo leer el STL '{ruta}': {e.Message}");
            return null;
        }
    }
}
