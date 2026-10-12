# MU Season 6 – servidor propio

Servidor de MU Online Season 6 basado en [OpenMU](https://github.com/MUnique/OpenMU).
Corre en Docker.

## Estructura

| Carpeta | Contenido |
|---|---|
| `server/OpenMU` | Código de OpenMU (git submodule) |
| `client-src/MuMain` | Cliente open-source MuMain (git submodule) |
| `client/` | Otros clientes y launcher. **No se versiona** |
| `docs/` | Hoja de ruta, notas y decisiones |
| `tools/` | Herramientas de terceros (vcpkg, etc.). No se versiona |

Los scripts `.ps1` tienen un `.cmd` al lado que los ejecuta sin cambiar la política de ejecución de
PowerShell. Se pueden abrir con doble clic.

## Levantar el servidor local

Requisitos: Docker Desktop y Git.

```bash
git clone --recurse-submodules https://github.com/joniamse/mu.git
cd mu
.\server\up.cmd
```

`up.cmd` admite las acciones `up` (por defecto), `down`, `logs` y `pull`. Usa el compose oficial de OpenMU más
[server/docker-compose.local.yml](server/docker-compose.local.yml), que contiene nuestros ajustes:
IP loopback y los puertos viejos que usa la imagen de Docker Hub.

- Panel de administración: http://localhost/
- Connect server (cliente original): `127.127.127.127:44405`
- Connect server (cliente open-source MuMain): puerto `44406`
- Game servers: `55901-55906` (la DB actual); chat: `55980`
- Cuentas de prueba: `test0`…`test9`, `test300`, `test400`, `testgm`, etc. La contraseña es igual al usuario.
  **Hay que borrarlas antes de abrir el servidor al público.**

## Cliente (MuMain)

`client-src/MuMain` es el cliente open-source ([sven-n/MuMain](https://github.com/sven-n/MuMain)).
Para compilarlo hacen falta las VS 2022 Build Tools (C++ + CMake), el .NET 10 SDK, Python 3 y vcpkg en `tools/vcpkg`.

```bash
.\client-src\build-client.cmd
```

Para jugar (se conecta a `127.127.127.127:44406`):

```bash
.\client-src\play.cmd
```

Ver [docs/ROADMAP.md](docs/ROADMAP.md).
