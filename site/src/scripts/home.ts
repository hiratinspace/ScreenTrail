/**
 * The home page's motion: the line-art figures, the pinned scene, the replaying mocks, the pill clock,
 * the session ribbon and Ctrl+Enter on the form. The page is complete without any of it.
 *
 * It never writes HTML from strings (no innerHTML), so the site's Content-Security-Policy can require
 * Trusted Types and allow no script but this file.
 */

const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
document.documentElement.classList.add('js');

const byId = <T extends HTMLElement | SVGElement = HTMLElement>(id: string) => document.getElementById(id) as T | null;
const clamp = (v: number, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const ease = (v: number) => 1 - Math.pow(1 - v, 3);
const fmt = (v: number) => `${String(Math.floor(v / 60)).padStart(2, '0')}:${String(Math.floor(v % 60)).padStart(2, '0')}`;

/* One frame per scroll for everything scroll moves. Each step reads the page and returns what to
   write; every read happens before any write, so no write forces the next read to lay the page out
   again. */
type Step = () => (() => void) | void;
const steps: Step[] = [];
const runSteps = () => { steps.map((s) => s()).forEach((w) => w?.()); };
let queuedFrame = false;
addEventListener('scroll', () => {
  if (queuedFrame) return;
  queuedFrame = true;
  requestAnimationFrame(() => { queuedFrame = false; runSteps(); });
}, { passive: true });

/* ---------- Mocks replay once when they come into view ---------- */
const replay = new IntersectionObserver(
  (entries) => entries.forEach((e) => {
    if (e.isIntersecting && !reduce) {
      e.target.classList.add('play');
      replay.unobserve(e.target);
    }
  }),
  { threshold: 0.35 },
);
document.querySelectorAll('[data-replay]').forEach((el) => replay.observe(el));

const mockClock = document.querySelector<HTMLElement>('[data-clock]');
if (mockClock && !reduce) {
  let secs = 137;
  let timer = 0;
  // Ticks only while its mock is on screen.
  new IntersectionObserver(([e]) => {
    if (e?.isIntersecting && !timer) timer = window.setInterval(() => { secs += 1; mockClock.textContent = fmt(secs); }, 1000);
    else if (!e?.isIntersecting && timer) { clearInterval(timer); timer = 0; }
  }).observe(mockClock);
}

/* ---------- Line-art figures: 1px Iris strokes on canvas, drifting slowly ---------- */
const IRIS = '#8f74dc';
const FAINT = 'rgba(143,116,220,0.35)';
const INK = '#141030';
type Draw = (c: CanvasRenderingContext2D, w: number, h: number, t: number) => void;
// Each drawing fills the card's body below its header, inset by the same P on every side.
const P = 14;
const draw: Record<string, Draw> = {
  voice(c, w, h, t) {
    const n = 38, top = P, bot = h - P, mid = (top + bot) / 2;
    for (let i = 0; i < n; i++) {
      const x = P + (w - 2 * P) * i / (n - 1);
      const a = Math.sin(i * 0.33 + t * 1.4) * 0.5 + Math.sin(i * 0.11 - t * 0.7) * 0.5;
      const amp = (bot - top) / 2 * (0.15 + 0.85 * Math.abs(a));
      c.strokeStyle = i % 2 ? FAINT : IRIS;
      c.beginPath(); c.moveTo(x, mid - amp); c.lineTo(x, mid + amp); c.stroke();
    }
  },
  ribbon(c, w, h, t) {
    // The real mark as line art: both ribbon hooks hatched, the fold cross-hatched, a brighter band
    // sweeping along the ribbon, and a note being typed on the screen.
    const s = Math.min(w - 2 * P, h - 2 * P) / 64, ox = (w - 64 * s) / 2, oy = (h - 64 * s) / 2;
    const hook = new Path2D('M60 4 L54 16 H21 A5 5 0 0 0 21 26 H32 V38 H21 A17 17 0 0 1 21 4 Z');
    const fold = new Path2D('M16.17 19.71 L-10.04 29.05 L-8 64 L44 64 L44 19.71 Z');
    const sweep = (t * 14) % 150 - 75;
    c.save(); c.translate(ox, oy); c.scale(s, s); c.lineWidth = 1 / s;
    for (let r = 0; r < 2; r++) {
      c.save();
      if (r) { c.translate(32, 32); c.rotate(Math.PI); c.translate(-32, -32); }
      c.save(); c.clip(hook);
      for (let k = -72; k < 72; k += 1.7) {
        c.strokeStyle = Math.abs(k - sweep) < 5 ? IRIS : FAINT;
        c.beginPath(); c.moveTo(k - 10, -10); c.lineTo(k + 80, 80); c.stroke();
      }
      c.clip(fold);
      c.strokeStyle = FAINT;
      for (let k = -72; k < 150; k += 1.7) { c.beginPath(); c.moveTo(k, -10); c.lineTo(k - 90, 80); c.stroke(); }
      c.restore();
      c.strokeStyle = IRIS; c.stroke(hook);
      c.restore();
    }
    c.save(); c.beginPath(); c.roundRect(13, 17, 38, 30, 7); c.clip(); c.clearRect(-20, -20, 110, 110); c.restore();
    c.strokeStyle = IRIS; c.beginPath(); c.roundRect(16, 20, 32, 24, 4); c.stroke();
    c.fillStyle = IRIS;
    [21, 25.5, 30].forEach((x) => { c.beginPath(); c.arc(x, 25.5, 1.25, 0, Math.PI * 2); c.fill(); });
    const p = Math.min(1, (t * 0.35) % 1.4);
    c.fillStyle = INK; c.fillRect(20, 30.5, 22, 2.2);
    c.fillStyle = IRIS; c.fillRect(20, 36.5, 2 + 12 * p, 2.2);
    c.restore();
  },
  windows(c, w, h, t) {
    // Three remote windows stacked, the one in front outlined in Iris, a cursor moving in it.
    const dx = 14, dy = 9, ww = w - 2 * P - 2 * dx, hh = h - 2 * P - 2 * dy;
    for (let k = 2; k >= 0; k--) {
      const x = P + (2 - k) * dx, y = P + (2 - k) * dy;
      c.strokeStyle = k ? FAINT : IRIS;
      c.strokeRect(x + 0.5, y + 0.5, ww, hh);
      c.beginPath(); c.moveTo(x, y + 9.5); c.lineTo(x + ww, y + 9.5); c.stroke();
    }
    const fx = P + 2 * dx, fy = P + 2 * dy;
    const px = fx + ww * (0.25 + 0.5 * (Math.sin(t * 0.8) * 0.5 + 0.5));
    const py = fy + 12 + (hh - 26) * (0.5 + 0.4 * Math.sin(t * 1.3));
    c.fillStyle = INK; c.beginPath(); c.moveTo(px, py); c.lineTo(px + 7, py + 11); c.lineTo(px + 2, py + 9); c.lineTo(px, py + 14); c.closePath(); c.fill();
    c.strokeStyle = IRIS; c.beginPath(); c.arc(px, py, 5 + (t * 6 % 6), 0, Math.PI * 2); c.stroke();
  },
  note(c, w, h, t) {
    // The output: a ticket note being written, three steps beside their screenshots, then stamped.
    const x = P, y = P, nw = w - 2 * P - 5, nh = h - 2 * P - 5;
    c.strokeStyle = FAINT; c.strokeRect(x + 5.5, y + 5.5, nw, nh);
    c.clearRect(x, y, nw, nh);
    c.strokeStyle = IRIS; c.strokeRect(x + 0.5, y + 0.5, nw, nh);
    c.beginPath(); c.moveTo(x, y + 12.5); c.lineTo(x + nw, y + 12.5); c.stroke();
    c.fillStyle = IRIS; c.fillRect(x + 7, y + 5, nw * 0.34, 3);
    const cycle = (t * 0.22 + 1.2) % 1.5, rows = 3, top = y + 20, gap = (nh - 20 - 18) / rows;
    for (let k = 0; k < rows; k++) {
      const ly = top + k * gap, p = clamp(cycle * 3.4 - k * 0.9);
      c.fillStyle = FAINT; c.fillRect(x + 7, ly, (nw - 44) * p, 2);
      c.fillRect(x + 7, ly + 5, (nw - 66) * clamp(p * 1.6 - 0.6), 2);
      if (p > 0.5) { c.strokeStyle = IRIS; c.strokeRect(x + nw - 25.5, ly - 2.5, 18, 10); }
    }
    if (cycle > 1) { c.fillStyle = INK; c.fillRect(x + 7, y + nh - 14, nw - 14, 8); c.fillStyle = '#fff'; c.fillRect(x + 11, y + nh - 11, 14, 2); }
  },
  mask(c, w, h, t) {
    // A screen's text as dots; the middle run of one line is a password, painted over.
    const gx = 15, gy = 5, sx = (w - 2 * P) / (gx - 1), sy = (h - 2 * P) / (gy - 1);
    for (let i = 0; i < gx; i++) for (let j = 0; j < gy; j++) {
      const x = P + i * sx, y = P + j * sy;
      const masked = i > 3 && i < 11 && j === 2;
      c.fillStyle = masked ? INK : ((i + j + Math.floor(t * 2)) % 7 ? FAINT : IRIS);
      if (masked) c.fillRect(x - sx / 2, y - 3, sx + 0.5, 6); else c.fillRect(x - 1, y - 1, 2, 2);
    }
  },
  clicks(c, w, h, t) {
    const pts = [[0, 0.8], [0.2, 0.4], [0.4, 0.65], [0.6, 0.3], [0.8, 0.55], [1, 0.15]].map(([a, b]) => [P + 4 + a! * (w - 2 * P - 8), P + 4 + b! * (h - 2 * P - 8)] as const);
    c.strokeStyle = FAINT; c.beginPath(); pts.forEach(([x, y], i) => (i ? c.lineTo(x, y) : c.moveTo(x, y))); c.stroke();
    pts.forEach(([x, y], i) => { c.fillStyle = i === pts.length - 1 ? INK : IRIS; c.beginPath(); c.arc(x, y, 2.5, 0, Math.PI * 2); c.fill(); });
    const [lx, ly] = pts[pts.length - 1]!;
    c.strokeStyle = IRIS; c.beginPath(); c.arc(lx, ly, 4 + (t * 5 % 7), 0, Math.PI * 2); c.stroke();
  },
};

type Fig = { el: HTMLCanvasElement; ctx: CanvasRenderingContext2D; fn: Draw; on: boolean; w: number; h: number };
const figs: Fig[] = [];
document.querySelectorAll<HTMLCanvasElement>('canvas[data-draw]').forEach((el) => {
  const ctx = el.getContext('2d');
  const fn = draw[el.dataset.draw ?? ''];
  if (ctx && fn) figs.push({ el, ctx, fn, on: true, w: 0, h: 0 });
});
function sizeFig(o: Fig) {
  const r = o.el.getBoundingClientRect(), d = Math.min(2, devicePixelRatio || 1);
  o.w = r.width; o.h = r.height;
  o.el.width = Math.max(1, r.width * d); o.el.height = Math.max(1, r.height * d);
  o.ctx.setTransform(d, 0, 0, d, 0, 0);
}
function paint(t: number) {
  figs.forEach((o) => {
    if (!o.on || !o.w) return;
    o.ctx.clearRect(0, 0, o.w, o.h); o.ctx.lineWidth = 1;
    o.fn(o.ctx, o.w, o.h, t / 1000);
  });
}
figs.forEach(sizeFig);
paint(0);
// The loop runs only while a figure is on screen, and stops when none is.
let looping = false;
let last = 0;
const loop = (t: number) => {
  if (!figs.some((f) => f.on)) { looping = false; return; }
  if (t - last > 33) { paint(t); last = t; }
  requestAnimationFrame(loop);
};
const startLoop = () => { if (!reduce && !looping) { looping = true; requestAnimationFrame(loop); } };
const figVisible = new IntersectionObserver((es) => {
  es.forEach((e) => {
    const o = figs.find((f) => f.el === e.target);
    if (o) o.on = e.isIntersecting;
  });
  startLoop();
});
figs.forEach((o) => figVisible.observe(o.el));
// A rotation or a resized window changes each canvas's box; its pixels follow.
addEventListener('resize', () => { figs.forEach(sizeFig); paint(performance.now()); });

/* ---------- The pinned scene: scroll runs four beats ----------
   The problem week is read on the way in, so the scene moves from the first scroll after it pins.
   .02 to .32 a now line runs down each day, Monday to Friday, and every session it passes gets its note
   (the tally counts them, 38 to 46). It stops on Friday's #48213. .33 to .41 the rest of the week
   steps back; .36 to .5 #48213's tile opens in place into its note, written as it opens (.36 to .49).
   .66 the last caption and .68 published, held to the end so it is read before the page moves on.
   With reduced motion, or without this script, the section is the still layout in home.css. */
const sceneEl = byId('scene');
const wall = byId('wall');
const stageEl = byId('stage');
const tallyEl = byId('tally');
const noteEl = byId('scene-note');
const playhead = byId('playhead');
const phLabel = byId('ph-label');
if (sceneEl && wall && stageEl && tallyEl && noteEl && playhead && phLabel) {
  const tiles = Array.from(wall.querySelectorAll<HTMLElement>('.tile'));
  const dayEls = Array.from(wall.querySelectorAll<HTMLElement>('.day'));
  const colEls = dayEls.map((d) => d.querySelector<HTMLElement>('.col')!);
  const target = wall.querySelector<HTMLElement>('.tile.this') ?? tiles[tiles.length - 1]!;
  // The calendar: 08:00 to 17:00. Each session goes at its start, as tall as it ran. Done here, not in
  // the markup, because the CSP forbids inline styles; with reduced motion too, it is still a calendar.
  const DAY0 = 8 * 60, SPAN = 9 * 60;
  const start = tiles.map((t) => Number(t.dataset.start));
  const dayOf = tiles.map((t) => dayEls.indexOf(t.closest<HTMLElement>('.day')!));
  const weekEnd = Number(target.dataset.start) + Number(target.dataset.dur);
  tiles.forEach((t, i) => {
    t.style.top = `${(((start[i]! - DAY0) / SPAN) * 100).toFixed(3)}%`;
    t.style.height = `${((Number(t.dataset.dur) / SPAN) * 100).toFixed(3)}%`;
  });
  wall.classList.add('cal');
  const hhmm = (m: number) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(Math.floor(m % 60)).padStart(2, '0')}`;
  const caps = Array.from(document.querySelectorAll<HTMLElement>('.cap'));
  const noteParts = Array.from(noteEl.querySelectorAll<HTMLElement>('.sn-body h3, .sn-body .ln'));
  const stamp = noteEl.querySelector<HTMLElement>('.sn-stamp');
  let cols: { x: number; y: number; w: number; h: number }[] = [];
  // The note's transform at the start (on the tile) and at the end (open, fitted to the stage).
  let from = { x: 0, y: 0, s: 1 };
  let to = { x: 0, y: 0, s: 1 };
  let lastBeat = -1;
  let lastLabel = '';
  let pinned = false;
  // A window too short to hold the scene (a phone on its side) gets the still layout instead.
  const short = matchMedia('(max-height: 600px)');
  const measure = () => {
    wall.style.transform = '';
    noteEl.style.transform = '';
    // The wall and the note are scaled down to the room the stage has, so on any screen the whole
    // beat is inside the pinned view: nothing runs under the caption or off the bottom.
    const room = stageEl.clientHeight;
    const wallFit = clamp(room / Math.max(1, wall.offsetHeight), 0.4, 1);
    const noteFit = clamp(room / Math.max(1, noteEl.offsetHeight), 0.4, 1);
    // Scaled from the top of the stage, where an overflowing box starts; one that fits stays centred.
    wall.classList.toggle('scaled', wallFit < 1);
    noteEl.classList.toggle('scaled', noteFit < 1);
    if (wallFit < 1) wall.style.transform = `scale(${wallFit.toFixed(4)})`;
    // Each day's column, in the wall's own (unscaled) pixels, for the now line. From the boxes on screen
    // (offsetLeft would count from a day mid-way through its entrance, which has a transform).
    const wb = wall.getBoundingClientRect();
    cols = colEls.map((c) => {
      const r = c.getBoundingClientRect(), day = c.closest<HTMLElement>('.day')!;
      const lift = new DOMMatrixReadOnly(getComputedStyle(day).transform === 'none' ? undefined : getComputedStyle(day).transform).m42;
      return { x: (r.left - wb.left) / wallFit - wall.clientLeft, y: (r.top - wb.top) / wallFit - lift - wall.clientTop, w: r.width / wallFit, h: r.height / wallFit };
    });
    const n = noteEl.getBoundingClientRect();
    const r = target.getBoundingClientRect();
    // A small copy of the note, as wide as the tile and on its spot, so it is never stretched.
    from = { x: r.left - n.left, y: r.top - n.top, s: r.width / n.width };
    to = { x: (n.width * (1 - noteFit)) / 2, y: 0, s: noteFit };
  };
  const read: Step = () => {
    if (!pinned) return;
    const r = sceneEl.getBoundingClientRect();
    // 0 the moment the scene pins under the 60px header, 1 when it lets go.
    const p = clamp((60 - r.top) / Math.max(1, r.height - innerHeight + 60));
    return () => write(p);
  };
  const tick = () => read()?.();
  const write = (p: number) => {
    const beat = p < 0.02 ? 0 : p < 0.36 ? 1 : p < 0.66 ? 2 : 3;
    wall.classList.toggle('opening', p > 0.3 && p < 0.55);
    if (beat !== lastBeat) {
      lastBeat = beat;
      // Only what is seen changes: all four captions stay readable to assistive technology.
      caps.forEach((c, i) => c.classList.toggle('on', i === beat));
    }
    // The now line: how far through the week (0 Monday 08:00, 1 Friday as #48213 ends). It runs down
    // each day from 08:00 to 17:00, then on to the next; Friday stops at the end of #48213.
    const sweep = clamp((p - 0.02) / 0.3);
    const u = Math.min(sweep * 5, 4.9999);
    const d = Math.floor(u);
    const minute = DAY0 + (u - d) * ((d === 4 ? weekEnd : DAY0 + SPAN) - DAY0);
    const col = cols[d];
    if (col) {
      playhead.style.width = `${col.w}px`;
      playhead.style.transform = `translate(${col.x.toFixed(1)}px, ${(col.y + (col.h * (minute - DAY0)) / SPAN).toFixed(1)}px)`;
    }
    // The rest of the week steps back once the line has stopped; the line goes with it.
    const back = ease(clamp((p - 0.33) / 0.08));
    playhead.style.opacity = String(clamp(p / 0.02) * (1 - back));
    dayEls.forEach((el, i) => el.classList.toggle('now', i === d && p > 0.01 && back < 1));
    // The tally is the count of tiles that have their note, so the number and the wall always agree.
    let written = 0;
    tiles.forEach((t, i) => {
      // A session gets its note once the line has passed its start.
      const healed = p > 0.02 && (dayOf[i]! < d || (dayOf[i] === d && start[i]! < minute));
      t.classList.toggle('healed', healed);
      if (healed || !t.classList.contains('bad')) written++;
      t.style.opacity = t === target ? '' : back ? String(1 - 0.82 * back) : '';
    });
    const label = hhmm(minute);
    if (label !== lastLabel) { lastLabel = label; phLabel.textContent = label; }
    tallyEl.textContent = String(written);
    tallyEl.classList.toggle('full', written === tiles.length);
    // #48213 opens in place: the note starts on its tile's box and grows to its own.
    const g = ease(clamp((p - 0.36) / 0.14));
    // The tile hands over to the note in the first part of the move, so neither is ever seen empty.
    const hand = clamp(g / 0.2);
    target.style.opacity = g > 0 ? String(1 - hand) : '';
    noteEl.style.opacity = String(hand);
    const x = from.x + (to.x - from.x) * g, y = from.y + (to.y - from.y) * g, s = from.s + (to.s - from.s) * g;
    noteEl.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px) scale(${s.toFixed(4)})`;
    // It is written while it opens: the last line lands just after it has settled.
    noteParts.forEach((el, i) => el.style.setProperty('--o', clamp((p - 0.36 - i * 0.012) / 0.03).toFixed(3)));
    stamp?.classList.toggle('on', p > 0.68);
  };
  const still = () => {
    tiles.forEach((t) => { t.style.opacity = ''; t.classList.remove('healed'); });
    dayEls.forEach((el) => el.classList.remove('now'));
    wall.style.transform = '';
    wall.classList.remove('scaled', 'opening');
    noteEl.classList.remove('scaled');
    noteEl.style.opacity = '';
    noteEl.style.transform = '';
    noteParts.forEach((el) => el.style.removeProperty('--o'));
    tallyEl.textContent = String(tiles.filter((t) => !t.classList.contains('bad')).length);
    tallyEl.classList.remove('full');
    lastBeat = -1;
  };
  const arrange = () => {
    pinned = !short.matches;
    document.documentElement.classList.toggle('js-scene', pinned);
    if (pinned) { measure(); tick(); } else still();
  };
  if (!reduce) {
    arrange();
    steps.push(read);
    addEventListener('resize', arrange);
    void document.fonts?.ready.then(arrange);
  }
}

