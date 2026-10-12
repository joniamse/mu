# Hoja de ruta

## Etapa 0 – Preparación ✅
- [x] Git, VS Code, .NET 10 SDK
- [x] WSL2 + Docker Desktop
- [x] Estructura del repo, con OpenMU y MuMain como submódulos
- [x] Primer push a GitHub

## Etapa 1 – Servidor local funcionando ✅
- [x] Servidor en Docker con IP loopback
- [x] Cliente MuMain compilado localmente
- [x] Entrar al juego con una cuenta de prueba (`testgm`)
- [~] Usuario admin del panel: se omite en local (red doméstica). **Obligatorio antes de la Etapa 5.**

## Etapa 2 – Entorno de desarrollo y primeros cambios
- [x] Servidor compilado desde nuestro código (`server\up.cmd build`, imagen `mu/openmu:dev`)
- [x] Experiencia x5 y zen x2 ([server/sql/rates.sql](../server/sql/rates.sql))
- [x] 29 configuration updates aplicadas, puertos migrados a 4590x
- [x] Backups de la DB ([OPERACIONES.md](OPERACIONES.md))
- [ ] **Prueba en el juego:** experiencia y zen con `test0`, los eventos nuevos, que no haya desconexiones
- [ ] Fork de OpenMU y MuMain en GitHub; apuntar los submódulos a los forks (antes del primer cambio de código)
- [ ] Depurar el servidor desde VS Code (C# Dev Kit)
- [ ] Leer la arquitectura y el sistema de plugins

## Etapa 3 – Calidad gráfica (cliente MuMain)
Prioridad del usuario: mejorar los gráficos con shaders y texturas HD de mapas, armas, armaduras, etc.
- [ ] Analizar el render de MuMain (SDL GPU, pipeline, shaders actuales, formatos de modelo `.bmd` y de textura `.ozj`/`.ozt`)
- [ ] Ajustes rápidos: resolución, anisotrópico, MSAA y mipmaps
- [ ] Post-procesado: bloom, color grading, SSAO, FXAA/SMAA
- [ ] Iluminación: normal maps y specular, sombras
- [ ] Pipeline de texturas HD: herramientas de conversión e upscaling (ESRGAN u otros), priorizando mapas → armas → armaduras
- [ ] Modelos mejorados: herramientas de import/export de `.bmd`

## Etapa 4 – Mecánicas propias
- [ ] Definir el estilo del servidor (fiel, low-rate o high-rate, custom)
- [ ] Drops, eventos, items y quests
- [ ] Preferir plugins y configuración antes que tocar el núcleo, para seguir recibiendo updates de OpenMU

## Etapa 5 – Online
- [ ] Usuario admin del panel y borrado de las cuentas de prueba
- [ ] VPS Linux + Docker, dominio, HTTPS (`deploy/all-in-one-traefik`)
- [ ] Backups automáticos de PostgreSQL, monitoreo, firewall
- [ ] Web pública: registro, rankings y descarga del cliente
- [ ] Revisión legal (ver abajo)

## Notas legales
El cliente original, los modelos y las texturas de MU son de Webzen, y MuMain deriva de código filtrado
de Webzen. Nada de eso se sube a este repo; los submódulos son solo referencias. Antes de abrir el
servidor al público, sobre todo si va a monetizarse, hay que revisar este tema. A largo plazo, los assets
propios (por ejemplo, las texturas HD que hagamos) ayudan a reducir el riesgo.
