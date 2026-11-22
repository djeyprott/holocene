# Climate validation — summary

The climate model is run with present-day forcing (1990 orbit, 357 ppm CO2, present sea level) from a warm,
ice-free start for 40 years, and compared with ERA-Interim 2 m temperature and GPCP precipitation.

**Works well**
- Global energy budget: absorbed solar 238 vs OLR 238 W/m² (CERES 240/239); planetary albedo 0.30; cloud cover 0.62.
- Monthly temperature: RMSE 5.4 °C, correlation 0.94. Tropics and subtropics within 1–3 °C; NH sea-ice extent ~15 M km² (observed ~15).
- Continental winters and summers in the interior (Yakutsk, Moscow, Chicago in July) within a few degrees.
- Dry belts: Atacama, Alice Springs, Sahel winter, central Asia.

**Known errors (to fix in phase 1b)**
- Global mean +1.5 °C, mostly a warm ocean bias; the Southern Ocean is too warm and Antarctic sea ice is almost absent
  (missing wind-driven Ekman transport and upwelling, no thermohaline overturning).
- Precipitation is ~40 % too high globally (RMSE 5.9 mm/day, land annual correlation 0.47). No Indian or West African
  monsoon; mountains (Andes, Mexico, Tibet) and the Levant/Mesopotamia far too wet; desert summers 7–15 °C too cool.
  These feed each other (wet soil cools deserts, cool deserts saturate more easily). Likely causes: no representation
  of large-scale subsidence suppressing rain over summer heat lows; surface heat lows forced from sea-level-equivalent
  temperature over high terrain; monsoon cross-equatorial flow not captured by the pressure model.
- Winter cold is too weak on the east coasts of continents (Goose Bay, Beijing) — cold-air advection is damped.
- Antarctica ~15 °C too warm (no surface inversion over the ice plateau).
- At 225 km resolution narrow features (Seattle vs Yakima rain shadow) fall in one cell.

**Calibration history**: each formulation change and its effect is recorded in the parameter registry sources
(`data/parameters.json`) and in the development log in DESIGN.md §16.

---

Grid f32 (10242 cells), 40-year spin-up, year 1990, CO2 357 ppm.

# Climate validation (present-day forcing)

Observations: ERA-Interim monthly mean 2 m temperature, 2005; GPCP v2.2 monthly precipitation climatology 1979-2010.

## Global means

| Quantity | Model | Observed |
|---|---|---|
| Surface temperature (°C) | 15.92 | 14.38 |
| Precipitation (mm/day) | 3.83 | 2.67 |
| Planetary albedo (annual) | 0.300 | ~0.29 (CERES) |
| Cloud cover (annual) | 0.62 | ~0.65 (ISCCP) |
| Absorbed solar / OLR (W/m²) | 238.2 / 237.7 | 240 / 239 (CERES) |
| Max sea-ice area NH / SH (M km²) | 14.6 / 2.4 | ~15 / ~18 (NSIDC) |

## Errors (area-weighted)

| Field | Region | Bias | RMSE | Correlation |
|---|---|---|---|---|
| Monthly T (°C) | all | 1.54 | 5.38 | 0.942 |
| Monthly T (°C) | land | -1.75 | 5.24 | 0.945 |
| Monthly T (°C) | ocean | 2.25 | 4.61 | 0.942 |
| Monthly P (mm/day) | all | 1.15 | 5.94 | 0.296 |
| Monthly P (mm/day) | land | 1.42 | 5.87 | 0.354 |
| Monthly P (mm/day) | ocean | 1.07 | 6.07 | 0.265 |
| Seasonal T range (°C) | land | -3.70 | 6.58 | 0.913 |
| Annual P (mm/yr) | land | 520 | 1480 | 0.467 |

## Zonal means (annual)

| Lat band | T model | T obs | P model | P obs |
|---|---|---|---|---|
|  -80° |  -17.8 |  -33.5 |  0.75 |  0.52 |
|  -70° |   -4.4 |  -16.1 |  2.21 |  1.11 |
|  -60° |    6.5 |   -0.7 |  3.86 |  2.75 |
|  -50° |   12.6 |    6.1 |  4.90 |  2.88 |
|  -40° |   17.7 |   13.2 |  3.35 |  2.86 |
|  -30° |   21.3 |   19.1 |  1.52 |  2.30 |
|  -20° |   24.2 |   23.2 |  2.10 |  2.03 |
|  -10° |   26.1 |   25.3 |  5.70 |  3.25 |
|    0° |   26.9 |   26.0 | 13.10 |  4.35 |
|   10° |   25.9 |   26.6 |  3.70 |  4.39 |
|   20° |   23.4 |   25.4 |  1.54 |  1.91 |
|   30° |   17.6 |   19.9 |  1.86 |  2.13 |
|   40° |   11.6 |   12.4 |  3.12 |  2.70 |
|   50° |    6.4 |    5.5 |  3.44 |  2.50 |
|   60° |   -1.1 |   -0.5 |  2.83 |  2.21 |
|   70° |   -9.5 |   -8.6 |  1.22 |  1.33 |
|   80° |  -14.6 |  -14.0 |  0.53 |  0.61 |

## Annual energy budget by latitude band (W/m², land | ocean)