/* ---------- The pill clock: session time runs 00:00 to 10:00 down the page ---------- */
const pill = byId('clockpill');
const pillLabel = byId('cp-label');
const pillClock = byId('cp-clock');
const lastRow = document.querySelector<HTMLElement>('.feature[data-pill="published"]');
if (pill && pillLabel && pillClock && lastRow) {
  const names: Record<string, string> = { rec: 'Recording', paused: 'Paused: sensitive field', drafting: 'Drafting', published: 'Published to #48213' };
  const marks = Array.from(document.querySelectorAll<HTMLElement>('[data-pill]'));
  const session = byId('session');
  const read: Step = () => {
    const last = lastRow.getBoundingClientRect();
    const endY = last.top + scrollY - innerHeight * 0.5;
    const t = fmt(clamp(scrollY / Math.max(1, endY)) * 600);
    let s = 'rec';
    for (const m of marks) if (m.getBoundingClientRect().top <= innerHeight * 0.55) s = m.dataset.pill ?? s;
    // Shown for the session it narrates: from "One session, start to ticket" to the end of the four
    // stages. It stays out of the hero, out of the pinned scene, and off everything after the stages,
    // so it never sits on a button, a disclaimer or a form field.
    const from = session ? session.getBoundingClientRect().top : 0;
    const shown = from < innerHeight * 0.7 && last.bottom > innerHeight * 0.6;
    return () => {
      pillClock.textContent = t;
      if (pill.dataset.s !== s) { pill.dataset.s = s; pillLabel.textContent = names[s] ?? ''; }
      pill.classList.toggle('shown', shown);
    };
  };
  const tick = () => read()?.();
  steps.push(read);
  addEventListener('resize', tick);
  tick();
}

