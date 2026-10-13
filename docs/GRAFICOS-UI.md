# Estrategia gráfica: UI nueva y texturas HD

## Decisión (2026-10-12)

La interfaz se **rehace desde cero** con [RmlUi](https://github.com/mikke89/RmlUi) (HTML/CSS) en lugar de
escalar la UI vieja de 640×480. La migración es **ventana por ventana**: la UI vieja sigue funcionando
hasta que cada parte tenga su reemplazo.

| | UI vieja escalada | UI nueva (RmlUi) |
|---|---|---|
| Resolución | 640×480 estirado | Independiente (unidades `dp`) |
| Assets | De Webzen | Propios |
| Diseño | Hay que recompilar | `.rml` / `.rcss` editables, solo reiniciar el juego |

Las texturas HD con IA quedan solo para **modelos 3D** (armas, armaduras, mapas).

## Estado

- [x] Ventana de estado moderna (estilo L2): nivel, nombre, SD/HP/MP/AG, pulso con HP bajo, modo compacto
- [x] Estilo base compartido (`theme.rcss`), tooltips, input (mouse; teclado en campos de texto), imágenes PNG
- [x] Barra de skills: skill actual + 10 teclas, selector (Ctrl+1..0 asigna), cooldown, tooltip moderno
- [x] Dock de menú (personaje, inventario, amigos, menú, opciones modernas, tienda) y barra de experiencia
- [x] Las piezas viejas reemplazadas se ocultan; la UI clásica sigue intacta con la opción apagada
- [x] **Ventana "Modern settings"** con un interruptor por cada modernización (ver abajo)
- [ ] Pociones Q/W/E/R (la HUD vieja las dibuja como modelos 3D)
- [ ] Minimapa → inventario → personaje → tienda NPC → chat → resto

## Opciones modernas (todas desactivables)

Se abren desde **Opciones → "Modern settings..."** (última fila) o desde el engranaje del dock. La ventana
funciona también con la interfaz clásica. Cada cambio se guarda en `config.ini` al instante.

| Opción | Clave | Por defecto | Efecto |
|---|---|---|---|
| HD interface | `[UI] ModernHud` | Sí | HUD moderna; se aplica en el momento |
| WASD movement | `[Modern] WasdMovement` | No | Caminar con W/A/S/D (relativo a la cámara). Poción W, árbol master (A), comandos (D) y tienda personal (S) pasan a **Shift + tecla**. No mueve mientras se escribe ni con Ctrl/Shift |
| Enhanced textures | `[Modern] EnhancedTextures` | Sí | Mipmaps + filtrado trilineal + anisotrópico x8 en texturas del mundo; **requiere reiniciar** el juego |

### Notas técnicas de las mejoras

- **WASD** (`Input/KeyboardMovement.cpp`): convierte la dirección de pantalla en un ángulo con la misma
  fórmula que usa el mouse (`g_Camera.Angle[2]`), planifica 2 casillas adelante con `PathFinding2` y manda
  el mismo paquete de caminata que un clic. Replanifica por casilla y frena al soltar.
- **Texturas** (`Render/Textures/MipChain.cpp`, `Render/Sprites/GlobalBitmap.cpp`): la cadena de mipmaps se
  arma en la CPU (color ponderado por alfa, alfa = máximo para que el follaje recortado no desaparezca a
  distancia). Las texturas de `Interface\` no se tocan. Los samplers se comparten por estado (antes: uno
  por textura, cf. issue #620 de MuMain).
- **Trampa encontrada:** en Direct3D 12, activar anisotropía en samplers con ampliación *nearest* hacía
  fallar la creación de texturas posteriores (`CursorID.tga`). Por eso la anisotropía solo se activa en
  texturas con filtro lineal. En Vulkan no fallaba.

## Sombras (pendiente, analizado)

Las sombras de personajes y objetos son **proyecciones planas** (`BMD::RenderBodyShadow`): cada malla se
aplasta contra el terreno y se dibuja en negro con opacidad 1. Las mallas superpuestas acumulan oscuridad,
de ahí las manchas negras duras.

Para sombras suaves y uniformes hace falta que cada píxel se oscurezca **una sola vez** → **stencil buffer**.
El renderer SDL GPU hoy no tiene stencil (profundidad `D32_FLOAT`, las llamadas de stencil son no-op).
Plan: depth `D24_UNORM_S8_UINT`/`D32_FLOAT_S8_UINT`, variantes de pipeline con stencil, sombra con
opacidad ~0.45 y stencil "dibujar si es 0, luego incrementar". Opción `[Modern] SoftShadows`.
Más adelante: shadow mapping real.

## Cómo está hecho (MuMain, rama `feature/rmlui-hud`)

| Pieza | Archivo |
|---|---|
| Dependencia opcional (`MU_RMLUI`) | `vcpkg.json`, `src/CMakeLists.txt` |
| Puente con el renderer SDL GPU | `src/source/UI/Modern/RmlRenderInterface.*` |
| Reloj y log | `src/source/UI/Modern/RmlSystemInterface.*` |
| Datos del personaje → RML | `src/source/UI/Modern/HudDataModel.*` |
| Inicio, render y cierre | `src/source/UI/Modern/ModernHud.*` (se llama desde `MainScene.cpp` y `Winmain.cpp`) |
| Documentos | `src/bin/Data/UI/hud.rml`, `hud.rcss` (agregar con `git add -f`: `**/bin/**` está en `.gitignore`) |

Para editar el diseño: modificar `out/build/windows-x64/src/Release/Data/UI/hud.rcss` para probar en
caliente (con reiniciar el juego alcanza) y, cuando quede bien, copiar el cambio a `src/bin/Data/UI/`.

## Trampas conocidas

- **Configuración regional:** RmlUi convierte números a texto según el idioma de Windows. En español
  `42.5` sale como `42,5` y el CSS queda inválido. Los valores que terminan en CSS se formatean en C++
  con `std::to_chars` (ver `HudDataModel.cpp`). **Nunca pasar floats crudos a `data-style-*`.**
- **Macros viejas:** `ZzzMathLib.h` define `Vector4(...)` y `DotProduct(...)`. Incluir RmlUi **solo**
  vía `UI/Modern/RmlIncludes.h`.
- **Funciones de RmlUi no implementadas todavía:** shaders (`linear-gradient`, `box-shadow`), filtros,
  capas y transformaciones CSS. Usar `decorator: vertical-gradient(...)` / `horizontal-gradient(...)`.
- **Log:** los mensajes de RmlUi van a `MuError.log` (junto a `Main.exe`) con el canal `[rmlui]`.

## Texturas HD para modelos 3D (más adelante)

- Los modelos usan UV normalizado: una textura HD funciona sin cambios de código.
- Pipeline previsto: `.ozj`/`.ozt` → PNG → Real-ESRGAN (RTX 4050) → pack HD en una carpeta aparte.
- El pack HD **no** va al repo público (derivado de assets de Webzen).

## Hallazgos técnicos de la UI vieja (referencia)

- Coordenadas lógicas de 640×480 escaladas por `UI/Scaling/UITransform.h`.
- Texturas de UI con `GL_NEAREST` (`Render/Textures/ZzzTexture.h:17`): por eso se ven pixeladas.
- UV calculado en píxeles en `UI/NewUI/NewUICommon.cpp`: reemplazar una textura por otra de mayor
  resolución rompe el recorte.
