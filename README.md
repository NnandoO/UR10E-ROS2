# Plataforma de Simulación Robótica para Asistencia Quirúrgica en Realidad Virtual

![Badge de Licencia](https://img.shields.io/badge/License-MIT-green.svg) ![Badge de ROS2](https://img.shields.io/badge/ROS2-Jazzy-blue) ![Badge de Docker](https://img.shields.io/badge/Docker-Compose-red) ![Badge de Python](https://img.shields.io/badge/python-3.12%2B-orange) ![Badge de Unity](https://img.shields.io/badge/Unity-6.0LTS-black)

## Descripción General

Este repositorio contiene la implementación de una plataforma de simulación robótica avanzada, diseñada para la investigación y desarrollo de sistemas de asistencia quirúrgica. El sistema integra múltiples brazos robóticos, como el **UR10e** y el **Arctos V2**, junto con herramientas especializadas como **OpenRST**, en un entorno de alta fidelidad.

La plataforma utiliza **Unity** como motor de renderizado y simulación física, complementado por el framework de física **SofaPhysics** mediante el asset SofaUnity. Ofrece soporte nativo para interacción inmersiva con dispositivos de Realidad Virtual (**Meta Quest**). La arquitectura de software se basa en **ROS 2 (Jazzy)** para garantizar una comunicación robusta y modular entre los componentes del sistema. El entorno de desarrollo está completamente contenerizado con **Docker** para asegurar la portabilidad y reproducibilidad.

## Índice

- [Descripción General](#descripción-general)
- [Características Principales](#características-principales)
- [Arquitectura del Ecosistema](#arquitectura-del-ecosistema)
- [Requisitos de Sistema](#requisitos-de-sistema)
- [Guía de Instalación](#guía-de-instalación)
- [Código de Conducta](#código-de-conducta)
- [Licencia](#licencia)

## Características Principales

- **Simulación Multi-Robot:** Soporte para la simulación concurrente de diferentes arquitecturas de robots (UR10e, Arctos V2).
- **Integración de Realidad Virtual:** Interacción y visualización inmersiva a través de Meta Quest para un control y monitoreo intuitivo.
- **Física Avanzada con SofaPhysics:** Utilización del framework SofaPhysics para simulaciones de interacción precisas, especialmente en escenarios quirúrgicos.
- **Arquitectura Modular ROS 2:** Comunicación desacoplada y robusta entre los nodos de control, planificación y percepción.
- **Entorno Reproducible:** Configuración basada en Docker que simplifica el despliegue y la consistencia entre sistemas.
- **Instrumentación Quirúrgica:** Integración de herramientas especializadas como OpenRST para simular procedimientos complejos.

## Arquitectura del Ecosistema

El proyecto se compone de varios repositorios modulares que funcionan de manera conjunta:

- **Plataforma Central (Este Repositorio):** Contiene el entorno de simulación de Unity, la configuración de ROS 2 y la documentación principal.
- **[OpenRST]:** Modelo y lógica de control para el instrumento quirúrgico robótico.
- **[ros2_arctos_HCMUT]:** Paquetes ROS 2 específicos para la integración y control del robot Arctos V2.

[OpenRST]: https://github.com/jhulio435m/OpenRST
[ros2_arctos_HCMUT]: https://github.com/jhulio435m/ros2_arctos_HCMUT

## Requisitos de Sistema

- **Sistema Operativo:** Ubuntu 24.04 LTS (Nativo o vía WSL2). *Solo se garantiza la compatibilidad con esta versión.*
- **Software:** Git, Docker Engine, Docker Compose, Unity Hub (Editor 6.0 LTS), y ROS 2 Jazzy.
- **Hardware (VR):** Meta Quest 2 o superior con cable de enlace (Link Cable).
- **Compatibilidad de SofaUnity:** El asset SofaUnity, que integra SofaPhysics, es **únicamente compatible con el sistema operativo Windows** para la ejecución de la simulación. El desarrollo puede realizarse en entornos Linux compatibles con Unity, pero la ejecución que utilice SofaUnity debe realizarse en Windows.

## Guía de Instalación

El proceso de instalación ha sido modularizado. Siga las guías en la carpeta `docs` en el orden recomendado:


1.  **Configuración del Entorno ROS 2:**
    - [Ver guía de instalación de ROS 2](./docs/ros2_setup.md)

2.  **Instalación de Controladores de Robots:**
    - [Ver guía de instalación de drivers de UR](./docs/ur_drivers_setup.md)
    - *Nota: Para otros robots como Arctos, consulte la documentación de su respectivo repositorio.*

3.  **Configuración de Docker y URSim:**
    - [Ver guía de configuración de Docker](./docs/docker_setup.md)

4.  **Configuración de Unity y VR:**
    - [Ver guía de configuración de Unity](./docs/unity_setup.md)

5.  **Clonar el Repositorio:**
    ```bash
    git clone https://github.com/jhulio435m/ur10e_ROS2-UNCP
    cd ur10e_FIS-UNCP
    ```
## Código de Conducta

Este proyecto se adhiere a un código de conducta para garantizar un entorno de colaboración respetuoso y constructivo. Se espera que todos los colaboradores sigan sus directrices.

- [Leer el Código de Conducta](./CODE_OF_CONDUCT.md)

## Licencia

Este proyecto se distribuye bajo los términos de la Licencia MIT.

- [Ver detalles de la Licencia](./LICENSE)