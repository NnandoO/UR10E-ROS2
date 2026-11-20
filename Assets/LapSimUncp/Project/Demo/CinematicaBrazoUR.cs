using UnityEngine;

/// <summary>
/// Cinemática directa e inversa de un brazo UR (6 articulaciones) a partir de sus ArticulationBody.
///
/// La directa reproduce exactamente la cadena de Unity: T_i = T_padre · T(parentAnchor) · Rx(q_i) · T(anchor)⁻¹
/// (verificada contra las transformaciones reales: error 0 mm / 0°). La inversa es por mínimos cuadrados
/// amortiguados (DLS) y controla la POSICIÓN del punto de agarre del robot y la DIRECCIÓN de un eje de
/// herramienta (el giro sobre ese eje queda libre).
///
/// No mueve nada por sí misma: <see cref="AplicarObjetivos"/> escribe los ángulos en los drives.
/// </summary>
public class CinematicaBrazoUR
{
    public readonly ArticulationBody[] juntas;
    private readonly Transform baseCuerpo;
    private readonly Matrix4x4[] tPadreAncla = new Matrix4x4[6];
    private readonly Matrix4x4[] tAnclaInv = new Matrix4x4[6];
    private Matrix4x4 tMunecaAPivote;   // de wrist_3 al punto de agarre (constante)
    public Vector3 ejeHerramientaLocal { get; private set; } // eje "hacia afuera" de la herramienta, en el marco del pivote

    /// <summary>Ángulos actuales de trabajo (grados), usados como semilla y como objetivo de los drives.</summary>
    public readonly float[] q = new float[6];

    public CinematicaBrazoUR(ArticulationBody[] juntas, Vector3 pivotePos, Quaternion pivoteRot)
    {
        this.juntas = juntas;
        baseCuerpo = juntas[0].transform.parent.GetComponentInParent<ArticulationBody>().transform;
        for (int i = 0; i < 6; i++)
        {
            var ab = juntas[i];
            tPadreAncla[i] = Matrix4x4.TRS(ab.parentAnchorPosition, ab.parentAnchorRotation, Vector3.one);
            tAnclaInv[i] = Matrix4x4.TRS(ab.anchorPosition, ab.anchorRotation, Vector3.one).inverse;
            q[i] = ab.jointPosition.dofCount > 0 ? ab.jointPosition[0] * Mathf.Rad2Deg : ab.xDrive.target;
        }
        var muneca = juntas[5].transform;
        var tMuneca = Matrix4x4.TRS(muneca.position, muneca.rotation, Vector3.one);
        var tPivote = Matrix4x4.TRS(pivotePos, pivoteRot, Vector3.one);
        tMunecaAPivote = tMuneca.inverse * tPivote;
        // Dirección de aproximación: de la muñeca hacia el pivote (a lo largo de la herramienta montada)
        Vector3 d = pivotePos - muneca.position;
        if (d.sqrMagnitude < 1e-6f) d = pivoteRot * Vector3.forward;
        ejeHerramientaLocal = (Quaternion.Inverse(pivoteRot) * d.normalized).normalized;
    }

    /// <summary>
    /// Redefine el eje de la herramienta como la recta pivote -> <paramref name="extremoMundo"/> (por ejemplo la
    /// mordaza del instrumento montado), para que sea el tubo VISIBLE el que la IK alinea con el objetivo.
    /// </summary>
    public void FijarEjeHacia(Vector3 extremoMundo, Vector3 pivotePos, Quaternion pivoteRot)
    {
        Vector3 d = extremoMundo - pivotePos;
        if (d.sqrMagnitude < 1e-6f) return;
        ejeHerramientaLocal = (Quaternion.Inverse(pivoteRot) * d.normalized).normalized;
    }

    /// <summary>Lee los ángulos reales actuales como punto de partida.</summary>
    public void LeerAngulosActuales()
    {
        for (int i = 0; i < 6; i++)
            q[i] = juntas[i].jointPosition.dofCount > 0 ? juntas[i].jointPosition[0] * Mathf.Rad2Deg : juntas[i].xDrive.target;
    }

    public Matrix4x4 FKPivote(float[] ang)
    {
        var t = Matrix4x4.TRS(baseCuerpo.position, baseCuerpo.rotation, Vector3.one);
        for (int i = 0; i < 6; i++)
            t = t * tPadreAncla[i] * Matrix4x4.Rotate(Quaternion.AngleAxis(ang[i], Vector3.right)) * tAnclaInv[i];
        return t * tMunecaAPivote;
    }

    public static Vector3 Pos(Matrix4x4 m) => m.GetColumn(3);
    public static Quaternion Rot(Matrix4x4 m) => Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
    public Vector3 Eje(Matrix4x4 m) => (Rot(m) * ejeHerramientaLocal).normalized;

    private readonly float[] tmp = new float[6];
    private readonly double[,] J = new double[6, 6];
    private readonly double[] r = new double[6];