| Lat | SWabs | OLR | TOA net | sfc SW | LWnet | Sens | Latent | sfc net | albedo | Ts | Ta |
|---|---|---|---|---|---|---|---|---|---|---|---|
| -70° land | 80 | 175 | -94 | 44 | 49 | -13 | 8 | -0 | 0.58 | -17.7 | -16.8 |
| -70° ocean | 145 | 206 | -62 | 105 | 48 | 25 | 39 | -6 | 0.35 | 6.4 | 2.5 |
| -50° land | 204 | 222 | -19 | 137 | 33 | 33 | 71 | 0 | 0.33 | 12.1 | 10.6 |
| -50° ocean | 198 | 224 | -27 | 136 | 32 | 22 | 85 | -3 | 0.32 | 15.3 | 12.8 |
| -30° land | 253 | 250 | 3 | 175 | 63 | 68 | 45 | -1 | 0.32 | 21.9 | 18.4 |
| -30° ocean | 265 | 249 | 16 | 186 | 37 | 19 | 127 | 3 | 0.27 | 24.4 | 21.9 |
| -10° land | 283 | 255 | 28 | 189 | 39 | 40 | 114 | -4 | 0.31 | 23.9 | 22.7 |
| -10° ocean | 297 | 259 | 38 | 214 | 17 | -1 | 200 | -2 | 0.27 | 26.9 | 26.9 |
| 10° land | 278 | 261 | 17 | 199 | 52 | 64 | 79 | 4 | 0.32 | 24.9 | 23.0 |
| 10° ocean | 299 | 259 | 40 | 219 | 17 | -3 | 210 | -5 | 0.27 | 26.5 | 26.6 |
| 30° land | 237 | 239 | -2 | 172 | 71 | 72 | 29 | -0 | 0.35 | 16.9 | 12.7 |
| 30° ocean | 267 | 247 | 20 | 189 | 39 | 22 | 125 | 3 | 0.27 | 23.7 | 20.9 |
| 50° land | 188 | 217 | -28 | 136 | 56 | 41 | 38 | -0 | 0.34 | 5.8 | 3.8 |
| 50° ocean | 202 | 219 | -17 | 145 | 40 | 29 | 79 | -3 | 0.31 | 13.1 | 9.6 |
| 70° land | 129 | 192 | -63 | 88 | 56 | 11 | 22 | -0 | 0.40 | -8.0 | -8.8 |
| 70° ocean | 108 | 189 | -81 | 69 | 47 | 13 | 12 | -4 | 0.47 | -8.5 | -7.8 |

## Named sites

| Site | Jan T m/o | Jul T m/o | Annual P m/o (mm) | Wettest month m/o |
|---|---|---|---|---|
| London (maritime west coast) |   4.4 /   6.5 |  15.3 /  16.9 |  2072 /   839 | Sep / Dec |
| Goose Bay, Labrador (same latitude, east coast) |  -6.4 / -18.7 |  14.4 /  14.4 |  1558 /   937 | Sep / Jul |
| Yakutsk (continental) | -27.3 / -34.4 |  18.7 /  18.8 |    29 /   374 | Oct / Jul |
| Moscow |  -8.8 /  -5.9 |  18.5 /  18.9 |  1448 /   866 | Jul / Jul |
| Cairo (desert) |  11.6 /  12.0 |  22.8 /  29.2 |   452 /    35 | Jun / Jan |
| Riyadh (desert) |  12.7 /  14.1 |  22.8 /  35.6 |   259 /   117 | Jan / Mar |
| Mumbai (monsoon) |  22.9 /  24.5 |  27.9 /  27.1 |     1 /  1203 | Jan / Jul |
| Kolkata (monsoon) |  19.5 /  17.5 |  28.0 /  28.6 |   149 /  1895 | Sep / Jul |
| Beijing |  -1.7 /  -3.8 |  25.1 /  27.5 |    92 /   522 | Apr / Jul |
| Manaus (rainforest) |  29.0 /  25.6 |  25.9 /  25.7 |  3646 /  2434 | Jun / Apr |
| Kinshasa |  26.1 /  23.9 |  23.8 /  23.0 |  3486 /  1617 | Dec / Nov |
| Singapore |  28.1 /  26.3 |  27.1 /  27.1 |  4837 /  2419 | Aug / Dec |
| Lima (cold coastal desert) |   6.7 /  10.9 |   1.3 /  10.2 |  7282 /   703 | Mar / Feb |
| Atacama |  14.3 /  19.7 |   6.0 /  11.3 |    26 /    60 | Jul / Jan |
| Sydney |  22.9 /  21.7 |  16.0 /  13.4 |   156 /   958 | Mar / Feb |
| Alice Springs |  23.9 /  30.2 |  14.9 /  12.5 |   256 /   277 | Jul / Feb |
| Seattle (windward) |   2.9 /   1.0 |  13.7 /  16.7 |  5504 /  1354 | Oct / Nov |
| Yakima (rain shadow) |   2.9 /   1.0 |  13.7 /  16.7 |  5504 /  1354 | Oct / Nov |
| Chicago |   3.9 /  -2.8 |  22.9 /  23.1 |   543 /  1127 | Feb / Jun |
| Mexico City (highland) |   8.8 /  12.2 |  13.8 /  16.5 |  8193 /  1024 | Sep / Sep |
| Lhasa (Tibet) | -20.2 /  -8.4 |  -3.1 /   9.7 |  1226 /   661 | Sep / Jul |
| Baghdad (Mesopotamia) |   8.4 /   9.7 |  22.9 /  37.6 |  1059 /   358 | Apr / Feb |
| Jericho (Levant) |   8.0 /   8.5 |  20.0 /  27.2 |  1446 /   207 | Feb / Jan |
| Niger bend (Sahel) |  18.5 /  22.6 |  25.1 /  31.5 |     4 /   316 | Dec / Aug |
| Cape Town |  22.8 /  19.6 |  17.6 /  13.8 |  1178 /   496 | Jul / Jul |
| Buenos Aires |  23.2 /  23.7 |  14.1 /   9.0 |    81 /  1129 | Feb / Mar |
