using Holocene.Core.Grid;
using Holocene.Core.Mathx;
using Holocene.Core.Parameters;
using Holocene.Core.World;

namespace Holocene.Core.Climate;

/// <summary>External drivers of the climate for one epoch.</summary>
public sealed record ClimateForcing(double YearCE, double Co2Ppm, double SeaLevel, double SolarConstant)
{
    public OrbitalElements Orbit { get; init; } = Orbital.ForYear(YearCE);
}

/// <summary>
/// Seasonal climate model (design §4.2). One prognostic temperature per cell (sea-level-equivalent), solved with
/// 24 implicit half-month steps per year:
///   C ∂T/∂t = S(1−α) − OLR + ∇·(p/g D ∇h) − C_air u_rot·∇T + ocean gyre/eddy heat transport,
/// with moist static energy h = c_pT + L·RH·q_sat (Hwang &amp; Frierson 2010). Surface winds come from a
/// sea-level-pressure field (zonal three-cell structure + hydrostatic thermal highs/lows) through a
/// frictional Ekman balance. Ocean surface currents are the Stommel solution for the simulated wind stress on the
/// real coastline. The water cycle advects column vapour with the full surface wind; precipitation follows the
/// observed column-relative-humidity law of Bretherton et al. (2004) plus upslope orographic lifting.
/// Snow, soil moisture (Manabe bucket), sea ice and vegetation feed back on albedo.
/// </summary>
public sealed class ClimateModel
{
    public readonly GeodesicGrid G;
    public readonly Terrain Terrain;
    private readonly ParameterRegistry P;
    public readonly int N;
    public readonly int Steps;

    // ---- surface description (depends on sea level) ----
    public readonly bool[] IsLand;
    public readonly double[] LandFrac;
    public readonly double[] Elev;          // m, mean elevation of the land part (0 for ocean)

    // ---- prognostic state ----
    public readonly double[] Ta;            // °C, near-surface air temperature, sea-level equivalent (θ-like)
    public readonly double[] Ts;            // °C, surface temperature, sea-level equivalent (land skin, ocean mixed layer, or ice surface)
    public readonly double[] SeaIce;        // m thickness
    public readonly double[] Snow;          // kg/m² water equivalent
    public readonly double[] Soil;          // kg/m²
    public readonly double[] W;             // kg/m² column water vapour
    public readonly double[] Cloud;         // cloud cover fraction
    public readonly double[] Veg;           // vegetation cover 0..1
    public readonly double[] Forest;        // forest fraction 0..1
    /// <summary>Near-surface relative humidity used for moist static energy and eddy vapour transport:
    /// the oceanic value over sea, reduced by soil wetness over land (dry desert air carries little latent energy).</summary>
    public readonly double[] RhEff;

    // ---- per-step diagnostics ----
    /// <summary>Solar radiation absorbed by the surface and by the atmosphere (W/m²).</summary>
    public readonly double[] SwSurface, SwAtmos;
    public readonly double[] Insol, Albedo, Pressure, Precip, Evap, PotEvap, Runoff, Olr;
    public readonly double[] AnnualCloud;
    /// <summary>Annual-mean energy budget terms per cell (W/m²): absorbed SW, OLR, surface net SW, net LW, sensible, latent, albedo, Ts, Ta.</summary>
    public readonly double[][] Budget = new double[9][];
    private readonly double[] _lwNet, _sens;
    public double AnnualPlanetaryAlbedo, AnnualAbsorbed, AnnualOlr;
    private double _accIns, _accAbs, _accOlr;
    public readonly Vec3[] Wind, WindRot;
    public readonly double[] OceanTransportOut; // per edge entry, m³/s out of the cell (gyres)
    public readonly double[] Psi;              // m³/s streamfunction (diagnostic)
    public double ItczLat;

    // ---- climatology of the last completed year ----
    public readonly double[][] MonthT;       // [12][N] °C at actual surface elevation
    public readonly double[][] MonthP;       // [12][N] mm/day
    public readonly double[][] MonthSnow;    // [12][N] kg/m²
    public readonly double[][] MonthSoil;    // [12][N] kg/m²
    public readonly double[][] MonthPET;     // [12][N] mm/day potential evaporation
    public readonly double[] AnnualRunoff;   // mm/yr
    public readonly double[] SeaIceMax, SeaIceMin;
    public readonly Vec3[] AnnualWind;
    public readonly double[] AnnualPressure;

    // ---- accumulators ----
    private readonly Vec3[] _tauAcc;
    private readonly double[] _accT, _accP, _accSnow, _accSoil, _accPET, _accRunoff, _accPres, _iceMax, _iceMin;
    private readonly Vec3[] _accWind;
    private readonly double[] _yearP, _yearPET, _warmest;

    // insolation table [step][lat index at 0.25°]
    private double[,] _insolTab = new double[0, 0], _muTab = new double[0, 0];

    // physical parameters cached
    private readonly double cAir, cOcean, cLand, cIce, B, Aolr, lapse, D, Lv, Lf, cp, rhoI, kIce, tFreeze, rhoA, ce, rhOcean, rhLand, hq, sigma;
    private readonly double omega, radius, dt, advFactor, oceanK, gyreEff;
    private ClimateForcing _forcing;
    private readonly GridSystem _sys;

