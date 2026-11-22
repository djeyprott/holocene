"""Print climate layers at named points from a saved climate file (HCLM format) and the grid centres."""
import struct, sys, math
import numpy as np
path, gridcsv = sys.argv[1], sys.argv[2]
f = open(path, 'rb'); assert f.read(4) == b'HCLM'
ver, n, count = struct.unpack('<iii', f.read(12))
def rstr():
    # .NET BinaryWriter 7-bit length prefix
    l = 0; shift = 0
    while True:
        b = f.read(1)[0]; l |= (b & 0x7f) << shift; shift += 7
        if b < 0x80: break
    return f.read(l).decode()
layers = {}
for _ in range(count):
    name = rstr(); layers[name] = np.frombuffer(f.read(4 * n), dtype='<f4')
lat, lon = np.loadtxt(gridcsv, delimiter=',', unpack=True)
pts = [("N Atl gyre 30N 50W", 30, -50), ("Gulf Stream 36N 72W", 36, -72), ("N Atl 50N 30W", 50, -30), ("N Pac 30N 170W", 30, -170),
       ("Kuroshio 33N 137E", 33, 137), ("S Pac 30S 120W", -30, -120), ("Trades 15N 40W", 15, -40), ("Westerlies 45N 30W", 45, -30),
       ("SH westerlies 50S 90E", -50, 90), ("Norwegian Sea 68N 5E", 68, 5)]
v = np.stack([np.cos(np.radians(lat))*np.cos(np.radians(lon)), np.cos(np.radians(lat))*np.sin(np.radians(lon)), np.sin(np.radians(lat))], 1)
for name, la, lo in pts:
    p = np.array([math.cos(math.radians(la))*math.cos(math.radians(lo)), math.cos(math.radians(la))*math.sin(math.radians(lo)), math.sin(math.radians(la))])
    c = int(np.argmax(v @ p))
    print(f"{name:26s} psi {layers['psi'][c]:7.1f} Sv  wind_e {layers['wind_e'][c]:5.1f} wind_n {layers['wind_n'][c]:5.1f}  T {layers['t_ann'][c]:6.1f}  seaice {layers['seaice_max'][c]:4.1f}  pres {layers['pressure'][c]:7.1f}")
