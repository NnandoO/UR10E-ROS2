using UnityEngine;
using SofaUnity;

namespace SofaUnity
{
    public class BisturiSofaInvisible : SofaLaserModel
    {
        void Awake()
        {
            // 1. Bloqueamos las variables de renderizado desde que nace el objeto
            m_drawLaser = false;
            m_drawLight = false;
        }

        // Sobreescribimos el Update del láser original para tener control total
        private new void Update()
        {
            // 2. APAGADO BRUTAL: Si SOFA intenta crear las luces o partículas, las apagamos a la fuerza
            if (m_lightSource != null) m_lightSource.SetActive(false);
            if (m_ps != null) m_ps.GetComponent<ParticleSystemRenderer>().enabled = false;

            // 3. Mantenemos el rayo activo permanentemente
            m_activateRay = true;
            m_isActivated = true;

            // 4. LA MATEMÁTICA EXACTA DEL LÁSER ORIGINAL
            if (m_initialized)
            {
                m_origin = transform.position;
                
                // SOFA exige estrictamente el forward (Z-axis) para no romper su cálculo interno
                m_direction = transform.forward; 

                // Ejecuta el corte
                CastRay();
            }
        }

        // Línea roja visible solo para ti en la pestaña Scene
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, transform.forward * m_length);
        }
    }
}