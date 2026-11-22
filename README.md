# Holocene

A human history simulator on a real Earth, starting around 10,000 BCE. Climate, ecology, demography, culture,
language, religion, technology, organizations and individual historical figures are simulated together, and the
resulting history can be explored in a browser "Legends" viewer in the spirit of Dwarf Fortress.

The full plan is in [docs/DESIGN.md](docs/DESIGN.md). This is phase 0–1: the physical world.

## What works now

- **Geodesic world grid**: 40,962 cells (~112 km) or 10,242 (~225 km), near-equal area, built from great-circle
  subdivision of an icosahedron.
- **Real Earth terrain** from ETOPO5. Each cell stores its full elevation distribution, so a changing sea level
  floods or exposes the right part of it (Doggerland, Beringia, Sundaland).
- **Orbital forcing**: Berger (1978) Milankovitch solution with daily insolation for any year.
- **Climate model**: a two-layer (air and surface) seasonal energy balance with moist-static-energy transport;
  sea-level-pressure-driven winds (Hadley/Ferrel structure, thermal highs and lows, Ekman friction); Stommel ocean
  gyres on the real coastline; a water cycle with column vapour, Bretherton precipitation, eddy vapour transport,
  orographic blocking and rain shadows; snow, sea ice, glaciers, soil moisture; vegetation feedback; and a
  shortwave scheme with water-vapour absorption. It's validated against ERA-Interim and GPCP
  ([docs/climate-validation.md](docs/climate-validation.md)).
- **Earth forcing histories**: EPICA Dome C CO₂ and the deglacial sea-level curve.
- **Hydrology**: rivers routed over the terrain from simulated runoff; lakes fill closed depressions until
  evaporation balances inflow, so they're terminal or overflowing depending on climate.
- **Biomes**: Köppen–Geiger climate classes, biome map, and Miami-model productivity.
- **Browser viewer** for the physical world: layers, months, rivers, lakes, winds, and a per-place climate chart.
- **Parameter registry** (`data/parameters.json`): every number has a unit, a kind (constant / empirical /
  numerical / tunable) and a source. Code refuses unregistered parameters.

## Download and run

Download the package for your system from the **Releases** page (Windows, Linux, macOS Intel and Apple Silicon).
They're self-contained, so no .NET install is needed. Unpack it and run `holocene` (`holocene.exe` on Windows):
it opens the world viewer in your browser with the bundled worlds for 10,000 BCE and the present. Run
`holocene help` for the simulation commands, e.g. `holocene world --freq 32 --year -5000 --id bce5000`.

On macOS the binary isn't signed: if Gatekeeper blocks it, right-click → Open, or run
`xattr -d com.apple.quarantine holocene`.

Releases are built by GitHub Actions (`.github/workflows/release.yml`) whenever a `v*` tag is pushed.

## Build from source

Requires the .NET 8 SDK (or newer). There are no package dependencies.

```bash
dotnet build -c Release
# import terrain (cached in data/cache)
dotnet run -c Release --project src/Holocene.Cli -- build-world --freq 32
# simulate the physical world at 10,000 BCE (astronomical year -9999) and export it for the viewer
dotnet run -c Release --project src/Holocene.Cli -- world --freq 32 --year -9999 --id bce10000
# present day, with the validation report
dotnet run -c Release --project src/Holocene.Cli -- world --freq 32 --year 1990 --id present --validate 1
# open the viewer at http://localhost:8080
dotnet run -c Release --project src/Holocene.Cli -- serve
# tests
dotnet run -c Release --project tests/Holocene.Tests
```

`--freq 64` gives the full 40,962-cell grid; it's about 4× slower than `--freq 32` (~20 s per simulated
climate year on 2 cores at f32). Override any parameter for an experiment with `--group.name value`, for example
`--transport.advection_factor 0.7`.

## Layout

```
src/Holocene.Core/   simulation library (no I/O beyond data loading)
  Grid/              geodesic grid
  Mathx/             vectors, deterministic RNG, sparse solver
  Parameters/        parameter registry
  Earth/             ETOPO5 import, Earth forcing histories
  Climate/           orbital forcing, climate model, validation
  World/             terrain, hydrology, biomes, world snapshots
src/Holocene.Cli/    command line, viewer export, static server
tests/               test runner and tests
viewer/              browser viewer (static)
data/                parameters, Earth data, validation data, caches
tools/               data preparation scripts (Python)
docs/                design document and validation reports
```

## Data sources

- ETOPO5 5-minute global relief (NOAA NGDC), via NCAR geocat-datafiles.
- ERA-Interim 2005 monthly 2 m temperature and GPCP v2.2 1979–2010 precipitation (validation only), via NCAR
  geocat-datafiles; converted by `tools/prep_validation.py`.
- Berger (1978) orbital series, as implemented in NCAR CESM `shr_orb_mod.F90`.
- EPICA Dome C CO₂ (Monnin et al. 2001, 2004), NOAA/WDS Paleoclimatology.
- Sea level: approximate digitization of Lambeck et al. (2014), to be replaced with the published table.
