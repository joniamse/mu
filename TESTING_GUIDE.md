# Guía de pruebas: VIP y fichas, misiones diarias, anuncios y herramientas

Esta guía cubre lo que se agregó en las fases 1 a 4. Todo corre dentro del servidor y del cliente:
no hay servicios externos. La recarga de fichas es **simulada** (no hay pagos reales).

> Aviso legal: MU Online es propiedad de Webzen. Vender fichas por dinero real en un servidor
> privado público tiene riesgo legal. Por eso la recarga es una simulación de prueba y se puede
> desactivar (`SimulatedRechargeEnabled`).

---

## 1. Resumen técnico

| Pieza | Dónde | Qué hace |
|---|---|---|
| Plugins del servidor | `server/custom/MuCustom.PlugIns` | VIP, fichas, `/baul`, `/offstore`, `/entradas`, misiones diarias, anuncios. |
| Parche al núcleo de OpenMU | `server/custom/patches/openmu-core.patch` | Puntos de extensión que faltaban: tasas (drop, Chaos Machine, MU Helper), mezcla terminada, selección de personaje, tienda offline, carga de baúles. |
| Imagen Docker | `server/custom/Dockerfile` | OpenMU + parche + plugins (`mu/openmu:dev`). |
| Base de datos | Tablas en `public` de `openmu` | Migraciones versionadas que se aplican solas al arrancar (`mucustom_schema_migrations`). |
| Ventana VIP del cliente | `client-src/MuMain`: `UI/Modern/VipWindowDataModel.*`, `Data/UI/vip.rml`, `vip.rcss` | Saldo, nivel, alcance, tiempo restante, beneficios, 6 paquetes y compra/renovación. |
| Cliente de prueba | `server/custom/MuCustom.TestClient` | Se conecta como un jugador sin gráficos y ejecuta comandos (sirve para pruebas automáticas). |
| Herramientas | `tools/audio_normalizer.py`, `tools/dialog_formatter.py` | Normalizar sonidos y reescribir diálogos de NPC. |

**Cómo se comunica la ventana VIP:** el cliente envía comandos de chat (`/vip ui`,
`/vip comprar N ui`, `/recargar N ui`) y el servidor responde con avisos que empiezan con
`#MUVIP|`. El cliente los saca del chat y arma la ventana. No hace falta ningún paquete nuevo.

### Niveles VIP (30 días)

| Nivel | Costo | Alcance | Exp | Drop | Chaos | Baúles | Tienda offline | MU Helper | BC/DS por día |
|---|---|---|---|---|---|---|---|---|---|
| 0 Free | - | - | - | - | - | 1 | no | - | - |
| 1 Plata | 500 | 1 personaje | +10% | +5% | +3 | 2 | 12 h | -30% | 1 |
| 2 Oro | 1000 | 1 personaje | +20% | +10% | +5 | 3 | 24 h | -50% | 2 |
| 3 Platino | 1500 | 1 personaje | +30% | +15% | +10 | 5 | sin límite | gratis | 3 |
| 4 Diamante | 2500 | toda la cuenta | +30% | +15% | +10 | 5 compartidos | sin límite, jugando con otro personaje | gratis | 3 por personaje |

