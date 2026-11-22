"""Convert observational climatologies (netCDF) into simple float32 grids for the C# validator.

Output: <name>.bin (float32 little-endian, [month][lat][lon], lat north->south, lon from 0 east)
        <name>.json header.
Inputs (NCAR geocat-datafiles):
  T2M_ERAINT_rectilinear_grid_2D.nc  ERA-Interim monthly 2 m temperature, 2005 (K)
  V22_GPCP.1979-2010.nc              GPCP v2.2 monthly precipitation (mm/day)
"""
import json, sys, numpy as np, netCDF4 as nc

src, out = sys.argv[1], sys.argv[2]

def write(name, data, lat, lon, units, desc):
    data = np.asarray(data, dtype=np.float64)
    if lat[0] < lat[-1]:
        lat = lat[::-1]; data = data[:, ::-1, :]
    lon = np.mod(lon, 360.0); order = np.argsort(lon); lon = lon[order]; data = data[:, :, order]
    data.astype('<f4').tofile(f"{out}/{name}.bin")
    json.dump({"nt": data.shape[0], "nlat": len(lat), "nlon": len(lon),
               "lat": [float(x) for x in lat], "lon": [float(x) for x in lon],
               "units": units, "description": desc}, open(f"{out}/{name}.json", "w"))
    print(name, data.shape, float(np.nanmin(data)), float(np.nanmax(data)))

d = nc.Dataset(f"{src}/T2M_ERAINT_rectilinear_grid_2D.nc")
t = d["T2M"][:].filled(np.nan) - 273.15
write("era_t2m_2005", t, d["lat"][:], d["lon"][:], "degC", "ERA-Interim monthly mean 2 m temperature, 2005")

d = nc.Dataset(f"{src}/V22_GPCP.1979-2010.nc")
p = d["PREC"][:].filled(np.nan)
clim = np.stack([np.nanmean(p[m::12], axis=0) for m in range(12)])
write("gpcp_precip_clim", clim, d["lat"][:], d["lon"][:], "mm/day", "GPCP v2.2 monthly precipitation climatology 1979-2010")
