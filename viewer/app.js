// Holocene world viewer: draws world snapshots exported by `holocene world`.
"use strict";

const MONTHS = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"];
const BIOME_COLORS = {
  Ocean: "#1d3a52", Ice: "#eef2f5", Tundra: "#9aa58c", BorealForest: "#3f6146", TemperateForest: "#4f8a3f",
  TemperateGrassland: "#b9b468", Mediterranean: "#9e9a4a", Desert: "#e3c88c", XericShrubland: "#c9a96a",
  Savanna: "#c2b04a", TropicalDryForest: "#7a9a2e", TropicalRainforest: "#1f6b2a", Lake: "#4f8fbf",
};
// Layer definitions: how to read and colour each quantity.
const LAYERS = [
  { id: "biome", name: "Biome", kind: "cat" },
  { id: "koppen", name: "Köppen climate", kind: "koppen" },
  { id: "elev", name: "Elevation (m)", kind: "seq", scale: "terrain", min: 0, max: 5000 },
  { id: "t", name: "Temperature, monthly (°C)", kind: "div", monthly: true, min: -40, max: 40 },
  { id: "t_ann", name: "Temperature, annual (°C)", kind: "div", min: -30, max: 30 },
  { id: "p", name: "Precipitation, monthly (mm/day)", kind: "seq", scale: "wet", monthly: true, min: 0, max: 12, log: true },
  { id: "p_ann", name: "Precipitation, annual (mm)", kind: "seq", scale: "wet", min: 0, max: 3000, log: true },
  { id: "npp", name: "Net primary productivity (g/m²/yr)", kind: "seq", scale: "green", min: 0, max: 2500 },
  { id: "veg", name: "Vegetation cover", kind: "seq", scale: "green", min: 0, max: 1 },
  { id: "forest", name: "Forest cover", kind: "seq", scale: "green", min: 0, max: 1 },
  { id: "snow_max", name: "Max snow (mm water)", kind: "seq", scale: "ice", min: 0, max: 500, log: true },
  { id: "seaice_max", name: "Max sea-ice thickness (m)", kind: "seq", scale: "ice", min: 0, max: 4 },
  { id: "runoff", name: "Runoff (mm/yr)", kind: "seq", scale: "wet", min: 0, max: 2000, log: true },
  { id: "pressure", name: "Sea-level pressure (hPa)", kind: "div", min: 995, max: 1031 },
  { id: "psi", name: "Ocean gyre streamfunction (Sv)", kind: "div", min: -30, max: 30 },
];

const RAMPS = {
  div: [[0, [49, 54, 149]], [0.25, [116, 173, 209]], [0.5, [246, 243, 230]], [0.75, [244, 109, 67]], [1, [165, 0, 38]]],
  wet: [[0, [140, 98, 57]], [0.3, [223, 194, 125]], [0.55, [128, 205, 193]], [0.8, [33, 113, 181]], [1, [8, 48, 107]]],
  green: [[0, [237, 226, 196]], [0.4, [173, 196, 109]], [0.75, [65, 135, 60]], [1, [18, 70, 30]]],
  ice: [[0, [40, 44, 52]], [0.5, [140, 170, 200]], [1, [250, 252, 255]]],
  terrain: [[0, [74, 120, 70]], [0.15, [160, 170, 100]], [0.35, [190, 160, 110]], [0.6, [150, 110, 80]], [0.85, [220, 215, 210]], [1, [255, 255, 255]]],
};

const state = { grid: null, snap: null, cache: {}, layer: "biome", month: 0, view: { x: 0, y: 0, k: 1 }, sel: -1, base: null, proj: null };
const $ = (id) => document.getElementById(id);
const canvas = $("map"), ctx = canvas.getContext("2d");

function ramp(name, f) {
  const r = RAMPS[name]; f = Math.max(0, Math.min(1, f));
  for (let i = 1; i < r.length; i++) if (f <= r[i][0]) {
    const [a, ca] = r[i - 1], [b, cb] = r[i], t = (f - a) / (b - a);
    return ca.map((v, k) => Math.round(v + (cb[k] - v) * t));
  }
  return r[r.length - 1][1];
}

