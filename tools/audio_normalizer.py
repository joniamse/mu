#!/usr/bin/env python3
"""Normaliza los sonidos .wav del cliente: reduce el siseo y lleva la sonoridad a -14 LUFS.

Recorre Data/Sound (o la carpeta indicada) de forma recursiva y, por cada .wav:

1. Reduce el ruido de fondo estacionario (siseo, zumbido de grabación) con una puerta
   espectral (noisereduce, modo estacionario). Los clips muy cortos no se tocan: no hay
   suficiente señal para estimar el ruido sin dañar el ataque del sonido.
2. Mide la sonoridad integrada según ITU-R BS.1770-4 (pyloudnorm) y aplica la ganancia
   para llegar al objetivo (por defecto -14 LUFS).
3. Si el pico supera el techo (por defecto -1 dBFS), baja la ganancia para no saturar.
   Esos archivos quedan algo por debajo del objetivo y el informe lo indica.

Por defecto NO modifica los originales: escribe una copia del árbol en --out
(Data/Sound_normalized). Con --in-place sobrescribe, guardando antes una copia en
--backup. Conserva frecuencia de muestreo, canales y formato (PCM 8/16/24 bits, float).

Uso:
    python tools/audio_normalizer.py "C:/MU/Data/Sound"
    python tools/audio_normalizer.py "C:/MU/Data/Sound" --in-place --backup "C:/MU/Sound_backup"
    python tools/audio_normalizer.py Data/Sound --dry-run --report informe.csv

Requisitos: pip install -r tools/requirements.txt
"""

from __future__ import annotations

import argparse
import csv
import shutil
import sys
import warnings
from concurrent.futures import ProcessPoolExecutor, as_completed
from dataclasses import asdict, dataclass, field
from pathlib import Path

try:
    import numpy as np
    import noisereduce as nr
    import pyloudnorm as pyln
    import soundfile as sf
except ImportError as error:  # pragma: no cover - depends on the environment
    sys.exit(f"Falta una dependencia ({error.name}). Instala con: pip install -r tools/requirements.txt")

# Clips más cortos que esto no se limpian de ruido (segundos).
MIN_DENOISE_SECONDS = 0.25
# pyloudnorm mide en bloques de 400 ms; para clips más cortos se usa un bloque menor.
STANDARD_BLOCK_SECONDS = 0.400
MIN_BLOCK_SECONDS = 0.050
# Por debajo de esto el archivo se considera silencio y se copia tal cual (LUFS).
SILENCE_LUFS = -70.0


@dataclass
class Settings:
    target_lufs: float = -14.0
    peak_ceiling_db: float = -1.0
    denoise: bool = True
    denoise_strength: float = 0.85
    dry_run: bool = False


@dataclass
class Result:
    file: str
    status: str = "ok"
    sample_rate: int = 0
    channels: int = 0
    seconds: float = 0.0
    lufs_before: float | None = None
    lufs_after: float | None = None
    peak_after_db: float | None = None
    gain_db: float | None = None
    denoised: bool = False
    limited: bool = False
    note: str = field(default="")


def measure_lufs(audio: np.ndarray, rate: int) -> float:
    """Integrated loudness; uses a shorter block for clips under 400 ms."""
    seconds = audio.shape[0] / rate
    block = STANDARD_BLOCK_SECONDS if seconds >= STANDARD_BLOCK_SECONDS else max(MIN_BLOCK_SECONDS, seconds * 0.9)
    meter = pyln.Meter(rate, block_size=block)
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        return float(meter.integrated_loudness(audio))


def denoise(audio: np.ndarray, rate: int, strength: float) -> np.ndarray:
    """Spectral gating of stationary noise, per channel."""
    # noisereduce expects (channels, samples).
    cleaned = nr.reduce_noise(y=audio.T, sr=rate, stationary=True, prop_decrease=strength)
    return np.asarray(cleaned, dtype=np.float64).reshape(audio.T.shape).T


def peak_db(audio: np.ndarray) -> float:
    peak = float(np.max(np.abs(audio))) if audio.size else 0.0
    return 20 * np.log10(peak) if peak > 0 else -np.inf


