# Configuración de Docker y URSim (Ubuntu 24.04 LTS)

Esta guía explica cómo instalar Docker y ejecutar el simulador oficial de Universal Robots (URSim) en un contenedor en un sistema Ubuntu 24.04 LTS.

## 1) Instalación de Docker

Docker es un requisito para ejecutar el entorno de simulación de forma aislada y reproducible.

- **Enlace:** [Guía de instalación de Docker para Ubuntu](https://docs.docker.com/engine/install/ubuntu/)
- **Instrucciones:** Sigue paso a paso los comandos listados en la página oficial para instalar Docker Engine en tu sistema. *Solo se garantiza la compatibilidad con Ubuntu 24.04 LTS.*

## 2) Configuración de Docker e Inicio de URSim

Una vez instalado Docker, configura los permisos de usuario e inicia el simulador de Universal Robots (E-Series).

- **Configuración:** Permite ejecutar Docker sin necesidad de `sudo` y descarga la imagen del simulador.
- **Instrucciones:** Copia y pega los siguientes bloques en tu terminal.

1.  **Añadir tu usuario al grupo `docker`:**
    Este paso es importante para poder ejecutar comandos de Docker sin `sudo`. **Deberás reiniciar tu sesión o el equipo** para que los cambios de permisos surtan efecto.

    ```bash
    # 1. Añadir el usuario actual al grupo de Docker
    sudo usermod -aG docker $USER
    ```

2.  **Descargar la imagen del simulador:**
    Este comando descarga la imagen de URSim para los robots de la E-Series.

    ```bash
    # 2. Descargar la imagen del simulador (E-Series)
    docker pull universalrobots/ursim_e-series
    ```

3.  **Ejecutar el simulador:**
    Este comando inicia el contenedor de URSim. Puedes acceder a la interfaz del robot a través de un cliente VNC o directamente desde un navegador web.

    ```bash
    # 3. Ejecutar el simulador
    # Acceso VNC: puerto 5900 | Acceso Web: puerto 6080
    docker run --rm -it -p 5900:5900 -p 6080:6080 --name ursim universalrobots/ursim_e-series
    ```

## Notas de Acceso

- **VNC:** Puedes conectarte a `localhost:5900` con cualquier cliente VNC (por ejemplo, Remmina en Linux).
- **Web:** Puedes acceder a la interfaz web en `http://localhost:6080`.
