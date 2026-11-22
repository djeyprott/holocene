# Holocene — Design Document

A human history simulator that starts around 10,000 BCE on a real Earth (with
generated worlds later), in which climate, ecology, demography, culture,
language, religion, technology, organizations and individual historical figures
are simulated together, and whose output can be explored afterwards in a
browser "Legends" viewer in the spirit of Dwarf Fortress.

Status: living document. Section 14 is the roadmap and shows what is built.

---

## 1. Principles

1. **Mechanism over magic numbers.** Every quantity in the simulation is one of:
   - a physical constant (solar constant, latent heat of vaporization …);
   - measured Earth data (elevation, present-day climate used for validation,
     ice-core CO2, sea-level curve, wild-progenitor ranges …);
   - an empirical relationship with a published source (OLR–temperature
     regression, Miami NPP model, model life tables, glottochronological
     replacement rates, Binford's forager densities …);
   - an emergent result of the simulation.
   Anything else is a **tunable**, and every tunable lives in the parameter
   registry (`data/parameters.json`) with a unit, a justification and, where
   possible, a source. Code never contains an unexplained literal. Tunables are
   reviewed when validation fails, rather than patched locally.
2. **Earth is the calibration target, not the script.** On Earth the physical
   world is validated against observations (present-day climate, Holocene
   climate events, plausible population densities). Human history is *not*
   forced to repeat real history; it only has to stay within plausible ranges.
   Generated worlds reuse exactly the same models, which is why the climate is
   validated on Earth first.
3. **Determinism.** A run is a pure function of (world, seed, parameters,
   version). All randomness comes from counter-based RNG streams keyed by
   (seed, subsystem, entity id, time), so parallel execution, save/load and
   re-runs give identical histories.
4. **Level of detail.** Populations are simulated as aggregated cohorts;
   individuals are simulated only when they matter (rulers, office holders,
   prophets, generals, merchants, rebels, their families and rivals). A person
   is promoted out of the cohort when they acquire a role, a notable deed or a
   relationship to an existing figure.
5. **Everything that happens is an event.** The simulation writes typed events
   with participants, place, time and *causes* (links to earlier events or
   states). The history viewer is built entirely from these events plus
   periodic snapshots, so "why did this happen?" can always be followed back.
6. **Separation of layers.** `Sim.Core` (pure simulation, no I/O) →
   `Sim.History` (event log, snapshots, export) → `Viewer` (static web app).
   Each physical/social subsystem is a module with explicit inputs/outputs so
   it can be tested and validated alone.

---

## 2. Time

| Process | Step |
|---|---|
| Orbital forcing, ice sheets, sea level, slow climate | recomputed every 10–50 years |
| Seasonal climate (mean state) | 24 half-month steps, solved to a periodic equilibrium when forcing changes |
| Interannual climate (ENSO, volcanoes, solar, AMOC) | yearly anomalies on top of the mean state |
| Demography, harvests, economy, culture, language | yearly, with seasonal sub-phases (sowing, harvest, campaign season) |
| Figures and organizations | event-driven agenda, month resolution |
| Battles, negotiations, coups | resolved as discrete episodes inside a month |

Calendar: astronomical year numbering internally (year −9999 = 10,000 BCE);
the viewer shows BCE/CE and, later, each culture's own calendar and eras.

---

## 3. World representation

### 3.1 Grid
An **icosahedral geodesic grid** (Voronoi cells around the vertices of a
subdivided icosahedron). Cells have near-equal area and 6 neighbours (12 have
5), there are no polar singularities, and the same grid works for any planet.
Resolution is a run setting: frequency 64 ⇒ 40,962 cells, ~112 km spacing,
~12,400 km² per cell. Climate can run on a coarser grid than society.

### 3.2 Terrain (Earth)
From ETOPO5 (5′ global relief, NOAA/NGDC). For every cell we store a
**hypsometric curve** (sorted elevation samples), not just a mean, so that
changing sea level floods or exposes the right fraction of a cell (Doggerland,
Beringia, Sundaland, the Persian Gulf, the Black Sea question). Derived:
mean/min/max elevation, ruggedness (std dev), slope, land fraction at any sea
level, coastal length.

### 3.3 Hydrology
Flow routing over the land graph (steepest descent with depression filling),
giving drainage basins, rivers with discharge from simulated runoff, lakes in
closed depressions (including **ice-dammed lakes** such as Agassiz, whose
drainage is a climate event), floodplains, deltas. Rivers change with climate.

### 3.4 Soils, vegetation and resources
- Vegetation/biome from the simulated climate (Holdridge-style life zones plus
  growing-degree days and water balance), with feedback to albedo and
  evapotranspiration (needed for a Green Sahara and its collapse).
- Net primary productivity: Lieth's Miami model (temperature and
  precipitation limited), later refined with a water-balance model.
