# Modos del simulador, escena quirúrgica y servidores UR10e

## Modos (selector al iniciar, o tecla **M** / botón **Menú** del mando izquierdo)

| Modo | Qué hace | ¿Necesita WSL? |
|---|---|---|
| **Simular operación (VR)** | Lo que ya tenías: te pones los lentes, la punta de cada brazo sigue tu mando y, cuando agarras un instrumento, salta a la punta del robot. | Sí (`seguimientoR.py` / `seguimientoL.py`) |
| **Ejecutar demo** | Los dos brazos operan solos (ver más abajo). Tú observas. | **No** |
| **Procedimiento guiado** | Operas tú, con marcadores y hologramas paso a paso. | Sí |

## Escena

- **Órganos SOFA dentro del cuerpo.** `SofaContext` se movió (-3 cm, -7,9 cm, +5 cm). El hígado queda bajo el reborde costal derecho, la vesícula junto al reborde y el estómago a la izquierda. SofaUnity convierte los rayos de corte y agarre con la transformación de `SofaContext`, así que la física no cambia.
- **Materiales de órganos** en `Assets/LapSimUncp/Materials/Organos/`: hígado, vesícula, estómago y duodeno.
- **Puertos de trocar.** Cada socket está orientado hacia la vesícula y el trocar entra unos 4,5 cm por la pared:

  | Socket | Puerto | Uso |
  |---|---|---|
  | `Punto_Trocar_1` | Cuadrante superior izquierdo | Brazo derecho: tijera / hook |
  | `Punto_Trocar_2` | Axilar anterior derecho | Brazo izquierdo: grasper |
  | `Punto_Trocar_3` | Umbilical | Laparoscopio |

- **Fulcro (`PuntoEntrada`)** en la cánula, 1 cm bajo la piel (antes estaba en la cabeza del trocar). Su Rigidbody ahora es cinemático; antes flotaba suelto dentro del trocar.
- **`PreparacionQuirurgica`** (en `GestorModos`) coloca los trocares en sus puertos al iniciar. Desactiva `colocarAlIniciar` si quieres practicar la colocación a mano.
- **`VistaAbdominal`** (tecla **T** o clic del joystick izquierdo) cambia entre tres vistas:
  - **Normal:** piel opaca; el interior solo se ve con el laparoscopio.
  - **Translúcida:** se ve la silueta de la piel y el campo operatorio despejado.
  - **Oculta.**

  Además oculta siempre las estructuras del modelo anatómico que quedaban dentro de los órganos SOFA (ganglios, vasos) y el epiplón.
- **`LuzLaparoscopio`**: foco detrás de la óptica, para que la imagen del laparoscopio no se vea negra ni quemada.

## Controles

| Acción | Teclado | Mando Meta Quest |
|---|---|---|
| Agarrar / trasladar instrumento | — | Gatillo (índice) |
| Acción SOFA (agarre / corte) | G | Grip (dedo medio) |
| Iniciar seguimiento / liberar Z | Espacio | — |
| Recalibrar muñeca | R | Clic stick derecho |
| Wrist 3 | — | A / B (derecho) |
| **Clutch** (mantener) | **Shift izq.** | X (izquierdo) |
| Cambiar modo | M | Menú (izquierdo) |
| Vista de la pared abdominal | T | Clic stick izquierdo |
| Procedimiento guiado: siguiente / anterior / reiniciar | N / B / Z | Y (izquierdo) = siguiente |

> `Tool_cut` y `Tool_grasp (1)` (herramientas de ejemplo de SofaUnity) están **desactivados** en la escena. Su `GameObjectController` activaba un láser de corte SOFA con la tecla **C** (y lo apagaba con V), y `Tool_cut` proyectaba una luz roja junto al paciente. Para recuperarlos, actívalos en la jerarquía; solo se referencian entre sí.

## Ejecutar demo (`DemoAutomaticaColecistectomia`)

La demo sigue el procedimiento estándar y dura unos 3 minutos:

