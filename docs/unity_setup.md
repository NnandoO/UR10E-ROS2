# Configuración de Unity

Esta guía cubre la instalación de Unity Hub, los paquetes necesarios para trabajar con modelos de robots en formato URDF y consideraciones para la física avanzada.

## 1) Instalación de Unity Hub

Unity Hub es la herramienta recomendada para gestionar las instalaciones del Editor de Unity y los proyectos.

- **Nota para usuarios de WSL:** Si estás utilizando el Subsistema de Windows para Linux (WSL), omite este paso e instala Unity Hub directamente en Windows.
- **Enlace:** [Guía de instalación de Unity Hub para Linux](https://docs.unity3d.com/hub/manual/InstallHub.html#install-hub-linux)
- **Instrucciones:** Deberás agregar el repositorio oficial de Unity Technologies a tu sistema y luego instalar el paquete `unityhub` mediante tu gestor de paquetes.

Una vez instalado, abre Unity Hub e instala la versión del editor recomendada para este proyecto (**Unity 6.0 LTS**).

## 2) Instalación de URDF Importer en Unity

Para importar, configurar y visualizar modelos de robots descritos en archivos URDF, necesitas el paquete `URDF-Importer` de Unity.

- **Repositorio:** [Unity-Technologies/URDF-Importer](https://github.com/Unity-Technologies/URDF-Importer)
- **Instrucciones:**
    1. Abre tu proyecto de Unity.
    2. Ve al menú **Window > Package Manager**.
    3. Haz clic en el botón **+** en la esquina superior izquierda.
    4. Selecciona la opción **"Add package from git URL..."**.
    5. Pega la siguiente dirección y haz clic en "Add":

    ```text
    https://github.com/Unity-Technologies/URDF-Importer.git?path=/com.unity.robotics.urdf-importer#v0.5.2
    ```

## 3) Consideraciones sobre SofaUnity

El asset SofaUnity, que permite la integración del framework de física SofaPhysics, es una parte fundamental de esta simulación para interacciones físicas avanzadas.

- **Compatibilidad:** Es crucial destacar que **SofaUnity solo es compatible con el sistema operativo Windows**. Para ejecutar simulaciones que hagan uso de SofaPhysics, deberás hacerlo en un entorno Windows. El desarrollo en el editor de Unity es posible en Linux, pero la funcionalidad de SofaPhysics estará limitada o inactiva en ese sistema operativo.
