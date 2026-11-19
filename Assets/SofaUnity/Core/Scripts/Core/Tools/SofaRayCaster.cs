using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SofaUnityAPI;

namespace SofaUnity
{
    public class SofaRayCaster : RayCaster
    {
        public SofaUnity.SofaContext m_sofaContext = null;
        protected SofaRayCasterAPI m_sofaRC = null;
        public bool startOnPlay = true;

        [SerializeField]
        protected bool m_isActivated = false;

        [SerializeField]
        protected SofaDefines.SRayInteraction m_rayType;

        [SerializeField]
        public bool m_drawRay = false;

        protected LineRenderer m_rayRenderer = null;
        [SerializeField]
        protected Color m_rayColor = Color.green;
        [SerializeField]
        protected float m_rayWidth = 0.5f;

        public bool automaticCast = false;

        [SerializeField]
        protected float m_stiffness = 10000f;

        public bool ActivateTool
        {
            get { return m_isActivated; }
            set
            {
                if (m_isActivated != value)
                {
                    ActivateTool_impl(value);
                }
            }
        }

        public SofaDefines.SRayInteraction RayInteractionType
        {
            get { return m_rayType; }
            set
            {
                if (m_rayType != value)
                {
                    m_rayType = value;
                    if (m_sofaContext != null && Application.isPlaying)
                    {
                        StopRay();
                        CreateSofaRayCaster_impl();
                    }
                }
            }
        }

        public float RayWidth
        {
            get { return m_rayWidth; }
            set
            {
                if (m_rayWidth != value)
                {
                    m_rayWidth = value;
                    UpdateRayRenderer();
                }
            }
        }

        public Color RayColor
        {
            get { return m_rayColor; }
            set
            {
                if (m_rayColor != value)
                {
                    m_rayColor = value;
                    UpdateRayRenderer();
                }
            }
        }

        public float AttachStiffness
        {
            get { return m_stiffness; }
            set
            {
                if (m_stiffness != value)
                {
                    m_stiffness = value;
                    if (m_sofaRC != null)
                    {
                        m_sofaRC.setToolAttribute("stiffness", m_stiffness);
                    }
                }
            }
        }

        void Awake()
        {
            if (!startOnPlay)
                return;

            CreateSofaRayCaster();
        }

        void Start()
        {
            CreateSofaRayCaster_impl();
        }

        public virtual void CreateSofaRayCaster()
        {
            if (m_sofaContext == null)
            {
                m_sofaContext = FindFirstObjectByType<SofaUnity.SofaContext>();
                if (m_sofaContext == null)
                {
                    GameObject _contextObject = GameObject.FindGameObjectWithTag("GameController");
                    if (_contextObject != null)
                        m_sofaContext = _contextObject.GetComponent<SofaUnity.SofaContext>();
                }

                if (m_sofaContext == null)
                {
                    Debug.LogError("RayCaster::loadContext - No SofaContext found.");
                    return;
                }
            }
        }

        public void LoadSofaRayCaster(SofaUnity.SofaContext _context)
        {
            StopRay();
            m_sofaContext = _context;
            CreateSofaRayCaster_impl();
        }

        public void UnloadSofaRayCaster()
        {
            StopRay();
            m_sofaContext = null;
        }

        public override void StopRay()
        {
            if (m_sofaRC != null)
            {
                m_sofaRC.activateTool(false);
                m_sofaRC.Dispose();
                m_sofaRC = null;
            }
            base.StopRay();
        }

        public override bool CastRay()
        {
            if (automaticCast && m_sofaRC != null && m_sofaContext != null)
            {
                if (m_isActivated)
                {
                    Vector3 originS = m_sofaContext.transform.InverseTransformPoint(m_origin);
                    Vector3 directionS = m_sofaContext.transform.InverseTransformDirection(m_direction);
                    m_sofaRC.castRay(originS, directionS);
                }
            }

            if (m_drawRay)
                DrawRay();

            return false;
        }

        protected virtual void ActivateTool_impl(bool value)
        {
            m_isActivated = value;
            if (m_sofaRC != null)
            {
                m_sofaRC.activateTool(m_isActivated);

                if (m_rayType == SofaDefines.SRayInteraction.AttachTool)
                    m_sofaRC.setToolAttribute("stiffness", m_stiffness);
            }

            if (m_rayRenderer)
            {
                if (value)
                    m_rayRenderer.endColor = Color.red;
                else
                    m_rayRenderer.endColor = m_rayColor;
            }
        }

        protected virtual void CreateSofaRayCaster_impl()
        {
            if (m_sofaContext == null) CreateSofaRayCaster();
            if (m_sofaContext == null) return;

            IntPtr _simu = m_sofaContext.GetSimuContext();
            if (_simu != IntPtr.Zero && m_sofaRC == null)
            {
                // CORRECCIÓN CLAVE: Nombre único en SOFA C++ para que dos objetos llamados "PuntaActiva" NO choquen entre sí!
                string uniqueRayName = $"{base.name}_{m_rayType}_{this.GetInstanceID()}";
                float raySofaLength = m_length * m_sofaContext.GetFactorUnityToSofa(1);

                if (m_rayType == SofaDefines.SRayInteraction.CuttingTool)
                {
                    m_sofaRC = new SofaRayCasterAPI(_simu, 0, uniqueRayName, raySofaLength);
                    Debug.Log(this.name + " create SofaRayCaster CuttingTool with length: " + raySofaLength + " Under Name: " + uniqueRayName);
                }
                else if (m_rayType == SofaDefines.SRayInteraction.AttachTool)
                {
                    m_sofaRC = new SofaRayCasterAPI(_simu, 1, uniqueRayName, raySofaLength);
                    Debug.Log(this.name + " create SofaRayCaster AttachTool with length: " + raySofaLength + " Under Name: " + uniqueRayName);
                }
                else if (m_rayType == SofaDefines.SRayInteraction.FixTool)
                {
                    m_sofaRC = new SofaRayCasterAPI(_simu, 2, uniqueRayName, raySofaLength);
                    Debug.Log(this.name + " create SofaRayCaster FixTool with length: " + raySofaLength + " Under Name: " + uniqueRayName);
                }
                else
                {
                    m_sofaRC = null;
                    m_initialized = false;
                }
            }

            if (m_sofaRC == null)
            {
                Debug.Log(this.name + " No SofaRayCaster created");
            }
            else
            {
                m_initialized = true;
                if (m_sofaContext.AsyncSimulation == true)
                    m_sofaContext.RegisterRayCaster(this);
                else
                    automaticCast = true;
            }
        }

        protected void DrawRay()
        {
            if (m_rayRenderer == null)
                InitialiseRayRenderer();

            Vector3 end = m_origin + m_direction * m_length;
            m_rayRenderer.SetPosition(0, m_origin);
            m_rayRenderer.SetPosition(1, end);
        }

        protected void InitialiseRayRenderer()
        {
            m_rayRenderer = this.gameObject.AddComponent<LineRenderer>();
            m_rayRenderer.material = Resources.Load("Materials/laser") as Material;
            m_rayRenderer.startWidth = m_rayWidth;
            m_rayRenderer.endWidth = m_rayWidth;
        }

        protected void UpdateRayRenderer()
        {
            if (m_rayRenderer == null) return;
            m_rayRenderer.startColor = m_rayColor;
            m_rayRenderer.endColor = m_rayColor;
            m_rayRenderer.startWidth = m_rayWidth;
            m_rayRenderer.endWidth = m_rayWidth;
        }
    }
}