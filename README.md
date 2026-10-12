# MU Season 6 – servidor propio

Servidor de MU Online Season 6 basado en [OpenMU](https://github.com/MUnique/OpenMU), con el cliente
open-source [MuMain](https://github.com/sven-n/MuMain). Corre en Docker.

- **Hoja de ruta:** [docs/ROADMAP.md](docs/ROADMAP.md)
- **Operación diaria** (comandos, puertos, backups, actualizaciones, problemas conocidos): [docs/OPERACIONES.md](docs/OPERACIONES.md)

## Estructura

| Carpeta | Contenido |
|---|---|
| `server/OpenMU` | Código del servidor OpenMU (git submodule) |
| `server/` | Nuestro override de Docker, scripts y `sql/` con la configuración versionada |
| `client-src/MuMain` | Código del cliente MuMain (git submodule) |
| `client-src/` | Scripts para compilar y abrir el cliente |
| `docs/` | Hoja de ruta, operación y decisiones |
| `client/` | Otros clientes y launcher. **No se versiona** |
| `tools/` | Herramientas de terceros (vcpkg, etc.). No se versiona |
| `backups/` | Backups de la DB. No se versiona |

Cada script `.ps1` tiene al lado un `.cmd` que lo ejecuta sin cambiar la política de ejecución de
PowerShell. Se puede abrir con doble clic.

## Inicio rápido

Requisitos del servidor: Docker Desktop y Git. Requisitos del cliente: VS 2022 Build Tools (C++ + CMake),
el .NET 10 SDK y Python 3. vcpkg va en `tools/vcpkg`; ver [client-src/build-client.ps1](client-src/build-client.ps1).

```bash
git clone --recurse-submodules https://github.com/joniamse/mu.git
```

```bash
.\server\up.cmd build
```

```bash
.\server\db.cmd server\sql\rates.sql
```

```bash
.\client-src\build-client.cmd
```

```bash
.\client-src\play.cmd
```

- Panel de administración: http://localhost/
- El cliente se conecta a `127.127.127.127:44406`
- Rates: experiencia x5, zen x2 ([server/sql/rates.sql](server/sql/rates.sql))
- Cuentas de prueba: `test0`…`test9`, `test300`, `test400`, `testgm`, etc. La contraseña es igual al usuario.
  **Hay que borrarlas antes de abrir el servidor al público.**