function b64(str, Type) {
  const bin = atob(str), bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return new Type(bytes.buffer);
}

async function loadGrid(name) {
  const j = await (await fetch("data/" + name)).json();
  return { n: j.n, lat: b64(j.lat, Float32Array), lon: b64(j.lon, Float32Array), start: b64(j.start, Int32Array), clat: b64(j.clat, Float32Array), clon: b64(j.clon, Float32Array) };
}

// Layers stay base64 in the bundle until first used.
async function layerData(id) {
  const key = state.snap.id + "/" + id;
  if (!state.cache[key]) {
    const raw = state.snap.layers[id];
    if (raw === undefined) throw new Error("missing layer " + id);
    state.cache[key] = b64(raw, Float32Array);
  }
  return state.cache[key];
}

function currentLayerId() {
  const L = LAYERS.find(l => l.id === state.layer);
  return L.monthly ? `${L.id}${String(state.month).padStart(2, "0")}` : L.id;
}

// ---- projection (equirectangular onto a base canvas, then pan/zoom) ----
const BASE_W = 2400, BASE_H = 1200;
const px = (lon) => (lon + 180) / 360 * BASE_W, py = (lat) => (90 - lat) / 180 * BASE_H;

function buildPolygons() {
  const g = state.grid; const polys = new Array(g.n);
  for (let i = 0; i < g.n; i++) {
    const s = g.start[i], e = g.start[i + 1]; const pts = [];
    const lon0 = g.lon[i];
    for (let k = s; k < e; k++) {
      let lo = g.clon[k];
      if (lo - lon0 > 180) lo -= 360; else if (lo - lon0 < -180) lo += 360;
      pts.push(px(lo), py(g.clat[k]));
    }
    let minx = Infinity, maxx = -Infinity;
    for (let k = 0; k < pts.length; k += 2) { minx = Math.min(minx, pts[k]); maxx = Math.max(maxx, pts[k]); }
    pts.minx = minx; pts.maxx = maxx;
    polys[i] = pts;
  }
  state.proj = polys;
}

function colorFor(L, v, i, land) {
  if (L.kind === "cat") return BIOME_COLORS[state.snap.biomes[v]] || "#555";
  if (L.kind === "koppen") return KOPPEN_COLORS[state.snap.koppen[i]] || "#1d3a52";
  if (Number.isNaN(v)) return "#333";
  let f;
  if (L.log) { const lo = Math.log(1 + L.min), hi = Math.log(1 + L.max); f = (Math.log(1 + Math.max(0, v)) - lo) / (hi - lo); }
  else f = (v - L.min) / (L.max - L.min);
  const c = ramp(L.kind === "div" ? "div" : L.scale, f);
  return `rgb(${c[0]},${c[1]},${c[2]})`;
}

async function render() {
  if (!state.snap) return;
  $("status").textContent = "drawing…";
  const L = LAYERS.find(l => l.id === state.layer);
  const land = await layerData("land");
  const vals = L.kind === "koppen" ? null : await layerData(L.kind === "cat" ? "biome" : currentLayerId());
  const base = state.base || (state.base = document.createElement("canvas"));
  base.width = BASE_W; base.height = BASE_H;
  const b = base.getContext("2d");
  b.fillStyle = "#1d3a52"; b.fillRect(0, 0, BASE_W, BASE_H);
  const g = state.grid;
  const landOnly = ["biome", "koppen", "elev", "npp", "veg", "forest", "snow_max", "runoff"].includes(L.id);
  for (let i = 0; i < g.n; i++) {
    const isLand = land[i] > 0.5;
    let col;
    if (!isLand && (landOnly || L.kind === "cat" || L.kind === "koppen")) col = "#1d3a52";
    else if (L.id === "seaice_max" && isLand) col = "#2b2a26";
    else if (L.id === "psi" && isLand) col = "#2b2a26";
    else col = colorFor(L, vals ? vals[i] : 0, i, isLand);
    drawCell(b, i, col);
  }
  if ($("lakes").checked) {
    const lake = await layerData("lake");
    for (let i = 0; i < g.n; i++) if (lake[i] >= 0 && land[i] > 0.5) drawCell(b, i, "#4f8fbf");
  }
  if ($("rivers").checked) await drawRivers(b, land);
  if ($("winds").checked) await drawWinds(b);
  draw();
  $("status").textContent = "";
}