- Soil fertility from climate, slope, floodplain alluvium, loess (wind-blown
  silt downwind of former ice margins and deserts — emergent from glacial
  history), volcanic parent material, and depletion by use.
- Fauna: game biomass from NPP and biome; megafauna that can be hunted out.
- Minerals: on Earth, a curated deposit table (copper, tin, iron, gold, silver,
  salt, obsidian, flint, coal …) with approximate locations and grades; on
  generated worlds, from the plate-tectonic generator.
- Wild species with domestication potential (wheat, barley, rice, millet,
  maize/teosinte, squash, potato, sorghum, aurochs, wild goat/sheep, boar,
  horse, camel, llama, chicken …) with climatic niches; on Earth starting from
  their known wild ranges.

---

## 4. Climate

The climate must (a) reproduce present-day climate when given present-day
forcing, (b) respond to forcing changes with the right sign and magnitude,
(c) vary naturally on all time scales, and (d) work on any planet.

### 4.1 Forcings
- **Orbital (Milankovitch)**: eccentricity, obliquity, precession from Berger
  (1978); daily insolation by latitude and day. Exact and cyclic.
- **Greenhouse gases**: Earth mode uses the ice-core CO2/CH4 record as
  forcing (ΔF = 5.35 ln(C/C₀), Myhre et al. 1998). A carbon-cycle model is a
  later option.
- **Volcanic**: stochastic eruptions with the magnitude–frequency statistics
  of ice-core sulfate records; aerosol forcing decaying over 1–3 years.
  Eruptions are also historical events (ash, local destruction).
- **Solar**: quasi-periodic variability with the spectral peaks seen in ¹⁴C/¹⁰Be
  (≈11, 88, 208 years) and stochastic grand minima.
- **Ice sheets and sea level**: ice-sheet model driven by the climate
  (positive-degree-day mass balance + shallow-ice flow); sea level from ice
  volume. Until that is validated, Earth mode uses the empirical sea-level curve.

### 4.2 Mean seasonal climate (the physical core)
Per cell, 24 half-month steps, solved implicitly to a periodic equilibrium:
- **Moist energy balance model**: C ∂T/∂t = S(1−α) − OLR + ∇·(D∇h) − u·∇(c T)
  + ocean heat transport; h = cₚT + Lq is moist static energy (Hwang &
  Frierson 2010), which gives realistic poleward heat transport and polar
  amplification. OLR = A + B·T (Budyko form, satellite-regressed), with A
  shifted by greenhouse forcing and clouds.
- **Heat capacity**: land ≈ atmospheric column + active soil layer; ocean =
  mixed layer. This alone produces continentality (big seasonal swings inland).
- **Surface pressure and winds**: zonal circulation with ITCZ at the thermal
  equator, Hadley cell edge from Held–Hou scaling of the simulated
  equator-to-pole temperature contrast, subpolar lows, polar highs; plus
  thermal lows/highs from land–sea temperature contrast (hydrostatic,
  ≈0.6 hPa/K for a 1.5 km boundary layer). Winds are geostrophic with
  frictional cross-isobar flow, becoming down-gradient near the equator.
  Monsoons emerge from seasonal land heating.
- **Ocean surface currents**: wind-driven barotropic circulation from the
  Stommel equation (β-effect + bottom friction) on the real coastline; this
  produces western boundary currents (Gulf Stream, Kuroshio) and cold eastern
  boundary currents without being painted in. Ocean heat is advected by them.
  Later: a thermohaline (AMOC) box model per basin driven by surface density
  (temperature, salinity from E−P, runoff, meltwater).
