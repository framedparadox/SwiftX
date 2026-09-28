/* ==========================================================================
   SwiftX — marketing site behaviour
   The orbit here is a small web port of the real one: eight slots 45° apart
   from straight up, Settings and Close pinned to the last two, groups that
   open a concentric outer orbit, a quadrant that grows its radius only as far
   as it must. Everything runs in the page; nothing is sent anywhere.
   ========================================================================== */
(() => {
  'use strict';

  document.documentElement.classList.remove('no-js');

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const TAU = Math.PI * 2;

  // ---- Icons -------------------------------------------------------------
  // Line glyphs in the spirit of Segoe Fluent, drawn on a 24-unit grid.

  const cog = (() => {
    const n = 8, ro = 9.2, ri = 7.2, pts = [];
    for (let i = 0; i < n; i++) {
      const t = (i / n) * TAU;
      [[-0.32, ri], [-0.17, ro], [0.17, ro], [0.32, ri]].forEach(([da, r]) =>
        pts.push([12 + Math.cos(t + da) * r, 12 + Math.sin(t + da) * r]));
    }
    return 'M' + pts.map(p => p.map(v => v.toFixed(2)).join(' ')).join('L') + 'Z';
  })();

  const ICONS = {
    notepad: '<path d="M7 3h7l4 4v14H7z"/><path d="M14 3v4h4"/><path d="M9.8 11.5h5.4M9.8 14.5h5.4M9.8 17.5h3.2"/>',
    calc: '<rect x="6" y="3" width="12" height="18" rx="2.2"/><rect x="8.6" y="5.6" width="6.8" height="3.4" rx=".7"/><path d="M9.2 12.6h.01M12 12.6h.01M14.8 12.6h.01M9.2 15.6h.01M12 15.6h.01M14.8 15.6h.01M9.2 18.4h.01M12 18.4h.01M14.8 18.4h.01" stroke-width="2.1"/>',
    wrench: '<path d="M14.6 3.6a4.6 4.6 0 0 0-4.1 6.3L4.3 16.1a1.9 1.9 0 0 0 2.7 2.7l6.2-6.2a4.6 4.6 0 0 0 6.3-4.1L17 10.6l-2.6-.4-.4-2.6z"/>',
    folder: '<path d="M3.5 7.2c0-1 .8-1.7 1.7-1.7h4.1l2 2h7.5c1 0 1.7.8 1.7 1.7v7.9c0 1-.8 1.7-1.7 1.7H5.2c-1 0-1.7-.8-1.7-1.7z"/><path d="M3.5 10h17"/>',
    download: '<path d="M12 4v10.5M7.6 10.4 12 14.8l4.4-4.4M5 19h14"/>',
    globe: '<circle cx="12" cy="12" r="8.5"/><path d="M3.5 12h17M12 3.5c2.4 2.5 3.5 5.3 3.5 8.5s-1.1 6-3.5 8.5c-2.4-2.5-3.5-5.3-3.5-8.5s1.1-6 3.5-8.5z"/>',
    gear: `<path d="${cog}"/><circle cx="12" cy="12" r="3"/>`,
    close: '<path d="M7.5 7.5l9 9M16.5 7.5l-9 9"/>',
    braces: '<path d="M9 4.5c-1.8 0-2.4.8-2.4 2.6v2c0 1.4-.7 2.4-2 2.9 1.3.5 2 1.5 2 2.9v2c0 1.8.6 2.6 2.4 2.6M15 4.5c1.8 0 2.4.8 2.4 2.6v2c0 1.4.7 2.4 2 2.9-1.3.5-2 1.5-2 2.9v2c0 1.8-.6 2.6-2.4 2.6"/>',
    clock: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.3V12l3.1 2"/>',
    lock: '<rect x="5.5" y="10.5" width="13" height="10" rx="2.2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5M12 14.4v2.4"/>',
    b64: '<text x="12" y="15.2" text-anchor="middle" font-size="8.6" font-weight="700" font-family="Segoe UI, Inter, system-ui, sans-serif">B64</text>',
    cert: '<rect x="3.5" y="5" width="17" height="11.5" rx="1.6"/><path d="M7 9h6M7 12h3.5"/><circle cx="16" cy="12" r="2.2"/><path d="M14.8 13.9l-.5 4.6 1.7-1 1.7 1-.5-4.6"/>',
    bolt: '<path d="M13.2 3.2 5.6 13.4h5.8l-.8 7.4 7.8-10.3h-5.9z"/>',
    plus: '<path d="M12 5.5v13M5.5 12h13"/>',
    terminal: '<rect x="3.5" y="5" width="17" height="14" rx="2.2"/><path d="m7.5 10 2.5 2-2.5 2M12.5 15h4"/>',
    mail: '<rect x="3.5" y="6" width="17" height="12" rx="2"/><path d="m4 7 8 6 8-6"/>',
    link: '<path d="M10 14a4 4 0 0 0 5.7 0l2.8-2.8a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-2.8 2.8a4 4 0 0 0 5.7 5.7l1-1"/>',
    photo: '<rect x="3.5" y="5" width="17" height="14" rx="2.2"/><circle cx="9" cy="10" r="1.6"/><path d="m4 17 5-4.5 3.5 3 3-2.5 4.5 4"/>',
  };
  const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] || ''}</svg>`;

  // ---- The mark ----------------------------------------------------------
  // AppLogo.cs, point for point: a 48-unit box, a 1.2-unit cross-shaped gap,
  // each petal a square with two opposite corners rounded at 46.6% of its edge.

  const PS = 23.4, CR = +(PS * 0.466).toFixed(4), FAR = 24.6;
  const MAIN = `M0 0H${(PS - CR).toFixed(4)}A${CR} ${CR} 0 0 1 ${PS} ${CR}V${PS}H${CR}A${CR} ${CR} 0 0 1 0 ${(PS - CR).toFixed(4)}Z`;
  const ANTI = `M${PS} 0V${(PS - CR).toFixed(4)}A${CR} ${CR} 0 0 1 ${(PS - CR).toFixed(4)} ${PS}H0V${CR}A${CR} ${CR} 0 0 1 ${CR} 0Z`;
  const PETALS = [
    { d: MAIN, x: 0, y: 0, g: [0, 0, 1, 1], c: ['#FF0A76', '#FFD510'] },
    { d: ANTI, x: FAR, y: 0, g: [1, 0, 0, 1], c: ['#2CD098', '#1D4ADB'] },
    { d: MAIN, x: FAR, y: FAR, g: [1, 1, 0, 0], c: ['#F1067E', '#1A65B5'] },
    { d: ANTI, x: 0, y: FAR, g: [0, 1, 1, 0], c: ['#2CD098', '#1D4ADB'] },
  ];
  let markSeq = 0;
  function markSVG({ anim = false, cls = '', label = '' } = {}) {
    const id = 'mk' + (++markSeq);
    const defs = PETALS.map((p, i) =>
      `<linearGradient id="${id}g${i}" x1="${p.g[0]}" y1="${p.g[1]}" x2="${p.g[2]}" y2="${p.g[3]}"><stop offset="0" stop-color="${p.c[0]}"/><stop offset="1" stop-color="${p.c[1]}"/></linearGradient>`).join('');
    const body = PETALS.map((p, i) =>
      `<g transform="translate(${p.x} ${p.y})"><path class="petal p${i}" d="${p.d}" fill="url(#${id}g${i})"/></g>`).join('');
    const a11y = label ? `role="img" aria-label="${label}"` : 'aria-hidden="true"';
    return `<svg class="mark${anim ? ' mark-anim' : ''} ${cls}" viewBox="0 0 48 48" ${a11y}><defs>${defs}</defs><g>${body}</g></svg>`;
  }
  // Anything marked data-mark gets the live, gradient mark drawn into it.
  $$('[data-mark]').forEach(el => {
    el.innerHTML = markSVG({ anim: el.dataset.mark === 'anim', label: el.dataset.label || '' });
  });

  // ---- Toast -------------------------------------------------------------

  const toastEl = $('#toast');
  let toastTimer;
  function toast(msg) {
    if (!toastEl) return;
    toastEl.textContent = msg;
    toastEl.classList.add('is-on');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toastEl.classList.remove('is-on'), 2600);
  }

  // ---- Items -------------------------------------------------------------

  const TOOLS = [
    { name: 'Data formatter', icon: 'braces', kind: 'tool', tool: 'data' },
    { name: 'Quick Format', icon: 'clock', kind: 'tool', tool: 'quick' },
    { name: 'Secure values', icon: 'lock', kind: 'tool', tool: 'secure' },
    { name: 'Base64', icon: 'b64', kind: 'tool', tool: 'base64' },
    { name: 'Certificate', icon: 'cert', kind: 'tool', tool: 'cert' },
  ];
  const SETTINGS = { name: 'Settings', icon: 'gear', kind: 'settings', action: true, children: [{ name: 'Add new', icon: 'plus', kind: 'add' }] };
  const CLOSE = { name: 'Close', icon: 'close', kind: 'close' };
  const ORBIT_ITEMS = [
    { name: 'Notepad', icon: 'notepad' },
    { name: 'Calculator', icon: 'calc' },
    { name: 'Tools', icon: 'wrench', children: TOOLS },
    { name: 'File Explorer', icon: 'folder' },
    { name: 'Downloads', icon: 'download' },
    { name: 'GitHub', icon: 'globe' },
    SETTINGS,
    CLOSE,
  ];
  const QUAD_ITEMS = [
    { name: 'QuickAccess', icon: 'bolt', children: [
      { name: 'GitHub', icon: 'globe' },
      { name: 'Outlook', icon: 'mail' },
      { name: 'Terminal', icon: 'terminal' },
      { name: 'Photos', icon: 'photo' },
    ] },
    { name: 'Notepad', icon: 'notepad' },
    { name: 'Tools', icon: 'wrench', children: TOOLS },
    SETTINGS,
  ];

  const SIZES = { small: 44, medium: 56, large: 68 };

  function contrastInk(hex) {
    const h = hex.replace('#', '');
    const [r, g, b] = [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16) / 255)
      .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.42 ? '#111111' : '#ffffff';
  }

  // The quarter circle grows its radius only as far as it must: neighbours may
  // not touch, and the first and last must clear the two screen edges.
  function quadrantGeometry(r, b, n) {
    let R = Math.max(r, b);
    for (let guard = 0; guard < 400; guard++, R += 3) {
      const clear = Math.asin(Math.min(1, (b / 2 + 10) / R));
      const span = Math.PI / 2 - 2 * clear;
      if (span <= 0) continue;
      const step = n > 1 ? span / (n - 1) : 0;
      if (n < 2 || 2 * R * Math.sin(step / 2) >= b + 10) {
        return { R, angles: Array.from({ length: n }, (_, k) => (n > 1 ? clear + k * step : Math.PI / 4)) };
      }
    }
    return { R, angles: Array.from({ length: n }, (_, k) => (k + 0.5) * (Math.PI / 2) / n) };
  }
  const toCanvas = {
    tl: a => a, tr: a => Math.PI - a, bl: a => -a, br: a => Math.PI + a,
  };

  // ---- Orbit -------------------------------------------------------------

  class Orbit {
    constructor(host, opts = {}) {
      this.host = host;
      this.o = Object.assign({
        radius: 150, size: 56, shape: 'circle', hover: 'invert', zoom: 'in', tint: '#111111', motion: 'pulse',
        theme: 'dark', layout: 'full', corner: 'tl', anchor: 'center', fitTo: 'ring', hub: true, smallHub: false,
        items: ORBIT_ITEMS, label: 'SwiftX orbit', onAction: null, onDismiss: null,
      }, opts);
      this.isOpen = false;
      this.orbs = [];
      this.fly = [];
      this.group = null;
      this.pillOwner = null;
      this.dwell = 0;

      const el = this.el = document.createElement('div');
      el.className = 'orbit';
      el.setAttribute('role', 'group');
      el.setAttribute('aria-label', this.o.label);
      el.innerHTML = '<div class="orbit-layer"><div class="orbit-scale"><div class="orbit-pill" aria-hidden="true"></div></div></div>';
      this.layer = el.firstChild;
      this.scaleEl = this.layer.firstChild;
      this.pill = this.scaleEl.firstChild;

      const hub = this.hub = document.createElement('button');
      hub.type = 'button';
      hub.className = 'orbit-hub';
      hub.setAttribute('aria-label', 'SwiftX hub: summon or dismiss the orbit');
      hub.innerHTML = markSVG();
      hub.addEventListener('click', e => { e.stopPropagation(); this.hubClick(); });
      this.scaleEl.appendChild(hub);

      host.appendChild(el);
      this.apply();
      this.setItems(this.o.items);
      if ('ResizeObserver' in window) new ResizeObserver(() => this.layout()).observe(host);
      else addEventListener('resize', () => this.layout());
    }

    set(opts) {
      Object.assign(this.o, opts);
      this.apply();
      if ('items' in opts) this.setItems(this.o.items);
      else this.layout();
    }

    apply() {
      const o = this.o, d = this.el.dataset;
      d.theme = o.theme; d.shape = o.shape; d.hover = o.hover; d.zoom = o.zoom;
      this.el.style.setProperty('--bubble', o.size + 'px');
      this.el.style.setProperty('--hover-tint', o.tint);
      this.el.style.setProperty('--tint-glyph', contrastInk(o.tint));
      this.el.style.setProperty('--motion', 'm-' + o.motion);
      this.el.classList.toggle('is-small-hub', !!o.smallHub);
      this.hub.hidden = !o.hub || o.layout === 'quadrant';
    }

    setItems(items) {
      this.closeGroup(true);
      this.orbs.forEach(b => b.remove());
      this.orbs = items.map((item, i) => this.makeOrb(item, i, false));
      this.layout();
    }

    makeOrb(item, i, child) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'orb' + (item.kind === 'close' ? ' is-close' : '');
      b.style.setProperty('--i', i);
      b.setAttribute('aria-label', item.name + (item.children && !item.action ? ', group' : ''));
      b.innerHTML = `<span class="orb-face">${icon(item.icon)}</span>` +
        (item.children && !item.action ? '<span class="orb-arc"></span>' : '');
      b._item = item;
      b._child = child;
      b.addEventListener('pointerenter', e => { if (e.pointerType !== 'touch') this.enter(b); });
      b.addEventListener('pointerleave', e => { if (e.pointerType !== 'touch') this.leave(b); });
      b.addEventListener('focus', () => this.enter(b));
      b.addEventListener('blur', () => this.leave(b));
      b.addEventListener('click', e => { e.stopPropagation(); this.activate(b); });
      this.scaleEl.appendChild(b);
      return b;
    }

    layout() {
      const o = this.o, w = this.host.clientWidth, h = this.host.clientHeight;
      if (!w || !h) return;
      const b = o.size, quad = o.layout === 'quadrant', n = this.orbs.length;
      let R = o.radius, angles;
      if (quad) {
        const g = quadrantGeometry(o.radius, b, n);
        R = g.R;
        angles = g.angles;
      } else {
        angles = this.orbs.map((_, k) => (-90 + 45 * k) * Math.PI / 180);
      }
      this.R = R;

      const flyOut = R + b * 1.5 + b / 2 + 8;
      const ring = R + b / 2 + 12;
      const need = o.fitTo === 'flyout' ? flyOut : ring;
      const avail = quad ? Math.min(w, h) - 6 : Math.min(w, h) / 2 - (o.anchor === 'center' ? 8 : 4);
      const fit = this.fit = Math.max(0.35, Math.min(1, avail / need));
      this.scaleEl.style.transform = `scale(${fit})`;

      const right = o.corner[1] === 'r', bottom = o.corner[0] === 'b';
      let cx = w / 2, cy = h / 2;
      if (quad) { cx = right ? w : 0; cy = bottom ? h : 0; }
      else if (o.anchor === 'corner') {
        const e = (R + b / 2 + 14) * fit;
        cx = right ? w - e : e;
        cy = bottom ? h - e : e;
      }
      this.layer.style.left = cx + 'px';
      this.layer.style.top = cy + 'px';

      this.orbs.forEach((orb, k) => {
        const local = angles[k];
        const t = quad ? toCanvas[o.corner](local) : local;
        this.place(orb, t, R, local);
      });
      if (this.group) this.placeFlyout(false);
      if (this.pillOwner) this.showPill(this.pillOwner);
    }

    place(orb, t, R, local) {
      const x = Math.cos(t) * R, y = Math.sin(t) * R;
      orb._pos = [x, y];
      orb._dir = [Math.cos(t), Math.sin(t)];
      orb._local = local;
      orb.style.setProperty('--tx', x.toFixed(2) + 'px');
      orb.style.setProperty('--ty', y.toFixed(2) + 'px');
      orb.style.setProperty('--deg', (t * 180 / Math.PI + 90).toFixed(1) + 'deg');
      const arc = orb.querySelector('.orb-arc');
      if (arc) arc.style.setProperty('--deg', (t * 180 / Math.PI + 90).toFixed(1) + 'deg');
    }

    // A group opens into a concentric outer orbit; the inner one never moves.
    placeFlyout(fresh) {
      const o = this.o, g = this.group, n = this.fly.length, b = o.size;
      const quad = o.layout === 'quadrant';
      const R2 = this.R + b * 1.5;
      let step = (b + 14) / R2;
      let list = this.fly.map((_, j) => g._local + (j - (n - 1) / 2) * step);
      if (quad && n) {
        const lo = Math.asin(Math.min(1, (b / 2 + 8) / R2)), hi = Math.PI / 2 - lo;
        if ((n - 1) * step > hi - lo) {
          step = (hi - lo) / Math.max(1, n - 1);
          list = this.fly.map((_, j) => g._local + (j - (n - 1) / 2) * step);
        }
        const min = list[0], max = list[n - 1];
        const shift = min < lo ? lo - min : max > hi ? hi - max : 0;
        list = list.map(a => a + shift);
      }
      this.fly.forEach((orb, j) => {
        const t = quad ? toCanvas[o.corner](list[j]) : list[j];
        if (fresh) {
          orb.style.setProperty('--tx', g._pos[0].toFixed(2) + 'px');
          orb.style.setProperty('--ty', g._pos[1].toFixed(2) + 'px');
          orb._target = [t, R2, list[j]];
        } else {
          this.place(orb, t, R2, list[j]);
        }
      });
    }

    enter(b) {
      clearTimeout(this.dwell);
      this.allOrbs().forEach(x => { if (x !== b && x !== this.group) x.classList.remove('is-hot'); });
      b.classList.add('is-hot');
      // An open group's own children sit where its label would; they carry the labels now.
      if (b !== this.group) this.showPill(b);
      const item = b._item;
      if (item.children && b !== this.group && !b._child) {
        this.dwell = setTimeout(() => this.openGroup(b), 260);
      } else if (!b._child && this.group && b !== this.group) {
        // Sweeping past on the way to a neighbour does not close it; resting does.
        this.dwell = setTimeout(() => this.closeGroup(), 260);
      }
    }

    leave(b) {
      clearTimeout(this.dwell);
      if (b !== this.group) b.classList.remove('is-hot');
      if (this.pillOwner === b) this.hidePill();
    }

    activate(b) {
      const item = b._item;
      if (item.children && !item.action) { this.openGroup(b); return; }
      if (item.kind === 'close') { this.close('close'); return; }
      const keepOpen = item.kind === 'settings' || item.kind === 'add';
      if (this.o.onAction) this.o.onAction(item, this);
      if (!keepOpen) this.close('launch');
    }

    openGroup(b) {
      if (this.group === b) return;
      this.closeGroup(true);
      this.group = b;
      b.classList.add('is-hot');
      if (this.pillOwner === b) this.hidePill();
      this.fly = b._item.children.map((child, j) => {
        const orb = this.makeOrb(child, j, true);
        orb.classList.add('is-home');
        return orb;
      });
      this.placeFlyout(true);
      void this.scaleEl.offsetWidth;
      requestAnimationFrame(() => {
        this.fly.forEach(orb => {
          if (!orb._target) return;
          const [t, R2, local] = orb._target;
          this.place(orb, t, R2, local);
          orb.classList.remove('is-home');
        });
      });
    }

    closeGroup(immediate) {
      clearTimeout(this.dwell);
      const g = this.group;
      if (!g) return;
      const kids = this.fly;
      this.fly = [];
      this.group = null;
      if (!g.matches(':hover')) g.classList.remove('is-hot');
      if (this.pillOwner && this.pillOwner._child) this.hidePill();
      kids.forEach(orb => {
        if (immediate) { orb.remove(); return; }
        orb.style.setProperty('--tx', g._pos[0].toFixed(2) + 'px');
        orb.style.setProperty('--ty', g._pos[1].toFixed(2) + 'px');
        orb.classList.add('is-home');
        setTimeout(() => orb.remove(), 420);
      });
    }

    allOrbs() { return this.orbs.concat(this.fly); }

    showPill(b) {
      if (!b._pos) return;
      const [x, y] = b._pos, [dx, dy] = b._dir;
      const off = this.o.size / 2 * 1.14 + 9;
      const p = this.pill;
      p.textContent = b._item.name;
      p.style.setProperty('--px', (x + dx * off).toFixed(1) + 'px');
      p.style.setProperty('--py', (y + dy * off).toFixed(1) + 'px');
      p.style.setProperty('--sx', dx > 0.35 ? '0%' : dx < -0.35 ? '-100%' : '-50%');
      p.style.setProperty('--sy', dy > 0.35 ? '0%' : dy < -0.35 ? '-100%' : '-50%');
      p.classList.add('is-on');
      this.pillOwner = b;
    }

    hidePill() { this.pill.classList.remove('is-on'); this.pillOwner = null; }

    open() {
      if (this.isOpen) return;
      this.isOpen = true;
      this.el.classList.add('is-open');
    }

    close(reason = 'dismiss') {
      if (!this.isOpen) return;
      this.closeGroup(false);
      this.hidePill();
      this.allOrbs().forEach(b => b.classList.remove('is-hot'));
      this.isOpen = false;
      this.el.classList.remove('is-open');
      if (this.o.onDismiss) this.o.onDismiss(reason, this);
    }

    toggle() { this.isOpen ? this.close('toggle') : this.open(); }

    hubClick() {
      if (this.group) this.closeGroup();
      else this.toggle();
    }

    // Esc closes an open group first, then the orbit.
    escape() {
      if (this.group) { this.closeGroup(); return true; }
      if (this.isOpen) { this.close('esc'); return true; }
      return false;
    }

    // Re-plays the entrance from the hub.
    replay() {
      if (this.isOpen) {
        this.close('replay');
        setTimeout(() => this.open(), 420);
      } else this.open();
    }
  }

  const whenVisible = (el, fn, threshold = 0.35) => {
    if (!('IntersectionObserver' in window)) { fn(); return; }
    const io = new IntersectionObserver(entries => {
      if (entries.some(e => e.isIntersecting)) { io.disconnect(); fn(); }
    }, { threshold });
    io.observe(el);
  };

  // What a click on an orbit item does on this page.
  function pageAction(item) {
    if (item.kind === 'settings') {
      toast('Settings opens the SwiftX app. Here, it takes you to Customize.');
      $('#customize')?.scrollIntoView({ behavior: 'smooth' });
      return;
    }
    if (item.kind === 'add') { toast('“Add new” opens the Add to orbit dialog.'); return; }
    if (item.kind === 'tool') {
      toast(`${item.name} opens in its own window. Try one below.`);
      const tab = item.tool === 'base64' ? 'base64' : 'data';
      selectToolTab(tab);
      $('#tools')?.scrollIntoView({ behavior: 'smooth' });
      return;
    }
    toast(`${item.name} launches, and the orbit gets out of the way.`);
  }

  // ---- Hero orbit --------------------------------------------------------

  const heroHost = $('#hero-orbit');
  let heroOrbit;
  if (heroHost) {
    let reopen;
    heroOrbit = new Orbit(heroHost, {
      radius: 136, size: 56, fitTo: 'ring', label: 'Interactive SwiftX orbit',
      onAction: pageAction,
      onDismiss: reason => {
        clearTimeout(reopen);
        if (reason === 'launch') reopen = setTimeout(() => heroOrbit.open(), 1500);
      },
    });
    const hint = document.createElement('div');
    hint.className = 'orbit-empty-hint';
    hint.textContent = 'Click the mark to summon it again';
    heroHost.appendChild(hint);
    setTimeout(() => heroOrbit.open(), 450);
  }

  // ---- Summon anywhere: Ctrl+Shift+X -------------------------------------

  const summonEl = $('#summon-layer');
  let summonOrbit;
  if (summonEl) {
    summonOrbit = new Orbit(summonEl, {
      radius: 150, size: 58, label: 'SwiftX orbit, summoned',
      onAction: pageAction,
      onDismiss: () => { summonEl.classList.remove('is-up'); summonEl.setAttribute('aria-hidden', 'true'); },
    });
    summonEl.addEventListener('click', () => summonOrbit.close('outside'));
  }
  function summon() {
    if (!summonOrbit) return;
    if (summonOrbit.isOpen) { summonOrbit.close('toggle'); return; }
    summonEl.classList.add('is-up');
    summonEl.removeAttribute('aria-hidden');
    summonOrbit.layout();
    requestAnimationFrame(() => summonOrbit.open());
  }
  $$('[data-summon]').forEach(b => b.addEventListener('click', e => { e.preventDefault(); summon(); }));

  const keycaps = {
    Control: $$('[data-key="Control"]'),
    Shift: $$('[data-key="Shift"]'),
    X: $$('[data-key="X"]'),
  };
  const setKey = (k, on) => (keycaps[k] || []).forEach(el => el.classList.toggle('is-down', on));
  const keyName = e => (e.key === 'Control' || e.key === 'Meta' ? 'Control' : e.key === 'Shift' ? 'Shift' : e.key.toLowerCase() === 'x' ? 'X' : null);

  addEventListener('keydown', e => {
    const k = keyName(e);
    if (k) setKey(k, true);
    if ((e.ctrlKey || e.metaKey) && e.shiftKey && !e.altKey && e.key.toLowerCase() === 'x') {
      e.preventDefault();
      summon();
      return;
    }
    if (e.key === 'Escape') {
      if (summonOrbit && summonOrbit.escape()) return;
      if (cornerOrbit && cornerOrbit.escape()) return;
      if (configOrbit && configOrbit.group) { configOrbit.escape(); return; }
      if (heroOrbit && heroOrbit.group) heroOrbit.escape();
    }
  });
  addEventListener('keyup', e => { const k = keyName(e); if (k) setKey(k, false); if (e.key === 'Control' || e.key === 'Meta') { setKey('Shift', false); setKey('X', false); } });
  addEventListener('blur', () => Object.keys(keycaps).forEach(k => setKey(k, false)));

  // ---- Configurator ------------------------------------------------------

  const configHost = $('#config-orbit');
  const configStage = $('#config-stage');
  let configOrbit;
  const HOVER_HINTS = {
    invert: 'Swaps the item to the opposite colour and grows it slightly.',
    zoom: 'Keeps the colour. Only the size changes: in grows, out shrinks.',
    tint: 'Keeps the size and fills the item with a colour you choose. The glyph picks black or white for contrast.',
    animate: 'Plays a motion for as long as the pointer rests on the item.',
  };
  if (configHost) {
    const state = { size: 'medium', shape: 'circle', radius: 110, hover: 'invert', zoom: 'in', tint: '#111111', motion: 'pulse', theme: 'dark' };
    configOrbit = new Orbit(configHost, {
      radius: state.radius, size: SIZES[state.size], fitTo: 'flyout', label: 'Orbit preview', onAction: pageAction,
      onDismiss: reason => { if (reason === 'launch' || reason === 'close') setTimeout(() => configOrbit.open(), 900); },
    });
    whenVisible(configStage, () => setTimeout(() => configOrbit.open(), 150));

    const sync = () => {
      configOrbit.set({
        size: SIZES[state.size], shape: state.shape, radius: state.radius, hover: state.hover,
        zoom: state.zoom, tint: state.tint, motion: state.motion, theme: state.theme,
      });
      configStage.dataset.wall = state.theme;
      $$('[data-sub]').forEach(el => { el.hidden = el.dataset.sub !== state.hover; });
      const hint = $('#hover-hint');
      if (hint) hint.textContent = HOVER_HINTS[state.hover];
    };

    $$('[data-control]').forEach(group => {
      const key = group.dataset.control;
      group.addEventListener('click', e => {
        const btn = e.target.closest('button[data-value]');
        if (!btn) return;
        $$('button[data-value]', group).forEach(b => b.setAttribute('aria-pressed', String(b === btn)));
        state[key] = btn.dataset.value;
        sync();
      });
    });
    const range = $('#radius');
    const out = $('#radius-out');
    const paintRange = () => {
      const pct = ((range.value - range.min) / (range.max - range.min)) * 100;
      range.style.setProperty('--pct', pct + '%');
      out.textContent = range.value + ' px';
    };
    range?.addEventListener('input', () => { state.radius = +range.value; paintRange(); sync(); });
    if (range) paintRange();
    $('#replay')?.addEventListener('click', () => configOrbit.replay());
    sync();
  }

  // ---- Hot corner demo ---------------------------------------------------

  const screen = $('#screen');
  let cornerOrbit;
  if (screen) {
    const host = $('#screen-orbit');
    const cs = { layout: 'orbit', corner: 'tl' };
    cornerOrbit = new Orbit(host, {
      radius: 100, size: 48, smallHub: true, anchor: 'corner', label: 'Hot corner orbit',
      onAction: item => {
        if (item.kind === 'settings') toast('Settings opens the SwiftX app.');
        else if (item.kind === 'tool') toast(`${item.name} opens in its own window.`);
        else toast(`${item.name} launches.`);
      },
      onDismiss: () => screen.classList.remove('has-orbit'),
    });

    const zones = $$('.hot-zone', screen);
    const syncZones = () => zones.forEach(z => z.classList.toggle('is-active', z.dataset.corner === cs.corner));

    const summonHere = source => {
      if (cornerOrbit.isOpen) { cornerOrbit.close('toggle'); return; }
      const opts = source === 'tray'
        ? { layout: 'full', anchor: 'center', items: ORBIT_ITEMS }
        : cs.layout === 'quadrant'
          ? { layout: 'quadrant', corner: cs.corner, items: QUAD_ITEMS }
          : { layout: 'full', anchor: 'corner', corner: cs.corner, items: ORBIT_ITEMS };
      cornerOrbit.set(opts);
      screen.classList.add('has-orbit');
      void host.offsetWidth;
      requestAnimationFrame(() => cornerOrbit.open());
    };

    zones.forEach(z => {
      let timer = 0, armed = true, firedAt = 0;
      z.addEventListener('pointerenter', e => {
        if (e.buttons) return;           // A held button is a drag, not a summon.
        if (!armed) return;
        z.classList.add('is-armed');
        timer = setTimeout(() => {       // 250 ms dwell, as in HotCornerWatcher.
          armed = false;
          firedAt = Date.now();
          summonHere('corner');
        }, 250);
      });
      z.addEventListener('pointermove', e => { if (e.buttons) { clearTimeout(timer); z.classList.remove('is-armed'); } });
      z.addEventListener('pointerleave', () => { clearTimeout(timer); armed = true; z.classList.remove('is-armed'); });
      z.addEventListener('click', e => {
        e.stopPropagation();
        if (Date.now() - firedAt > 400) summonHere('corner');
      });
    });

    $('#tray-btn')?.addEventListener('click', e => { e.stopPropagation(); summonHere('tray'); });
    screen.addEventListener('click', () => { if (cornerOrbit.isOpen) cornerOrbit.close('outside'); });

    $$('[data-corner-control]').forEach(group => {
      const key = group.dataset.cornerControl;
      group.addEventListener('click', e => {
        const btn = e.target.closest('button[data-value]');
        if (!btn) return;
        $$('button[data-value]', group).forEach(b => b.setAttribute('aria-pressed', String(b === btn)));
        cs[key] = btn.dataset.value;
        syncZones();
        if (cornerOrbit.isOpen) { cornerOrbit.close('reset'); setTimeout(() => summonHere('corner'), 380); }
        const hint = $('#screen-hint-corner');
        if (hint) hint.textContent = { tl: 'top-left', tr: 'top-right', bl: 'bottom-left', br: 'bottom-right' }[cs.corner];
      });
    });
    syncZones();

    const clock = $('#screen-clock');
    const tick = () => {
      if (!clock) return;
      const d = new Date();
      clock.innerHTML = `${d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}<br>${d.toLocaleDateString()}`;
    };
    tick();
    setInterval(tick, 30000);
  }

  // ---- Statement: words light up as you read -----------------------------

  $$('[data-lit]').forEach(p => {
    const words = [];
    const walk = node => {
      [...node.childNodes].forEach(child => {
        if (child.nodeType === 3) {
          const frag = document.createDocumentFragment();
          child.textContent.split(/(\s+)/).forEach(part => {
            if (!part) return;
            if (/^\s+$/.test(part)) { frag.appendChild(document.createTextNode(part)); return; }
            const s = document.createElement('span');
            s.className = 'w' + (node.dataset && node.dataset.g !== undefined ? ' g' : '');
            s.textContent = part;
            words.push(s);
            frag.appendChild(s);
          });
          child.replaceWith(frag);
        } else if (child.nodeType === 1) walk(child);
      });
    };
    walk(p);
    let raf = 0;
    const update = () => {
      raf = 0;
      const r = p.getBoundingClientRect(), vh = innerHeight;
      const t = Math.min(1, Math.max(0, (vh * 0.82 - r.top) / (r.height + vh * 0.3)));
      const lit = Math.round(t * words.length);
      words.forEach((w, i) => w.classList.toggle('is-lit', i < lit));
    };
    addEventListener('scroll', () => { if (!raf) raf = requestAnimationFrame(update); }, { passive: true });
    update();
  });

  // ---- Gallery -----------------------------------------------------------

  $$('.gallery').forEach(g => {
    const track = $('.gallery-track', g);
    const prev = $('[data-dir="-1"]', g), next = $('[data-dir="1"]', g);
    const stepSize = () => ($('.shot', track)?.getBoundingClientRect().width || 600) + 24;
    const sync = () => {
      prev.disabled = track.scrollLeft < 8;
      next.disabled = track.scrollLeft + track.clientWidth > track.scrollWidth - 8;
    };
    prev.addEventListener('click', () => track.scrollBy({ left: -stepSize(), behavior: 'smooth' }));
    next.addEventListener('click', () => track.scrollBy({ left: stepSize(), behavior: 'smooth' }));
    track.addEventListener('scroll', () => requestAnimationFrame(sync), { passive: true });
    addEventListener('resize', sync);
    sync();
  });

  // ---- Data formatter / Base64 demo --------------------------------------

  const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const escAttr = s => esc(s).replace(/"/g, '&quot;');

  function hlJSON(s) {
    const re = /("(?:\\.|[^"\\])*")(\s*:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|([{}[\],:])/g;
    let out = '', last = 0;
    for (const m of s.matchAll(re)) {
      out += esc(s.slice(last, m.index));
      if (m[1]) out += m[2] ? `<span class="tk-key">${esc(m[1])}</span><span class="tk-pun">${m[2]}</span>` : `<span class="tk-str">${esc(m[1])}</span>`;
      else if (m[3]) out += `<span class="tk-lit">${m[3]}</span>`;
      else if (m[4]) out += `<span class="tk-num">${m[4]}</span>`;
      else out += `<span class="tk-pun">${m[5]}</span>`;
      last = m.index + m[0].length;
    }
    return out + esc(s.slice(last));
  }
  function hlXML(s) {
    let out = '', last = 0;
    for (const m of s.matchAll(/<[^>]*>/g)) {
      out += esc(s.slice(last, m.index));
      out += esc(m[0])
        .replace(/([\w:.-]+)=("[^"]*")/g, '<span class="tk-attr">$1</span><span class="tk-pun">=</span><span class="tk-str">$2</span>')
        .replace(/^(&lt;[/?]?)([\w:.-]+)/, '<span class="tk-pun">$1</span><span class="tk-tag">$2</span>');
      last = m.index + m[0].length;
    }
    return out + esc(s.slice(last));
  }

  const detect = s => { const t = s.trim(); return t.startsWith('<') ? 'XML' : /^[[{"]|^-?\d|^(true|false|null)$/.test(t) ? 'JSON' : t ? 'Text' : ''; };

  function parseXML(s) {
    const doc = new DOMParser().parseFromString(s, 'application/xml');
    const err = doc.getElementsByTagName('parsererror')[0];
    if (err) throw new Error((err.textContent || 'Malformed XML').split('\n').find(Boolean).trim());
    return doc;
  }
  function printXML(doc, src, indent) {
    const nl = indent ? '\n' : '';
    const pad = d => ' '.repeat(d * indent);
    const walk = (node, d) => {
      if (node.nodeType === 3) { const v = node.nodeValue.trim(); return v ? pad(d) + esc(v) : ''; }
      if (node.nodeType === 4) return pad(d) + `<![CDATA[${node.nodeValue}]]>`;
      if (node.nodeType === 8) return pad(d) + `<!--${node.nodeValue}-->`;
      if (node.nodeType !== 1) return '';
      const attrs = [...node.attributes].map(a => ` ${a.name}="${escAttr(a.value)}"`).join('');
      const kids = [...node.childNodes].filter(c => !(c.nodeType === 3 && !c.nodeValue.trim()));
      if (!kids.length) return `${pad(d)}<${node.nodeName}${attrs}/>`;
      if (kids.length === 1 && kids[0].nodeType === 3) return `${pad(d)}<${node.nodeName}${attrs}>${esc(kids[0].nodeValue.trim())}</${node.nodeName}>`;
      return `${pad(d)}<${node.nodeName}${attrs}>${nl}${kids.map(k => walk(k, d + 1)).filter(Boolean).join(nl)}${nl}${pad(d)}</${node.nodeName}>`;
    };
    const decl = (src.trim().match(/^<\?xml[^>]*\?>/) || [''])[0];
    return (decl ? decl + nl : '') + walk(doc.documentElement, 0);
  }
  function xmlToJSON(el) {
    const kids = [...el.children];
    const text = [...el.childNodes].filter(c => c.nodeType === 3 || c.nodeType === 4).map(c => c.nodeValue).join('').trim();
    if (!el.attributes.length && !kids.length) return text;
    const o = {};
    [...el.attributes].forEach(a => { o['@' + a.name] = a.value; });
    kids.forEach(k => {
      const v = xmlToJSON(k);
      if (k.nodeName in o) { if (!Array.isArray(o[k.nodeName])) o[k.nodeName] = [o[k.nodeName]]; o[k.nodeName].push(v); }
      else o[k.nodeName] = v;
    });
    if (text) o['#text'] = text;
    return o;
  }
  function jsonToXML(value, indent) {
    const nl = indent ? '\n' : '';
    const pad = d => ' '.repeat(d * indent);
    const name = k => { let n = String(k).replace(/[^\w.-]/g, '_'); if (!/^[A-Za-z_]/.test(n)) n = '_' + n; return n; };
    const node = (k, v, d) => {
      if (Array.isArray(v)) return v.map(x => node(k, x, d)).join(nl);
      const tag = name(k);
      if (v === null || typeof v !== 'object') return `${pad(d)}<${tag}>${esc(v === null ? '' : String(v))}</${tag}>`;
      const entries = Object.entries(v);
      const attrs = entries.filter(([key]) => key.startsWith('@')).map(([key, x]) => ` ${name(key.slice(1))}="${escAttr(String(x))}"`).join('');
      const text = v['#text'];
      const kids = entries.filter(([key]) => !key.startsWith('@') && key !== '#text');
      if (!kids.length) return text != null ? `${pad(d)}<${tag}${attrs}>${esc(String(text))}</${tag}>` : `${pad(d)}<${tag}${attrs}/>`;
      const inner = kids.map(([key, x]) => node(key, x, d + 1));
      if (text != null) inner.unshift(pad(d + 1) + esc(String(text)));
      return `${pad(d)}<${tag}${attrs}>${nl}${inner.join(nl)}${nl}${pad(d)}</${tag}>`;
    };
    const keys = value && typeof value === 'object' && !Array.isArray(value) ? Object.keys(value) : [];
    return keys.length === 1 ? node(keys[0], value[keys[0]], 0) : node('root', value, 0);
  }
  const sortKeys = v => Array.isArray(v) ? v.map(sortKeys)
    : v && typeof v === 'object' ? Object.fromEntries(Object.keys(v).sort((a, b) => a.localeCompare(b)).map(k => [k, sortKeys(v[k])])) : v;

  function b64enc(str, urlSafe) {
    const bytes = new TextEncoder().encode(str);
    let bin = '';
    for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    const s = btoa(bin);
    return urlSafe ? s.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '') : s;
  }
  function b64dec(str) {
    let s = str.replace(/\s+/g, '').replace(/-/g, '+').replace(/_/g, '/');
    while (s.length % 4) s += '=';
    const bin = atob(s);
    const bytes = Uint8Array.from(bin, c => c.charCodeAt(0));
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  }

  const tw = $('#tool-window');
  let selectToolTab = () => {};
  if (tw) {
    const input = $('#tw-input'), output = $('#tw-output'), status = $('#tw-status'), kindEl = $('#tw-kind');
    const indentSel = $('#tw-indent'), urlSafe = $('#tw-urlsafe');
    const samples = {
      data: '{"name":"SwiftX","version":"2.0","slots":8,"summon":{"hotkey":"Ctrl+Shift+X","hotCorner":"TopLeft","layout":"Orbit"},"tools":["Data","SecureValues","Base64","Certificate"],"telemetry":null,"uploads":0}',
      base64: 'Everything stays on this device. ✨',
    };
    const memory = { data: samples.data, base64: samples.base64 };
    let tab = 'data', lastOut = '';

    const setStatus = (msg, cls = '') => { status.innerHTML = cls ? `<span class="${cls}">${msg}</span>` : msg; };
    const bytes = s => new TextEncoder().encode(s).length;
    const indent = () => +(indentSel?.value || 2);

    const show = (text, kind) => {
      lastOut = text;
      output.innerHTML = kind === 'JSON' ? hlJSON(text) : kind === 'XML' ? hlXML(text) : esc(text);
    };

    const run = op => {
      const src = input.value;
      const kind = detect(src);
      kindEl.textContent = kind || '—';
      if (!src.trim()) { show('', ''); setStatus('Paste or type something to start.'); return; }
      try {
        if (tab === 'base64') {
          if (op === 'decode') { show(b64dec(src), 'Text'); setStatus(`Decoded · ${bytes(lastOut)} bytes`, 'ok'); }
          else { show(b64enc(src, urlSafe?.checked), 'Text'); setStatus(`Encoded${urlSafe?.checked ? ' (URL-safe)' : ''} · ${bytes(src)} → ${lastOut.length} bytes`, 'ok'); }
          return;
        }
        if (kind === 'XML') {
          const doc = parseXML(src);
          if (op === 'minify') show(printXML(doc, src, 0), 'XML');
          else if (op === 'convert') { const j = JSON.stringify({ [doc.documentElement.nodeName]: xmlToJSON(doc.documentElement) }, null, indent()); show(j, 'JSON'); kindEl.textContent = 'XML → JSON'; }
          else if (op === 'sort') { show(printXML(doc, src, indent()), 'XML'); setStatus('Sort keys works on JSON. Formatted the XML instead.', 'ok'); return; }
          else show(printXML(doc, src, indent()), 'XML');
          setStatus(`Valid XML · ${bytes(lastOut)} bytes`, 'ok');
          return;
        }
        const value = JSON.parse(src);
        if (op === 'minify') show(JSON.stringify(value), 'JSON');
        else if (op === 'sort') show(JSON.stringify(sortKeys(value), null, indent()), 'JSON');
        else if (op === 'convert') { show(jsonToXML(value, indent()), 'XML'); kindEl.textContent = 'JSON → XML'; }
        else show(JSON.stringify(value, null, indent()), 'JSON');
        setStatus(`Valid JSON · ${bytes(lastOut)} bytes`, 'ok');
      } catch (err) {
        show(src, kind);
        setStatus(`${tab === 'base64' ? 'Not valid Base64' : 'Invalid ' + (kind || 'input')} · ${esc(String(err.message || err)).slice(0, 120)}`, 'bad');
      }
    };

    selectToolTab = name => {
      memory[tab] = input.value;
      tab = name;
      $$('.tw-tabs button', tw).forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === name)));
      $$('[data-actions]', tw).forEach(a => { a.hidden = a.dataset.actions !== name; });
      input.value = memory[name];
      input.setAttribute('aria-label', name === 'base64' ? 'Base64 input' : 'JSON or XML input');
      run(name === 'base64' ? 'encode' : 'format');
    };
    $$('.tw-tabs button', tw).forEach(b => b.addEventListener('click', () => selectToolTab(b.dataset.tab)));

    tw.addEventListener('click', e => {
      const btn = e.target.closest('button[data-op]');
      if (!btn) return;
      const op = btn.dataset.op;
      if (op === 'copy') {
        if (!lastOut) return;
        navigator.clipboard?.writeText(lastOut).then(() => toast('Copied to the clipboard.'), () => toast('Copy was blocked by the browser.'));
        return;
      }
      if (op === 'clear') { input.value = ''; run('format'); input.focus(); return; }
      if (op === 'sample') { input.value = samples[tab]; run(tab === 'base64' ? 'encode' : 'format'); return; }
      run(op);
    });
    // Pasting formats straight away, as it does in the app.
    input.addEventListener('paste', () => setTimeout(() => run(tab === 'base64' ? 'encode' : 'format'), 0));
    let deb;
    input.addEventListener('input', () => { clearTimeout(deb); deb = setTimeout(() => run(tab === 'base64' ? 'encode' : 'format'), 350); });
    indentSel?.addEventListener('change', () => run('format'));
    urlSafe?.addEventListener('change', () => run('encode'));
    input.value = memory.data;
    run('format');
  }

  // ---- Copy buttons ------------------------------------------------------

  $$('[data-copy]').forEach(btn => btn.addEventListener('click', () => {
    const target = $(btn.dataset.copy);
    if (!target) return;
    navigator.clipboard?.writeText(target.innerText.trim()).then(() => {
      btn.textContent = 'Copied';
      setTimeout(() => { btn.textContent = 'Copy'; }, 1600);
    });
  }));

  // ---- Nav ---------------------------------------------------------------

  const nav = $('.nav');
  if (nav) {
    const toggle = $('.nav-toggle', nav);
    toggle?.addEventListener('click', () => {
      const open = nav.dataset.open === 'true';
      nav.dataset.open = String(!open);
      toggle.setAttribute('aria-expanded', String(!open));
    });
    $$('.nav-links a', nav).forEach(a => a.addEventListener('click', () => { nav.dataset.open = 'false'; toggle?.setAttribute('aria-expanded', 'false'); }));

    // The bar takes the colour of whatever section is passing under it.
    let raf = 0;
    const tone = () => {
      raf = 0;
      const y = nav.getBoundingClientRect().bottom + 2;
      const under = $$('[data-tone]').find(s => { const r = s.getBoundingClientRect(); return r.top <= y && r.bottom > y; });
      nav.classList.toggle('is-light', under ? under.dataset.tone === 'light' : true);
    };
    addEventListener('scroll', () => { if (!raf) raf = requestAnimationFrame(tone); }, { passive: true });
    addEventListener('resize', tone);
    tone();
  }

  // ---- Reveal on scroll --------------------------------------------------

  if ('IntersectionObserver' in window) {
    const io = new IntersectionObserver(entries => entries.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add('is-in'); io.unobserve(e.target); }
    }), { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
    $$('.reveal').forEach(el => io.observe(el));
  } else {
    $$('.reveal').forEach(el => el.classList.add('is-in'));
  }

  $$('[data-year]').forEach(el => { el.textContent = new Date().getFullYear(); });

  // The 404 page's orbit, and anything else that just wants a live one.
  $$('[data-orbit]').forEach(host => {
    const o = new Orbit(host, { radius: +host.dataset.radius || 110, size: +host.dataset.size || 48, onAction: item => toast(`${item.name}: not here either.`) });
    setTimeout(() => o.open(), 300);
  });
})();