def process_file(source: Path, destination: Path, settings: Settings) -> Result:
    result = Result(file=str(source))
    try:
        info = sf.info(str(source))
        audio, rate = sf.read(str(source), dtype="float64", always_2d=True)
    except Exception as error:  # unsupported codec (e.g. ADPCM) or broken file
        result.status = "error"
        result.note = f"no se pudo leer: {error}"
        return result

    result.sample_rate = rate
    result.channels = audio.shape[1]
    result.seconds = round(audio.shape[0] / rate, 3)
    if audio.shape[0] == 0:
        result.status = "skipped"
        result.note = "archivo vacío"
        return result

    lufs = measure_lufs(audio, rate)
    result.lufs_before = round(lufs, 2) if np.isfinite(lufs) else None
    if not np.isfinite(lufs) or lufs < SILENCE_LUFS:
        result.status = "skipped"
        result.note = "silencio"
        if not settings.dry_run:
            destination.parent.mkdir(parents=True, exist_ok=True)
            if source.resolve() != destination.resolve():
                shutil.copy2(source, destination)
        return result

    processed = audio
    if settings.denoise and result.seconds >= MIN_DENOISE_SECONDS:
        processed = denoise(processed, rate, settings.denoise_strength)
        result.denoised = True
        lufs = measure_lufs(processed, rate)

    gain_db = settings.target_lufs - lufs
    processed = processed * (10 ** (gain_db / 20))
    peak = peak_db(processed)
    if peak > settings.peak_ceiling_db:
        reduction = settings.peak_ceiling_db - peak
        processed = processed * (10 ** (reduction / 20))
        gain_db += reduction
        result.limited = True
        result.note = "pico al techo: queda por debajo del objetivo"

    result.gain_db = round(gain_db, 2)
    result.peak_after_db = round(peak_db(processed), 2)
    after = measure_lufs(processed, rate)
    result.lufs_after = round(after, 2) if np.isfinite(after) else None

    if not settings.dry_run:
        destination.parent.mkdir(parents=True, exist_ok=True)
        # The same container and sample format as the original, so the client reads it like before.
        sf.write(str(destination), processed, rate, subtype=info.subtype, format=info.format)
    return result


def collect(root: Path) -> list[Path]:
    return sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() == ".wav")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("source", type=Path, nargs="?", default=Path("Data/Sound"), help="carpeta de sonidos (por defecto Data/Sound)")
    parser.add_argument("--out", type=Path, help="carpeta de salida (por defecto <source>_normalized)")
    parser.add_argument("--in-place", action="store_true", help="sobrescribe los originales (requiere --backup)")
    parser.add_argument("--backup", type=Path, help="copia de seguridad de los originales para --in-place")
    parser.add_argument("--target", type=float, default=-14.0, help="sonoridad objetivo en LUFS (por defecto -14)")
    parser.add_argument("--ceiling", type=float, default=-1.0, help="techo de pico en dBFS (por defecto -1)")
    parser.add_argument("--no-denoise", action="store_true", help="no reduce el ruido")
    parser.add_argument("--denoise-strength", type=float, default=0.85, help="intensidad de la reducción de ruido, 0 a 1 (por defecto 0.85: unos -8 dB de siseo)")
    parser.add_argument("--dry-run", action="store_true", help="solo mide e informa, no escribe archivos")
    parser.add_argument("--report", type=Path, help="guarda un informe CSV")
    parser.add_argument("--jobs", type=int, default=0, help="procesos en paralelo (por defecto, todos los núcleos)")
    args = parser.parse_args(argv)

    source: Path = args.source
    if not source.is_dir():
        print(f"No existe la carpeta {source}", file=sys.stderr)
        return 2
    if args.in_place and not args.backup and not args.dry_run:
        print("--in-place necesita --backup: los originales de Webzen no se recuperan de otra forma.", file=sys.stderr)
        return 2
    if not 0.0 <= args.denoise_strength <= 1.0:
        print("--denoise-strength debe estar entre 0 y 1.", file=sys.stderr)
        return 2

    files = collect(source)
    if not files:
        print(f"No hay archivos .wav en {source}")
        return 0

    out_root = source if args.in_place else (args.out or source.with_name(source.name + "_normalized"))
    if args.in_place and args.backup and not args.dry_run:
        for file in files:
            target = args.backup / file.relative_to(source)
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists():
                shutil.copy2(file, target)
        print(f"Copia de seguridad en {args.backup}")

    settings = Settings(args.target, args.ceiling, not args.no_denoise, args.denoise_strength, args.dry_run)
    results: list[Result] = []
    with ProcessPoolExecutor(max_workers=args.jobs or None) as pool:
        futures = {pool.submit(process_file, f, out_root / f.relative_to(source), settings): f for f in files}
        for index, future in enumerate(as_completed(futures), 1):
            result = future.result()
            results.append(result)
            before = "-" if result.lufs_before is None else f"{result.lufs_before:6.1f}"
            after = "-" if result.lufs_after is None else f"{result.lufs_after:6.1f}"
            print(f"[{index}/{len(files)}] {result.status:7} {before} -> {after} LUFS  {Path(result.file).relative_to(source)} {result.note}")

    results.sort(key=lambda r: r.file)
    if args.report:
        with args.report.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(asdict(results[0]).keys()))
            writer.writeheader()
            writer.writerows(asdict(r) for r in results)
        print(f"Informe: {args.report}")

    done = [r for r in results if r.status == "ok"]
    errors = [r for r in results if r.status == "error"]
    limited = [r for r in done if r.limited]
    print()
    print(f"Procesados: {len(done)}  Omitidos: {len(results) - len(done) - len(errors)}  Errores: {len(errors)}  Limitados por pico: {len(limited)}")
    if not args.dry_run:
        print(f"Salida: {out_root}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
