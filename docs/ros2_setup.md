# Configuración de ROS 2 Jazzy

Esta guía detalla los pasos para instalar y configurar ROS 2 Jazzy en un sistema Ubuntu 24.04 LTS.

## 1) Instalación de ROS 2 (Jazzy)

Para instalar la distribución "Jazzy" de ROS 2 en Ubuntu 24.04 LTS mediante paquetes Debian, es necesario seguir la documentación oficial. Este paso es fundamental para asegurar que todas las dependencias y paquetes principales de ROS 2 estén correctamente instalados.

- **Enlace:** [Guía de instalación de ROS 2 en Ubuntu](https://docs.ros.org/en/jazzy/Installation/Ubuntu-Install-Debs.html)
- **Instrucciones:** Asegúrate de configurar la localización UTF-8, agregar los repositorios de ROS 2 y ejecutar el comando de instalación completo que se indica en la guía oficial. *Solo se garantiza la compatibilidad con Ubuntu 24.04 LTS.*

## 2) Instalación de Xacro

Xacro es una herramienta que facilita la creación y el mantenimiento de archivos URDF (Unified Robot Description Format) mediante el uso de macros.

- **Instrucciones:** Ejecuta los siguientes comandos para instalar Xacro para ROS 2 Jazzy.

```bash
sudo apt update
sudo apt install ros-jazzy-xacro
```

## 3) Configuración Automática del Entorno

Para evitar tener que cargar manualmente las variables de entorno de ROS 2 cada vez que abres una nueva terminal, puedes agregar el comando `source` al archivo de configuración de tu shell (`.bashrc`).

- **Propósito:** Carga automáticamente el entorno de ROS 2 en cada nueva sesión de terminal.
- **Instrucciones:** Ejecuta el siguiente comando para añadir la línea de configuración al final de tu archivo `.bashrc`. Deberás cerrar y volver a abrir todas las terminales para que el cambio surta efecto.

```bash
# Cerrar todas las terminales luego de ejecutar esto
echo "source /opt/ros/jazzy/setup.bash" >> ~/.bashrc
```