    /// <summary>
    /// Ajusta <see cref="q"/> para llevar el pivote a <paramref name="posObjetivo"/> con el eje de herramienta
    /// apuntando a <paramref name="ejeObjetivo"/> (si es cero, solo posición). Devuelve el error de posición (m).
    /// </summary>
    public float Resolver(Vector3 posObjetivo, Vector3 ejeObjetivo, int iteraciones = 12, float pesoEje = 0.25f, float lambda = 0.02f)
    {
        bool conEje = ejeObjetivo.sqrMagnitude > 1e-6f;
        if (conEje) ejeObjetivo.Normalize();
        float error = 0f;
        const float delta = 0.2f; // grados
        for (int it = 0; it < iteraciones; it++)
        {
            var m = FKPivote(q);
            Vector3 p = Pos(m);
            Vector3 eo = conEje ? Vector3.Cross(Eje(m), ejeObjetivo) : Vector3.zero;
            Vector3 ep = posObjetivo - p;
            error = ep.magnitude;
            if (error < 0.0005f && eo.magnitude < 0.002f) break;
            r[0] = ep.x; r[1] = ep.y; r[2] = ep.z;
            r[3] = -pesoEje * eo.x; r[4] = -pesoEje * eo.y; r[5] = -pesoEje * eo.z;

            for (int j = 0; j < 6; j++)
            {
                System.Array.Copy(q, tmp, 6);
                tmp[j] += delta;
                var mj = FKPivote(tmp);
                Vector3 dp = (Pos(mj) - p) / (delta * Mathf.Deg2Rad);
                Vector3 deo = conEje ? (Vector3.Cross(Eje(mj), ejeObjetivo) - eo) / (delta * Mathf.Deg2Rad) : Vector3.zero;
                J[0, j] = dp.x; J[1, j] = dp.y; J[2, j] = dp.z;
                J[3, j] = pesoEje * deo.x; J[4, j] = pesoEje * deo.y; J[5, j] = pesoEje * deo.z;
            }
            // Δq = Jᵀ (J Jᵀ + λ² I)⁻¹ r   (radianes)
            var A = new double[6, 7];
            for (int a = 0; a < 6; a++)
            {
                for (int b = 0; b < 6; b++)
                {
                    double s = 0; for (int k = 0; k < 6; k++) s += J[a, k] * J[b, k];
                    A[a, b] = s + (a == b ? lambda * lambda : 0);
                }
                A[a, 6] = r[a];
            }
            if (!Resolver6(A)) break;
            for (int j = 0; j < 6; j++)
            {
                double s = 0; for (int a = 0; a < 6; a++) s += J[a, j] * A[a, 6];
                float paso = Mathf.Clamp((float)s * Mathf.Rad2Deg, -5f, 5f); // pasos pequeños = movimiento estable
                q[j] += paso;
            }
        }
        return error;
    }

    // Eliminación gaussiana con pivoteo parcial sobre una matriz aumentada 6x7. La solución queda en la columna 6.
    private static bool Resolver6(double[,] A)
    {
        for (int c = 0; c < 6; c++)
        {
            int piv = c; double max = System.Math.Abs(A[c, c]);
            for (int f = c + 1; f < 6; f++) if (System.Math.Abs(A[f, c]) > max) { max = System.Math.Abs(A[f, c]); piv = f; }
            if (max < 1e-12) return false;
            if (piv != c) for (int k = 0; k < 7; k++) { var t = A[c, k]; A[c, k] = A[piv, k]; A[piv, k] = t; }
            for (int f = 0; f < 6; f++)
            {
                if (f == c) continue;
                double fac = A[f, c] / A[c, c];
                for (int k = c; k < 7; k++) A[f, k] -= fac * A[c, k];
            }
        }
        for (int f = 0; f < 6; f++) A[f, 6] /= A[f, f];
        return true;
    }

    /// <summary>Mueve los drives hacia <see cref="q"/> limitando la velocidad articular (grados/s).</summary>
    public void AplicarObjetivos(float velMaxGradosSeg, float dt)
    {
        float maxPaso = velMaxGradosSeg * dt;
        for (int i = 0; i < 6; i++)
        {
            var ab = juntas[i];
            var d = ab.xDrive;
            d.target = Mathf.MoveTowards(d.target, q[i], maxPaso);
            if (d.stiffness < 1f || d.forceLimit < 1f) { d.stiffness = 100000f; d.damping = 10000f; d.forceLimit = 3.402823e+38f; }
            ab.xDrive = d;
        }
    }

    /// <summary>¿Los drives ya alcanzaron los ángulos de trabajo?</summary>
    public bool EnObjetivo(float tolGrados = 0.5f)
    {
        for (int i = 0; i < 6; i++) if (Mathf.Abs(juntas[i].xDrive.target - q[i]) > tolGrados) return false;
        return true;
    }
}
