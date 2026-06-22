# Instalación de Drivers de Universal Robots para ROS 2

Esta guía cubre la instalación de los drivers necesarios para comunicar ROS 2 con los robots de Universal Robots (reales o simulados).

## Instalación de los Drivers

Para establecer la comunicación y el control, es necesario instalar los drivers oficiales para ROS 2 Jazzy.

- **Descripción:** El metapaquete `ros-jazzy-ur` contiene los drivers del robot, las descripciones (URDF) y las herramientas de comunicación.
- **Instrucciones:** Ejecuta los siguientes comandos en orden para instalar y configurar los controladores desde el código fuente.

1.  **Instalar dependencias de compilación y herramientas de ROS:**

    ```bash
    sudo apt-get update
    sudo apt-get install ros-jazzy-ur
    sudo apt install python3-colcon-common-extensions python3-vcstool
    ```

2.  **Crear un espacio de trabajo de Colcon y clonar el repositorio:**

    ```bash
    export COLCON_WS=~/workspace/ros_ur_driver
    mkdir -p $COLCON_WS/src
    cd $COLCON_WS
    git clone -b jazzy https://github.com/UniversalRobots/Universal_Robots_ROS2_Driver.git src/Universal_Robots_ROS2_Driver
    ```

3.  **Instalar dependencias de ROS:**

    ```bash
    # Inicializa rosdep si no lo has hecho antes
    rosdep init
    rosdep update
    # Importa las dependencias del repositorio y las instala
    vcs import src --skip-existing --input src/Universal_Robots_ROS2_Driver/Universal_Robots_ROS2_Driver.jazzy.repos
    rosdep update
    rosdep install --ignore-src --from-paths src -y
    ```

4.  **Construir el espacio de trabajo:**

    ```bash
    cd $COLCON_WS
    colcon build --cmake-args -DCMAKE_BUILD_TYPE=Release
    ```

5.  **Cargar el entorno del nuevo espacio de trabajo:**

    Para usar los paquetes recién compilados, carga el overlay de tu workspace. Es recomendable añadir este comando a tu `.bashrc` si trabajas frecuentemente en este proyecto.

    ```bash
    source $COLCON_WS/install/setup.bash
    ```
