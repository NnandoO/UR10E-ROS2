# Plan: recrear la colecistectomía a mano con Meta Quest 3

Objetivo: que una persona con los lentes pueda hacer, paso a paso y con sus manos, la misma operación
que hoy hace sola la demo automática (`DemoAutomaticaColecistectomia`), con guía y evaluación.

## 0. Requisito previo, ya hecho: órganos blandos

Antes, cada nodo de cada órgano estaba atado a su posición de reposo con un resorte rígido, así que al
tirar solo se movía el vértice agarrado (un pico) y el resto del órgano no se deformaba.

Ahora (`Assets/SofaUnity/Scenes/Organos/Escena_final.scn`):

- Solo quedan fijos los nodos de anclaje anatómico (`FixedConstraint`): en la vesícula, el lecho hepático
  del cuerpo y el extremo del conducto cístico (32 de 435 nodos; el fondo queda libre); en el hígado, la
  cara posterior y la cúpula; en duodeno y estómago, la cara posterior.
- El resorte de reposo por nodo baja de 400–4500 a 0,2–1, y el módulo de Young a 8 000 (vesícula) y
  12 000 (resto).

| Medida (tirón con el grasper) | Antes | Ahora |
|---|---|---|
| Vesícula, nodos que se mueven más de 1 mm en la tracción de la demo | 6 (1 %) | 88 (20 %) |
| Hígado, nodos que se mueven más de 1 mm con un tirón de 30 mm | 3 | 172 (11 %) |
| Vuelven a su forma al soltar | sí | sí |
| Demo completa (corte del conducto en 2 piezas, consola) | bien | bien |

Límites que conviene conocer: los órganos no chocan entre sí (están en el mismo grupo de colisión), y el
agarre de SofaUnity sujeta un punto, no un parche de tejido como una pinza real.

## 1. Qué hay hoy y qué falta, fase por fase

| Fase de la demo | Hoy en modo manual | Qué falta |
|---|---|---|
| 0 · Puertos | Los trocares se pueden agarrar y encajan en sus sockets; el obturador se retira solo; el trocar bascula con el instrumento | Modo que empiece con los trocares en la mesa; marca de incisión; guía de dónde va cada puerto |
| 0 · Óptica | El laparoscopio se agarra y su cámara se ve en el visor | Que se quede sujeto al soltarlo (asistente) y una pantalla cómoda en VR |
| 1 · Instrumentos | Al agarrar un instrumento salta a la punta del robot; fulcro, topes y salida guiada funcionan | La **entrada** al trocar no está guiada; en VR se ve el instrumento laparoscópico, no el OpenRST; la muñeca del OpenRST cuelga suelta |
| 2 · Tracción | Agarre SOFA real con el grasper | Mantener la tracción sin tener el botón apretado (trinquete) |
| 3 · Disección | El hook existe, pero como herramienta de corte SOFA | No hay grasa ni peritoneo que despejar: no hay forma de "lograr" la visión crítica |
| 4 · Clipado | No existe. La demo fabrica su clipadora y sus clips por código | Clipadora real como instrumento, con clips que se fijan al conducto |
| 5 · Sección | La tijera corta de verdad | El rayo de corte mide 5–8 cm y puede dañar lo que hay detrás; no se comprueba que haya clips antes de cortar |
| 6 · Lecho hepático | No existe. La demo lo simula con efectos | Liberar de verdad la vesícula de su lecho |
| 7 · Extracción | No existe. La demo sustituye la malla por una copia | Bolsa de extracción manejable y salida por el trocar |
| 9 · Cierre | Los trocares se pueden sacar a mano | Detectarlo y cerrar las marcas |
| Guía | `DemoColecistectomia` tiene 4 pasos que se avanzan a mano | 10 pasos con avance automático y evaluación |

Hay scripts antiguos sin usar en la escena que sirven de punto de partida: `ClipadoraLaparoscopica.cs`,
`AtraparEndobag.cs` y `ElectrocauterioLaparoscopico.cs`.