- **Water cycle**: column water vapour advected by the winds; evaporation by
  bulk formula (ocean) or limited by soil moisture (land); precipitation from
  moisture convergence (ITCZ, monsoons), upslope flow (orography, rain shadows),
  baroclinic eddies (storm tracks) and saturation; suppressed under
  subsidence (subtropical deserts, cold eastern-ocean coasts).
- **Snow, sea ice, albedo**: snowpack and sea ice with albedo feedback;
  vegetation albedo from biome; cloud albedo from cloud fraction.
- **Elevation**: lapse rate 6.5 K/km; orographic precipitation.

### 4.3 Variability (makes history)
- Interannual modes with spatial patterns obtained by perturbing the model
  itself (linear response functions), not by painting regions:
  ENSO-like recharge oscillator (Jin 1997), North-Atlantic-type oscillation,
  volcanic cooling, solar variation.
- Abrupt events emerge: meltwater from draining ice-dammed lakes weakening
  the overturning circulation (8.2 ka-type cold event); vegetation–albedo
  feedback collapsing a humid Sahara as precession weakens the monsoon;
  multi-decadal droughts from stacked negative anomalies.
- Each year the climate layer outputs per-cell monthly temperature,
  precipitation, growing-season length, frost days, drought index and storm
  risk, which ecology and agriculture consume.

### 4.4 Validation targets
Present-day forcing vs ERA-Interim 2 m temperature (monthly) and GPCP
precipitation climatology 1979–2010: area-weighted RMSE and bias by month,
zonal means, seasonal amplitude, and named checks (Sahara/Atacama dry,
Amazon/Congo/Indonesia wet, India monsoon seasonality, West Europe vs
Labrador temperature, Siberian winter cold). Paleo: early-Holocene northern
summer warming, monsoon strengthening at 9–6 ka.

---

## 5. Ecology and subsistence

- **Foraging**: optimal-foraging diet breadth model. Resources (plants, game,
  fish, shellfish) have energy returns per hour by biome and season; a band
  takes resources in order of return until the marginal return falls below the
  average. Population density emerges and is checked against Binford's (2001)
  hunter-gatherer dataset. Overhunting depletes game; megafauna can be lost.
- **Domestication**: cultivation intensity of wild progenitors rises with
  population pressure, sedentism (storage, aquatic resources) and climate
  stability; domestication traits accumulate over generations of selection
  (centuries to millennia, consistent with archaeobotany). There is no
  "unlock agriculture" switch.
- **Agriculture**: crop yields from growing-degree days, water balance,
  frost, soil fertility, technique (fallow, manure, irrigation, terraces,
  plough) and labour; harvest variability comes from the yearly climate.
  Pastoralism: herd dynamics on rangeland NPP. Fishing: coastal and riverine
  productivity.
- **Storage and famine**: stores carry over between years and spoil; famine
  feeds back into mortality, fertility, migration, unrest and religion.

---

## 6. Demography

- The basic unit is a **community** (band, village, town quarter, nomadic
  camp): an age–sex pyramid in 5-year bins, plus culture, language, religion,
  social strata and an economic profile. Cities are clusters of communities.
- Fertility depends on energy balance, birth spacing (lactation norms),
  marriage age and patterns (from culture), contraception/infanticide norms.
- Mortality from model life tables (Coale–Demeny families), shifted by
  nutrition, disease load, violence and age-specific hazards.
- **Disease**: endemic burden by climate (malaria where temperature and water
  allow); crowd diseases need a connected population above a persistence
  threshold (emergent with cities and trade); zoonoses arise from domestic
  animal contact; epidemics spread along contact networks with SIR dynamics,
  and are far deadlier in populations without prior exposure.
- **Migration**: households compare expected welfare (food per capita, safety,
  land, kin networks, religious and ethnic welcome) at known destinations
  against the cost and risk of moving. Colonization, chain migration, refugee
  flows and invasions all come from the same rule plus organizational orders.
- **Social structure**: strata (free/unfree, elite/commoner, castes, guild
  members) emerge from economic roles and institutions, each with its own
  consumption and fertility.

---

## 7. Identity layer: culture, language, religion