    public ClimateModel(GeodesicGrid grid, Terrain terrain, ParameterRegistry p, ClimateForcing forcing)
    {
        G = grid; Terrain = terrain; P = p; N = grid.Count; _forcing = forcing;
        Steps = (int)p["numerics.steps_per_year"];
        dt = 365.0 * 86400.0 / Steps;
        IsLand = new bool[N]; LandFrac = new double[N]; Elev = new double[N];
        Ta = new double[N]; Ts = new double[N]; SeaIce = new double[N]; Snow = new double[N]; Soil = new double[N]; W = new double[N];
        Cloud = new double[N]; Veg = new double[N]; Forest = new double[N]; RhEff = new double[N];
        Insol = new double[N]; Albedo = new double[N]; SwSurface = new double[N]; SwAtmos = new double[N]; Pressure = new double[N]; Precip = new double[N];
        Evap = new double[N]; PotEvap = new double[N]; Runoff = new double[N]; Olr = new double[N]; AnnualCloud = new double[N];
        for (int b = 0; b < Budget.Length; b++) Budget[b] = new double[N];
        _lwNet = new double[N]; _sens = new double[N];
        Wind = new Vec3[N]; WindRot = new Vec3[N];
        OceanTransportOut = new double[grid.Nbr.Length]; Psi = new double[N];
        MonthT = Alloc12(); MonthP = Alloc12(); MonthSnow = Alloc12(); MonthSoil = Alloc12(); MonthPET = Alloc12();
        AnnualRunoff = new double[N]; SeaIceMax = new double[N]; SeaIceMin = new double[N];
        AnnualWind = new Vec3[N]; AnnualPressure = new double[N];
        _tauAcc = new Vec3[N]; _accT = new double[N]; _accP = new double[N]; _accSnow = new double[N]; _accSoil = new double[N];
        _accPET = new double[N]; _accRunoff = new double[N]; _accPres = new double[N]; _iceMax = new double[N]; _iceMin = new double[N];
        _accWind = new Vec3[N]; _yearP = new double[N]; _yearPET = new double[N]; _warmest = new double[N];
        _sys = new GridSystem(grid);

        double g = p["planet.gravity"];
        cp = p["thermo.cp_air"]; Lv = p["thermo.latent_vap"]; Lf = p["thermo.latent_fus"];
        cAir = p["planet.surface_pressure"] / g * cp;
        cOcean = p["thermo.rho_water"] * p["thermo.cp_water"] * p["transport.ocean_mixed_layer"];
        cLand = p["transport.land_heat_capacity"]; cIce = p["transport.ice_surface_heat_capacity"];
        sigma = p["radiation.stefan_boltzmann"];
        B = p["radiation.olr_b"]; Aolr = p["radiation.olr_a"]; lapse = p["thermo.lapse_rate"];
        D = p["transport.diffusivity"];
        rhoI = p["thermo.rho_ice"]; kIce = p["thermo.k_ice"]; tFreeze = p["thermo.t_freeze_sea"];
        rhoA = p["thermo.rho_air"]; ce = p["hydro.exchange_coef"]; rhOcean = p["hydro.rh_surface_ocean"]; rhLand = p["hydro.rh_surface_land"];
        hq = p["hydro.vapour_scale_height"];
        omega = p["planet.omega"]; radius = grid.Radius;
        advFactor = p["transport.advection_factor"]; oceanK = p["transport.ocean_diffusivity"]; gyreEff = p["transport.gyre_heat_efficiency"];

        SetForcing(forcing);
        // Initial state: a warm, ice-free and fully vegetated planet. Snow/ice–albedo feedback gives the climate
        // multiple equilibria, so the starting point selects a branch; starting warm and letting ice and deserts
        // form selects the interglacial branch of today. (Glacial states come from the ice-sheet history instead.)
        for (int i = 0; i < N; i++)
        {
            double s = Math.Sin(G.Lat[i]);
            Ta[i] = Ts[i] = 30 - 30 * s * s;
            Soil[i] = IsLand[i] ? 0.5 * p["hydro.bucket_capacity"] : 0;
            Cloud[i] = p["cloud.cover_mean"];
            Veg[i] = IsLand[i] ? 1 : 0; Forest[i] = IsLand[i] ? 1 : 0;
            W[i] = 20;
        }
    }

    private double[][] Alloc12() { var a = new double[12][]; for (int m = 0; m < 12; m++) a[m] = new double[N]; return a; }

    /// <summary>Changes forcing (orbit, CO2, sea level). Recomputes the land mask and insolation table.</summary>
    public void SetForcing(ClimateForcing f)
    {
        _forcing = f;
        for (int i = 0; i < N; i++)
        {
            LandFrac[i] = Terrain.FractionAbove(i, f.SeaLevel);
            bool land = LandFrac[i] >= 0.5;
            if (land && !IsLand[i]) { SeaIce[i] = 0; Soil[i] = 75; }
            if (!land && IsLand[i]) { Snow[i] = 0; Soil[i] = 0; }
            IsLand[i] = land;
            Elev[i] = land ? Math.Max(0, Terrain.MeanAbove(i, f.SeaLevel) - f.SeaLevel) : 0;
        }
        BuildInsolation();
    }

    private void BuildInsolation()
    {
        const int nLat = 721, sub = 15;
        _insolTab = new double[Steps, nLat]; _muTab = new double[Steps, nLat];
        var o = _forcing.Orbit;
        for (int s = 0; s < Steps; s++)
            for (int l = 0; l < nLat; l++)
            {
                double lat = (-90 + 0.25 * l) * Math.PI / 180;
                double q = 0, qm = 0;
                for (int k = 0; k < sub; k++)
                {
                    double day = 1 + (s + (k + 0.5) / sub) * Orbital.DaysPerYear / Steps;
                    var (dec, fac) = Orbital.Declination(o, day);
                    double ins = Orbital.DailyInsolation(_forcing.SolarConstant, lat, dec, fac);
                    q += ins; qm += ins * Orbital.MeanCosZenith(lat, dec);
                }
                _insolTab[s, l] = q / sub; _muTab[s, l] = q > 0 ? qm / q : 0;
            }
    }

    private int LatIndex(int i) => (int)Math.Round((G.LatDeg(i) + 90) * 4);

    /// <summary>Near-surface air temperature at the cell's actual elevation (°C).</summary>
    public double SurfaceT(int i) => Ta[i] - lapse * Elev[i];
    /// <summary>Surface (skin/SST/ice) temperature at the cell's actual elevation (°C).</summary>
    public double SkinT(int i) => Ts[i] - lapse * Elev[i];

    // =====================================================================================================
    // Driver
    // =====================================================================================================

    /// <summary>Runs whole years; the last year's statistics end up in the Month*/Annual* arrays.</summary>
    public void RunYears(int years, Action<int>? progress = null)
    {
        for (int y = 0; y < years; y++)
        {
            ResetAccumulators();
            for (int s = 0; s < Steps; s++) Step(s);
            FinishYear();
            progress?.Invoke(y);
        }
    }

