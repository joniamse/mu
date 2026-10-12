# Hoja de ruta

## Etapa 0 – Preparación
- [x] Git, VS Code, .NET 10 SDK
- [x] WSL2 + Docker Desktop
- [x] Estructura del repo + OpenMU como submódulo
- [ ] Primer push a GitHub

## Etapa 1 – Servidor local funcionando
- [x] Servidor levantado con `server/up.ps1` (IP loopback y puertos 5590x)
- [ ] Crear el usuario admin en el panel (http://localhost/)
- [x] Cliente MuMain compilado localmente (`client-src/build-client.ps1`)
- [x] Entrar al juego con una cuenta de prueba (`testgm`)

## Etapa 2 – Fork y entorno de desarrollo
- [ ] Fork de OpenMU en GitHub; apuntar el submódulo al fork
- [ ] Compilar y depurar desde el código fuente (VS Code + C# Dev Kit)
- [ ] Leer la arquitectura y el sistema de plugins
- [ ] Primer cambio propio (por ejemplo, rates de experiencia) visible en el juego

## Etapa 3 – Mecánicas propias
- [ ] Definir el estilo del servidor (fiel, low-rate o high-rate, custom)
- [ ] Rates, drops, eventos, items y quests
- [ ] Preferir plugins y configuración antes que tocar el núcleo, para seguir recibiendo updates de OpenMU

## Etapa 4 – Cliente: gráficos y modelos
- [ ] Evaluar [MuMain](https://github.com/sven-n/MuMain), el cliente open-source compatible con OpenMU
- [ ] Reemplazo de texturas y modelos
- [ ] Objetivo a largo plazo: assets propios (ver la sección legal)

## Etapa 5 – Online
- [ ] VPS Linux + Docker, dominio, HTTPS (`deploy/all-in-one-traefik`)
- [ ] Backups automáticos de PostgreSQL, monitoreo, firewall
- [ ] Web pública: registro, rankings y descargas

## Notas legales
El cliente, los modelos y las texturas de MU son de Webzen. No se suben a este repo.
Antes de abrir el servidor al público, sobre todo si va a monetizarse, hay que revisar este tema.
