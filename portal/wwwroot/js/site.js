/* Piezas comunes de la web: configuracion, menu, pie, estado del servidor, brasas del fondo y utilidades. */
const CLASSES = [
  { name: 'Mago', role: 'Magia a distancia', text: 'Hechizos de área y control. Frágil, pero devastador cuando nadie lo alcanza.' },
  { name: 'Caballero', role: 'Guerrero', text: 'Vida, defensa y tajos contundentes. El que abre la pelea.' },
  { name: 'Elfa', role: 'Arquera · Invocadora', text: 'Flechas rápidas, curas y su criatura invocada que ella misma dirige.' },
  { name: 'Gladiador Mágico', role: 'Híbrido', text: 'Espada y magia a la vez: pega fuerte de cerca y de lejos.' },
  { name: 'Dark Lord', role: 'Comandante', text: 'Cuervo, caballo y gritos de guerra: lidera a su equipo.' },
  { name: 'Invocadora', role: 'Control', text: 'Maldiciones, bestias y estados alterados para ahogar al rival.' },
  { name: 'Rage Fighter', role: 'Luchador', text: 'Combos veloces y puños de fuego. Siempre en el cuerpo a cuerpo.' },
];

const Site = {
  config: null,

  async api(path, options) {
    const response = await fetch('/api' + path, options);
    let data = null;
    try { data = await response.json(); } catch (e) { /* sin cuerpo */ }
    if (!response.ok) { throw Object.assign(new Error((data && data.error) || 'Error ' + response.status), { status: response.status }); }
    return data;
  },

  esc(text) { return String(text ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); },

  fmt(n) { return Number(n || 0).toLocaleString('es'); },

  classIcon(i) { return '/img/class-' + (Number(i) >= 0 && Number(i) < 7 ? Number(i) : 0) + '.png'; },

  className(i) { return (CLASSES[i] || CLASSES[0]).name; },

  duration(seconds) { const m = Math.floor(seconds / 60); return m + ':' + String(seconds % 60).padStart(2, '0'); },

  ago(iso) {
    const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
    if (s < 3600) return 'hace ' + Math.max(1, Math.round(s / 60)) + ' min';
    if (s < 86400) return 'hace ' + Math.round(s / 3600) + ' h';
    return 'hace ' + Math.round(s / 86400) + ' d';
  },

  async mount(page) {
    const params = new URLSearchParams(location.search);
    try { this.config = await this.api('/config'); } catch (e) { this.config = { name: 'Eclipse Arena', subtitle: 'MU MOBA · ONLINE', notice: '', theme: 'eclipse', tagline: '', registrationEnabled: true }; }
    const theme = params.get('theme') || this.config.theme || 'gold';
    document.documentElement.dataset.theme = theme;
    document.title = (page && page.title ? page.title + ' · ' : '') + this.config.name;

    const links = [
      ['/', 'Inicio', 'home'], ['/jugar.html', 'Cómo jugar', 'play'], ['/ranking.html', 'Ranking', 'ranking'], ['/noticias.html', 'Noticias', 'news'],
    ];
    const qs = params.get('theme') ? '?theme=' + encodeURIComponent(params.get('theme')) : '';
    const nav = document.createElement('nav');
    nav.className = 'nav';
    nav.innerHTML = '<div class="wrap"><a class="brand" href="/' + qs + '">' + this.esc(this.config.name) + '</a>'
      + '<button class="burger" aria-label="Menú">☰</button><ul>'
      + links.map(l => '<li><a href="' + l[0] + qs + '" class="' + (page && page.id === l[2] ? 'active' : '') + '">' + l[1] + '</a></li>').join('')
      + '<li><a class="cta" href="/registro.html' + qs + '">Crear cuenta</a></li></ul></div>';
    document.body.prepend(nav);
    nav.querySelector('.burger').onclick = () => nav.querySelector('ul').classList.toggle('open');
    const onScroll = () => nav.classList.toggle('solid', window.scrollY > 40 || !document.querySelector('.hero'));
    addEventListener('scroll', onScroll, { passive: true });
    onScroll();

    const footer = document.createElement('footer');
    footer.innerHTML = '<div class="wrap"><a class="brand" href="/' + qs + '">' + this.esc(this.config.name) + '</a>'
      + '<span class="notice">' + this.esc(this.config.notice || 'Servidor privado de MU Online hecho por fans. Sin fines de lucro y sin afiliación con Webzen.') + '</span>'
      + '<span><a href="/jugar.html' + qs + '">Cómo jugar</a> · <a href="/ranking.html' + qs + '">Ranking</a> · <a href="/noticias.html' + qs + '">Noticias</a></span></div>';
    document.body.append(footer);

    const io = new IntersectionObserver(entries => entries.forEach(e => { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } }), { threshold: 0.12 });
    document.querySelectorAll('.reveal').forEach(el => io.observe(el));
    if (/[?&]shot\b/.test(location.search)) {
      // modo captura: muestra todo sin esperar al scroll y limita el alto del hero
      document.querySelectorAll('.reveal').forEach(el => { el.style.transition = 'none'; el.classList.add('in'); });
      document.documentElement.style.setProperty('--hero-h', '780px');
    }
    this.embers();
    this.status();
    setInterval(() => this.status(), 20000);
  },

  async status() {
    const targets = document.querySelectorAll('[data-status]');
    if (!targets.length) return;
    let html;
    try {
      const s = await this.api('/status');
      const dotClass = s.online ? 'on' : 'off';
      const players = s.players === null || s.players === undefined ? '' : ' · <b>' + s.players + '</b> jugando' + (s.inMatch ? ' · <b>' + s.inMatch + '</b> en partida' : '');
      html = '<span class="dot ' + dotClass + '"></span><span>Servidor <b>' + (s.online ? 'en línea' : 'fuera de línea') + '</b>' + (s.online ? players : '') + '</span>';
    } catch (e) {
      html = '<span class="dot"></span><span>Estado no disponible</span>';
    }
    targets.forEach(t => { t.innerHTML = html; });
  },

  /* brasas flotando sobre el fondo del hero */
  embers() {
    const canvas = document.getElementById('embers');
    if (!canvas || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const ctx = canvas.getContext('2d');
    let w, h, dots = [];
    const color = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#f1c24a';
    const resize = () => { w = canvas.width = canvas.offsetWidth; h = canvas.height = canvas.offsetHeight; };
    resize();
    addEventListener('resize', resize);
    for (let i = 0; i < 70; i++) dots.push({ x: Math.random() * w, y: Math.random() * h, r: Math.random() * 2.2 + .5, v: Math.random() * .6 + .2, d: Math.random() * 6.28, a: Math.random() * .7 + .2 });
    const tick = () => {
      ctx.clearRect(0, 0, w, h);
      for (const p of dots) {
        p.y -= p.v; p.x += Math.sin(p.d += .01) * .4;
        if (p.y < -10) { p.y = h + 10; p.x = Math.random() * w; }
        ctx.beginPath();
        ctx.fillStyle = color;
        ctx.globalAlpha = p.a * (0.5 + 0.5 * Math.sin(p.d * 2));
        ctx.shadowColor = color; ctx.shadowBlur = 12;
        ctx.arc(p.x, p.y, p.r, 0, 6.28);
        ctx.fill();
      }
      requestAnimationFrame(tick);
    };
    tick();
  },

  lightbox() {
    const box = document.createElement('div');
    box.className = 'lightbox';
    box.innerHTML = '<img alt="">';
    box.onclick = () => box.classList.remove('open');
    document.body.append(box);
    document.querySelectorAll('.gallery figure').forEach(fig => {
      fig.onclick = () => { box.querySelector('img').src = fig.querySelector('img').src; box.classList.add('open'); };
    });
  },
};
