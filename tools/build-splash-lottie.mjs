import { writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const FR = 60;
const IP = 0;
const OP = 80;
const CX = 128;
const CY = 128;
const RING_R = 76;
const DASH = 82;
const GAP = 22;
const STROKE = 18;
const GLYPH = [0.94, 1, 0.9882352941176471, 1];

const ease = {
  entranceSharp: { i: { x: [0.34], y: [0.94] }, o: { x: [0.2], y: [0.75] } },
  settleSoft: { i: { x: [0.51], y: [0.99] }, o: { x: [0], y: [0.65] } },
  travel: { i: { x: [0], y: [0.55] }, o: { x: [1], y: [0.49] } },
  pop: { i: { x: [0.34], y: [0.94] }, o: { x: [0.94], y: [0.75] } },
};

const round = (n, d = 4) => Number(n.toFixed(d));

function kf(t, s, e, easing, hold = false) {
  const frame = { t, s };
  if (e !== undefined) frame.e = e;
  if (hold) frame.h = 1;
  if (easing) {
    frame.i = easing.i;
    frame.o = easing.o;
  }
  return frame;
}

function staticValue(k) {
  return { a: 0, k };
}

function animated(frames) {
  return { a: 1, k: frames };
}

function transform({ p = [0, 0], a = [0, 0], s = [100, 100], r = 0, o = 100, nm = "Transform" } = {}) {
  return {
    ty: "tr",
    p: typeof p === "object" && p.a !== undefined ? p : staticValue(p),
    a: typeof a === "object" && a.a !== undefined ? a : staticValue(a),
    s: typeof s === "object" && s.a !== undefined ? s : staticValue(s),
    r: typeof r === "object" && r.a !== undefined ? r : staticValue(r),
    o: typeof o === "object" && o.a !== undefined ? o : staticValue(o),
    sk: staticValue(0),
    sa: staticValue(0),
    nm,
  };
}

function trimEnd(frames, nm = "Trim") {
  return {
    ty: "tm",
    s: staticValue(0),
    e: animated(frames),
    o: staticValue(0),
    m: 1,
    nm,
  };
}

function stroke({ c = GLYPH, o = 100, w = STROKE, nm = "Stroke" } = {}) {
  return {
    ty: "st",
    c: staticValue(c),
    o: staticValue(o),
    w: staticValue(w),
    lc: 2,
    lj: 2,
    ml: 4,
    nm,
  };
}

function pathShape(path, nm) {
  return {
    ty: "sh",
    ks: staticValue(path),
    nm,
  };
}

function linePath(a, b) {
  return {
    i: [[0, 0], [0, 0]],
    o: [[0, 0], [0, 0]],
    v: [a, b],
    c: false,
  };
}

function arcPath(a0, a1) {
  const alpha = a1 - a0;
  const k = (4 / 3) * Math.tan(alpha / 4);
  const p0 = [CX + RING_R * Math.cos(a0), CY + RING_R * Math.sin(a0)];
  const p3 = [CX + RING_R * Math.cos(a1), CY + RING_R * Math.sin(a1)];
  const o0 = [k * RING_R * -Math.sin(a0), k * RING_R * Math.cos(a0)];
  const i1 = [k * RING_R * Math.sin(a1), k * RING_R * -Math.cos(a1)];
  return {
    i: [[0, 0], [round(i1[0]), round(i1[1])]],
    o: [[round(o0[0]), round(o0[1])], [0, 0]],
    v: [
      [round(p0[0]), round(p0[1])],
      [round(p3[0]), round(p3[1])],
    ],
    c: false,
  };
}

function ringDashes() {
  const circumference = 2 * Math.PI * RING_R;
  const period = DASH + GAP;
  const dashes = [];
  for (let start = 0, index = 0; start < circumference - 0.001; start += period, index += 1) {
    const end = Math.min(start + DASH, circumference);
    dashes.push({
      index,
      a0: start / RING_R,
      a1: end / RING_R,
    });
  }

  const twelveOClock = (3 * Math.PI) / 2;
  const clockwiseDelta = (angle) => (angle - twelveOClock + Math.PI * 2) % (Math.PI * 2);
  return dashes
    .map((dash) => ({ ...dash, order: clockwiseDelta(dash.a0) }))
    .sort((a, b) => a.order - b.order);
}

function plateGradient() {
  return {
    ty: "gf",
    o: staticValue(100),
    r: 1,
    bm: 0,
    t: 1,
    g: {
      p: 3,
      k: staticValue([
        0, 0.0784313725490196, 0.7686274509803922, 0.7607843137254902,
        0.48, 0, 0.6078431372549019, 0.6392156862745098,
        1, 0.043137254901960784, 0.396078431372549, 0.4392156862745098,
        0, 1, 1, 1,
      ]),
    },
    s: staticValue([46.08, 0]),
    e: staticValue([209.92, 256]),
    nm: "Plate Gradient",
  };
}

function spotGradient() {
  return {
    ty: "gf",
    o: staticValue(100),
    r: 1,
    bm: 0,
    t: 2,
    g: {
      p: 3,
      k: staticValue([
        0, 1, 1, 1,
        0.4, 1, 1, 1,
        1, 1, 1, 1,
        0, 0.16, 0.4, 0.04, 1, 0,
      ]),
    },
    s: staticValue([71.68, 46.08]),
    e: staticValue([250.88, 46.08]),
    nm: "Spot Gradient",
  };
}

function sheenGradient() {
  return {
    ty: "gf",
    o: staticValue(100),
    r: 1,
    bm: 0,
    t: 1,
    g: {
      p: 3,
      k: staticValue([
        0, 1, 1, 1,
        0.4, 1, 1, 1,
        1, 0, 0.20392156862745098, 0.23529411764705882,
        0, 0.12, 0.4, 0, 1, 0.14,
      ]),
    },
    s: staticValue([128, 0]),
    e: staticValue([128, 256]),
    nm: "Sheen Gradient",
  };
}

function roundedRect(size, radius, nm) {
  return {
    ty: "rc",
    d: 1,
    s: staticValue(size),
    p: staticValue([CX, CY]),
    r: staticValue(radius),
    nm,
  };
}

function group(nm, items) {
  return { ty: "gr", nm, it: items };
}

function layer(nm, ind, shapes, ks) {
  return {
    ddd: 0,
    ind,
    ty: 4,
    nm,
    sr: 1,
    ks,
    ao: 0,
    shapes,
    ip: IP,
    op: OP,
    st: 0,
    bm: 0,
  };
}

const dashes = ringDashes();

const ringGroups = dashes.map((dash, index) => {
  const start = 8 + index * 6;
  const end = start + 18;
  return group(`Ring Dash ${index + 1}`, [
    pathShape(arcPath(dash.a0, dash.a1), `Arc ${index + 1}`),
    stroke({ nm: `Ring Stroke ${index + 1}` }),
    trimEnd([
      kf(0, [0], [0], undefined, true),
      kf(start, [0], [100], ease.entranceSharp),
      kf(end, [100]),
    ], `Ring Trim ${index + 1}`),
    transform({ p: [0, 0], a: [0, 0] }),
  ]);
});

const lottie = {
  v: "5.12.2",
  fr: FR,
  ip: IP,
  op: OP,
  w: 256,
  h: 256,
  nm: "LocalSend Splash Logo",
  ddd: 0,
  assets: [],
  layers: [
    layer("Arrow", 1, [
      group("Shaft", [
        pathShape(linePath([79, 128], [177, 128]), "Shaft Path"),
        stroke({ nm: "Shaft Stroke" }),
        trimEnd([
          kf(0, [0], [0], undefined, true),
          kf(6, [0], [100], ease.travel),
          kf(34, [100]),
        ], "Shaft Trim"),
        transform({
          o: animated([
            kf(0, [0], [0], undefined, true),
            kf(6, [0], [100], ease.entranceSharp),
            kf(12, [100]),
          ]),
        }),
      ]),
      group("Head Upper", [
        pathShape(linePath([179, 128], [150, 99]), "Upper Wing"),
        stroke({ nm: "Upper Stroke" }),
        trimEnd([
          kf(0, [0], [0], undefined, true),
          kf(32, [0], [100], ease.pop),
          kf(50, [100]),
        ], "Upper Trim"),
        transform({
          o: animated([
            kf(0, [0], [0], undefined, true),
            kf(32, [0], [100], ease.entranceSharp),
            kf(38, [100]),
          ]),
        }),
      ]),
      group("Head Lower", [
        pathShape(linePath([179, 128], [150, 157]), "Lower Wing"),
        stroke({ nm: "Lower Stroke" }),
        trimEnd([
          kf(0, [0], [0], undefined, true),
          kf(34, [0], [100], ease.pop),
          kf(52, [100]),
        ], "Lower Trim"),
        transform({
          o: animated([
            kf(0, [0], [0], undefined, true),
            kf(34, [0], [100], ease.entranceSharp),
            kf(40, [100]),
          ]),
        }),
      ]),
    ], {
      o: staticValue(100),
      r: staticValue(0),
      p: staticValue([CX, CY, 0]),
      a: staticValue([CX, CY, 0]),
      s: staticValue([100, 100, 100]),
    }),
    layer("Ring", 2, ringGroups, {
      o: staticValue(100),
      r: staticValue(0),
      p: staticValue([CX, CY, 0]),
      a: staticValue([CX, CY, 0]),
      s: staticValue([100, 100, 100]),
    }),
    layer("Plate", 3, [
      group("Ceramic", [
        roundedRect([256, 256], 56, "Plate Rectangle"),
        plateGradient(),
        transform(),
      ]),
      group("Spot", [
        roundedRect([256, 256], 56, "Spot Rectangle"),
        spotGradient(),
        transform(),
      ]),
      group("Sheen", [
        roundedRect([256, 256], 56, "Sheen Rectangle"),
        sheenGradient(),
        transform(),
      ]),
      group("Rim", [
        roundedRect([253.5, 253.5], 54.75, "Rim Rectangle"),
        stroke({ c: [1, 1, 1, 1], o: 22, w: 2.5, nm: "Rim Stroke" }),
        transform(),
      ]),
    ], {
      o: animated([
        kf(0, [0], [100], ease.settleSoft),
        kf(16, [100]),
      ]),
      r: staticValue(0),
      p: staticValue([CX, CY, 0]),
      a: staticValue([CX, CY, 0]),
      s: animated([
        kf(0, [90, 90, 100], [100, 100, 100], ease.settleSoft),
        kf(28, [100, 100, 100]),
      ]),
    }),
  ],
  markers: [{ tm: 56, cm: "settled", dr: 24 }],
};

const outPath = resolve(dirname(fileURLToPath(import.meta.url)), "..", "src", "LocalSendDotNet.App", "Assets", "SplashLogo.json");
writeFileSync(outPath, `${JSON.stringify(lottie)}\n`);
console.log(`wrote ${outPath}`);
console.log(`dashes=${dashes.length} order=${dashes.map((d) => d.index).join(",")}`);
