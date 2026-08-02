import Sofa
import Sofa.Core

def createScene(root):
    # Configuración principal
    root.gravity = [0, -9.8, 0]
    root.dt = 0.05

    # Cargar todos los plugins necesarios
    plugins = [
        "Sofa.Component.Collision.Detection.Algorithm",
        "Sofa.Component.Collision.Detection.Intersection",
        "Sofa.Component.Collision.Geometry",
        "Sofa.Component.Collision.Response.Contact",
        "Sofa.Component.Constraint.Projective",
        "Sofa.Component.Engine.Select",
        "Sofa.Component.IO.Mesh",
        "Sofa.Component.LinearSolver.Iterative",
        "Sofa.Component.Mapping.Linear",
        "Sofa.Component.Mass",
        "Sofa.Component.MechanicalLoad",
        "Sofa.Component.ODESolver.Backward",
        "Sofa.Component.SolidMechanics.FEM.Elastic",
        "Sofa.Component.SolidMechanics.Spring",
        "Sofa.Component.StateContainer",
        "Sofa.Component.Topology.Container.Dynamic",
        "Sofa.Component.Topology.Mapping",
        "Sofa.Component.Topology.Container.Constant",
        "Sofa.Component.Visual",
        "Sofa.GL.Component.Rendering3D"
    ]
    for p in plugins:
        root.addObject("RequiredPlugin", name=p)

    root.addObject("VisualStyle", displayFlags="showVisual")
    root.addObject("DefaultAnimationLoop")
    root.addObject("DefaultVisualManagerLoop")

    # Pipeline de Colisiones (¡Con los valores en miniatura para que no se congele!)
    root.addObject("CollisionPipeline", verbose="0")
    root.addObject("BruteForceBroadPhase")
    root.addObject("BVHNarrowPhase")
    root.addObject("CollisionResponse", response="PenalityContactForceField")
    root.addObject("MinProximityIntersection", name="Proximity", alarmDistance=0.01, contactDistance=0.005)
    root.addObject("CollisionDetectionDisplay", name="CollisionDetectionDisplay")

    # ==================== SUELO ====================
    suelo = root.addChild("Suelo")
    suelo.addObject("MeshTopology", name="grid", position=[-50, -15, -50,  50, -15, -50,  -50, -15, 50,  50, -15, 50], triangles=[0, 2, 1, 1, 2, 3])
    mo_suelo = suelo.addObject("MechanicalObject", name="mo_suelo")
    suelo.addObject("TriangleCollisionModel", simulated=False, moving=False)
    suelo.addObject("LineCollisionModel", simulated=False, moving=False)
    suelo.addObject("PointCollisionModel", simulated=False, moving=False)

    visuSuelo = suelo.addChild("VisuSuelo")
    visuSuelo.addObject("OglModel", name="Visual_suelo", color=[0.5, 0.5, 0.5, 1.0])
    visuSuelo.addObject("IdentityMapping", input=mo_suelo.getLinkPath(), output="@Visual_suelo")

    # ==================== FUNCIÓN CONSTRUCTORA DE ÓRGANOS ====================
    # Esta función crea toda la estructura física, visual y de colisión automáticamente
    def add_organ(parent_node, name, mesh_file, scale, trans, rot, color_name):
        vol = parent_node.addChild(f"{name}Volume")
        
        vol.addObject("EulerImplicitSolver", name=f"cg_odesolver_{name.lower()}", rayleighStiffness=0.1, rayleighMass=0.1)
        vol.addObject("CGLinearSolver", name=f"linear_solver_{name.lower()}", iterations=25, tolerance=1e-9, threshold=1e-9)

        loader = vol.addObject("MeshGmshLoader", name=f"loader_{name.lower()}", filename=mesh_file)
        mo = vol.addObject("MechanicalObject", name=f"Volume_{name.lower()}", src=loader.getLinkPath(), scale3d=scale, translation=trans, rotation=rot)

        container = vol.addObject("TetrahedronSetTopologyContainer", name=f"TetraContainer_{name.lower()}", src=loader.getLinkPath())
        vol.addObject("TetrahedronSetTopologyModifier", name=f"Modifier_{name.lower()}")
        vol.addObject("TetrahedronSetGeometryAlgorithms", name=f"GeomAlgo_{name.lower()}", template="Vec3d")

        vol.addObject("MeshMatrixMass", massDensity=1.0, topology=container.getLinkPath())
        
        # Anclaje para que no caigan al vacío
        vol.addObject("FixedProjectiveConstraint", indices=[0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        
        # Física elástica
        vol.addObject("TetrahedronFEMForceField", name=f"FEM_{name.lower()}", youngModulus=100.0, poissonRatio=0.3, method="large", computeGlobalMatrix=False, computeVonMisesStress=True)

        surf = vol.addChild(f"{name}Surface")
        tri_container = surf.addObject("TriangleSetTopologyContainer", name=f"TriContainer_{name.lower()}")
        surf.addObject("TriangleSetTopologyModifier", name=f"Modifier_{name.lower()}_surf")
        surf.addObject("TriangleSetGeometryAlgorithms", name=f"GeomAlgo_{name.lower()}_surf", template="Vec3d")

        surf.addObject("Tetra2TriangleTopologicalMapping", input=container.getLinkPath(), output=tri_container.getLinkPath())
        surf.addObject("TriangularBendingSprings", name=f"FEM-Bend_{name.lower()}", stiffness=300.0, damping=1.0)

        surf.addObject("TriangleCollisionModel", tags="CarvingSurface")
        surf.addObject("LineCollisionModel", tags="CarvingSurface")
        surf.addObject("PointCollisionModel", tags="CarvingSurface")

        visu = surf.addChild(f"{name}Visu")
        visu.addObject("OglModel", name=f"Visual_{name.lower()}", color=color_name)
        visu.addObject("IdentityMapping", input=mo.getLinkPath(), output=f"@Visual_{name.lower()}")

    # ==================== INSTANCIACIÓN DE LOS ÓRGANOS ====================
    # Llamamos a la función constructora pasándole tus coordenadas exactas
    add_organ(root, "Higado", "HIGADOGMSH.msh", [0.1, 0.1, 0.1], [-1.415769, 0.1238585, -0.1545094], [-87.577, -380.034, 197.892], "blue")
    add_organ(root, "Estomago", "ESTOMAGO.msh", [0.1, 0.1, 0.1], [-12.9002, -2.18534, -5.785468], [7.493, 158.221, -6.203], "red")
    add_organ(root, "Duodeno", "DUODENO.msh", [0.1, 0.1, 0.1], [4.601434, -9.201647, 5.557661], [-9.033, 24.584, 36.054], "green")
    
    # Conducto biliar con su escala de 0.15
    add_organ(root, "ConductoBiliar", "CONDUCTOBILIARGMSH.msh", [0.15, 0.15, 0.15], [-0.2345983, 0.734071, 6.268752], [2.176, 0.578, 19.135], "yellow")
    
    add_organ(root, "Vesicula", "VESICULAGMSH.msh", [0.1, 0.1, 0.1], [4.235449, -0.8514917, -15.77367], [2.319, 158.858, 27.663], "cyan")

    return root