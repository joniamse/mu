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

- [x] **Prueba de concepto:** panel de vitales (nombre, nivel, HP/MP/SD/AG) funcionando en el juego, nítido y escalable
- [ ] Ocultar las esferas de HP/MP de la HUD vieja cuando la nueva está activa
- [ ] Input: mouse y teclado hacia RmlUi (botones, ventanas que se pueden arrastrar)
- [ ] Carga de imágenes PNG (íconos, marcos) en `LoadTexture`
- [ ] Opción `[UI] ModernHud=1` en `config.ini`
- [ ] Migrar: barra de skills → minimapa → inventario → personaje → tienda NPC → chat → resto
- [ ] Definir el lenguaje visual (paleta, tipografía, marcos) en un único `.rcss` base

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
