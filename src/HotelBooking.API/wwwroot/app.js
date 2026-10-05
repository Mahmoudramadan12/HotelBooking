/* Manzil web app: hash-routed single page app talking to /api. No build step. */
(() => {
'use strict';

/* ============================== helpers ============================== */
const CURRENCY = 'USD';
const TYPES = ['Hotel', 'Apartment', 'Villa', 'Resort'];
const CITIES = ['Cairo', 'Giza', 'Alexandria', 'Luxor', 'Aswan', 'Sharm El Sheikh', 'Hurghada', 'Dahab'];

const money = n => new Intl.NumberFormat('en-US', { style: 'currency', currency: CURRENCY, maximumFractionDigits: 0 }).format(n ?? 0);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const $ = (sel, root = document) => root.querySelector(sel);
const pad = n => String(n).padStart(2, '0');
const toISO = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => toISO(new Date());
const addDays = (iso, n) => { const d = new Date(iso + 'T00:00:00'); d.setDate(d.getDate() + n); return toISO(d); };
const nightsBetween = (a, b) => (a && b) ? Math.round((new Date(b + 'T00:00:00') - new Date(a + 'T00:00:00')) / 864e5) : 0;
const fmtDate = s => new Date(s).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
const fmtShort = s => new Date(s).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
const plural = (n, w) => `${n} ${w}${n === 1 ? '' : 's'}`;
const validDates = s => !!(s.checkIn && s.checkOut && nightsBetween(s.checkIn, s.checkOut) >= 1);

const main = $('#main');
const dlg = $('#dialog');
let roomsChanged = null; // callback fired once when the rooms manager dialog closes
dlg.addEventListener('close', () => { const f = roomsChanged; roomsChanged = null; if (f) f(); });

function toast(msg, kind = 'info') {
  const el = document.createElement('div');
  el.className = 'toast ' + kind;
  el.textContent = msg;
  $('#toasts').append(el);
  setTimeout(() => el.remove(), 4500);
}

/* ============================== auth & api ============================== */
const KEY = 'manzil.auth';
const Auth = {
  get() {
    try {
      const a = JSON.parse(localStorage.getItem(KEY));
      if (a && new Date(a.expiresAtUtc) > new Date()) return a;
    } catch { /* ignore */ }
    try { localStorage.removeItem(KEY); } catch { /* ignore */ }
    return null;
  },
  set(a) { try { localStorage.setItem(KEY, JSON.stringify(a)); } catch { /* storage unavailable */ } },
  clear() { try { localStorage.removeItem(KEY); } catch { /* ignore */ } }
};
const isManager = a => !!a && (a.role === 'Owner' || a.role === 'Admin');

class ApiError extends Error { constructor(message, status) { super(message); this.status = status; } }

function errorMessage(data, status) {
  if (data && data.error) return data.error;
  if (data && data.errors) return Object.values(data.errors).flat().join(' ');
  if (data && data.title) return data.title;
  if (status === 429) return 'Too many attempts. Wait a minute and try again.';
  if (status === 403) return 'You do not have permission to do that.';
  return `Request failed (${status}). Please try again.`;
}

async function api(path, { method = 'GET', body, auth = true } = {}) {
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const a = Auth.get();
  if (auth && a) headers.Authorization = 'Bearer ' + a.token;

  let res;
  try {
    res = await fetch('/api' + path, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined });
  } catch {
    throw new ApiError('Cannot reach the server. Check your connection and try again.', 0);
  }
  if (res.status === 204) return null;

  let data = null;
  const text = await res.text();
  if (text) { try { data = JSON.parse(text); } catch { /* not json */ } }

  if (!res.ok) {
    if (res.status === 401 && auth && a) {
      Auth.clear();
      renderHeader();
      toast('Your session has expired. Sign in again.', 'error');
      location.hash = '#/login?next=' + encodeURIComponent(location.hash.slice(1) || '/');
    }
    throw new ApiError(errorMessage(data, res.status), res.status);
  }
  return data;
}

/* ============================== router ============================== */
let routeToken = 0;
const parseHash = () => {
  const raw = location.hash.slice(1) || '/';
  const i = raw.indexOf('?');
  return { path: i < 0 ? raw : raw.slice(0, i), query: new URLSearchParams(i < 0 ? '' : raw.slice(i + 1)) };
};

function go(patch) { // update the home page query string
  const p = new URLSearchParams(parseHash().query);
  for (const [k, v] of Object.entries(patch)) { if (v === '' || v == null) p.delete(k); else p.set(k, v); }
  const s = p.toString();
  location.hash = '#/' + (s ? '?' + s : '');
}

const toLogin = () => { location.hash = '#/login?next=' + encodeURIComponent(location.hash.slice(1) || '/'); };

function errorView(e) {
  const missing = e.status === 404;
  return `<section class="wrap state">
    <h1>${missing ? 'We could not find that' : 'Something went wrong'}</h1>
    <p>${esc(e.message || 'Unexpected error.')}</p>
    ${missing ? '<a class="btn" href="#/">Browse stays</a>' : '<button class="btn" data-act="reload">Try again</button>'}
  </section>`;
}

async function route() {
  const { path, query } = parseHash();
  const my = ++routeToken;
  roomsChanged = null;
  if (dlg.open) dlg.close();
  renderHeader();
  const ctx = { query, params: [], alive: () => my === routeToken };
  const routes = [
    [/^\/$/, HomePage],
    [/^\/hotel\/([0-9a-f-]{36})$/i, HotelPage],
    [/^\/login$/, LoginPage],
    [/^\/register$/, RegisterPage],
    [/^\/trips$/, TripsPage],
    [/^\/manage$/, ManagePage]
  ];
  for (const [re, fn] of routes) {
    const m = path.match(re);
    if (!m) continue;
    ctx.params = m.slice(1);
    window.scrollTo(0, 0);
    try { await fn(ctx); } catch (e) { if (ctx.alive()) main.innerHTML = errorView(e); }
    return;
  }
  document.title = 'Page not found | Manzil';
  main.innerHTML = errorView(new ApiError('This page does not exist.', 404));
}

/* ============================== header ============================== */
function renderHeader() {
  const a = Auth.get();
  const path = parseHash().path;
  const link = (href, label, active) => `<a class="nav-link" href="#${href}" ${active ? 'aria-current="page"' : ''}>${label}</a>`;
  $('#header').innerHTML = `
    <div class="wrap bar">
      <a class="brand" href="#/" aria-label="Manzil, home">
        <svg viewBox="0 0 32 32" width="30" height="30" aria-hidden="true"><path d="M5 27V15a11 11 0 0 1 22 0v12" fill="none" stroke="currentColor" stroke-width="3"/><path d="M5 23q2.75-3 5.5 0t5.5 0 5.5 0 5.5 0" fill="none" stroke="#c99a2e" stroke-width="2.6"/></svg>
        <span>Manzil</span>
      </a>
      <nav class="nav" aria-label="Main">
        ${link('/', 'Explore', path === '/' || path.startsWith('/hotel'))}
        ${a ? link('/trips', 'My trips', path === '/trips') : ''}
        ${isManager(a) ? link('/manage', 'Manage', path === '/manage') : ''}
      </nav>
      <div class="account">
        ${a
          ? `<span class="who">${esc(a.fullName)}</span><button class="btn small secondary" data-act="signout">Sign out</button>`
          : `<a class="btn small ghost" href="#/login">Sign in</a><a class="btn small" href="#/register">Create account</a>`}
      </div>
    </div>`;
}

/* ============================== dialogs & forms ============================== */
function showDialog(html, onClick) {
  dlg.innerHTML = `<div class="dlg-body">${html}</div>`;
  dlg.onclick = e => {
    if (e.target === dlg) return dlg.close();
    if (onClick) onClick(e);
  };
  if (!dlg.open) dlg.showModal();
}

function confirmDialog(title, text, confirmLabel, danger = false) {
  return new Promise(resolve => {
    showDialog(`<h2 id="dlgTitle">${esc(title)}</h2><p>${esc(text)}</p>
      <div class="dlg-actions"><button class="btn secondary" data-d="no">Keep it</button><button class="btn ${danger ? 'danger' : ''}" data-d="yes">${esc(confirmLabel)}</button></div>`,
      e => { const b = e.target.closest('[data-d]'); if (b) { resolve(b.dataset.d === 'yes'); dlg.close(); } });
    dlg.addEventListener('close', () => resolve(false), { once: true });
  });
}

function fieldHtml(f) {
  const id = 'f_' + f.name;
  const req = f.required ? 'required' : '';
  const hint = f.hint ? `<span class="hint">${esc(f.hint)}</span>` : '';
  switch (f.type) {
    case 'textarea':
      return `<label>${esc(f.label)}<textarea name="${f.name}" rows="${f.rows || 3}" maxlength="${f.max || 2000}" ${req}>${esc(f.value)}</textarea>${hint}</label>`;
    case 'select':
      return `<label>${esc(f.label)}<select name="${f.name}" ${req}>${f.options.map(o => `<option value="${esc(o.value)}" ${String(o.value) === String(f.value) ? 'selected' : ''}>${esc(o.label)}</option>`).join('')}</select>${hint}</label>`;
    case 'checkbox':
      return `<label class="check-single"><input type="checkbox" name="${f.name}" ${f.value ? 'checked' : ''}> ${esc(f.label)}</label>`;
    case 'checkboxes':
      return `<fieldset class="fieldset"><legend>${esc(f.label)}</legend><div class="checks">${f.options.map(o =>
        `<label><input type="checkbox" name="${f.name}" value="${esc(o.value)}" ${(f.value || []).includes(o.value) ? 'checked' : ''}> ${esc(o.label)}</label>`).join('')}</div></fieldset>`;
    default:
      return `<label for="${id}">${esc(f.label)}<input id="${id}" name="${f.name}" type="${f.type || 'text'}" value="${esc(f.value)}" ${req}
        ${f.min != null ? `min="${f.min}"` : ''} ${f.max != null ? `max="${f.max}"` : ''} ${f.step ? `step="${f.step}"` : ''} maxlength="${f.maxlength || 300}">${hint}</label>`;
  }
}

/** Opens a form in the shared dialog. onSubmit(values) may throw; the message is shown inline. */
function openForm({ title, intro, fields, submit, onSubmit, onCancel }) {
  showDialog(`<h2 id="dlgTitle">${esc(title)}</h2>${intro ? `<p class="muted">${esc(intro)}</p>` : ''}
    <form novalidate>
      ${fields.map(fieldHtml).join('')}
      <p class="form-error" role="alert" hidden></p>
      <div class="dlg-actions"><button type="button" class="btn secondary" data-d="cancel">Cancel</button><button class="btn" type="submit">${esc(submit)}</button></div>
    </form>`,
    e => { if (e.target.closest('[data-d="cancel"]')) { if (onCancel) onCancel(); else dlg.close(); } });

  const form = $('form', dlg);
  const err = $('.form-error', form);
  form.addEventListener('submit', async e => {
    e.preventDefault();
    err.hidden = true;
    const fd = new FormData(form);
    const values = {};
    for (const f of fields) {
      if (f.type === 'checkbox') values[f.name] = fd.has(f.name);
      else if (f.type === 'checkboxes') values[f.name] = fd.getAll(f.name);
      else if (f.type === 'number') values[f.name] = fd.get(f.name) === '' ? null : Number(fd.get(f.name));
      else values[f.name] = String(fd.get(f.name) ?? '').trim();
      if (f.required && (values[f.name] === '' || values[f.name] === null)) {
        err.textContent = `${f.label} is required.`; err.hidden = false; return;
      }
    }
    const btn = $('button[type="submit"]', form);
    btn.disabled = true;
    try { await onSubmit(values); }
    catch (ex) { err.textContent = ex.message; err.hidden = false; btn.disabled = false; }
  });
  const first = $('input:not([type=checkbox]), textarea, select', form);
  if (first) first.focus();
}

/* ============================== shared view bits ============================== */
const cover = (url, extra = '') => `<div class="media">${url ? `<img src="${esc(url)}" alt="" loading="lazy">` : ''}${extra}</div>`;
const ratingHtml = h => h.reviewCount > 0
  ? `<span class="rating"><span class="star" aria-hidden="true">★</span> ${Number(h.rating).toFixed(1)} <span class="muted small">(${h.reviewCount})</span></span>`
  : '<span class="chip info">New</span>';
const statusChip = s => {
  const map = { Confirmed: 'ok', Pending: 'warn', Cancelled: 'bad', CheckedIn: 'info', CheckedOut: '' };
  const label = { CheckedIn: 'Checked in', CheckedOut: 'Completed' }[s] || s;
  return `<span class="chip ${map[s] ?? ''}">${label}</span>`;
};
const paymentChip = s => `<span class="chip ${s === 'Paid' ? 'ok' : s === 'Refunded' ? 'info' : s === 'Failed' ? 'bad' : 'warn'}">${s}</span>`;
const skeletons = n => `<div class="grid">${Array.from({ length: n }, () => '<div class="skeleton"></div>').join('')}</div>`;

/* ============================== home ============================== */
async function HomePage(ctx) {
  document.title = 'Manzil | Book stays across Egypt';
  const q = ctx.query;
  const f = {
    city: q.get('city') || '', checkIn: q.get('checkIn') || '', checkOut: q.get('checkOut') || '',
    guests: q.get('guests') || '2', type: q.get('type') || '', maxPrice: q.get('maxPrice') || '',
    sort: q.get('sort') || '', page: Math.max(1, Number(q.get('page')) || 1)
  };
  const t = today();

  main.innerHTML = `
    <section class="hero">
      <div class="wrap">
        <h1>Rooms across Egypt, booked in a minute</h1>
        <p class="lede">Hotels, resorts, villas and apartments from the Nile to the Red Sea. Cancel free until the day before you arrive.</p>
        <form class="search" id="searchForm" novalidate>
          <label class="field-city">Where to
            <input name="city" list="cities" value="${esc(f.city)}" placeholder="City or region" autocomplete="off">
            <datalist id="cities">${CITIES.map(c => `<option value="${c}">`).join('')}</datalist>
          </label>
          <label>Check-in<input type="date" name="checkIn" min="${t}" value="${esc(f.checkIn)}"></label>
          <label>Check-out<input type="date" name="checkOut" min="${f.checkIn ? addDays(f.checkIn, 1) : addDays(t, 1)}" value="${esc(f.checkOut)}"></label>
          <label>Guests<input type="number" name="guests" min="1" max="20" value="${esc(f.guests)}"></label>
          <button class="btn gold" type="submit">Search stays</button>
          <p class="form-error search-note" role="alert" hidden></p>
        </form>
      </div>
    </section>
    <section class="results wrap">
      <div class="toolbar">
        <div class="filters">
          <div class="seg" role="group" aria-label="Property type">
            ${['', ...TYPES].map(x => `<button type="button" data-type="${x}" aria-pressed="${f.type === x}">${x ? x + 's' : 'All types'}</button>`).join('')}
          </div>
          <label>Max price a night<input type="number" id="maxPrice" min="1" step="10" placeholder="Any" value="${esc(f.maxPrice)}"></label>
          <label>Sort by
            <select id="sort">
              <option value="" ${f.sort === '' ? 'selected' : ''}>Best rated</option>
              <option value="price" ${f.sort === 'price' ? 'selected' : ''}>Lowest price</option>
              <option value="name" ${f.sort === 'name' ? 'selected' : ''}>Name</option>
            </select>
          </label>
        </div>
        <p class="count" id="count" aria-live="polite"></p>
      </div>
      <div id="list">${skeletons(6)}</div>
      <nav class="pager" id="pager" aria-label="Pages" hidden></nav>
    </section>`;

  const form = $('#searchForm');
  const note = $('.search-note', form);
  const inEl = form.elements.checkIn, outEl = form.elements.checkOut;
  inEl.addEventListener('change', () => {
    if (!inEl.value) return;
    outEl.min = addDays(inEl.value, 1);
    if (!outEl.value || outEl.value <= inEl.value) outEl.value = addDays(inEl.value, 1);
  });
  form.addEventListener('submit', e => {
    e.preventDefault();
    note.hidden = true;
    const v = Object.fromEntries(new FormData(form));
    if ((v.checkIn && !v.checkOut) || (!v.checkIn && v.checkOut)) { note.textContent = 'Choose both a check-in and a check-out date, or leave both empty.'; note.hidden = false; return; }
    if (v.checkIn && nightsBetween(v.checkIn, v.checkOut) < 1) { note.textContent = 'Check-out must be after check-in.'; note.hidden = false; return; }
    go({ city: v.city.trim(), checkIn: v.checkIn, checkOut: v.checkOut, guests: v.guests, page: '' });
  });
  main.querySelectorAll('[data-type]').forEach(b => b.addEventListener('click', () => go({ type: b.dataset.type, page: '' })));
  $('#sort').addEventListener('change', e => go({ sort: e.target.value, page: '' }));
  $('#maxPrice').addEventListener('change', e => go({ maxPrice: e.target.value, page: '' }));

  const p = new URLSearchParams({ pageSize: '9', page: String(f.page), guests: f.guests || '1' });
  if (f.city) p.set('city', f.city);
  if (validDates(f)) { p.set('checkIn', f.checkIn); p.set('checkOut', f.checkOut); }
  if (f.type) p.set('type', f.type);
  if (f.maxPrice) p.set('maxPrice', f.maxPrice);
  if (f.sort) p.set('sort', f.sort);

  const data = await api('/hotels?' + p, { auth: false });
  if (!ctx.alive()) return;

  const hrefQuery = validDates(f) ? `?checkIn=${f.checkIn}&checkOut=${f.checkOut}&guests=${encodeURIComponent(f.guests)}` : '';
  $('#count').textContent = data.totalCount
    ? `${plural(data.totalCount, 'stay')}${f.city ? ' in ' + f.city : ''}`
    : '';

  if (!data.items.length) {
    $('#list').innerHTML = `<div class="state"><h2>No stays match yet</h2>
      <p>Try different dates, a higher price limit or another city.</p>
      <a class="btn secondary" href="#/">Clear all filters</a></div>`;
    return;
  }
  $('#list').innerHTML = `<div class="grid">${data.items.map(h => `
    <a class="card" href="#/hotel/${h.id}${hrefQuery}">
      ${cover(h.coverImage, `<span class="tag">${esc(h.type)}</span>`)}
      <div class="card-body">
        <div class="card-top"><h3>${esc(h.name)}</h3>${ratingHtml(h)}</div>
        <p class="muted small">${esc(h.city)}, ${esc(h.country)}</p>
        <p class="clamp">${esc(h.description)}</p>
        <div class="card-foot">
          <span class="muted small">${h.minPrice != null ? 'From' : ''}</span>
          <span class="price">${h.minPrice != null ? `<strong>${money(h.minPrice)}</strong> a night` : '<span class="muted">No rooms listed</span>'}</span>
        </div>
      </div>
    </a>`).join('')}</div>`;

  const pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
  if (pages > 1) {
    const pager = $('#pager');
    pager.hidden = false;
    pager.innerHTML = `<button class="btn secondary small" data-page="${f.page - 1}" ${f.page <= 1 ? 'disabled' : ''}>Previous</button>
      <span>Page ${f.page} of ${pages}</span>
      <button class="btn secondary small" data-page="${f.page + 1}" ${f.page >= pages ? 'disabled' : ''}>Next</button>`;
    pager.onclick = e => { const b = e.target.closest('[data-page]'); if (b && !b.disabled) go({ page: b.dataset.page }); };
  }
}

/* ============================== hotel ============================== */
async function HotelPage(ctx) {
  const id = ctx.params[0];
  const st = {
    checkIn: ctx.query.get('checkIn') || '', checkOut: ctx.query.get('checkOut') || '',
    guests: Math.max(1, Number(ctx.query.get('guests')) || 2), roomId: null, hotel: null
  };
  main.innerHTML = `<section class="wrap page">${skeletons(1)}</section>`;

  const load = () => {
    const p = new URLSearchParams();
    if (validDates(st)) { p.set('checkIn', st.checkIn); p.set('checkOut', st.checkOut); }
    return api(`/hotels/${id}?${p}`);
  };
  st.hotel = await load();
  if (!ctx.alive()) return;
  const h = st.hotel;
  document.title = `${h.name} | Manzil`;

  const shown = h.images.length >= 5 ? 5 : h.images.length >= 3 ? 3 : h.images.length;
  const gallery = shown === 0
    ? `<div class="gallery" data-n="1">${cover(null)}</div>`
    : `<div class="gallery" data-n="${shown}">${h.images.slice(0, shown).map(u => cover(u)).join('')}</div>`;

  main.innerHTML = `
    <section class="wrap page">
      <a class="crumb" href="#/">Back to results</a>
      ${gallery}
      <div class="hotel-head">
        <div>
          <h1>${esc(h.name)}</h1>
          <p class="muted">${esc(h.address)}, ${esc(h.city)}, ${esc(h.country)}</p>
        </div>
        <div class="chips">${ratingHtml(h)}<span class="chip">${esc(h.type)}</span>${h.isActive ? '' : '<span class="chip bad">Hidden from guests</span>'}</div>
      </div>
      <div class="layout">
        <div>
          <div class="section"><h2>About this stay</h2><p class="prose">${esc(h.description)}</p></div>
          <div class="section"><h2>Rooms</h2><ul class="rooms" id="rooms"></ul></div>
          <div class="section">
            <div class="page-head" style="margin-bottom:1rem"><h2>Guest reviews</h2><button class="btn secondary small" id="writeReview">Write a review</button></div>
            <div class="reviews" id="reviews"><p class="muted">Loading reviews…</p></div>
          </div>
        </div>
        <aside class="ticket" aria-label="Book this stay">
          <form id="bookForm" novalidate>
            <div class="ticket-top">
              <div class="pair">
                <label>Check-in<input type="date" id="tIn" min="${today()}" value="${esc(st.checkIn)}"></label>
                <label>Check-out<input type="date" id="tOut" min="${st.checkIn ? addDays(st.checkIn, 1) : addDays(today(), 1)}" value="${esc(st.checkOut)}"></label>
              </div>
              <label>Guests<input type="number" id="tGuests" min="1" max="20" value="${st.guests}"></label>
            </div>
            <div class="ticket-cut" aria-hidden="true"></div>
            <div class="ticket-bottom">
              <div id="summary" aria-live="polite"></div>
              <label>Note for the property <span class="hint">(optional)</span><textarea id="tNotes" rows="2" maxlength="500"></textarea></label>
              <p class="form-error" id="bookError" role="alert" hidden></p>
              <button class="btn block" id="bookBtn" type="submit">Book this room</button>
            </div>
          </form>
        </aside>
      </div>
    </section>`;

  const roomState = r => {
    if (!r.isActive) return { ok: false, why: 'Hidden from guests' };
    if (st.guests > r.capacity) return { ok: false, why: `Sleeps up to ${r.capacity}` };
    if (r.available === false) return { ok: false, why: 'Booked for these dates' };
    return { ok: true };
  };

  function renderRooms() {
    const rooms = st.hotel.rooms;
    const sel = rooms.find(r => r.id === st.roomId);
    if (sel && !roomState(sel).ok) st.roomId = null;
    $('#rooms').innerHTML = rooms.length ? rooms.map(r => {
      const s = roomState(r);
      const selected = st.roomId === r.id;
      return `<li class="room ${selected ? 'is-selected' : ''} ${s.ok ? '' : 'is-off'}">
        <div class="room-info">
          <h3>${esc(r.name)}</h3>
          ${r.description ? `<p class="muted">${esc(r.description)}</p>` : ''}
          <p class="small muted">Sleeps ${r.capacity}</p>
          ${r.amenities.length ? `<ul class="chips">${r.amenities.map(a => `<li class="chip">${esc(a)}</li>`).join('')}</ul>` : ''}
        </div>
        <div class="room-buy">
          <p class="price"><strong>${money(r.pricePerNight)}</strong> a night</p>
          ${s.ok
            ? `<button type="button" class="btn small ${selected ? '' : 'secondary'}" data-room="${r.id}" aria-pressed="${selected}">${selected ? 'Selected' : 'Select room'}</button>`
            : `<p class="why">${esc(s.why)}</p>`}
        </div>
      </li>`;
    }).join('') : '<li class="muted">No rooms are listed for this property yet.</li>';
  }

  function renderSummary() {
    const r = st.hotel.rooms.find(x => x.id === st.roomId);
    const n = validDates(st) ? nightsBetween(st.checkIn, st.checkOut) : 0;
    const a = Auth.get();
    let html;
    if (!r) html = '<p class="muted">Select a room to see your total.</p>';
    else if (!n) html = `<p class="muted">Choose your dates to book ${esc(r.name)}.</p>`;
    else html = `<dl class="sum">
        <div><dt>${money(r.pricePerNight)} × ${plural(n, 'night')}</dt><dd>${money(r.pricePerNight * n)}</dd></div>
        <div class="total"><dt>Total</dt><dd>${money(r.pricePerNight * n)}</dd></div>
      </dl><p class="small muted" style="margin-top:.5rem">Free cancellation until the day before arrival.</p>`;
    $('#summary').innerHTML = html;
    const btn = $('#bookBtn');
    btn.textContent = a ? 'Book this room' : 'Sign in to book';
    btn.disabled = a ? !(r && n) : false;
  }

  const hashFor = () => {
    const p = new URLSearchParams();
    if (st.checkIn) p.set('checkIn', st.checkIn);
    if (st.checkOut) p.set('checkOut', st.checkOut);
    p.set('guests', st.guests);
    return `#/hotel/${id}?${p}`;
  };

  async function refresh() {
    try { st.hotel = await load(); } catch (e) { toast(e.message, 'error'); return; }
    if (!ctx.alive()) return;
    renderRooms();
    renderSummary();
  }

  renderRooms();
  renderSummary();

  $('#rooms').addEventListener('click', e => {
    const b = e.target.closest('[data-room]');
    if (!b) return;
    st.roomId = b.dataset.room;
    renderRooms();
    renderSummary();
  });

  const tIn = $('#tIn'), tOut = $('#tOut'), tGuests = $('#tGuests');
  const onChange = async () => {
    st.checkIn = tIn.value; st.checkOut = tOut.value;
    st.guests = Math.min(20, Math.max(1, Number(tGuests.value) || 1));
    history.replaceState(null, '', hashFor());
    await refresh();
  };
  tIn.addEventListener('change', () => {
    if (tIn.value) {
      tOut.min = addDays(tIn.value, 1);
      if (!tOut.value || tOut.value <= tIn.value) tOut.value = addDays(tIn.value, 1);
    }
    onChange();
  });
  tOut.addEventListener('change', onChange);
  tGuests.addEventListener('change', onChange);

  $('#bookForm').addEventListener('submit', async e => {
    e.preventDefault();
    if (!Auth.get()) { location.hash = '#/login?next=' + encodeURIComponent(location.hash.slice(1)); return; }
    if (!st.roomId || !validDates(st)) return;
    const btn = $('#bookBtn'), err = $('#bookError');
    btn.disabled = true; err.hidden = true;
    try {
      await api('/bookings', { method: 'POST', body: { roomId: st.roomId, checkIn: st.checkIn, checkOut: st.checkOut, guests: st.guests, notes: $('#tNotes').value.trim() || null } });
      toast('Booking confirmed. Your room is reserved.', 'ok');
      location.hash = '#/trips';
    } catch (ex) {
      if (!ctx.alive()) return;
      err.textContent = ex.message; err.hidden = false;
      btn.disabled = false;
      refresh();
    }
  });

  $('#writeReview').addEventListener('click', () => {
    if (!Auth.get()) { toLogin(); return; }
    openReviewForm(id, loadReviews);
  });

  async function loadReviews() {
    const box = $('#reviews');
    try {
      const list = await api(`/hotels/${id}/reviews`, { auth: false });
      if (!ctx.alive()) return;
      box.innerHTML = list.length ? list.map(r => `
        <article class="review">
          <div class="review-head"><strong>${esc(r.authorName)}</strong><span class="muted small">${fmtShort(r.createdAtUtc)}</span></div>
          <div class="stars" role="img" aria-label="${r.rating} out of 5">${'★'.repeat(r.rating)}${'☆'.repeat(5 - r.rating)}</div>
          <p>${esc(r.comment)}</p>
        </article>`).join('') : '<p class="muted">No reviews yet. Guests can review after a completed stay.</p>';
    } catch (e) { box.innerHTML = `<p class="muted">${esc(e.message)}</p>`; }
  }
  loadReviews();
}

function openReviewForm(hotelId, done) {
  openForm({
    title: 'Write a review',
    intro: 'Only guests with a completed stay can review a property.',
    fields: [
      { name: 'rating', label: 'Rating', type: 'select', value: '5', options: [5, 4, 3, 2, 1].map(n => ({ value: n, label: `${n} out of 5` })) },
      { name: 'comment', label: 'Your review', type: 'textarea', required: true, rows: 4, max: 1000 }
    ],
    submit: 'Post review',
    onSubmit: async v => {
      await api('/reviews', { method: 'POST', body: { hotelId, rating: Number(v.rating), comment: v.comment } });
      dlg.close();
      toast('Review posted. Thank you.', 'ok');
      if (done) done();
    }
  });
}

/* ============================== auth pages ============================== */
function afterAuth(ctx) {
  const next = ctx.query.get('next');
  location.hash = '#' + (next && next.startsWith('/') ? next : '/');
}

async function LoginPage(ctx) {
  document.title = 'Sign in | Manzil';
  const local = ['localhost', '127.0.0.1'].includes(location.hostname);
  main.innerHTML = `<section class="wrap"><div class="auth">
    <h1 style="font-size:2rem">Sign in</h1>
    <form id="f" novalidate>
      <label>Email<input name="email" type="email" autocomplete="email" required></label>
      <label>Password<input name="password" type="password" autocomplete="current-password" required></label>
      <p class="form-error" role="alert" hidden></p>
      <button class="btn block" type="submit">Sign in</button>
    </form>
    <p class="muted">New here? <a href="#/register${ctx.query.get('next') ? '?next=' + encodeURIComponent(ctx.query.get('next')) : ''}">Create an account</a></p>
    ${local ? `<details class="demo"><summary>Demo accounts</summary>
      <p>Guest: <code>customer@hotel.local</code> / <code>Customer123!</code></p>
      <p>Owner: <code>owner@hotel.local</code> / <code>Owner123!</code></p>
      <p>Admin: <code>admin@hotel.local</code> / <code>Admin123!</code></p></details>` : ''}
  </div></section>`;
  const form = $('#f'), err = $('.form-error', form);
  form.elements.email.focus();
  form.addEventListener('submit', async e => {
    e.preventDefault();
    err.hidden = true;
    const btn = $('button', form); btn.disabled = true;
    try {
      const a = await api('/auth/login', { method: 'POST', auth: false, body: { email: form.elements.email.value.trim(), password: form.elements.password.value } });
      Auth.set(a); renderHeader(); toast(`Welcome back, ${a.fullName}.`, 'ok'); afterAuth(ctx);
    } catch (ex) { err.textContent = ex.message; err.hidden = false; btn.disabled = false; }
  });
}

async function RegisterPage(ctx) {
  document.title = 'Create account | Manzil';
  main.innerHTML = `<section class="wrap"><div class="auth">
    <h1 style="font-size:2rem">Create your account</h1>
    <form id="f" novalidate>
      <label>Full name<input name="fullName" autocomplete="name" maxlength="100" required></label>
      <label>Email<input name="email" type="email" autocomplete="email" required></label>
      <label>Password<input name="password" type="password" autocomplete="new-password" required>
        <span class="hint">At least 8 characters with upper and lower case letters, a number and a symbol.</span></label>
      <fieldset class="fieldset"><legend>I want to</legend>
        <div class="choice">
          <label><input type="radio" name="role" value="Customer" checked> Book stays</label>
          <label><input type="radio" name="role" value="Owner"> List my property</label>
        </div>
      </fieldset>
      <p class="form-error" role="alert" hidden></p>
      <button class="btn block" type="submit">Create account</button>
    </form>
    <p class="muted">Already registered? <a href="#/login${ctx.query.get('next') ? '?next=' + encodeURIComponent(ctx.query.get('next')) : ''}">Sign in</a></p>
  </div></section>`;
  const form = $('#f'), err = $('.form-error', form);
  form.elements.fullName.focus();
  form.addEventListener('submit', async e => {
    e.preventDefault();
    err.hidden = true;
    const btn = $('button[type=submit]', form); btn.disabled = true;
    try {
      const a = await api('/auth/register', { method: 'POST', auth: false, body: {
        fullName: form.elements.fullName.value.trim(), email: form.elements.email.value.trim(),
        password: form.elements.password.value, role: form.elements.role.value } });
      Auth.set(a); renderHeader(); toast(`Welcome to Manzil, ${a.fullName}.`, 'ok');
      if (!ctx.query.get('next') && isManager(a)) location.hash = '#/manage'; else afterAuth(ctx);
    } catch (ex) { err.textContent = ex.message; err.hidden = false; btn.disabled = false; }
  });
}

/* ============================== my trips ============================== */
async function TripsPage(ctx) {
  document.title = 'My trips | Manzil';
  if (!Auth.get()) return toLogin();
  main.innerHTML = `<section class="wrap page"><div class="page-head"><h1>My trips</h1></div><div id="trips">${skeletons(2)}</div></section>`;

  async function load() {
    const list = await api('/bookings/mine');
    if (!ctx.alive()) return;
    const box = $('#trips');
    if (!list.length) {
      box.innerHTML = `<div class="state"><h2>No trips yet</h2><p>When you book a stay it will show up here, with everything you need on the day.</p><a class="btn" href="#/">Find a place to stay</a></div>`;
      return;
    }
    box.innerHTML = `<div class="trips">${list.map(b => {
      const cancellable = b.status === 'Confirmed' || b.status === 'Pending';
      return `<article class="trip">
        <div class="trip-main">
          <h3><a href="#/hotel/${b.hotelId}">${esc(b.hotelName)}</a></h3>
          <p class="muted">${esc(b.roomName)}</p>
          <dl class="dates">
            <div><dt>Check-in</dt><dd>${fmtDate(b.checkIn)}</dd></div>
            <div><dt>Check-out</dt><dd>${fmtDate(b.checkOut)}</dd></div>
            <div><dt>Guests</dt><dd>${b.guests}</dd></div>
          </dl>
          ${b.notes ? `<p class="small muted">Your note: ${esc(b.notes)}</p>` : ''}
        </div>
        <div class="trip-side">
          <div>
            <div class="chips">${statusChip(b.status)}${paymentChip(b.paymentStatus)}</div>
            <p class="trip-total" style="margin-top:.6rem">${money(b.totalAmount)}</p>
            <p class="small muted">${plural(b.nights, 'night')}</p>
          </div>
          <div class="actions">
            ${cancellable ? `<button class="btn danger small" data-cancel="${b.id}">Cancel booking</button>` : ''}
            ${b.status === 'CheckedOut' ? `<button class="btn small" data-review="${b.hotelId}">Write a review</button>` : ''}
          </div>
        </div>
      </article>`;
    }).join('')}</div>`;
  }
  await load();

  $('#trips').addEventListener('click', async e => {
    const c = e.target.closest('[data-cancel]');
    if (c) {
      const ok = await confirmDialog('Cancel this booking?', 'You get a full refund when you cancel before the day of arrival. This cannot be undone.', 'Cancel booking', true);
      if (!ok) return;
      try { await api(`/bookings/${c.dataset.cancel}/cancel`, { method: 'POST' }); toast('Booking cancelled.', 'ok'); await load(); }
      catch (ex) { toast(ex.message, 'error'); }
      return;
    }
    const r = e.target.closest('[data-review]');
    if (r) openReviewForm(r.dataset.review);
  });
}

/* ============================== manage (owner / admin) ============================== */
async function ManagePage(ctx) {
  document.title = 'Manage | Manzil';
  const a = Auth.get();
  if (!a) return toLogin();
  if (!isManager(a)) {
    main.innerHTML = `<section class="wrap state"><h1>Owner access only</h1><p>Create an account with the "List my property" option to add hotels, rooms and manage reservations.</p><a class="btn" href="#/register">Create an owner account</a></section>`;
    return;
  }
  const tab = ctx.query.get('tab') === 'reservations' ? 'reservations' : 'properties';
  main.innerHTML = `<section class="wrap page">
    <div class="page-head"><h1>Manage</h1>
      <nav class="tabs" aria-label="Manage sections">
        <a href="#/manage" ${tab === 'properties' ? 'aria-current="page"' : ''}>Properties</a>
        <a href="#/manage?tab=reservations" ${tab === 'reservations' ? 'aria-current="page"' : ''}>Reservations</a>
      </nav></div>
    <div id="pane">${skeletons(2)}</div></section>`;
  if (tab === 'reservations') await Reservations(ctx); else await Properties(ctx);
}

const hotelFields = (h = {}, editing = false) => [
  { name: 'name', label: 'Property name', required: true, value: h.name || '', maxlength: 200 },
  { name: 'type', label: 'Type', type: 'select', value: h.type || 'Hotel', options: TYPES.map(t => ({ value: t, label: t })) },
  { name: 'address', label: 'Address', required: true, value: h.address || '' },
  { name: 'city', label: 'City', required: true, value: h.city || '', maxlength: 100 },
  { name: 'country', label: 'Country', required: true, value: h.country || 'Egypt', maxlength: 100 },
  { name: 'description', label: 'Description', type: 'textarea', required: true, rows: 4, max: 4000, value: h.description || '' },
  ...(editing
    ? [{ name: 'isActive', label: 'Visible to guests', type: 'checkbox', value: h.isActive !== false }]
    : [{ name: 'imageUrl', label: 'Photo link', type: 'url', value: '', hint: 'Optional. You can add more photos later.', maxlength: 1000 }])
];

async function Properties(ctx) {
  const pane = $('#pane');
  let list = [];

  const draw = () => {
    pane.innerHTML = `<div class="toolbar"><p class="count">${plural(list.length, 'property')}</p><button class="btn" data-act="add-hotel">Add property</button></div>
      ${list.length ? `<ul class="prop-list">${list.map(h => `
        <li class="prop">
          ${cover(h.coverImage)}
          <div>
            <h3>${esc(h.name)}</h3>
            <p class="muted small">${esc(h.type)} in ${esc(h.city)}, ${esc(h.country)}</p>
            <div class="chips" style="margin-top:.4rem">${h.isActive ? '<span class="chip ok">Visible</span>' : '<span class="chip bad">Hidden</span>'}${ratingHtml(h)}</div>
          </div>
          <div class="actions">
            <button class="btn small" data-act="rooms" data-id="${h.id}">Rooms and photos</button>
            <button class="btn small secondary" data-act="edit" data-id="${h.id}">Edit details</button>
            <a class="btn small ghost" href="#/hotel/${h.id}">View</a>
          </div>
        </li>`).join('')}</ul>`
        : `<div class="state"><h2>List your first property</h2><p>Add the property, then its rooms and prices. Guests can book as soon as it is visible.</p><button class="btn" data-act="add-hotel">Add property</button></div>`}`;
  };

  async function reload() { list = await api('/hotels/mine'); if (ctx.alive()) draw(); }
  await reload();

  const addHotel = () => openForm({
    title: 'Add property', fields: hotelFields(), submit: 'Add property',
    onSubmit: async v => {
      await api('/hotels', { method: 'POST', body: { name: v.name, description: v.description, type: v.type, address: v.address, city: v.city, country: v.country, imageUrl: v.imageUrl || null } });
      dlg.close(); toast('Property added. Now add its rooms.', 'ok'); await reload();
    }
  });

  const editHotel = h => openForm({
    title: 'Edit details', fields: hotelFields(h, true), submit: 'Save changes',
    onSubmit: async v => {
      await api(`/hotels/${h.id}`, { method: 'PUT', body: { name: v.name, description: v.description, type: v.type, address: v.address, city: v.city, country: v.country, isActive: v.isActive } });
      dlg.close(); toast('Changes saved.', 'ok'); await reload();
    }
  });

  pane.addEventListener('click', e => {
    const b = e.target.closest('[data-act]');
    if (!b) return;
    if (b.dataset.act === 'add-hotel') addHotel();
    if (b.dataset.act === 'edit') editHotel(list.find(h => h.id === b.dataset.id));
    if (b.dataset.act === 'rooms') openRooms(b.dataset.id, reload);
  });
}

async function openRooms(hotelId, onChanged) {
  roomsChanged = onChanged || null;
  showDialog('<p class="muted">Loading…</p>');
  let h, amenities;
  try { [h, amenities] = await Promise.all([api(`/hotels/${hotelId}`), api('/amenities', { auth: false })]); }
  catch (e) { showDialog(`<h2 id="dlgTitle">Could not load rooms</h2><p>${esc(e.message)}</p><div class="dlg-actions"><button class="btn" data-d="close">Close</button></div>`, ev => { if (ev.target.closest('[data-d]')) dlg.close(); }); return; }

  const roomForm = room => openForm({
    title: room ? 'Edit room' : 'Add room',
    fields: [
      { name: 'name', label: 'Room name', required: true, value: room?.name || '', maxlength: 200 },
      { name: 'description', label: 'Description', type: 'textarea', value: room?.description || '', rows: 3 },
      { name: 'capacity', label: 'Sleeps (guests)', type: 'number', required: true, min: 1, max: 20, value: room?.capacity ?? 2 },
      { name: 'pricePerNight', label: `Price a night (${CURRENCY})`, type: 'number', required: true, min: 1, step: '0.01', value: room?.pricePerNight ?? '' },
      { name: 'amenityIds', label: 'Amenities', type: 'checkboxes', options: amenities.map(a => ({ value: a.id, label: a.name })),
        value: room ? amenities.filter(a => room.amenities.includes(a.name)).map(a => a.id) : [] },
      ...(room ? [{ name: 'isActive', label: 'Available to book', type: 'checkbox', value: room.isActive }] : [])
    ],
    submit: room ? 'Save room' : 'Add room',
    onCancel: () => openRooms(hotelId, onChanged),
    onSubmit: async v => {
      const body = { name: v.name, description: v.description, capacity: v.capacity, pricePerNight: v.pricePerNight, amenityIds: v.amenityIds };
      if (room) await api(`/hotels/${hotelId}/rooms/${room.id}`, { method: 'PUT', body: { ...body, isActive: v.isActive } });
      else await api(`/hotels/${hotelId}/rooms`, { method: 'POST', body });
      toast(room ? 'Room saved.' : 'Room added.', 'ok');
      await openRooms(hotelId, onChanged);
    }
  });

  const photoForm = () => openForm({
    title: 'Add photo', intro: 'Paste a link to an image that is already online.',
    fields: [{ name: 'url', label: 'Photo link', type: 'url', required: true, value: '', maxlength: 1000 }],
    submit: 'Add photo', onCancel: () => openRooms(hotelId, onChanged),
    onSubmit: async v => { await api(`/hotels/${hotelId}/images`, { method: 'POST', body: { url: v.url } }); toast('Photo added.', 'ok'); await openRooms(hotelId, onChanged); }
  });

  showDialog(`
    <h2 id="dlgTitle">${esc(h.name)}</h2>
    <section><h3 style="margin-bottom:.4rem">Rooms</h3>
      ${h.rooms.length ? `<ul>${h.rooms.map(r => `<li class="room-row">
        <div><strong>${esc(r.name)}</strong> <span class="muted small">sleeps ${r.capacity}</span><br>
          <span class="small">${money(r.pricePerNight)} a night</span> ${r.isActive ? '' : '<span class="chip bad">Not bookable</span>'}</div>
        <div class="actions"><button class="btn small secondary" data-edit="${r.id}">Edit</button>
          <button class="btn small ghost" data-toggle="${r.id}">${r.isActive ? 'Stop bookings' : 'Allow bookings'}</button></div>
      </li>`).join('')}</ul>` : '<p class="muted">No rooms yet. Add the first one so guests can book.</p>'}
      <div style="margin-top:.8rem"><button class="btn small" data-add-room>Add room</button></div>
    </section>
    <section><h3 style="margin-bottom:.5rem">Photos</h3>
      ${h.images.length ? `<div class="thumbs">${h.images.map(u => cover(u)).join('')}</div>` : '<p class="muted">No photos yet.</p>'}
      <div style="margin-top:.8rem"><button class="btn small secondary" data-add-photo>Add photo</button></div>
    </section>
    <div class="dlg-actions"><button class="btn" data-close>Done</button></div>`,
    async e => {
      if (e.target.closest('[data-close]')) { dlg.close(); return; }
      if (e.target.closest('[data-add-room]')) return roomForm(null);
      if (e.target.closest('[data-add-photo]')) return photoForm();
      const ed = e.target.closest('[data-edit]');
      if (ed) return roomForm(h.rooms.find(r => r.id === ed.dataset.edit));
      const tg = e.target.closest('[data-toggle]');
      if (tg) {
        const r = h.rooms.find(x => x.id === tg.dataset.toggle);
        try {
          await api(`/hotels/${hotelId}/rooms/${r.id}`, { method: 'PUT', body: { name: r.name, description: r.description, capacity: r.capacity, pricePerNight: r.pricePerNight, isActive: !r.isActive, amenityIds: null } });
          await openRooms(hotelId, onChanged);
        } catch (ex) { toast(ex.message, 'error'); }
      }
    });
}

async function Reservations(ctx) {
  const pane = $('#pane');
  let list = [];
  let filter = '';

  const draw = () => {
    const rows = filter ? list.filter(b => b.status === filter) : list;
    const count = s => list.filter(b => b.status === s).length;
    const revenue = list.filter(b => b.paymentStatus === 'Paid' && b.status !== 'Cancelled').reduce((s, b) => s + b.totalAmount, 0);
    pane.innerHTML = `
      <dl class="stats">
        <div><dt>Arriving</dt><dd>${count('Confirmed')}</dd></div>
        <div><dt>In house</dt><dd>${count('CheckedIn')}</dd></div>
        <div><dt>Completed</dt><dd>${count('CheckedOut')}</dd></div>
        <div><dt>Paid revenue</dt><dd>${money(revenue)}</dd></div>
      </dl>
      <div class="toolbar"><label style="min-width:200px">Show
        <select id="statusFilter">
          ${[['', 'All reservations'], ['Confirmed', 'Arriving'], ['CheckedIn', 'In house'], ['CheckedOut', 'Completed'], ['Cancelled', 'Cancelled']].map(([v, l]) => `<option value="${v}" ${filter === v ? 'selected' : ''}>${l}</option>`).join('')}
        </select></label></div>
      ${rows.length ? `<div class="table-wrap"><table>
        <thead><tr><th>Guest</th><th>Property and room</th><th>Stay</th><th>Total</th><th>Status</th><th><span class="sr">Actions</span></th></tr></thead>
        <tbody>${rows.map(b => `<tr>
          <td><strong>${esc(b.customerName || 'Guest')}</strong><br><span class="muted small">${esc(b.customerEmail || '')}</span></td>
          <td>${esc(b.hotelName)}<br><span class="muted small">${esc(b.roomName)}</span></td>
          <td>${fmtShort(b.checkIn)} to ${fmtShort(b.checkOut)}<br><span class="muted small">${plural(b.nights, 'night')}, ${plural(b.guests, 'guest')}</span></td>
          <td>${money(b.totalAmount)}<br>${paymentChip(b.paymentStatus)}</td>
          <td>${statusChip(b.status)}</td>
          <td>${b.status === 'Confirmed' ? `<button class="btn small" data-in="${b.id}">Check in</button>` : ''}${b.status === 'CheckedIn' ? `<button class="btn small secondary" data-out="${b.id}">Check out</button>` : ''}</td>
        </tr>`).join('')}</tbody></table></div>`
        : '<div class="state"><h2>No reservations here</h2><p>Reservations for your properties will appear as guests book.</p></div>'}`;
  };

  async function reload() { list = await api('/admin/bookings'); if (ctx.alive()) draw(); }
  await reload();

  pane.addEventListener('change', e => { if (e.target.id === 'statusFilter') { filter = e.target.value; draw(); } });
  pane.addEventListener('click', async e => {
    const i = e.target.closest('[data-in]'), o = e.target.closest('[data-out]');
    const btn = i || o;
    if (!btn) return;
    btn.disabled = true;
    try {
      await api(`/admin/bookings/${i ? i.dataset.in : o.dataset.out}/${i ? 'check-in' : 'check-out'}`, { method: 'POST' });
      toast(i ? 'Guest checked in.' : 'Guest checked out.', 'ok');
      await reload();
    } catch (ex) { toast(ex.message, 'error'); btn.disabled = false; }
  });
}

/* ============================== global events & boot ============================== */
document.addEventListener('click', e => {
  const b = e.target.closest('[data-act]');
  if (!b) return;
  if (b.dataset.act === 'signout') {
    Auth.clear(); toast('You have signed out.');
    if (parseHash().path === '/' ) route(); else location.hash = '#/';
    if (parseHash().path !== '/') return;
  }
  if (b.dataset.act === 'reload') route();
});
// Hide photos that fail to load so the gradient placeholder shows instead of a broken icon.
document.addEventListener('error', e => { if (e.target && e.target.tagName === 'IMG') e.target.classList.add('broken'); }, true);
window.addEventListener('hashchange', route);
route();
})();
