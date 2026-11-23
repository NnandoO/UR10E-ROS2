# Guía: hacer la colecistectomía a mano con el Meta Quest 3

En este modo haces tú toda la operación con los mandos: controlas los dos brazos robóticos, agarras con
ellos trocares e instrumentos y operas mirando la imagen del laparoscopio en una pantalla flotante.

> **Estado de las pruebas.** Todo el recorrido se probó de principio a fin con mandos simulados por
> software, sin errores. **No se ha probado con los lentes puestos.** Lo que solo se puede ajustar con
> el visor está en la sección 7.

## 1. Antes de empezar

1. **El simulador corre en el PC**, no dentro del visor (SOFA solo existe para Windows). Conecta el
   Quest 3 con Link o Air Link y deja Meta Quest Link como runtime de OpenXR.
2. Abre el proyecto en Unity 6 (6000.0.74f1), escena `Assets/LapSimUncp/Scenes/MainScene.unity`.
3. **No hacen falta los servidores de WSL.**
4. Pulsa Play con el visor conectado: la escena arranca directamente en este modo.

## 2. Qué pasa al arrancar

1. **Los brazos van solos a su posición inicial de trabajo** (unos 6 s). Es la pose que mandaban los
   servidores de WSL al conectar. Al terminar la operación vuelven a ella.
2. El paciente está con las **piernas abiertas 30°** (15° cada una).
3. El **cuadro de guía** (imagen del laparoscopio arriba, paso actual e instrucciones debajo) es un
   **monitor en la cabecera de la camilla**, por encima del paciente: no tapa el campo de trabajo.
   Si lo quieres un momento delante de ti, haz clic con el stick derecho (o tecla P); otro clic lo
   devuelve a la cabecera.
4. Desde ese momento **cada brazo sigue a su mando**.

## 3. Controles

| Botón | Qué hace |
|---|---|
| **Gatillo** (índice), mantenido | **Seguimiento:** el brazo de ese mando sigue a tu mano |
| **Grip** (dedo medio) | **Agarrar**, en los dos mandos (ver abajo) |
| **X** (mando izquierdo) | Fija o suelta el agarre del grasper sobre el tejido |
| **A** (mando derecho) (o V) | Cambia entre **primera persona** y **vista del laparoscopio** |
| **B** o **Y** (o Enter) | Pide al asistente que haga el siguiente paso por ti |
| Clic del **stick derecho** (o P) | Trae el cuadro de guía delante de ti / lo devuelve a la cabecera |
| **Menú** del mando izquierdo (o M) | Cambia de modo |

Mando derecho = brazo derecho; mando izquierdo = brazo izquierdo.

### Primera persona y vista del laparoscopio

El botón **A** alterna entre dos formas de ver; una etiqueta sobre la imagen dice en cuál estás:

- **Primera persona** (la de arranque): ves la sala con tus ojos, los brazos, el paciente y los trocares.
  La imagen del laparoscopio y la guía están en el monitor de la cabecera de la camilla.
- **Vista del laparoscopio:** la imagen va pegada a tu cabeza y ocupa casi todo tu campo de visión
  (1 m de ancho a 55 cm), con solo el nombre del paso debajo. Es como mirar por la óptica: sirve para operar
  con precisión. Por los lados sigues viendo algo de la sala.

Usa primera persona para ver dónde y cómo se mueven los brazos, y la del laparoscopio para trabajar
dentro. El tamaño y la distancia se ajustan con `anchoVistaLaparoscopio` y `distanciaVistaLaparoscopio`.

### Qué agarra el grip

- **Con tu mano**, sobre un trocar o un instrumento: en tu mano queda un **clon** y el objeto real
  vuela al brazo. El clon desaparece al soltar el grip.
- **Con la pinza del brazo** (brazo libre, fuera del paciente): una pulsación **coge** lo que tenga a
  menos de 9 cm (trocar o instrumento); otra pulsación lo **suelta**. No hace falta mantenerlo apretado.
- **Con el instrumento dentro del paciente**: es la acción del instrumento mientras lo mantienes
  (agarrar tejido, cauterizar, poner un clip, cortar).

### Cómo se mueve el brazo

- **Fuera del paciente (control libre):** escala 1:1. El brazo se desplaza lo mismo que tu mano y su
  muñeca gira lo mismo que giras el mando. El límite es el alcance real del brazo.
- **Con el instrumento dentro (control por el trocar):** el movimiento va referido a la imagen (mano a
  la derecha = punta a la derecha en pantalla; mano hacia delante = más adentro), a escala 2:1 y siempre
  pasando por el trocar.
- En los dos casos es **relativo**: solo se mueve con el gatillo apretado. Si te quedas sin recorrido,
  suelta el gatillo, recoloca la mano y vuelve a apretar.

## 4. La operación, paso a paso

| Paso | Qué haces | Cuándo avanza |
|---|---|---|
| **0 · Puertos** | Coloca los 3 trocares en las esferas verdes, en el orden que quieras (ver 4.1) | Con los 3 colocados. Al colocar el del ombligo entra solo el laparoscopio |
| **1 · Instrumentos** | Coge de la mesa con la mano el grasper y el disector (ver 4.2), o pulsa **B / Y** | Con los dos dentro de sus trocares |
| **2 · Tracción** | **Mano izquierda:** lleva el grasper a la esfera amarilla del fondo de la vesícula, aprieta el **grip**, tira hacia arriba y pulsa **X** para fijar | Agarre fijado y vesícula traccionada más de 1,5 cm |
| **3 · Disección** | **Mano derecha:** lleva el disector a las bolitas amarillas de grasa y mantén el **grip** | Sin grasa: visión crítica de seguridad |
| **4 · Clipado** | Cambia al aplicador de clips (mano o **B / Y**). Llévalo a cada esfera verde y aprieta el **grip**: 1 clip del lado de la vesícula y 2 del lado del paciente | Con los 3 clips |
| **5 · Sección** | Cambia a la tijera. Lleva su punta a la esfera **azul** y mantén el **grip** | Conducto cortado |
| **6 · Lecho hepático** | Cambia al disector. Recorre las esferas naranjas manteniendo el **grip** | Sin esferas |
| **7 · Extracción** | **B / Y** para embolsar. Luego, **mano izquierda:** tira del grasper hacia su trocar | La bolsa sale y va a la bandeja |
| **8 · Revisión** | Mira el muñón con sus clips y pulsa **B / Y** | |
| **9 · Cierre** | Retira los trocares con la mano, o pulsa **B / Y** | Informe final: tiempos y avisos |