0. **Insuflación, puertos y docking (flujo daVinci y video guía).** Insuflación de CO2. Primero el puerto de la óptica en el umbilical, después se introduce el laparoscopio; ya con la óptica dentro, cada brazo hace la incisión en la piel (marca de ~14 mm), recibe su trocar de trabajo, lo introduce por el eje del puerto y lo deja encajado: primero el axilar derecho (grasper) y después el cuadrante superior izquierdo (disector).
1. **Entrega e inserción:** el grasper va al brazo izquierdo y el hook al derecho. Entran por sus trocares, quedan anclados y pivotan en el fulcro.
2. **Tracción del fondo vesicular** con el grasper, hacia arriba y hacia la cabeza (agarre **SOFA** real).
3. **Disección del triángulo de Calot** con el hook (electrocauterio), hasta la **visión crítica de seguridad**. La narración comprueba los tres criterios del video guía: triángulo hepatocístico despejado, tercio inferior de la vesícula separado de la placa cística, y sólo el conducto cístico entrando en la vesícula. **La arteria cística no se modela** (el modelo SOFA sólo tiene el conducto), así que no se clipa ni se secciona.
4. **Clipado.** Cambio de hook por **clipadora**. En la arteria y en el conducto se colocan **2 clips del lado del paciente y 1 del lado de la vesícula**.
5. **Sección entre clips.** Cambio de clipadora por tijera; se cortan la arteria y luego el conducto.
6. **Disección del lecho hepático.** Cambio de tijera por hook; se separa la vesícula del cuello al fondo.
7. **Extracción.** La vesícula se introduce en una **bolsa de extracción transparente** (endobag, como en el video guía), sale por el trocar sujeta por el grasper y se deposita en una bandeja de la mesa de instrumental.
8. **Revisión:** los muñones quedan cerrados con dos clips cada uno. Se retiran los instrumentos y los brazos vuelven a su pose.
9. **Retirada de la óptica y de los trocares.** Como en el video guía: sale el laparoscopio y después cada trocar por su incisión; las incisiones se cierran a una línea fina y los trocares y la óptica vuelven a la mesa con su obturador. Al terminar, los trocares quedan en la mesa (cinemáticos) y se pueden volver a colocar a mano.

Detalles de implementación:

- Los brazos se mueven con IK en Unity (`CinematicaBrazoUR`), usando sus `ArticulationBody` reales; `seguimiento` /
  `seguimientoL` quedan en pausa (`PausadoPorDemo`) y los mandos no agarran nada. La entrada al trocar es en dos
  tiempos (punto de espera 6 cm arriba y descenso lento) para que el brazo no caiga de golpe.
- **Colocación de puertos.** Al iniciar, la demo encaja un instante los trocares para leer la pose exacta que da cada
  socket, los devuelve a la mesa con el socket desactivado y luego los colocan los brazos (`ColocarPuerto`). El estado
  final es el mismo que deja `PreparacionQuirurgica` (trocar seleccionado por su `XRSocketInteractor`). Si cancelas la
  demo a mitad, los puertos vuelven a como estaban antes de empezarla.
- **Entrada y salida por dentro del trocar.** El instrumento espera con la punta 3 cm por encima del cabezal del
  trocar y recorre toda la cánula por su eje; al retirarlo sale hasta rebasar el cabezal (antes se quedaba dentro).
- **Instrumental OpenRST (jcolan/OpenRST).** Ambos UR10e llevan montado el instrumento OpenRST real
  (base → tubo de 31 cm → pitch → dedos). En la demo el robot opera con él: el instrumento laparoscópico sigue
  haciendo la física SOFA pero invisible, con su punta en la mordaza. Las mordazas intercambiables del repositorio
  (en `Models/OpenRST_EE/`, cargadas de los STL en tiempo real) se cambian por fase: **fenestrada** para el grasper,
  **Maryland** para disección y sección, **porta-agujas** como aplicador de clips; la pinza se abre y cierra al actuar.
  Se controla con `usarInstrumentalOpenRST` en el Inspector.
- **El conducto cístico es REAL**: es la cola fina de la malla SOFA de la vesícula (`VESICULA.msh`, remallada con
  tetraedros de ~5 mm para que el corte funcione). No se añade ninguna estructura que no exista en el modelo.