/* ---------- The session ribbon: the logo's band threaded through the events ---------- */
const eventsBox = byId('events');
const ribbon = byId<SVGSVGElement>('ribbon');
if (eventsBox && ribbon) {
  const NS = 'http://www.w3.org/2000/svg';
  const drawRibbon = () => {
    const box = eventsBox.getBoundingClientRect();
    const pts = Array.from(eventsBox.querySelectorAll('.knot')).map((k) => {
      const r = k.getBoundingClientRect();
      return { x: r.left - box.left + 16, y: r.top - box.top + r.height / 2 };
    });
    if (pts.length < 2) return;
    const first = pts[0]!, lastPt = pts[pts.length - 1]!;
    const column = Math.abs(pts[1]!.x - first.x) < 4, W = column ? 6 : 10, edge = box.width + 6;
    const k = Math.max(16, Math.min(80, (innerWidth - box.right - 8) / 0.75));
    let d = column ? `M ${first.x} ${first.y - 40}` : `M ${-box.left} ${first.y}`;
    d += ` L ${first.x} ${first.y}`;
    const folds: string[] = [];
    for (let i = 1; i < pts.length; i++) {
      const a = pts[i - 1]!, b = pts[i]!;
      if (column || Math.abs(a.y - b.y) < 4) { d += ` L ${b.x} ${b.y}`; continue; }
      const turn = `C ${edge + k} ${a.y} ${edge + k} ${b.y} ${edge} ${b.y}`;
      d += ` L ${edge} ${a.y} ${turn} L ${b.x} ${b.y}`;
      folds.push(`M ${edge} ${a.y} ${turn}`);
    }
    if (!column) d += ` L ${-box.left} ${lastPt.y}`;
    const path = (cls: string, dd: string) => {
      const el = document.createElementNS(NS, 'path');
      el.setAttribute('class', cls);
      el.setAttribute('d', dd);
      el.setAttribute('stroke-width', String(W));
      return el;
    };
    ribbon.replaceChildren(path('band', d), ...folds.map((f) => path('fold', f)));
  };
  addEventListener('resize', drawRibbon);
  drawRibbon();
  void document.fonts?.ready.then(drawRibbon);
}