### 7.1 Culture
A culture is a vector of traits modelled on the Ethnographic Atlas
(Murdock) variables: subsistence practices, kinship and descent, post-marital
residence, marriage form, inheritance, settlement pattern, stratification,
political integration, gender division of labour, norms on violence, honour,
hospitality, trust, plus aesthetic traits (dress, ornament, architecture
style) used for flavour. Traits are linked to their material causes where the
literature supports it (e.g. plough agriculture ↔ patrilocality, pastoralism ↔
honour cultures, bridewealth with polygyny). Change: vertical/oblique/
horizontal transmission, conformist and prestige bias, drift inversely
proportional to population size, adaptation to economy. Cultures split when
communities diverge past a threshold and merge/assimilate under contact.

### 7.2 Language
Real generated languages, not labels:
- phoneme inventory and phonotactics; a lexicon over ~400 core meanings
  (Swadesh-200 and cultural vocabulary) plus derived words; morphology and
  word-order features (WALS-like).
- **Change**: regular sound changes drawn from attested types (lenition,
  palatalization, vowel shifts, loss of codas …) applied as rules, so cognates
  stay recognisable; lexical replacement with per-meaning rates (Pagel et al.
  2007: frequent words change slowly); grammaticalisation.
- **Contact**: loanwords adapted to the borrower's phonology, more borrowing
  in prestige and trade vocabulary; language shift when a community's
  speakers adopt another language; creoles and pidgins at trade hubs.
- **Splits**: a dialect becomes a language when mutual intelligibility
  (measured lexical + phonetic distance) drops below threshold, so language
  families with real trees emerge.