function drawCell(b, i, col) {
  const p = state.proj[i];
  b.fillStyle = col; b.strokeStyle = col; b.lineWidth = 0.6;
  for (const shift of [0, -BASE_W, BASE_W]) {
    const minx = p.minx + shift, maxx = p.maxx + shift;
    if (maxx < 0 || minx > BASE_W) continue;
    b.beginPath(); b.moveTo(p[0] + shift, p[1]);
    for (let k = 2; k < p.length; k += 2) b.lineTo(p[k] + shift, p[k + 1]);
    b.closePath(); b.fill(); b.stroke();
  }
}

async function drawRivers(b, land) {
  const q = await layerData("discharge"), down = await layerData("downstream"); const g = state.grid;
  b.strokeStyle = "rgba(70,140,200,0.95)"; b.lineCap = "round";
  for (let i = 0; i < g.n; i++) {
    const d = down[i]; if (d < 0 || land[i] < 0.5 || q[i] < 800) continue;
    let x0 = px(g.lon[i]), y0 = py(g.lat[i]), x1 = px(g.lon[d]), y1 = py(g.lat[d]);
    if (Math.abs(x1 - x0) > BASE_W / 2) continue;
    b.lineWidth = Math.min(5.5, 0.8 + Math.log10(q[i] / 800) * 1.8);
    b.beginPath(); b.moveTo(x0, y0); b.lineTo(x1, y1); b.stroke();
  }
}

async function drawWinds(b) {
  const ue = await layerData("wind_e"), un = await layerData("wind_n"); const g = state.grid;
  b.strokeStyle = "rgba(255,255,255,0.55)"; b.lineWidth = 0.8;
  for (let i = 0; i < g.n; i += 3) {
    const x = px(g.lon[i]), y = py(g.lat[i]); const sx = ue[i] * 2.2, sy = -un[i] * 2.2;
    b.beginPath(); b.moveTo(x, y); b.lineTo(x + sx, y + sy); b.stroke();
    b.fillStyle = "rgba(255,255,255,0.55)"; b.fillRect(x + sx - 0.8, y + sy - 0.8, 1.6, 1.6);
  }
}

function fit() {
  const r = canvas.getBoundingClientRect(); canvas.width = r.width * devicePixelRatio; canvas.height = r.height * devicePixelRatio;
  const k = canvas.width / BASE_W; state.view = { x: 0, y: (canvas.height - BASE_H * k) / 2, k };
}

function draw() {
  if (!state.base) return;
  ctx.fillStyle = "#12110f"; ctx.fillRect(0, 0, canvas.width, canvas.height);
  const v = state.view;
  ctx.imageSmoothingEnabled = v.k < 1.5;
  ctx.setTransform(v.k, 0, 0, v.k, v.x, v.y);
  for (const s of [-BASE_W, 0, BASE_W]) ctx.drawImage(state.base, s, 0);
  if (state.sel >= 0) {
    const g = state.grid; ctx.strokeStyle = "#ffd75e"; ctx.lineWidth = 2 / v.k;
    ctx.beginPath(); ctx.arc(px(g.lon[state.sel]), py(g.lat[state.sel]), 8 / v.k, 0, 7); ctx.stroke();
  }
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  legend();
}

function toLatLon(ev) {
  const r = canvas.getBoundingClientRect(); const v = state.view;
  const x = ((ev.clientX - r.left) * devicePixelRatio - v.x) / v.k, y = ((ev.clientY - r.top) * devicePixelRatio - v.y) / v.k;
  let lon = x / BASE_W * 360 - 180; lon = ((lon + 540) % 360) - 180;
  return [90 - y / BASE_H * 180, lon];
}

