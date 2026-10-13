# tools/

Herramientas de terceros (editores de modelos, texturas, vcpkg, etc.): su contenido no se versiona.

Las que sí se versionan son nuestras herramientas Python:

| Herramienta | Qué hace |
|---|---|
| `audio_normalizer.py` | Recorre `Data/Sound` del cliente, reduce el siseo (puerta espectral) y normaliza cada `.wav` a -14 LUFS sin pasar de -1 dBFS de pico. No toca los originales salvo con `--in-place --backup`. |
| `dialog_formatter.py` | Reescribe los diálogos de NPC de Lorencia, Noria y Devias (`client-src/MuMain/src/Localization/Dialog.es.resx`) con ortografía limpia y tono de fantasía oscura, a partir de `dialog_catalog.es.json`. Muestra el diff; con `--write` guarda. |

```powershell
pip install -r tools\requirements.txt            # solo para audio_normalizer.py
python tools\audio_normalizer.py "<cliente>\Data\Sound" --report informe.csv
python tools\dialog_formatter.py                  # vista previa
python tools\dialog_formatter.py --clean-all --write
```

Las opciones de cada una se ven con `--help`. Después de cambiar los diálogos hay que recompilar el
cliente (`client-src\build-client.cmd`): los textos se compilan dentro de `Main.exe`.