- **Clips tipo Hem-o-lok** (polímero violeta, como en el video guía: "Colecistectomía Laparoscópica con el Sistema
  Weck Hem-o-lok"): 2 del lado del paciente y 1 del lado de la vesícula.
- **El corte es corte SOFA real.** El rayo de corte talla donde golpea la superficie, así que la tijera se queda a
  9 mm del conducto y el rayo, dirigido con `apuntarHacia` (`SofaInstrumentoQuirurgico`), barre en abanico el disco
  de la sección saliendo hacia el lado libre. La demo verifica con `VesiculaSeccionada()` (componentes conexas de la
  malla) que quedó en dos piezas, y reintenta hasta 3 veces.
- En la extracción, la malla cortada se divide por el plano del corte: el cuerpo viaja con el grasper (con su clip)
  a la bandeja, y el **muñón clipado se queda en el paciente**. Al salir del modo todo se restaura.

## Correcciones en scripts existentes

- **Robots que se caían.** El `Controller` del importador URDF pone stiffness, damping y forceLimit a 0 al iniciar. Sin datos de Python los brazos quedaban flácidos. Ahora `SeguimientoBrazoBase` mantiene los motores rígidos y sostiene la pose hasta recibir la de Python.
- **`IntercambioHerramienta`.** Solo traslada al robot objetos con `ControladorFulcroLaparoscopio`; antes, agarrar un trocar lo teletransportaba a la punta del brazo. También añade una API para la demo.
- **`ControladorFulcroLaparoscopio`.**
  - Evita el volteo de 180° cuando la base cruza el fulcro.
  - Limpia la lista estática de trocares ocupados al entrar en Play.
  - Añade `CalcularManoParaPunta()`.
  - **Topes del trocar (centro de movimiento remoto, como en daVinci).** Los instrumentos se mueven de forma
    cinemática, así que un collider no los detiene; los topes son matemáticos y valen para la demo y para el modo VR:
    - *Tope del cabezal* (`topeCabezalTrocar`, `margenCabezal`): la mano o el cabezal del robot no puede pasar del
      cabezal del trocar (16,8 cm sobre el fulcro), así que la punta no entra más de lo que el tubo permite. Al
      agarrar nunca es más restrictivo que la posición actual de la mano, para que no haya saltos.
    - *Salida guiada* (`guiarSalidaPorCanula`): al retirar, el instrumento sigue pasando por el eje del trocar hasta
      que la punta sale por el cabezal; solo entonces queda libre. Antes se liberaba dentro de la cánula y podía
      atravesar sus paredes.
- **`PreparacionQuirurgica`.** Guarda la pose de cada trocar en la mesa y añade `ColocarUno()`, `Liberar()` y
  `EstaColocado()` por puerto. También añade a cada trocar el componente `TrocarQuirurgico`.

## Fulcro y trocares: que el instrumento vaya por el centro

Lo que estaba mal y cómo quedó (medido en Play con la demo y con una mano simulada):

| Problema | Causa | Arreglo |
|---|---|---|
| El instrumento cruzaba el trocar hasta a 35° | El trocar estaba rígido en su socket; con 20 cm de largo, cualquier giro de más de ~1° atraviesa la pared | **`TrocarQuirurgico`**: el trocar bascula alrededor de su fulcro siguiendo el eje del instrumento, y vuelve a su reposo al retirarlo |
| El fulcro caía sobre la pared de la cánula | `PuntoEntrada` estaba en y=0, pero el tubo del modelo está centrado en y≈6,7 mm e inclinado 2,74° | `PuntoEntrada` movido al eje real del tubo; su +X local es ahora ese eje |
| El instrumento atravesaba el mango negro | El modelo trae el **obturador** puesto (punzón y mango) | Al quedar el trocar en su puerto el obturador se retira y queda la cánula hueca; en la demo lo saca el brazo y lo deja en la mesa |
| El instrumento pivotaba apoyado en su superficie | `Physics.ClosestPoint` da un punto de la superficie del tubo, no de su eje | El punto de pivote se proyecta sobre el eje del tubo (el que pasa por la punta activa) |
| El brazo derecho se quedaba a 3–5 cm del trocar | No llegaba: con el codo recto le faltaban ~3 cm de alcance | El grupo `robots/ur10e` (robot derecho con su carro) está 15 cm más cerca de la cabecera |
| La mordaza del OpenRST colgaba torcida | `pitch_link` y los dedos son articulaciones sin rigidez (el importador URDF pone stiffness 0) | La demo las sujeta con sus motores: muñeca recta y pinza a la apertura pedida. La IK controla el tubo rígido (`interface_link`), no un punto de los dedos |

Resultado en la demo, con el instrumento dentro del trocar: el tubo pasa a 1–3 mm del centro y a menos de 1,7° del
eje del trocar (antes 3–7 cm y hasta 35°), y la IK converge siempre.

`TrocarQuirurgico` tiene dos opciones en el Inspector (durante Play): `obturadorAutomatico` y `tiempoRetorno`.
- **`SofaInstrumentoQuirurgico`.** `ForzarAccion()` / `LiberarAccionForzada()` permiten controlar la acción SOFA desde código.

## Servidores Python (WSL: `~/PlanoFijo/`)

| Brazo | Comando | Robot |
|---|---|---|
| Derecho | `python3 seguimientoR.py` (`--modo sim`) | Ninguno |
| Derecho | `python3 seguimientoR.py --modo fisico` | **El único UR10e real** |
| Izquierdo | `python3 seguimientoL.py` | Siempre simulado (rechaza `--modo fisico`) |

- Modelo UR10e oficial (DH) con IK en C++ (`ik_LM`, unos 0,1 ms).
- `TCP_NODELAY`.
- Caja de trabajo ±0,35 / 0,45 / 0,45 m alrededor del TCP inicial.
- Velocidad máxima: 0,25 m/s en cartesiano y 1 rad/s por articulación.
- En modo físico se usa la pose real (`getActualQ()`) como origen, y se llama a `servoStop()` al desconectar.