function nearestCell(lat, lon) {
  const g = state.grid; let best = -1, bd = -2;
  const cl = Math.cos(lat * Math.PI / 180), x = cl * Math.cos(lon * Math.PI / 180), y = cl * Math.sin(lon * Math.PI / 180), z = Math.sin(lat * Math.PI / 180);
  for (let i = 0; i < g.n; i++) {
    const c = Math.cos(g.lat[i] * Math.PI / 180);
    const d = c * Math.cos(g.lon[i] * Math.PI / 180) * x + c * Math.sin(g.lon[i] * Math.PI / 180) * y + Math.sin(g.lat[i] * Math.PI / 180) * z;
    if (d > bd) { bd = d; best = i; }
  }
  return best;
}

async function inspect(i) {
  state.sel = i; draw();
  const g = state.grid, s = state.snap;
  const get = async (id) => (await layerData(id))[i];
  const land = (await get("land")) > 0.5;
  const T = [], P = [];
  for (let m = 0; m < 12; m++) { T.push(await get("t" + String(m).padStart(2, "0"))); P.push(await get("p" + String(m).padStart(2, "0")) * 30.4); }
  const rows = [];
  const row = (k, v) => rows.push(`<tr><td>${k}</td><td>${v}</td></tr>`);
  row("Position", `${g.lat[i].toFixed(1)}°, ${g.lon[i].toFixed(1)}°`);
  row("Surface", land ? `land (${Math.round((await get("landfrac")) * 100)}% of cell)` : "sea");
  if (land) {
    row("Elevation", `${Math.round(await get("elev"))} m`);
    row("Köppen", s.koppen[i] || "—"); row("Biome", s.biomes[await get("biome")].replace(/([a-z])([A-Z])/g, "$1 $2"));
    row("NPP", `${Math.round(await get("npp"))} g/m²/yr`);
    const q = await get("discharge"); if (q > 30) row("River discharge", `${Math.round(q).toLocaleString()} m³/s`);
    const lk = await get("lake"); if (lk >= 0) { const L = s.lakes.find(l => l.id === lk); row("Lake", L ? `${L.area_km2.toLocaleString()} km², level ${L.level} m, ${L.terminal ? "terminal" : "overflowing"}` : `#${lk}`); }
    if (await get("glacier") > 0) row("Ice", "glacier / ice sheet");
  }
  row("Mean temperature", `${(await get("t_ann")).toFixed(1)} °C`);
  row("Warmest / coldest month", `${Math.max(...T).toFixed(1)} / ${Math.min(...T).toFixed(1)} °C`);
  row("Annual precipitation", `${Math.round(P.reduce((a, b) => a + b, 0))} mm`);
  if (!land) row("Max sea ice", `${(await get("seaice_max")).toFixed(1)} m`);
  row("Sea-level pressure", `${(await get("pressure")).toFixed(1)} hPa`);
  const ue = await get("wind_e"), un = await get("wind_n");
  row("Mean wind", `${Math.hypot(ue, un).toFixed(1)} m/s from ${compass(Math.atan2(-ue, -un))}`);
  $("cellInfo").innerHTML = `<h2>Cell ${i}</h2><table>${rows.join("")}</table><h3>Climate chart</h3>${climograph(T, P)}`;
}

function compass(a) { const d = ((a * 180 / Math.PI) + 360) % 360; return ["N","NE","E","SE","S","SW","W","NW"][Math.round(d / 45) % 8]; }

