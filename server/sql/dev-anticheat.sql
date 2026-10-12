-- SOLO DESARROLLO LOCAL: el anti speed-hack solo avisa; no banea ni desconecta.
-- Motivo: el cliente MuMain dispara falsos positivos en el chequeo de caminata
-- ("traveled 1 tiles in 76ms, expected at least 277ms") y, con AutoBan, las
-- cuentas de prueba quedaban baneadas a las 3 advertencias.
-- ANTES DE ABRIR EL SERVIDOR AL PUBLICO: revertir con dev-anticheat-revert.sql
-- y resolver la causa en el cliente.
-- Aplicar con:  server\db.cmd server\sql\dev-anticheat.sql   y reiniciar: docker restart openmu-startup

update config."PlugInConfiguration"
   set "CustomConfiguration" = jsonb_set(jsonb_set("CustomConfiguration"::jsonb,
           '{AutoBan}', 'false'), '{DisconnectOnViolation}', 'false')::text
 where "TypeId" = 'a95a8d2f-a0c3-442e-995c-005b5c1b42d2';

-- Desbanear las cuentas de prueba que hayan caido por falsos positivos.
-- AccountState: 0 Normal, 1 Spectator, 2 GameMaster, 3 GameMasterInvisible, 4 Banned, 5 TemporarilyBanned
update data."Account" set "State" = 0
 where "State" in (4, 5) and ("LoginName" like 'test%' or "LoginName" in ('quest1', 'quest2', 'quest3', 'ancient', 'socket'));

select "CustomConfiguration" from config."PlugInConfiguration"
 where "TypeId" = 'a95a8d2f-a0c3-442e-995c-005b5c1b42d2';