- "Chaos +3" son puntos porcentuales: una mezcla de 60% pasa a 63%. Solo se aplica a mezclas que pueden fallar.
- Comprar el mismo nivel activo **renueva** (+30 días sobre la fecha de vencimiento). Comprar uno mayor lo reemplaza. No se puede comprar uno menor al activo.
- Las entradas BC/DS se entregan como tickets (Capa de Invisibilidad y Devil's Invitation del nivel del personaje) con `/entradas`.

### Comandos

| Comando | Qué hace |
|---|---|
| `/vip` | Estado y beneficios (texto). `/vip ui` abre la ventana. |
| `/vip comprar <1-4>` | Compra o renueva una membresía. |
| `/fichas` | Saldo de la cuenta. |
| `/recargar` | Lista de paquetes. `/recargar <1-6>` acredita un paquete (simulado). |
| `/baul [1-5]` | Muestra o cambia el baúl activo (se abre en el NPC del baúl). |
| `/offstore` | Con la tienda personal abierta: deja la tienda en el mundo y desconecta. `/offstore cerrar` la cierra desde otro personaje. |
| `/entradas` | Entradas diarias VIP a Blood Castle y Devil Square. |
| `/diarias` | Misiones del día con su progreso, p. ej. `[34/50 Death Knight]`. |
| `/reclamar [N]` | Cobra las misiones completas (o solo la N). |

---

## 2. Puesta en marcha

```powershell
.\server\up.cmd build        # compila la imagen con los plugins y reinicia el servidor
.\server\up.cmd logs         # en otra terminal: ver el arranque
```

En el log tiene que aparecer, en el primer arranque:

```
MuCustom: applied database migration 1 ("vip_and_fichas").
MuCustom: applied database migration 2 ("daily_quests").
```

Y el cliente: `client-src\build-client.cmd` y luego `client-src\play.cmd`.

---

## 3. SQL de prueba

Abrir la consola de la base con `server\db.cmd` (o pasarle un archivo `.sql`).

```sql
-- Migraciones aplicadas
SELECT * FROM mucustom_schema_migrations ORDER BY version;

-- Paquetes de recarga (6 filas: Básico 500 ... Leyenda 14000)
SELECT id, name, price_usd, base_fichas, bonus_fichas, total_fichas FROM shop_recharge_packages ORDER BY id;

-- Saldo de prueba para TODAS las cuentas
UPDATE account_cash_currency SET fichas_balance = 5000;

-- ...o solo para una cuenta
UPDATE account_cash_currency SET fichas_balance = 5000
 WHERE account_id = (SELECT "Id" FROM data."Account" WHERE "LoginName" = 'test0');

-- Saldo, membresías y movimientos de una cuenta
SELECT a."LoginName", c.fichas_balance FROM account_cash_currency c JOIN data."Account" a ON a."Id" = c.account_id WHERE a."LoginName" = 'test0';
SELECT ch."Name", s.vip_level, s.expires_at FROM character_vip_status s JOIN data."Character" ch ON ch."Id" = s.character_id;
SELECT a."LoginName", s.vip_level, s.expires_at FROM account_vip_status s JOIN data."Account" a ON a."Id" = s.account_id;
SELECT * FROM fichas_ledger ORDER BY id DESC LIMIT 20;
```

> Las cuentas nuevas reciben su fila de saldo al entrar al juego. Un `UPDATE` hecho con el
> jugador conectado vale en el acto: el saldo se lee de la base en cada consulta.

---

## 4. Ventana VIP, recarga y compra

1. Entrar al juego con un personaje.
2. Abrir la ventana:
   - con la **interfaz HD**: botón dorado **VIP** del dock (abajo a la derecha);
   - con la interfaz clásica: escribir `/vip ui` en el chat.
3. Comprobar que muestra: saldo de fichas, insignia del nivel, alcance, tiempo restante (y fecha de
   vencimiento), personaje y baúl activo, las 4 membresías con sus beneficios y los 6 paquetes.
4. **Recarga simulada:** clic en un paquete (p. ej. *Leyenda*). Arriba aparece el aviso con las
   fichas acreditadas y el saldo se actualiza. En SQL: una fila `recharge_simulated` en `fichas_ledger`.
5. **Compra:** clic en *Comprar* de una membresía: el botón pasa a "Confirmar N" (dorado); segundo
   clic para comprar. Se descuenta el costo, la insignia cambia y el tiempo restante muestra ~30 días.
   - Con saldo insuficiente el aviso lo dice y no se descuenta nada.
   - *Renovar* en el nivel activo suma 30 días.
6. **Diamante:** entrar con otro personaje de la misma cuenta: el nivel sigue siendo Diamante ("Toda la cuenta").

Lo mismo sin ventana: `/recargar 6`, `/vip comprar 2`, `/fichas`, `/vip`.

### Validar los multiplicadores

| Beneficio | Cómo verlo |
|---|---|
| Experiencia | Matar el mismo monstruo con y sin VIP: con Oro da un 20% más (el número que muestra el cliente al matar). |
| Drop | Estadístico (sube la probabilidad de cada grupo de drop). Se nota en un rato de caza; no hay un número que mirar. |
| Chaos Machine | Al combinar, el log del servidor muestra `Crafting success chance: 60 %` y luego `Crafting success chance modified by plugins: 63 %` (Plata). |
| MU Helper | El cobro periódico del helper baja (Plata -30%, Oro -50%, Platino/Diamante gratis). |
| Baúles | `/baul 2` y abrir el NPC del baúl: aparece vacío; poner un item, `/baul 1`, abrir: está el baúl original. Sin VIP, `/baul 2` se rechaza. |
| Tienda offline | Abrir la tienda personal (S), poner precios, abrirla y escribir `/offstore`: el juego se cierra y la tienda queda. Desde otra cuenta se ve y se puede comprar. Con Plata/Oro dura 12/24 h y se cierra al volver a entrar con la cuenta. Con Diamante se puede entrar con otro personaje; `/offstore cerrar` la cierra. |
| Entradas BC/DS | `/entradas` entrega los tickets del día (según nivel del personaje); una segunda vez avisa que ya se reclamaron. |

Los logs del servidor (`.\server\up.cmd logs`) muestran cada compra y recarga con el prefijo `MuCustom:`.

---

## 5. Misiones diarias

- [ ] `/diarias` lista las misiones del nivel del personaje, con `[0/50 Spider]` y el tiempo hasta el reinicio (00:00 UTC).
- [ ] Matar monstruos del objetivo en el mapa indicado: cada 10 muertes aparece el progreso; al llegar al total, "¡Completaste ...!".
- [ ] Las muertes en otro mapa o de otro monstruo no cuentan (salvo "Exterminador", que cuenta cualquiera).
- [ ] En party, las muertes cuentan para los miembros en el mismo mapa (`DailyQuestsShareWithParty`).
- [ ] Salir y volver a entrar: el progreso se mantiene.
- [ ] `/reclamar` entrega zen, experiencia y fichas de las completas.
- [ ] Un segundo `/reclamar` (o `/reclamar N`) dice que ya se reclamó: no se cobra dos veces en el mismo día.
- [ ] Con el inventario casi lleno de zen, `/reclamar` avisa que no entra y no cobra.

Simular el día siguiente sin esperar (con el personaje desconectado):

```sql
UPDATE openmu_quest_progress SET period_date = period_date - 1;
```

Al volver a entrar, `/diarias` muestra todo en cero y se puede reclamar de nuevo.

Las misiones se editan en la tabla (se recargan cada 5 minutos):

```sql
SELECT id, code, title, map_number, monster_number, required_count, reward_zen, reward_exp, reward_fichas, active FROM openmu_quests ORDER BY sort_order;
UPDATE openmu_quests SET required_count = 5 WHERE code = 'lorencia_spiders';   -- para probar rápido
```

---

## 6. Anuncios globales

### Chaos Machine (+10 a +15)

- [ ] En el Chaos Goblin (Noria) combinar un item +10 con 1 Jewel of Chaos y 2 Bless + 2 Soul (mezcla "+11").
- [ ] Si sale bien, todos los jugadores ven: `[Sistema] ¡El jugador {Nombre} mejoró con éxito su {Ítem} a +{Nivel}!`
- [ ] Si falla y el item se destruye: `[Sistema] La Chaos Machine ha destruido el {Ítem} de {Nombre} al intentar mejorarlo a +{Nivel}.`
- [ ] Las mejoras por debajo de +10 no se anuncian (`AnnouncedMinimumUpgradeLevel`).

El anuncio sale en el centro de la pantalla y queda en el chat.

### Jefes de mundo

- [ ] Con un GM, fuera de la zona segura: `/createmonster 43` (Golden Budge Dragon) y matarlo (o `/killall`).
- [ ] Todos ven: `[Sistema] ¡{Nombre} derrotó a Golden Budge Dragon en Lorencia!`
- [ ] La lista de jefes se edita en el panel de administración, plugin "MuCustom: nucleo", `WorldBossMonsterNumbers`.

---

## 7. Configuración (panel de administración)

`http://localhost` → Plugins → **MuCustom: nucleo** → configuración:

| Opción | Por defecto | |
|---|---|---|
| `SimulatedRechargeEnabled` | sí | **Desactivar en un servidor público.** |
| `AnnounceChaosUpgrades` / `AnnouncedMinimumUpgradeLevel` | sí / 10 | |
| `AnnounceWorldBosses` / `WorldBossMonsterNumbers` | sí / 15 jefes | |
| `ShowVipStatusOnEnter` | sí | Línea con VIP y fichas al entrar. |
| `DailyQuestsShareWithParty` | sí | |

Cada plugin MuCustom se puede desactivar por separado en la misma lista.

---

## 8. Pruebas automáticas con el cliente de prueba

Con el servidor corriendo:

```powershell
dotnet build server\custom\MuCustom.TestClient -p:ci=true
$c = "server\custom\MuCustom.TestClient\bin\Debug\MuCustom.TestClient.exe"
& $c --user test1 --pass test1 --char test1Dk "/fichas" "/recargar 2" "/vip comprar 1" "/vip" "/baul 2" "/baul 1" "/diarias"
```

Imprime todo lo que responde el servidor. Otras acciones: `!npc 240` (hablar con el guardián del
baúl y ver cuántos items tiene), `!mover 0 <slot> 3 <slot>` + `!mix <tipo>` (Chaos Machine, con
`--version 10404`), `!tienda <nombre>` (abrir la tienda personal). Ver el encabezado de `Program.cs`.

Resultado de la última corrida (13/10/2026):

- Recarga, compra, renovación, saldo insuficiente y nivel mayor activo: OK.
- `/baul`: el baúl 2 con 3 items y el 1 con 13 se alternan en el NPC; límite por VIP OK.
- Diarias: 290 muertes contadas por monstruo y mapa, misión completa, reclamo con zen/exp/fichas, segundo reclamo bloqueado, progreso persistido.
- Jefe de mundo y Chaos Machine (éxito y destrucción) anunciados; bonus VIP de Chaos 60% → 63% en el log.
- Tienda offline Plata (se cierra al reconectar) y Diamante (sigue abierta jugando con otro personaje; `/offstore cerrar`; elegir el personaje de la tienda cierra la tienda y pide reconectar).

---

## 9. Herramientas Python

```powershell
pip install -r tools\requirements.txt
```

### `tools/audio_normalizer.py`

- [ ] `python tools\audio_normalizer.py "<cliente>\Data\Sound" --dry-run` mide sin escribir.
- [ ] Sin `--dry-run` escribe en `<cliente>\Data\Sound_normalized` (los originales no se tocan).
- [ ] El informe (`--report informe.csv`) muestra LUFS antes/después: los procesados quedan en -14,0 salvo los marcados "pico al techo".
- [ ] Silencios y archivos ilegibles se informan como `skipped` / `error`.
- [ ] Para aplicar en el cliente: `--in-place --backup "<carpeta de respaldo>"`.

> En la instalación actual del cliente no hay carpeta `Data\Sound` (no tiene los `.wav`). La herramienta
> se probó con archivos generados: 6 de 6 quedaron en -14,0 LUFS y el siseo bajó unos 8 dB.

### `tools/dialog_formatter.py`

- [ ] `python tools\dialog_formatter.py` muestra el diff de los diálogos de Lorencia, Noria y Devias.
- [ ] Ya aplicado en `client-src/MuMain/src/Localization/Dialog.es.resx` (78 cambios: reescrituras + ortografía, p. ej. "piedrá", "vió", "Atlance", "Y?Semeden"). Correrlo de nuevo da 0 cambios.
- [ ] Recompilar el cliente y hablar con Pasi, el guardián del baúl o el Chaos Goblin con el juego en español: textos nuevos.
- [ ] `--cities devias`, `--no-rewrite --clean-all` (solo ortografía de todo el archivo), `--report cambios.md`.