function climograph(T, P) {
  const W = 300, H = 150, pad = 26, bw = (W - 2 * pad) / 12;
  const pMax = Math.max(100, ...P), tMin = Math.min(-10, ...T), tMax = Math.max(30, ...T);
  const yT = (t) => H - pad - (t - tMin) / (tMax - tMin) * (H - 2 * pad);
  let s = `<svg width="${W}" height="${H}">`;
  P.forEach((p, m) => { const h = p / pMax * (H - 2 * pad); s += `<rect x="${pad + m * bw + 2}" y="${H - pad - h}" width="${bw - 4}" height="${h}" fill="#3f7fb0"/>`; });
  s += `<polyline fill="none" stroke="#e0603a" stroke-width="2" points="${T.map((t, m) => `${pad + (m + 0.5) * bw},${yT(t)}`).join(" ")}"/>`;
  MONTHS.forEach((mn, m) => s += `<text x="${pad + m * bw + 3}" y="${H - 8}">${mn[0]}</text>`);
  s += `<text x="0" y="${yT(tMax) + 4}">${Math.round(tMax)}°</text><text x="0" y="${yT(tMin)}">${Math.round(tMin)}°</text>`;
  s += `<text x="${W - pad + 2}" y="${pad}">${Math.round(pMax)}</text><text x="${W - pad + 2}" y="${pad + 11}">mm</text></svg>`;
  return s;
}

function legend() {
  const L = LAYERS.find(l => l.id === state.layer); const el = $("legend");
  if (L.kind === "cat") {
    el.innerHTML = `<b>${L.name}</b><br>` + state.snap.biomes.filter(b => b !== "Ocean").map(b => `<span class="cat"><span class="sw" style="background:${BIOME_COLORS[b]}"></span>${b.replace(/([a-z])([A-Z])/g, "$1 $2")}</span>`).join("");
    return;
  }
  if (L.kind === "koppen") {
    const present = [...new Set(state.snap.koppen.filter(k => k))].sort();
    el.innerHTML = `<b>${L.name}</b><br>` + present.map(k => `<span class="cat"><span class="sw" style="background:${KOPPEN_COLORS[k]}"></span>${k}</span>`).join("");
    return;
  }
  const stops = [];
  for (let k = 0; k <= 10; k++) { const c = ramp(L.kind === "div" ? "div" : L.scale, k / 10); stops.push(`rgb(${c}) ${k * 10}%`); }
  const tick = (f) => { const v = L.log ? Math.exp(Math.log(1 + L.min) + f * (Math.log(1 + L.max) - Math.log(1 + L.min))) - 1 : L.min + f * (L.max - L.min); return Math.abs(v) >= 100 ? Math.round(v / 10) * 10 : Math.abs(v) >= 10 ? Math.round(v) : +v.toFixed(1); };
  el.innerHTML = `<b>${L.name}${L.monthly ? " — " + MONTHS[state.month] : ""}</b><div class="bar" style="background:linear-gradient(90deg,${stops.join(",")})"></div><div class="ticks">${[0, 0.25, 0.5, 0.75, 1].map(f => `<span>${tick(f)}</span>`).join("")}</div>`;
}

// Köppen colours after the conventional palette of Peel et al. (2007)
const KOPPEN_COLORS = {
  Af: "#0000ff", Am: "#0078ff", Aw: "#46aafa", As: "#46aafa", BWh: "#ff0000", BWk: "#ff9696", BSh: "#f5a500", BSk: "#ffdc64",
  Csa: "#ffff00", Csb: "#c8c800", Csc: "#969600", Cwa: "#96ff96", Cwb: "#64c864", Cwc: "#329632",
  Cfa: "#c8ff50", Cfb: "#64ff50", Cfc: "#32c800", Dsa: "#ff00ff", Dsb: "#c800c8", Dsc: "#963296", Dsd: "#966496",
  Dwa: "#aaafff", Dwb: "#5a78dc", Dwc: "#4b50b4", Dwd: "#320087", Dfa: "#00ffff", Dfb: "#37c8ff", Dfc: "#007d7d", Dfd: "#00465f",
  ET: "#b2b2b2", EF: "#666666",
};