- **Naming**: places, people, organizations, artifacts and events are named
  in their own languages from meaningful roots ("Red Ford", "Son of the
  Wolf"), with translations shown in the viewer, as in Dwarf Fortress.
- **Writing**: invented under administrative need (accounting, tribute),
  progressing from tallies/proto-writing to logographic, syllabic and
  alphabetic systems by adaptation and borrowing; literacy is a population
  share that affects knowledge retention.

### 7.3 Religion
A religion is a set of beliefs and practices: cosmology (animist, ancestral,
polytheist, henotheist, monotheist, philosophical), deities with spheres that
reflect the environment and society that produced them (storm gods where
storms matter, fertility gods among farmers), moralising high gods more likely
with large anonymous societies (as the Seshat data suggest), afterlife
beliefs, rituals, taboos (food, sexual, purity), sacred places, and
institutions (shamans, priesthoods, temples, monasteries, churches as
organizations). Change: syncretism under contact, reforms by figures,
prophetic movements that arise more often under crisis and are led by
charismatic figures, conversion through social networks, state sponsorship,
missionaries and conquest; schisms when doctrinal or political divides grow.

---

## 8. Technology

- Knowledge items form a **directed graph**: each needs prerequisite
  knowledge, *materials present in the environment* (no bronze without copper
  and tin/arsenic, no iron smelting without ore and fuel), and social
  preconditions (specialists, surplus, population scale).
- Discovery is a stochastic process whose rate grows with the number of
  people doing related work, the stock of related knowledge (recombination)
  and connectivity; knowledge can also be **lost** when the population holding
  it is too small or the specialists die (Henrich's Tasmania effect).
- Each item changes a mechanism rather than a bonus number: crop yields,
  labour needs, storage losses, transport cost per tonne-km, ship range,
  fortification strength, weapon effectiveness, information speed, record
  keeping, mortality from disease.
- Diffusion through contact, trade, migration, captured or hired artisans,
  and texts. Paths differ between worlds and regions: pottery before or after
  farming, metallurgy that skips bronze, wheels that never get used where
  there are no draught animals.

---

## 9. Economy

- Households allocate labour between food, crafts, building, military service
  and leisure. Surplus supports specialists: craftsmen, priests, soldiers,
  scribes, rulers.
- Settlements have markets; prices come from supply and demand; trade
  between markets moves goods when price differences exceed transport costs
  (terrain, rivers, sea routes, roads, pack animals, ships, security).
- Money emerges as the most traded durable good, then coinage by
  organizations. Credit and debt between figures and organizations; banks
  appear as organizations that take deposits and lend.
- Tribute and taxation by organizations; corvée labour; slavery as an economic
  and military institution where cultures allow it.

---

## 10. Organizations

One generic **Organization** entity covers bands, clans, tribes, chiefdoms,
states, empires, temples and churches, guilds, merchant houses, banks, gangs,
bandit groups, mercenary companies, secret societies and philosophical
schools. What kind of organization it is follows from its *structure*:
- membership (figures and/or communities) and how one joins or leaves;
- resources (stores, treasury, land claims, armed followers, buildings);
- **offices** with powers (command troops, tax, judge, appoint, declare war,
  make treaties) and **succession rules** (election, primogeniture, seniority,
  appointment, acclamation, tanistry);
- rules and institutions (councils, laws, customs) that can be changed;
- goals and a territory if it has one (a state controls cells; a guild
  controls a trade in a market; a bank has none).
A "state" is an organization that claims a territory, taxes it and holds
coercive force there; this is detected, not declared. Organizations can be
members of other organizations (vassals, confederations, a guild inside a
city).

### 10.1 Internal politics
Power inside an organization comes from offices, wealth, armed followers,
patronage networks, kin, and **legitimacy** — how members judge the holder
according to their own culture and religion. Figures form **factions**
(coalitions with shared aims). Following selectorate theory (Bueno de
Mesquita et al.), rulers must reward a winning coalition; when they cannot,
coalition members defect. Actions: lobbying, bribery, marriage alliances,
purges, reforms, coups, assassinations, civil war, secession, succession
crises. **Outside organizations can intervene**: funding a faction, offering
refuge, marrying into it, sending troops, recognising a pretender.

---

## 11. Historical figures

- **Attributes**: Big-Five personality (with heritable component), intellect,
  health and constitution, appearance; skills learned by practice; values
  from their culture and religion with personal deviation.
- **Relationships**: per-pair opinion, trust, fear, obligations (favours,
  debts), kinship, marriage, rivalry, mentorship, and memories of what the
  other did. Information is local: a figure only knows what they witnessed or
  were told, with delay and distortion.
- **Goals and plans**: needs and life-stage goals (survive, marry, provide,
  gain status, protect kin, piety, revenge, wealth, power, knowledge, glory),
  weighted by personality and culture. Goals decompose into plans (HTN
  planning) whose actions are scored by expected utility under the figure's
  beliefs, with bounded rationality and personality-shaped risk attitudes.
- **Life course**: birth, upbringing, apprenticeship, marriage, children,
  career, illness, injury, old age, death — all from the demographic and
  disease models, so famous figures can die young.
- **Deeds** become events and reputation: battles, works built, books
  written, crimes, heresies, discoveries, betrayals.

---

## 12. Diplomacy and war

- **Between figures**: alliances, marriages, patronage, feuds, vendettas,
  oaths, betrayals, ransoms, hostage exchange, gift exchange creating obligation.
- **Between organizations**: trade agreements, tribute, alliances,
  vassalage, marriage alliances, recognition, border agreements, peace
  treaties. Decisions use the bargaining model of war (Fearon 1995): war
  happens when sides disagree about relative strength (private information,
  incentives to bluff) or cannot trust commitments (shifting power, indivisible
  stakes). Reputation for keeping agreements is tracked.
- **Negotiation** as offers and counter-offers by the responsible figures,
  coloured by their personalities and relationships.
- **War**: armies raised from communities (levies, retainers, mercenaries,
  professionals), supplied through logistics limited by terrain, season and
  transport technology; campaigns, raids, sieges and battles (Lanchester-type
  attrition modified by terrain, technology, morale, leadership and surprise);
  casualties, captives, plunder, devastation and disease all feed back into
  demography and the economy.

---

## 13. History recording and the Legends viewer

- **Event log**: append-only, typed events (birth, death, marriage, founding,
  office change, battle, treaty, discovery, schism, migration, famine,
  eruption, flood …) with time, place, participants, and cause links.
- **Snapshots**: every N years, compact snapshots of map layers (climate,
  biomes, populations, cultures, languages, religions, borders) and entity
  states.
- **Export**: chunked JSON (or a compact binary + JSON index) readable by a
  static web app, so a run can be browsed offline or shared.
- **Viewer** (browser):
  - Encyclopedia pages for every figure, organization, settlement, culture,
    language (with sound changes and word lists), religion, war, technology,
    era and region; every name links to its page; generated prose summaries
    in the style of Dwarf Fortress legends.
  - Map with time slider: terrain, climate, biomes, population density,
    cultures, languages, religions, political control, trade routes, wars.
  - Timelines, family trees, organization office histories, language trees,
    religion trees, causality chains ("why did this war start?").
  - Search and filters.

---

## 14. Roadmap

Each phase ends with validation and a usable build.

| Phase | Content | Status |
|---|---|---|
| 0 | Solution, deterministic RNG, parameter registry, geodesic grid, Earth terrain import, test harness | done |
| 1 | Climate: orbital forcing, moist EBM, winds, ocean gyres, water cycle, snow/sea ice, validation vs ERA-Interim/GPCP; hydrology; biomes/NPP; 10,000 BCE sea level | done (v1; see validation) |
| 1b | Climate fixes (Ekman transport/upwelling, monsoon and subsidence, inversions); ice sheets and ice-dammed lakes; AMOC box model; interannual variability (ENSO, volcanic, solar); paleo validation | next |
| 2 | Foragers: communities, demography, foraging ecology, migration; event log; Legends viewer v1 | planned |
| 3 | Culture, language (with sound change and naming), religion | planned |
| 4 | Domestication, agriculture, pastoralism, sedentism, disease | planned |
| 5 | Figures and organizations (bands → chiefdoms), internal politics | planned |
| 6 | Economy: markets, trade, specialists, money, guilds, banks | planned |
| 7 | States, diplomacy, warfare | planned |
| 8 | Full technology graph, writing, knowledge loss/diffusion | planned |
| 9 | Full Legends viewer, performance, generated worlds (tectonics → terrain) | planned |

## 15. Data sources used so far
- ETOPO5 5-minute global relief (NOAA NGDC), via NCAR geocat-datafiles.
- ERA-Interim monthly 2 m temperature, 2005 (ECMWF), via NCAR geocat-datafiles — validation only.
- GPCP v2.2 monthly precipitation 1979–2010 — validation only.
- Berger (1978) orbital solution.
- Sea-level and CO2 histories (Lambeck et al. 2014; EPICA Dome C, Monnin et al. 2001/2004) — Earth-mode forcing.

## 16. Development log — climate model v1

The model went through these formulation changes, each driven by a validation failure (monthly T RMSE vs ERA-Interim):

1. Single-temperature EBM → runaway NH sea ice: cold continental air pinned SSTs (advective coupling ~250 W/m²/K).
   **Fix:** two layers (air, surface) coupled by bulk sensible heat and longwave exchange.
2. No extratropical rain: diffusing actual vapour gave rain only in convergence zones.
   **Fix:** eddy vapour flux down the gradient of temperature-determined humidity (Siler, Roe & Armour 2018).
3. Evaporation independent of air temperature; dry deserts treated as moist in MSE diffusion.
   **Fix:** E = ρC_E|U|(q_s(Ts) − RH·q_s(Ta)); soil-wetness-limited humidity in the moist enhancement over land.
4. Glaciated Eurasia: snow–albedo feedback has multiple equilibria. **Fix:** warm ice-free initial state (selects the
   interglacial branch, as on Earth today) and vegetation with Brovkin et al. response times.
5. Mountains as moisture sinks: frictional inflow carried the whole vapour column; eddies drew vapour uphill.
   **Fix:** only boundary-layer vapour moves with the cross-isobar wind; flow into higher terrain passes only the vapour
   above the height step (blocked vapour partly rains on the windward side); eddy exchange compares vapour above the
   higher surface.
6. Cloud LW anomaly with too little cloud cooled the planet ~6 °C. **Fix:** separate ocean/land non-precipitating cover;
   cloud albedo calibrated to planetary albedo 0.29.
7. Deserts received too little surface sunshine. **Fix:** energy-conserving single-layer shortwave with water-vapour
   absorption (Lacis & Hansen 1974) instead of a fixed 33 % atmospheric share.

Result: monthly T RMSE 13.3 → 5.4 °C; monthly P RMSE 8.0 → 5.9 mm/day; global energy budget closed.
