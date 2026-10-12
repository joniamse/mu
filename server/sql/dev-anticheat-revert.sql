-- Restaura el anti speed-hack de produccion (valores por defecto de OpenMU).
update config."PlugInConfiguration"
   set "CustomConfiguration" = jsonb_set(jsonb_set("CustomConfiguration"::jsonb,
           '{AutoBan}', 'true'), '{DisconnectOnViolation}', 'true')::text
 where "TypeId" = 'a95a8d2f-a0c3-442e-995c-005b5c1b42d2';

select "CustomConfiguration" from config."PlugInConfiguration"
 where "TypeId" = 'a95a8d2f-a0c3-442e-995c-005b5c1b42d2';
