# Operación del servidor

Guía práctica para el día a día. Todos los comandos se ejecutan desde `D:\Mu`.

## Comandos

| Qué | Comando |
|---|---|
| Levantar el servidor | `.\server\up.cmd` |
| Recompilar desde `server/OpenMU/src` y levantar | `.\server\up.cmd build` |
| Apagar | `.\server\up.cmd down` |
| Ver logs en vivo | `.\server\up.cmd logs` |
| Reiniciar solo el servidor del juego | `docker restart openmu-startup` |
| Consola SQL | `.\server\db.cmd` |
| Ejecutar un .sql | `.\server\db.cmd server\sql\archivo.sql` |
| Backup de la DB | `.\server\backup.cmd [etiqueta]` |
| Restaurar un backup | `.\server\backup.cmd restore backups\archivo.dump` |
| Compilar el cliente | `.\client-src\build-client.cmd` |
| Jugar | `.\client-src\play.cmd` |

## Arquitectura local

```
Cliente MuMain ──► 127.127.127.127:44406 (connect server)
                └► 127.127.127.127:45901-45906 (game servers)
Navegador ──► localhost:80 (nginx) ──► openmu-startup:8080 (admin panel)
openmu-startup ──► database:5432 (PostgreSQL, volumen all-in-one_dbdata)
```

- Tres contenedores: `openmu-startup` (todo OpenMU en un proceso), `database` (PostgreSQL) y `nginx-80`.
- Compose = `server/OpenMU/deploy/all-in-one/docker-compose.yml` + nuestro `server/docker-compose.local.yml`.
- La imagen `mu/openmu:dev` se compila desde nuestro código; no se usa la de Docker Hub.

| Puerto | Uso |
|---|---|
| 80 | Panel de administración |
| 44405 | Connect server para el cliente original de Webzen |
| 44406 | Connect server para MuMain |
| 45901-45906 | Game servers (3 servidores, 2 endpoints cada uno) |
| 45980 | Chat server |

## Dónde vive la configuración

Casi toda la configuración del juego (rates, items, drops, mapas, eventos) vive en la **base de datos**,
no en el código. Se edita desde el panel (http://localhost/) o por SQL.

Para que sea reproducible, todo cambio de configuración que hagamos a mano queda en un script dentro de
`server/sql/`:

| Script | Contenido |
|---|---|
| [rates.sql](../server/sql/rates.sql) | Experiencia x5, zen x2 |

## Actualizar OpenMU

1. Backup: `.\server\backup.cmd pre-update`
2. Traer el código nuevo: `git -C server/OpenMU pull` (o actualizar el submódulo al commit deseado)
3. Recompilar: `.\server\up.cmd build`
4. Abrir el panel → **Updates** y aplicar las configuration updates nuevas. Tardan unos minutos y van de a una.
5. Reiniciar: `docker restart openmu-startup`
6. Verificar que las rates sigan bien: `.\server\db.cmd server\sql\rates.sql` (es idempotente)
7. Commit del nuevo puntero del submódulo.

## Historial

- **2026-10-12**: se aplicaron 29 configuration updates (Castle Siege, Kanturu, Imperial Guardian,
  Doppelganger, Raklion, cash shop, árboles master de SUM/RF, migración de puertos 5590x → 4590x, etc.).
  Hay backups antes y después en `backups/`. Durante el proceso, el plugin del evento Kanturu registró un
  error de Entity Framework ("AttributeDefinition cannot be tracked"). No frenó las actualizaciones y,
  después de reiniciar, el servidor arranca bien. Si aparecen problemas con Kanturu, empezar a buscar por ahí.

## Problemas conocidos

| Síntoma | Causa / solución |
|---|---|
| Panel con 502 Bad Gateway | nginx apunta a la IP vieja del contenedor después de recrearlo. `docker restart nginx-80`. `up.cmd build` ya lo hace solo. |
| "la ejecución de scripts está deshabilitada" | Usar los `.cmd` en lugar de los `.ps1`. |
| El cliente no conecta a `127.0.0.1` | El cliente bloquea esa IP. Usar `127.127.127.127`. |
| `libgssapi_krb5.so.2` en los logs | Aviso inofensivo del driver de PostgreSQL. |