## 2. Decisiones que condicionan el plan

1. **Se ejecuta en el PC, no dentro del visor.** El plugin nativo de SOFA solo existe para Windows (no hay
   librerías Android en el proyecto). El Quest 3 se usa por Link o Air Link con OpenXR, que ya está
   configurado (`com.unity.xr.meta-openxr`).
2. **Teleoperación sin WSL (recomendado).** Hoy los brazos siguen a los mandos mediante los servidores
   Python de WSL. La demo ya tiene una IK propia en Unity (`CinematicaBrazoUR`) que converge bien. Propongo
   usarla también en modo manual cuando no haya servidor, y dejar WSL solo para el UR10e físico.
3. **Qué instrumento se ve.** Para que el modo manual se parezca a la demo, el robot debe operar con su
   OpenRST visible, igual que en la demo. La alternativa es seguir viendo el instrumento laparoscópico.
4. **Quién sostiene la óptica.** Con dos manos ocupadas en los instrumentos, la óptica debe quedarse
   quieta al soltarla o seguir sola a los instrumentos.

Las decisiones 2, 3 y 4 son tuyas; el plan asume las opciones recomendadas.

## 3. Etapas de implementación

Cada etapa deja algo que se puede probar por separado. El tamaño es relativo (S, M, L).

### Etapa A · Base de teleoperación (L)
- `SeguimientoBrazoBase`: teleoperación local con `CinematicaBrazoUR` cuando no hay servidor Python.
  Conserva clutch, suavizado y límites de velocidad.
- Extraer de la demo a un componente común el manejo del OpenRST (`InstrumentalOpenRST`): mostrarlo,
  sujetar la muñeca, elegir mordaza y abrir o cerrar la pinza con el gatillo analógico.
- **Hecho cuando:** con los lentes, cada brazo sigue su mando sin WSL y la pinza abre y cierra con el gatillo.

### Etapa B · Entrada guiada al trocar (M)
- `ControladorFulcroLaparoscopio`: capturar el instrumento al entrar por el cabezal del trocar, no solo al
  llegar al fulcro, y alinearlo con el eje del tubo mientras baja.
- **Hecho cuando:** la prueba de mano simulada entra torcida 20° y el instrumento queda centrado (menos de 2 mm).

### Etapa C · Puertos y óptica a mano (M)
- Modo manual que empieza con los trocares en la mesa (`PreparacionQuirurgica.colocarAlIniciar = false`).
- Marcadores de dónde va cada puerto y marca de incisión al encajar (reutiliza `CrearMarcaIncision`).
- Óptica: se queda fija al soltarla; pantalla del laparoscopio anclada frente al usuario.
- **Hecho cuando:** se colocan los 3 puertos y la óptica a mano y el paso de la guía avanza solo.

### Etapa D · Clipadora y corte seguro (L)
- Sacar de la demo a una clase compartida la medición del conducto (`CalcularDucto`, `PuntoDucto`,
  `CrearClip`, `VesiculaSeccionada`).
- Clipadora real: instrumento con fulcro; al disparar cerca del conducto, el clip se fija en ese punto.
- Tijera: rayo de corte corto (1,5 cm, fijado antes de crear el rayo) y activo solo cerca del tejido.
- Regla: si se corta sin 2 clips del lado del paciente y 1 del lado de la vesícula, se registra como error.
- **Hecho cuando:** a mano se ponen 3 clips, se corta entre ellos y la malla queda en 2 piezas.

### Etapa E · Disección y visión crítica (L)
- Añadir una capa de grasa y peritoneo sobre el triángulo de Calot que el hook va eliminando al
  cauterizar (malla o parches; no es anatomía nueva del modelo, es lo que tapa el conducto).
- El hook deja de ser herramienta de corte SOFA: cauteriza la capa y no talla el conducto.
- La visión crítica se da por lograda cuando la capa está despejada en la zona del conducto.
- **Hecho cuando:** no se puede clipar "a ciegas" y la guía solo avanza con el conducto expuesto.