/* ---------- App screenshots that pan on a narrow screen can be panned from the keyboard ---------- */
const pans = Array.from(document.querySelectorAll<HTMLElement>('.pan'));
const markPans = () => pans.forEach((el) => {
  if (el.scrollWidth > el.clientWidth + 1) {
    el.tabIndex = 0;
    el.setAttribute('role', 'group');
    el.setAttribute('aria-label', 'Screenshot, scrolls sideways');
  } else {
    el.removeAttribute('tabindex');
    el.removeAttribute('role');
    el.removeAttribute('aria-label');
  }
});
addEventListener('resize', markPans);
addEventListener('load', markPans);
markPans();

/* ---------- A link to a place far down the page jumps most of the way ----------
   Smooth scrolling 4,000px ran the pinned scene in fast forward on the way. A target more than two
   screens off is jumped to within half a screen, and the last stretch is still a smooth scroll, so
   the reader sees where they landed. */
document.addEventListener('click', (e) => {
  const a = (e.target as Element).closest<HTMLAnchorElement>('a[href*="#"]');
  if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const url = new URL(a.href);
  if (url.pathname !== location.pathname || !url.hash) return;
  const target = document.getElementById(decodeURIComponent(url.hash.slice(1)));
  if (!target) return;
  const top = target.getBoundingClientRect().top;
  if (reduce || Math.abs(top) < innerHeight * 2) return;
  const html = document.documentElement;
  html.style.scrollBehavior = 'auto';
  scrollBy(0, top - Math.sign(top) * innerHeight * 0.5);
  html.style.scrollBehavior = '';
  // The link then does what it always did: a smooth scroll the rest of the way, and the address.
});

/* ---------- The small-screen menu closes when one of its links is followed ---------- */
const menu = document.querySelector<HTMLDetailsElement>('details.menu');
menu?.addEventListener('click', (e) => { if ((e.target as Element).closest('a')) menu.open = false; });

/* ---------- Ctrl+Enter sends the form, as Publish does in the app ---------- */
const form = byId<HTMLFormElement>('pilot-form');
form?.addEventListener('keydown', (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
    e.preventDefault();
    form.requestSubmit();
  }
});