    private void ResetAccumulators()
    {
        Array.Clear(_tauAcc); Array.Clear(_accRunoff); Array.Clear(_accPres); Array.Clear(_accWind);
        Array.Clear(_yearP); Array.Clear(_yearPET); Array.Clear(AnnualCloud);
        foreach (var b in Budget) Array.Clear(b);
        _accIns = _accAbs = _accOlr = 0;
        for (int i = 0; i < N; i++) { _iceMax[i] = 0; _iceMin[i] = double.MaxValue; _warmest[i] = double.MinValue; }
    }

    /// <summary>Wall-clock profile per component and solver iteration counts (diagnostics).</summary>
    public readonly Dictionary<string, double> Profile = new();
    public int LastTempIters, LastMoistIters, LastNewton;

    private void Timed(string name, Action a)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); a();
        Profile[name] = Profile.GetValueOrDefault(name) + sw.Elapsed.TotalSeconds;
    }

    /// <summary>Debug hook called after each component with its name.</summary>
    public Action<string>? AfterComponent;

    public void Step(int s)
    {
        Timed("radiation", () => ComputeInsolationAndAlbedo(s)); AfterComponent?.Invoke("radiation");
        Timed("winds", ComputeWinds); AfterComponent?.Invoke("winds");
        Timed("temperature", SolveEnergy); AfterComponent?.Invoke("temperature");
        UpdateSeaIce(); AfterComponent?.Invoke("seaice");
        Timed("moisture", SolveMoisture); AfterComponent?.Invoke("moisture");
        UpdateSurfaceWater();
        UpdateClouds(); AfterComponent?.Invoke("surface");
        Timed("accumulate", () => Accumulate(s));
    }

    // =====================================================================================================
    // Radiation
    // =====================================================================================================

    private void ComputeInsolationAndAlbedo(int s)
    {
        double aDesert = P["radiation.albedo_desert"], aForest = P["radiation.albedo_forest"], aSnow = P["radiation.albedo_snow"];
        double aSnowForest = P["radiation.albedo_snow_forest"], aGlacier = P["radiation.albedo_glacier"], aSeaIce = P["radiation.albedo_sea_ice"];
        double aCloud = P["radiation.cloud_albedo"], aAtm = P["radiation.clear_sky_albedo_atm"];
        double aOzone = P["radiation.ozone_absorption"], aCld = P["radiation.cloud_absorption"], path = P["radiation.vapour_path_factor"];
        double snowHalf = P["hydro.snow_cover_half"], glacier = P["hydro.glacier_swe"];
        Parallel.For(0, N, i =>
        {
            int l = LatIndex(i);
            Insol[i] = _insolTab[s, l];
            double aS;
            if (IsLand[i])
            {
                if (Snow[i] >= glacier) aS = aGlacier;
                else
                {
                    double aVeg = aDesert - (aDesert - aForest) * Veg[i];
                    double fs = Snow[i] / (Snow[i] + snowHalf);
                    double aSnowEff = aSnow * (1 - Forest[i]) + aSnowForest * Forest[i];
                    aS = aVeg + (aSnowEff - aVeg) * fs;
                }
            }
            else aS = SeaIce[i] > 0 ? aSeaIce : Thermo.OceanAlbedo(_muTab[s, l]);
            // Single-layer shortwave with multiple reflection between sky and surface (energy-conserving):
            // a column with reflectance R and absorptance A transmits T = 1 − R − A; then
            //   reflected to space = R + T²αs/(1 − αs R), absorbed in air = A + A·T·αs/(1 − αs R),
            //   absorbed at surface = T(1 − αs)/(1 − αs R).
            // Water-vapour absorption from the simulated column (Lacis & Hansen 1974), u in cm along the slant path.
            double u = W[i] / 10.0 * path;
            double aWv = 2.9 * u / (Math.Pow(1 + 141.5 * u, 0.635) + 5.925 * u);
            double refl = 0, atm = 0, sfc = 0;
            foreach (var (frac, R, Aabs) in new[] { (1 - Cloud[i], aAtm, aOzone + aWv), (Cloud[i], aCloud, aOzone + aWv + aCld) })
            {
                double T = Math.Max(0, 1 - R - Aabs), den = 1 - aS * R;
                refl += frac * (R + T * T * aS / den);
                atm += frac * (Aabs + Aabs * T * aS / den);
                sfc += frac * (T * (1 - aS) / den);
            }
            Albedo[i] = refl;
            SwAtmos[i] = Insol[i] * atm;
            SwSurface[i] = Insol[i] * sfc;
        });
    }

    // =====================================================================================================
    // Pressure and winds
    // =====================================================================================================

    private const int ZBins = 90; // 2° latitude bands

    private double[] ZonalMean(double[] v, Func<int, bool>? include = null)
    {
        var s = new double[ZBins]; var w = new double[ZBins];
        for (int i = 0; i < N; i++)
        {
            if (include != null && !include(i)) continue;
            int b = Math.Clamp((int)((G.LatDeg(i) + 90) / 2), 0, ZBins - 1);
            s[b] += v[i] * G.Area[i]; w[b] += G.Area[i];
        }
        for (int b = 0; b < ZBins; b++) s[b] = w[b] > 0 ? s[b] / w[b] : double.NaN;
        // fill empty bins from neighbours
        for (int b = 0; b < ZBins; b++) if (double.IsNaN(s[b])) s[b] = b > 0 ? s[b - 1] : 0;
        return s;
    }

    private static double Interp(double[] z, double latDeg)
    {
        double x = (latDeg + 90) / 2 - 0.5;
        int b = Math.Clamp((int)Math.Floor(x), 0, ZBins - 2);
        double f = Math.Clamp(x - b, 0, 1);
        return z[b] * (1 - f) + z[b + 1] * f;
    }

    /// <summary>Smooth interpolation through (lat, value) nodes sorted by latitude.</summary>
    private static double CosInterp((double lat, double v)[] nodes, double lat)
    {
        if (lat <= nodes[0].lat) return nodes[0].v;
        for (int k = 0; k < nodes.Length - 1; k++)
        {
            var (a, va) = nodes[k]; var (b, vb) = nodes[k + 1];
            if (lat <= b)
            {
                double f = (lat - a) / (b - a);
                double w = 0.5 - 0.5 * Math.Cos(Math.PI * f);
                return va + (vb - va) * w;
            }
        }
        return nodes[^1].v;
    }

    private void ComputeWinds()
    {
        double kTh = P["circulation.thermal_pressure_coef"];
        double ridge = P["circulation.ridge_lat"], shift = P["circulation.ridge_shift"], subpolar = P["circulation.subpolar_lat"];
        double pItcz = P["circulation.itcz_anom"], pRidge = P["circulation.ridge_anom"], pSub = P["circulation.subpolar_anom"], pPolar = P["circulation.polar_anom"];
        double tanO = Math.Tan(P["circulation.cross_isobar_ocean"] * Math.PI / 180), tanL = Math.Tan(P["circulation.cross_isobar_land"] * Math.PI / 180);
        double r0 = P["circulation.equatorial_friction"];

        var zm = ZonalMean(Ta);
        // Thermal equator: latitude of maximum zonal-mean temperature within the tropics (smoothed over ±6°).
        double best = double.MinValue; ItczLat = 0;
        for (int b = 0; b < ZBins; b++)
        {
            double lat = -90 + 2 * b + 1;
            if (Math.Abs(lat) > 25) continue;
            double v = 0; int n = 0;
            for (int d = -3; d <= 3; d++) { int bb = b + d; if (bb >= 0 && bb < ZBins) { v += zm[bb]; n++; } }
            v /= n;
            if (v > best) { best = v; ItczLat = lat; }
        }
        var nodes = new[]
        {
            (-90.0, pPolar), (-subpolar + shift * ItczLat * 0.5, pSub), (-ridge + shift * ItczLat, pRidge),
            (ItczLat, pItcz), (ridge + shift * ItczLat, pRidge), (subpolar + shift * ItczLat * 0.5, pSub), (90.0, pPolar),
        };
        var pth = new double[N];
        Parallel.For(0, N, i => pth[i] = -kTh * (Ta[i] - Interp(zm, G.LatDeg(i))));
        // Pressure is smoother than surface temperature: a few neighbour-averaging passes (≈ 2–3 cell radius).
        var tmp = new double[N];
        for (int pass = 0; pass < 3; pass++)
        {
            Parallel.For(0, N, i =>
            {
                double s = pth[i] * 2, w = 2;
                for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++) { s += pth[G.Nbr[k]]; w += 1; }
                tmp[i] = s / w;
            });
            Array.Copy(tmp, pth, N);
        }
        Parallel.For(0, N, i => Pressure[i] = 1013.0 + CosInterp(nodes, G.LatDeg(i)) + pth[i]);

        Parallel.For(0, N, i =>
        {
            // Green–Gauss gradient of pressure (Pa/m).
            var grad = new Vec3(0, 0, 0);
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
                grad = grad + G.EdgeDir[k] * (0.5 * (Pressure[G.Nbr[k]] - Pressure[i]) * 100.0 * G.EdgeLength[k]);
            grad = grad / G.Area[i];
            var acc = grad * (-1.0 / rhoA);                 // pressure-gradient acceleration
            double f = 2 * omega * Math.Sin(G.Lat[i]);
            double tan = IsLand[i] ? tanL : tanO;
            double r = Math.Sqrt(f * f * tan * tan + r0 * r0);
            var kx = Vec3.Cross(G.Center[i], acc);         // k × G
            double den = r * r + f * f;
            WindRot[i] = kx * (-f / den);
            Wind[i] = acc * (r / den) + WindRot[i];
        });
    }

    // =====================================================================================================
    // Ocean gyres (Stommel model), recomputed once a year from the mean wind stress
    // =====================================================================================================

    public void SolveOceanCurrents(Vec3[] tau)
    {
        double rhoW = P["thermo.rho_water"];
        double spacing = Math.Sqrt(G.Area.Average());
        double beta30 = 2 * omega * Math.Cos(Math.PI / 6) / radius;
        double rFric = beta30 * P["transport.stommel_width"] * spacing;
        _sys.Clear();
        var curl = new double[N];
        Parallel.For(0, N, i =>
        {
            if (IsLand[i]) return;
            double c = 0;
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
            {
                int j = G.Nbr[k];
                var tE = (tau[i] + tau[j]) * 0.5;
                var tangent = Vec3.Cross(G.Center[i], G.EdgeDir[k]); // counter-clockwise along the cell boundary
                c += Vec3.Dot(tE, tangent) * G.EdgeLength[k];
            }
            curl[i] = c / G.Area[i];
        });
        for (int i = 0; i < N; i++)
        {
            if (IsLand[i]) { _sys.Diag[i] = 1; _sys.Rhs[i] = 0; continue; }
            double beta = 2 * omega * Math.Cos(G.Lat[i]) / radius;
            // −r∇²ψ − βψ_x = −curlτ/ρ (signs flipped for a positive diagonal)
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
            {
                double a = rFric * G.EdgeLength[k] / G.EdgeDist[k] / G.Area[i];
                _sys.Diag[i] += a; _sys.Off[k] -= a;
            }
            // Upwind (eastern-side) derivative: Sverdrup information propagates westward from the eastern boundary.
            double sw2 = 0;
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++) { double w = Math.Max(0, Vec3.Dot(G.East[i], G.EdgeDir[k])); sw2 += w * w; }
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
            {
                double w = Math.Max(0, Vec3.Dot(G.East[i], G.EdgeDir[k]));
                if (w == 0) continue;
                double a = beta * w / G.EdgeDist[k] / sw2;
                _sys.Off[k] -= a; _sys.Diag[i] += a;
            }
            _sys.Rhs[i] = -curl[i] / rhoW;
        }
        Array.Clear(Psi);
        _sys.Solve(Psi, 1e-9, 5000);
        for (int i = 0; i < N; i++) if (IsLand[i]) Psi[i] = 0;

        // Volume transport across each Voronoi edge from corner streamfunction values (exactly non-divergent).
        Parallel.For(0, N, i =>
        {
            int s0 = G.NbrStart[i], m = G.Degree(i);
            for (int k = 0; k < m; k++)
            {
                int jPrev = G.Nbr[s0 + (k + m - 1) % m], jk = G.Nbr[s0 + k], jNext = G.Nbr[s0 + (k + 1) % m];
                double cPrev = (Psi[i] + Psi[jPrev] + Psi[jk]) / 3;   // corner k−1
                double cK = (Psi[i] + Psi[jk] + Psi[jNext]) / 3;      // corner k
                OceanTransportOut[s0 + k] = (IsLand[i] || IsLand[jk]) ? 0 : cPrev - cK;
            }
        });
    }

    // =====================================================================================================
    // Temperature
    // =====================================================================================================

    /// <summary>
    /// Two-layer energy balance per cell, solved by block Gauss–Seidel between the atmosphere (horizontally coupled by
    /// diffusion and advection) and the surface (ocean cells horizontally coupled by gyres and eddies):
    ///   atmosphere: C_a dTa/dt = SW_a + (σTs⁴ − εσTa⁴) + H + L·E − OLR(Ta) + ∇·(p/g D ∇h) − C_a u·∇Ta
    ///   surface:    C_s dTs/dt = SW_s − (σTs⁴ − εσTa⁴) − H − L·E [+ conduction through sea ice] [+ ocean transport]
    /// H = ρ c_p C_H |U| (Ts − Ta) is the bulk sensible heat flux; ε is the Brutsaert clear-sky emissivity raised by
    /// cloud; evaporation moves latent heat from the surface into the air column, and its horizontal transport is the
    /// moist part of the moist-static-energy diffusion (Hwang &amp; Frierson 2010). Radiative terms are linearised.
    /// </summary>
    private void SolveEnergy()
    {
        double dF = P["radiation.co2_forcing_coef"] * Math.Log(_forcing.Co2Ppm / P["radiation.co2_ref"]);
        double cLw = P["radiation.cloud_lw_coef"], cMean = P["cloud.cover_mean"];
        double pOverG = P["planet.surface_pressure"] / P["planet.gravity"];
        double oceanVolHeat = P["thermo.rho_water"] * P["thermo.cp_water"];
        double hml = P["transport.ocean_mixed_layer"];
        double rhMse = P["transport.rh_mse"];
        double brut = P["radiation.brutsaert_coef"];
        double chO = P["transport.heat_exchange_ocean"], chL = P["transport.heat_exchange_land"], gust = P["circulation.gustiness"];
        var hs = new double[N]; var eps = new double[N];
        double cap = P["hydro.bucket_capacity"];
        Parallel.For(0, N, i =>
        {
            RhEff[i] = IsLand[i] ? rhMse * Math.Min(1, Math.Max(Soil[i] / (0.75 * cap), Snow[i] > 0 ? 1 : 0)) : rhMse;
            double speed = Math.Sqrt(Vec3.Dot(Wind[i], Wind[i]) + gust * gust);
            hs[i] = rhoA * Thermo.PressureAt(Elev[i]) / 1013.25 * cp * (IsLand[i] ? chL : chO) * speed;
            double taK = SurfaceT(i) + 273.15;
            double rh = IsLand[i] ? rhLand : rhOcean;
            double e = rh * Thermo.Esat(SurfaceT(i));
            double clear = Math.Min(1, brut * Math.Pow(e / taK, 1.0 / 7.0));
            eps[i] = clear * (1 - Cloud[i]) + Cloud[i];
        });

        var taOld = (double[])Ta.Clone(); var tsOld = (double[])Ts.Clone();
        var xa = (double[])Ta.Clone(); var xs = (double[])Ts.Clone();
        for (int sweep = 0; sweep < 4; sweep++)
        {
            // ---------------- atmosphere ----------------
            _sys.Clear();
            var sys = _sys;
            Parallel.For(0, N, i =>
            {
                double A = G.Area[i];
                double aOlr = Aolr - dF - cLw * (Cloud[i] - cMean);
                double taK = xa[i] - lapse * Elev[i] + 273.15, tsK = xs[i] - lapse * Elev[i] + 273.15;
                double f0 = eps[i] * sigma * taK * taK * taK * taK, f1 = 4 * eps[i] * sigma * taK * taK * taK;
                double up = sigma * tsK * tsK * tsK * tsK;
                double colMass = Thermo.PressureAt(Elev[i]) / 1013.25;
                double ca = cAir * colMass;
                sys.Diag[i] = A * (ca / dt + B + f1 + hs[i]);
                sys.Rhs[i] = A * (ca / dt * taOld[i] + SwAtmos[i] + up - f0 + f1 * xa[i] + hs[i] * xs[i]
                                  + Lv * Evap[i] / 86400.0 - aOlr + B * lapse * Elev[i]);
            });
            for (int i = 0; i < N; i++)
                for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
                {
                    int j = G.Nbr[k];
                    double lOverD = G.EdgeLength[k] / G.EdgeDist[k];
                    if (j > i)
                    {
                        // Moist static energy diffusion, linearised: ∇h = (c_p + L·RH·dq_sat/dT)∇T.
                        double te = 0.5 * (xa[i] - lapse * Elev[i] + xa[j] - lapse * Elev[j]);
                        double pe = Thermo.PressureAt(0.5 * (Elev[i] + Elev[j]));
                        double rhE = 0.5 * (RhEff[i] + RhEff[j]);
                        sys.AddExchange(i, k, pOverG * D * (cp + Lv * rhE * Thermo.DQsatDT(te, pe)) * lOverD);
                    }
                    double un = Vec3.Dot((WindRot[i] + WindRot[j]) * 0.5, G.EdgeDir[k]) * advFactor;
                    if (un < 0) { double c = cAir * (-un) * G.EdgeLength[k]; sys.Diag[i] += c; sys.Off[k] -= c; }
                }
            LastTempIters = sys.Solve(xa, 1e-10, 3000).iterations;

            // ---------------- surface ----------------
            sys.Clear();
            Parallel.For(0, N, i =>
            {
                double A = G.Area[i];
                bool ice = !IsLand[i] && SeaIce[i] > 0;
                double C = IsLand[i] ? cLand : ice ? cIce : cOcean;
                double taK = xa[i] - lapse * Elev[i] + 273.15, tsK = xs[i] - lapse * Elev[i] + 273.15;
                double down = eps[i] * sigma * taK * taK * taK * taK;
                double u0 = sigma * tsK * tsK * tsK * tsK, u1 = 4 * sigma * tsK * tsK * tsK;
                sys.Diag[i] = A * (C / dt + u1 + hs[i]);
                sys.Rhs[i] = A * (C / dt * tsOld[i] + SwSurface[i] - u0 + u1 * xs[i] + down + hs[i] * xa[i]
                                  - Lv * Evap[i] / 86400.0);
                if (ice)
                {
                    double kh = kIce / Math.Max(SeaIce[i], 0.1);
                    sys.Diag[i] += A * kh; sys.Rhs[i] += A * kh * tFreeze;
                }
            });
            for (int i = 0; i < N; i++)
            {
                if (IsLand[i] || SeaIce[i] > 0) continue;
                for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
                {
                    int j = G.Nbr[k];
                    if (IsLand[j] || SeaIce[j] > 0) continue;
                    double lOverD = G.EdgeLength[k] / G.EdgeDist[k];
                    if (j > i) sys.AddExchange(i, k, oceanVolHeat * hml * oceanK * lOverD);
                    double v = OceanTransportOut[k];
                    if (v < 0) { double c = oceanVolHeat * gyreEff * (-v); sys.Diag[i] += c; sys.Off[k] -= c; }
                }
            }
            sys.Solve(xs, 1e-10, 3000);
        }
        Array.Copy(xa, Ta, N); Array.Copy(xs, Ts, N);
        Parallel.For(0, N, i =>
        {
            Olr[i] = Aolr - dF - cLw * (Cloud[i] - cMean) + B * SurfaceT(i);
            double taK = SurfaceT(i) + 273.15, tsK = SkinT(i) + 273.15;
            _lwNet[i] = sigma * Math.Pow(tsK, 4) - eps[i] * sigma * Math.Pow(taK, 4);
            _sens[i] = hs[i] * (Ts[i] - Ta[i]);
        });
    }

    private void UpdateSeaIce()
    {
        double rhoLf = rhoI * Lf;
        double oceanVolHeat = P["thermo.rho_water"] * P["thermo.cp_water"];
        var oceanHeat = new double[N];
        // Ocean heat carried by the gyres into ice-covered cells melts ice from below.
        Parallel.For(0, N, i =>
        {
            if (IsLand[i] || SeaIce[i] <= 0) return;
            double q = 0;
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
            {
                int j = G.Nbr[k];
                double v = OceanTransportOut[k];
                if (v < 0 && !IsLand[j] && SeaIce[j] <= 0) q += oceanVolHeat * gyreEff * (-v) * (Ts[j] - tFreeze);
            }
            oceanHeat[i] = q / G.Area[i];
        });
        Parallel.For(0, N, i =>
        {
            if (IsLand[i]) return;
            if (SeaIce[i] <= 0)
            {
                if (Ts[i] < tFreeze)
                {
                    SeaIce[i] = cOcean * (tFreeze - Ts[i]) / rhoLf;
                    Ts[i] = tFreeze;
                }
                return;
            }
            double h = SeaIce[i];
            double kh = kIce / Math.Max(h, 0.1);
            double melt = oceanHeat[i] * dt / rhoLf;
            if (Ts[i] > 0)
            {
                melt += (cIce / dt + kh) * Ts[i] * dt / rhoLf;
                Ts[i] = 0;
            }
            double grow = kh * (tFreeze - Ts[i]) * dt / rhoLf;
            h += grow - melt;
            if (h <= 0)
            {
                Ts[i] = tFreeze + (-h) * rhoLf / cOcean;
                SeaIce[i] = 0;
            }
            else SeaIce[i] = h;
        });
    }

    // =====================================================================================================
    // Water cycle
    // =====================================================================================================

    private double WSat(int i)
    {
        double ts = SurfaceT(i), ps = Thermo.PressureAt(Elev[i]);
        return ps / 1013.25 * rhoA * hq * Thermo.Qsat(ts, ps);
    }

    private void SolveMoisture()
    {
        double a = P["hydro.precip_crh_a"], b = P["hydro.precip_crh_b"];
        // Eddy vapour transport equivalent to Hwang & Frierson's moist-static-energy diffusion: they diffuse q with the
        // full column mass p/g; the column vapour W ≈ ρ_a H_q q, so the diffusivity for W is D·(p/g)/(ρ_a H_q).
        double kW = D * (P["planet.surface_pressure"] / P["planet.gravity"]) / (rhoA * hq);
        double eps = P["hydro.orographic_efficiency"];
        double rhMse = P["transport.rh_mse"];
        double gust = P["circulation.gustiness"], cap = P["hydro.bucket_capacity"], snowHalf = P["hydro.snow_cover_half"];
        var wsat = new double[N]; var evap = new double[N]; var oro = new double[N];
        // Vapour-carrying wind: the rotational (near-geostrophic) flow carries the whole column, the frictional
        // cross-isobar component only the vapour inside the boundary layer.
        double fBL = 1 - Math.Exp(-P["circulation.boundary_layer_depth"] / hq);
        var um = new Vec3[N];
        for (int i = 0; i < N; i++) um[i] = WindRot[i] + (Wind[i] - WindRot[i]) * fBL;
        Parallel.For(0, N, i =>
        {
            wsat[i] = WSat(i);
            double ts = SkinT(i), ps = Thermo.PressureAt(Elev[i]);
            double speed = Math.Sqrt(Vec3.Dot(Wind[i], Wind[i]) + gust * gust);
            // Bulk formula: E = ρ C_E |U| (q_sat(T_surface) − q_air), with q_air = RH·q_sat(T_air).
            double rh = IsLand[i] ? rhLand : rhOcean;
            double dq = Thermo.Qsat(ts, ps) - rh * Thermo.Qsat(SurfaceT(i), ps);
            double pot = rhoA * ps / 1013.25 * ce * speed * Math.Max(0, dq);
            PotEvap[i] = pot;
            double betaS = 1;
            if (IsLand[i])
            {
                betaS = Math.Min(1, Soil[i] / (0.75 * cap));
                betaS = Math.Max(betaS, Snow[i] / (Snow[i] + snowHalf));
            }
            evap[i] = pot * betaS;
        });
        // Orographic blocking: flow into higher terrain carries only the vapour above the height step,
        // ∝ exp(−Δz/H_q). The blocked vapour is lifted; a fraction ε precipitates on the windward cell and the
        // rest is deflected (stays upstream). oro[i] is the resulting windward precipitation rate per unit W (1/s).
        Parallel.For(0, N, i =>
        {
            double q = 0;
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
            {
                int j = G.Nbr[k];
                double un = Vec3.Dot((um[i] + um[j]) * 0.5, G.EdgeDir[k]);
                double dz = Elev[j] - Elev[i];
                if (un > 0 && dz > 0) q += eps * un * G.EdgeLength[k] * (1 - Math.Exp(-dz / hq));
            }
            oro[i] = q / G.Area[i];
        });

        // Column relative humidity above ~1.1 would rain out within minutes; bounding the iterate keeps the
        // exponential precipitation law and the linear solver well conditioned.
        const double rMax = 1.1;
        // Transient-eddy vapour flux down the gradient of the temperature-determined humidity field
        // (Siler, Roe & Armour 2018): F = −K ∇(RH·W_sat(T_air)). Its convergence feeds the storm tracks and its
        // divergence dries the subtropics, independently of the column's actual vapour content.
        var weq = new double[N]; var eddy = new double[N];
        Parallel.For(0, N, i =>
        {
            double ps = Thermo.PressureAt(Elev[i]);
            // Fixed relative humidity, as in Siler et al.: the eddy flux follows the temperature field. (Using the
            // local, soil-limited humidity instead would turn dry deserts into moisture sinks.)
            weq[i] = rhMse * ps / 1013.25 * rhoA * hq * Thermo.Qsat(SurfaceT(i), ps);
        });
        Parallel.For(0, N, i =>
        {
            double q = 0;
            for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
            {
                // Eddies exchange vapour horizontally, above the terrain: compare only the parts of the two columns
                // above the higher surface (vapour decays with height on the scale H_q). Without this, cold high
                // terrain with its small saturated column would draw moisture from all sides.
                int j = G.Nbr[k];
                double top = Math.Max(Elev[i], Elev[j]);
                double wj = weq[j] * Math.Exp(-(top - Elev[j]) / hq), wi = weq[i] * Math.Exp(-(top - Elev[i]) / hq);
                q += kW * G.EdgeLength[k] / G.EdgeDist[k] * (wj - wi);
            }
            eddy[i] = q / G.Area[i];
        });
        var x = new double[N];
        for (int i = 0; i < N; i++) x[i] = Math.Min(W[i], 0.9 * wsat[i]);
        for (int newton = 0; newton < 12; newton++)
        {
            _sys.Clear();
            var sys = _sys;
            Parallel.For(0, N, i =>
            {
                double A = G.Area[i];
                double r = x[i] / wsat[i];
                double p0 = Math.Exp(a * (r - b)) / 86400.0;           // kg/m²/s
                double dp = p0 * a / wsat[i];
                sys.Diag[i] = A * (dp + oro[i]);
                sys.Rhs[i] = A * (evap[i] + eddy[i] - p0 + dp * x[i]);
            });
            for (int i = 0; i < N; i++)
                for (int k = G.NbrStart[i]; k < G.NbrStart[i + 1]; k++)
                {
                    int j = G.Nbr[k];
                    // Conservative upwind flux with the vapour-carrying wind: convergence moistens the column.
                    double un = Vec3.Dot((um[i] + um[j]) * 0.5, G.EdgeDir[k]);
                    if (un > 0)
                    {
                        double dz = Elev[j] - Elev[i];
                        double pass = dz > 0 ? Math.Exp(-dz / hq) : 1;   // part of the column that clears the step
                        double c = un * G.EdgeLength[k];
                        sys.Diag[i] += c * pass;              // leaves i
                        sys.Off[G.EdgeReverse[k]] -= c * pass; // enters j
                    }
                }
            var prev = (double[])x.Clone();
            LastMoistIters = sys.Solve(x, 1e-10, 3000).iterations;
            LastNewton = newton + 1;
            double change = 0;
            for (int i = 0; i < N; i++)
            {
                if (!(x[i] >= 0)) x[i] = 0;
                // damp large Newton steps on the exponential
                if (x[i] > prev[i] + 0.3 * wsat[i]) x[i] = prev[i] + 0.3 * wsat[i];
                if (x[i] > rMax * wsat[i]) x[i] = rMax * wsat[i];
                change = Math.Max(change, Math.Abs(x[i] - prev[i]) / Math.Max(wsat[i], 1));
            }
            if (change < 1e-4) break;
        }
        Array.Copy(x, W, N);
        Parallel.For(0, N, i =>
        {
            Precip[i] = (Math.Exp(a * (W[i] / wsat[i] - b)) / 86400.0 + oro[i] * W[i]) * 86400.0; // mm/day
            Evap[i] = evap[i] * 86400.0;
        });
    }

    private void UpdateSurfaceWater()
    {
        double lo = P["hydro.snow_rain_low"], hi = P["hydro.snow_rain_high"], ddf = P["hydro.degree_day_factor"];
        double cap = P["hydro.bucket_capacity"];
        double days = dt / 86400.0;
        Parallel.For(0, N, i =>
        {
            Runoff[i] = 0;
            if (!IsLand[i]) return;
            double ts = SurfaceT(i);
            double p = Precip[i] * days;
            double fSnow = Math.Clamp((hi - ts) / (hi - lo), 0, 1);
            Snow[i] += p * fSnow;
            double e = Evap[i] * days;
            // evaporation/sublimation comes from snow first
            double fromSnow = Math.Min(Snow[i], e);
            Snow[i] -= fromSnow; e -= fromSnow;
            double melt = Math.Min(Snow[i], ddf * Math.Max(ts, 0) * days);
            Snow[i] -= melt;
            double soil = Soil[i] + p * (1 - fSnow) + melt - e;
            if (soil > cap) { Runoff[i] = soil - cap; soil = cap; }
            Soil[i] = Math.Max(0, soil);
        });
    }

    private void UpdateClouds()
    {
        double baseO = P["cloud.cover_base_ocean"], baseL = P["cloud.cover_base_land"];
        double scale = P["cloud.cover_precip_scale"], max = P["cloud.cover_max"];
        Parallel.For(0, N, i =>
        {
            double baseC = IsLand[i] ? baseL : baseO;
            Cloud[i] = baseC + (max - baseC) * (1 - Math.Exp(-Precip[i] / scale));
        });
    }

    // =====================================================================================================
    // Statistics
    // =====================================================================================================

    private void Accumulate(int s)
    {
        double rhoAir = rhoA, cd = P["transport.ocean_drag"], gust = P["circulation.gustiness"];
        int month = s * 12 / Steps;
        bool first = s * 12 % Steps == 0;
        int perMonth = Steps / 12;
        double days = dt / 86400.0;
        _accIns += GlobalMean(i => Insol[i]); _accAbs += GlobalMean(i => Insol[i] * (1 - Albedo[i])); _accOlr += GlobalMean(i => Olr[i]);
        Parallel.For(0, N, i =>
        {
            if (first) { _accT[i] = 0; _accP[i] = 0; _accSnow[i] = 0; _accSoil[i] = 0; _accPET[i] = 0; }
            double ts = SurfaceT(i);
            _accT[i] += ts; _accP[i] += Precip[i]; _accSnow[i] += Snow[i]; _accSoil[i] += Soil[i]; _accPET[i] += PotEvap[i] * 86400;
            double speed = Math.Sqrt(Vec3.Dot(Wind[i], Wind[i]) + gust * gust);
            _tauAcc[i] = _tauAcc[i] + Wind[i] * (rhoAir * cd * speed / Steps);
            _accWind[i] = _accWind[i] + Wind[i] / Steps;
            _accPres[i] += Pressure[i] / Steps;
            AnnualCloud[i] += Cloud[i] / Steps;
            double abs = Insol[i] * (1 - Albedo[i]);
            Budget[0][i] += abs / Steps; Budget[1][i] += Olr[i] / Steps;
            Budget[2][i] += SwSurface[i] / Steps; Budget[3][i] += _lwNet[i] / Steps;
            Budget[4][i] += _sens[i] / Steps; Budget[5][i] += Lv * Evap[i] / 86400.0 / Steps;
            Budget[6][i] += Albedo[i] / Steps; Budget[7][i] += SkinT(i) / Steps; Budget[8][i] += SurfaceT(i) / Steps;
            _accRunoff[i] += Runoff[i];
            _yearP[i] += Precip[i] * days; _yearPET[i] += PotEvap[i] * 86400 * days;
            _iceMax[i] = Math.Max(_iceMax[i], SeaIce[i]); _iceMin[i] = Math.Min(_iceMin[i], SeaIce[i]);
            if ((s + 1) % perMonth == 0)
            {
                MonthT[month][i] = _accT[i] / perMonth; MonthP[month][i] = _accP[i] / perMonth;
                MonthSnow[month][i] = _accSnow[i] / perMonth; MonthSoil[month][i] = _accSoil[i] / perMonth;
                MonthPET[month][i] = _accPET[i] / perMonth;
                _warmest[i] = Math.Max(_warmest[i], MonthT[month][i]);
            }
        });
    }

    private void FinishYear()
    {
        AnnualPlanetaryAlbedo = 1 - _accAbs / _accIns;
        AnnualAbsorbed = _accAbs / Steps; AnnualOlr = _accOlr / Steps;
        Array.Copy(_accRunoff, AnnualRunoff, N);
        Array.Copy(_accWind, AnnualWind, N); Array.Copy(_accPres, AnnualPressure, N);
        Array.Copy(_iceMax, SeaIceMax, N); Array.Copy(_iceMin, SeaIceMin, N);
        SolveOceanCurrents(_tauAcc);
        // Vegetation follows the aridity index P/PET (UNEP 1992 classes: <0.05 hyper-arid … >0.65 humid) and warmth.
        double treeline = P["vegetation.treeline_warmest_month"];
        double tauGrass = P["vegetation.grass_response_time"], tauForest = VegetationSpinupFast ? 1.0 : P["vegetation.forest_response_time"];
        Parallel.For(0, N, i =>
        {
            if (!IsLand[i]) { Veg[i] = 0; Forest[i] = 0; return; }
            double ai = _yearPET[i] > 0 ? _yearP[i] / _yearPET[i] : 10;
            double moisture = Math.Clamp((ai - 0.05) / 0.6, 0, 1);
            double warmth = Math.Clamp(_warmest[i] / treeline, 0, 1);
            double vegEq = moisture * warmth;
            double forestEq = Math.Clamp((ai - 0.4) / 0.4, 0, 1) * Math.Clamp((_warmest[i] - treeline) / 4, 0, 1);
            // Relax toward equilibrium with the response times of grass and forest (Brovkin et al. 1997).
            Veg[i] += (vegEq - Veg[i]) * (1 - Math.Exp(-1.0 / tauGrass));
            Forest[i] += (forestEq - Forest[i]) * (1 - Math.Exp(-1.0 / tauForest));
        });
    }

    /// <summary>During spin-up from an arbitrary state the forest may equilibrate with a 1-year time scale; normal runs
    /// use the observed ~50-year forest response.</summary>
    public bool VegetationSpinupFast { get; set; }

    /// <summary>Area-weighted global mean of a per-cell quantity.</summary>
    public double GlobalMean(Func<int, double> f, Func<int, bool>? where = null)
    {
        double s = 0, w = 0;
        for (int i = 0; i < N; i++) { if (where != null && !where(i)) continue; s += f(i) * G.Area[i]; w += G.Area[i]; }
        return s / w;
    }
}
