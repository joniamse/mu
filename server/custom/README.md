# server/custom: nuestras funciones sobre OpenMU

Todo lo que agregamos al servidor vive acá, separado del OpenMU oficial (`server/OpenMU`, submódulo).

| Carpeta / archivo | Qué es |
|---|---|
| `MuCustom.PlugIns/` | Plugins propios (C#, .NET 10): VIP y fichas, misiones diarias, anuncios globales, tienda offline, baúles múltiples. |
| `MuCustom.TestClient/` | Cliente de juego sin gráficos para probar los comandos contra el servidor que está corriendo. |
| `patches/openmu-core.patch` | Los pocos cambios al núcleo de OpenMU que necesitan los plugins (ver abajo). |
| `patch.ps1` | Aplica, quita o regenera el parche en el submódulo. |
| `Dockerfile` | Imagen `mu/openmu:dev`: OpenMU + parche + plugins. La usa `server/up.cmd build`. |

## Cómo se carga

OpenMU descubre los plugins en los ensamblados cargados. El parche agrega `MuCustom.PlugIns` como
referencia del proyecto `Startup` y llama a `MuCustom.PlugIns.MuCustomPlugIns.EnsureLoaded()` al
inicio. Los plugins aparecen en el panel de administración (Plugins) y se pueden desactivar uno por uno.
La configuración (recarga simulada, anuncios, jefes de mundo...) es la del plugin **"MuCustom: nucleo"**.

## Base de datos

Tablas propias en el schema `public` de la base `openmu`, con migraciones versionadas
(`MuCustom.PlugIns/Data/Migrations/NNNN_nombre.sql`, registro en `mucustom_schema_migrations`).
Se aplican solas al arrancar el servidor; si la base aún no existe, se reintentan cada 30 s.
Conexión: la misma que OpenMU (`DB_HOST`, `DB_ADMIN_USER`, `DB_ADMIN_PW`), o
`MUCUSTOM_CONNECTION_STRING` si se define.

| Tabla | Contenido |
|---|---|
| `account_cash_currency` | Saldo de fichas por cuenta. |
| `fichas_ledger` | Cada movimiento de fichas (recarga, compra, recompensa). |
| `shop_recharge_packages` | Los 6 paquetes de recarga (se cargan con la migración). |
| `account_vip_status` | VIP Diamante (de cuenta). |
| `character_vip_status` | VIP Plata, Oro y Platino (de personaje). |
| `account_vaults`, `account_vault_state` | Qué item storage de OpenMU es cada baúl (`/baul`). |
| `vip_event_entry_claims` | Entradas diarias VIP a Blood Castle / Devil Square ya entregadas. |
| `openmu_quests` | Misiones diarias (editables: activar, cambiar objetivos o recompensas). |
| `openmu_quest_progress` | Progreso por personaje y día (UTC); `claimed_at` impide cobrar dos veces. |

## Cambios al núcleo (parche)

| Cambio | Para qué |
|---|---|
| `IGameplayRateModifierPlugIn` + llamadas en el drop, la Chaos Machine y el MU Helper | Bonus VIP de drop, éxito de mezcla y descuento del helper. |
| `IItemCraftingCompletedPlugIn` en `ItemCraftAction` | Anuncios de +10 a +15 (éxito y destrucción). |
| `ICharacterSelectingPlugIn` en `SelectCharacterAction` | Proteger el personaje de una tienda offline abierta. |
| `OfflinePlayerManager.StartAsync` con fábrica y `OfflinePlayer.StaysOnLogin` | Tienda offline sobre las sesiones offline de OpenMU; con Diamante sigue abierta al entrar con otro personaje. |
| `ItemStorageRepository` | Cargar un baúl (item storage) con sus items, para cambiar de baúl en el juego. |
| `Startup`: referencia y `EnsureLoaded()` | Cargar los plugins. |

Para tocar el núcleo: `.\server\custom\patch.ps1 apply`, editar en `server/OpenMU/src`, compilar y
`.\server\custom\patch.ps1 update`. Para dejar el submódulo como el oficial: `patch.ps1 revert`.
El build de Docker aplica el parche si hace falta.

## Compilar y probar

```powershell
dotnet build server\custom\MuCustom.PlugIns -p:ci=true      # solo los plugins (rápido)
.\server\up.cmd build                                         # imagen nueva y reinicio del servidor
dotnet build server\custom\MuCustom.TestClient -p:ci=true
server\custom\MuCustom.TestClient\bin\Debug\MuCustom.TestClient.exe --user test1 --pass test1 --char test1Dk "/vip" "/diarias"
```

`-p:ci=true` evita que OpenMU regenere sus clases de persistencia (no hace falta y requiere herramientas extra).
La guía de pruebas completa está en [`TESTING_GUIDE.md`](../../TESTING_GUIDE.md).