// ---- interaction ----
let drag = null;
canvas.addEventListener("mousedown", (e) => { drag = { x: e.clientX, y: e.clientY, vx: state.view.x, vy: state.view.y, moved: false }; });
window.addEventListener("mouseup", (e) => {
  if (drag && !drag.moved && state.grid) { const [la, lo] = toLatLon(e); inspect(nearestCell(la, lo)); }
  drag = null;
});
canvas.addEventListener("mousemove", (e) => {
  if (drag) {
    const dx = (e.clientX - drag.x) * devicePixelRatio, dy = (e.clientY - drag.y) * devicePixelRatio;
    if (Math.abs(dx) + Math.abs(dy) > 4) drag.moved = true;
    state.view.x = drag.vx + dx; state.view.y = drag.vy + dy;
    state.view.x = ((state.view.x % (BASE_W * state.view.k)) + BASE_W * state.view.k) % (BASE_W * state.view.k) - BASE_W * state.view.k;
    draw();
  }
  const [la, lo] = toLatLon(e); const h = $("hover");
  h.style.display = "block"; h.style.left = (e.offsetX + 14) + "px"; h.style.top = (e.offsetY + 10) + "px";
  h.textContent = `${la.toFixed(1)}°, ${lo.toFixed(1)}°`;
});
canvas.addEventListener("mouseleave", () => $("hover").style.display = "none");
canvas.addEventListener("wheel", (e) => {
  e.preventDefault();
  const r = canvas.getBoundingClientRect(); const mx = (e.clientX - r.left) * devicePixelRatio, my = (e.clientY - r.top) * devicePixelRatio;
  const v = state.view, f = Math.exp(-e.deltaY * 0.0015), k = Math.max(canvas.width / BASE_W, Math.min(12, v.k * f));
  v.x = mx - (mx - v.x) * k / v.k; v.y = my - (my - v.y) * k / v.k; v.k = k; draw();
}, { passive: false });
window.addEventListener("resize", () => { fit(); draw(); });

$("layer").addEventListener("change", (e) => { state.layer = e.target.value; $("monthWrap").style.visibility = LAYERS.find(l => l.id === state.layer).monthly ? "visible" : "hidden"; render(); });
$("month").addEventListener("input", (e) => { state.month = +e.target.value; $("monthName").textContent = MONTHS[state.month]; render(); });
for (const id of ["rivers", "lakes", "winds"]) $(id).addEventListener("change", render);
$("snapshot").addEventListener("change", (e) => loadSnapshot(e.target.value));

async function loadSnapshot(id) {
  $("status").textContent = "loading…";
  const m = await (await fetch(`data/${id}.json`)).json();
  if (!state.grid || state.gridName !== m.grid) { state.grid = await loadGrid(m.grid); state.gridName = m.grid; buildPolygons(); }
  state.snap = m;
  const yr = m.year <= 0 ? `${1 - m.year} BCE` : `${m.year} CE`;
  const lakes = m.lakes.slice(0, 6).map(l => `<tr><td>${l.lat.toFixed(0)}°, ${l.lon.toFixed(0)}°</td><td>${l.area_km2.toLocaleString()} km² ${l.terminal ? "(terminal)" : ""}</td></tr>`).join("");
  $("epochInfo").innerHTML = `<h2>${yr}</h2><table><tr><td>CO₂</td><td>${m.co2.toFixed(0)} ppm</td></tr><tr><td>Sea level</td><td>${m.sea_level.toFixed(0)} m</td></tr></table>` +
    (lakes ? `<h3>Largest lakes</h3><table>${lakes}</table>` : "") + (m.notes ? `<p class="note">${m.notes}</p>` : "");
  if (state.sel >= 0) inspect(state.sel);
  await render();
}

async function init() {
  $("layer").innerHTML = LAYERS.map(l => `<option value="${l.id}">${l.name}</option>`).join("");
  $("monthWrap").style.visibility = "hidden";
  fit();
  const idx = await (await fetch("data/index.json")).json();
  $("snapshot").innerHTML = idx.map(e => `<option value="${e.id}">${e.title}</option>`).join("");
  const first = idx.find(e => e.id === "bce10000") || idx[0];
  $("snapshot").value = first.id;
  await loadSnapshot(first.id);
}
init().catch(e => { $("status").textContent = "Could not load data: " + e.message + " — run `holocene world`, then `holocene serve`."; });