### Etapa F · Lecho hepático y extracción (L, con una prueba previa)
- **Prueba previa (S):** comprobar si SofaUnity permite cambiar en vivo los índices del anclaje
  (`Anclas_ves`). Si se puede, el hook libera los nodos del lecho al cauterizar junto a ellos.
- Si no se puede: cuando todo el lecho está cauterizado, se sustituye la malla por la copia libre, como
  hace hoy la demo (`SepararVesicula`), pero sujeta al grasper.
- Bolsa de extracción como instrumento (partiendo de `AtraparEndobag.cs`): la vesícula entra, se cierra y
  sale por el trocar; se deposita en la bandeja.
- **Hecho cuando:** la vesícula sale en la bolsa y el muñón clipado queda en el paciente.

### Etapa G · Guía, evaluación y cierre (M)
- `DemoColecistectomia`: pasar de 4 pasos manuales a los 10 de la demo, con condiciones automáticas
  nuevas: trocar colocado, instrumento anclado, tejido agarrado, zona despejada, clips puestos, conducto
  seccionado, vesícula en bolsa, trocares retirados.
- Evaluación: tiempo por paso y errores (corte antes de la visión crítica, corte sin clips, daño a hígado,
  duodeno o estómago medido por tetraedros perdidos, golpes contra el tope del trocar).
- Informe final en un panel dentro de la escena.

### Etapa H · Ajuste para Quest 3 (M)
- Rendimiento: medir el coste de SOFA por paso. El estómago tiene 3 165 nodos y casi no se manipula;
  si hace falta, simplificar su malla o dejarlo estático.
- Comodidad: altura y escala del usuario, paneles legibles, vibración de los mandos al agarrar, cortar y
  tocar un tope (bHaptics ya está integrado como opción).
- Controles propuestos:

| Acción | Mando |
|---|---|
| Agarrar o soltar instrumento | Gatillo (índice) |
| Acción del instrumento (pinza, corte, clip, cauterio) | Grip (dedo medio), analógico |
| Trinquete del grasper | A (derecho) / X (izquierdo) al agarrar |
| Clutch | Mantener B / Y |
| Siguiente paso de la guía | Menú |
| Vista de la pared abdominal | Clic del stick izquierdo |

## 4. Orden y dependencias

1. **A** y **B** primero: sin teleoperación fiable y entrada centrada, nada de lo demás se puede evaluar.
2. **C** y **D** después; son independientes entre sí.
3. **E** antes que **F** (la disección decide cuándo se puede liberar el lecho).
4. **G** va creciendo con cada etapa (cada una añade su condición de avance) y se cierra al final.
5. **H** en paralelo desde que **A** funcione con los lentes.

## 5. Cómo se verifica

- **Sin lentes**, en cada etapa: la mano simulada que ya se usa para el fulcro (un objeto que hace de
  mando), las mediciones automáticas de la demo (componentes de la malla, deformación de órganos,
  alineación con el trocar) y la demo automática completa como prueba de regresión.
- **Con lentes**, al cerrar cada etapa: una lista corta de comprobación hecha por una persona. Es la
  única forma de validar comodidad, alcance de los brazos y sensación de los órganos.

## 6. Riesgos

- **Sensación del tejido:** el agarre sujeta un solo punto; si en VR se ve "pico", habrá que agarrar
  varios nodos a la vez desde Unity.
- **Órganos que se atraviesan:** al quedar libres pueden cruzarse entre sí. Activar su colisión mutua
  tiene coste de rendimiento y hay que probarlo.
- **Liberación del lecho:** depende de la prueba previa de la etapa F.
- **Alcance del brazo derecho:** hoy llega justo, tras acercarlo 15 cm. Con un usuario moviéndose
  libremente puede volver a quedarse corto; habrá que limitar el espacio de trabajo o avisar.
- **Nada de lo que hay hoy se ha probado con los lentes puestos:** topes, pivote del trocar y órganos
  blandos se validaron solo con la mano simulada y la demo.