### 4.1 Colocar un trocar

Hay tres formas; puedes mezclarlas:

- **Con la mano y el brazo.** Coge un trocar de la mesa con el **grip**: va a la pinza del brazo de esa
  mano. Con el **gatillo**, lleva el brazo hacia una esfera verde del abdomen: **al llegar a menos de
  13 cm encaja solo**, sin apretar nada. También encaja si acercas a la esfera el **clon** que tienes
  en la mano.
- **Solo con el brazo.** Si un trocar está al alcance de la pinza (9 cm), aprieta el **grip** para
  cogerlo y haz lo mismo.
- **Con el asistente.** Pulsa **B / Y**: el brazo hace la incisión y coloca el siguiente puerto.

Los tres trocares son iguales: cualquiera vale para cualquier puerto. Al encajar aparece la marca de
incisión y se retira el obturador.

### 4.2 Montar o cambiar un instrumento

- **Con la mano.** Coge el instrumento de la mesa con el **grip**: en tu mano queda un clon y el real
  va a su brazo. Si los puertos ya están colocados, entra solo por su trocar; si ese brazo llevaba otro
  instrumento, primero lo retira y lo devuelve a la mesa.
- **Con el asistente.** Pulsa **B / Y** y monta el instrumento que toca en ese paso.

Cada instrumento tiene su brazo, da igual con qué mano lo cojas: el **grasper va al izquierdo** y el
disector, el aplicador de clips y la tijera van al **derecho**.

Con el brazo libre y un instrumento en la pinza, otra pulsación del grip lo devuelve a la mesa.

### 4.3 Avisos

Aparecen bajo las instrucciones. Cuentan como error en el informe:

- Poner un clip sobre la marca roja del corte.
- Intentar cortar antes de tener los 3 clips.
- Apretar el grip con la tijera a más de 2 cm de la marca.

Los demás avisos solo orientan (acercar más el aplicador, tijera fuera de la esfera azul, brazo
ocupado, nada al alcance de la pinza).

## 5. Consejos

- **Fija la tracción con X** nada más agarrar el fondo: la mano izquierda queda libre.
- **La esfera azul importa.** El corte solo actúa si el rayo atraviesa el conducto de lado.
- **Si te pierdes**, mira la sala: los tubos de los instrumentos se ven entrar por los trocares. La
  tecla T (o clic del stick izquierdo) cambia la transparencia de la pared abdominal.
- **Para repetir**, cambia de modo con Menú y vuelve a entrar: todo se restaura.

## 6. Qué hace el simulador por ti

- **Manual:** mover los brazos, agarrar y colocar trocares, montar y cambiar instrumentos, traccionar,
  cauterizar, colocar cada clip, cortar, disecar el lecho y sacar la vesícula.
- **Asistido:** al cortar, mientras mantienes el grip en la esfera azul, el rayo barre solo la sección
  del conducto. La introducción del instrumento por el trocar y el encaje final del trocar en el puerto
  también los completa el simulador.
- **Automático si lo pides con B / Y:** colocar un puerto, montar el instrumento que toca, retirar los
  trocares.
- **Siempre automático:** introducir el laparoscopio, embolsar y depositar la vesícula.
- **Simplificado:** la disección del lecho son puntos que se cauterizan y no hay arteria cística,
  porque el modelo no la tiene.

## 7. Lo que falta validar con los lentes

- **Dirección del movimiento.** Si algún eje se siente invertido, es un ajuste pequeño en
  `OperacionManualVR.cs`.
- **Sensibilidad.** `escalaLibre`, `velocidadMaxLibre`, `escalaMovimiento` y `velocidadMaxPunta`, en el
  componente `DemoAutomaticaColecistectomia` del objeto `GestorModos`.
- **Agarrar con tu mano.** Probé lo que ocurre después del agarre (clon, vuelo al brazo, encaje), pero
  no el gesto real con el mando. La mesa de instrumental está a unos 2 m del paciente.
- **El aplicador de clips** se crea al vuelo y le añadí lo necesario para cogerlo con la mano; es lo
  menos probado de todo.
- **Los brazos no llegan a la mesa de instrumental.** Por eso los instrumentos se cogen con la mano y
  viajan al brazo; la pinza solo coge directamente lo que tenga a 9 cm.
- **Monitor de guía:** altura, distancia y tamaño con `alturaMonitor`, `distanciaMonitor` y
  `escalaMonitor`. **Radio de encaje** de los trocares con `radioEncajePuerto` (13 cm).
- **Rendimiento** con el visor.

## 8. Cambios que afectan a los otros modos

- **El agarre con la mano pasó del gatillo al grip** en los dos mandos, para toda la escena. En el modo
  "Simular operación" el grip es también la acción SOFA del instrumento, así que ahí agarrar y actuar
  usan el mismo botón.
- **Las piernas abiertas** están guardadas en la escena (se ven también en el editor, sin Play) y valen
  para todos los modos.
