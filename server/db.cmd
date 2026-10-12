@echo off
rem Ejecuta un archivo .sql contra la DB de OpenMU, o abre psql si no se pasa archivo.
rem Uso: server\db.cmd [archivo.sql]
if "%~1"=="" (
  docker exec -it database psql -U postgres -d openmu
) else (
  docker exec -i database psql -U postgres -d openmu < "%~1"
)
