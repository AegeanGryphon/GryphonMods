/* ============================================================
   GryphonMods — Main JS
   ============================================================ */

// ── Nav scroll state ────────────────────────────────────────
const nav = document.querySelector('.nav');
if (nav) {
  const onScroll = () => nav.classList.toggle('scrolled', window.scrollY > 20);
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
}

// ── Mobile hamburger ─────────────────────────────────────────
const hamburger = document.querySelector('.nav__hamburger');
const mobileNav = document.querySelector('.nav__mobile');
if (hamburger && mobileNav) {
  hamburger.addEventListener('click', () => {
    const open = mobileNav.classList.toggle('open');
    hamburger.setAttribute('aria-expanded', open);
    const [s1, s2, s3] = hamburger.querySelectorAll('span');
    if (open) {
      s1.style.transform = 'translateY(7px) rotate(45deg)';
      s2.style.opacity   = '0';
      s3.style.transform = 'translateY(-7px) rotate(-45deg)';
    } else {
      s1.style.transform = s3.style.transform = '';
      s2.style.opacity = '';
    }
  });
  // Close on nav link click
  mobileNav.querySelectorAll('a').forEach(a =>
    a.addEventListener('click', () => {
      mobileNav.classList.remove('open');
      hamburger.setAttribute('aria-expanded', false);
      hamburger.querySelectorAll('span').forEach(s => {
        s.style.transform = '';
        s.style.opacity   = '';
      });
    })
  );
}

// ── Active nav link ──────────────────────────────────────────
document.querySelectorAll('.nav__link[data-page]').forEach(link => {
  const page = link.dataset.page;
  if (window.location.pathname.endsWith(page) ||
     (page === 'index.html' && (window.location.pathname === '/' ||
      window.location.pathname.endsWith('/')))) {
    link.classList.add('active');
  }
});

// ── Scroll reveal ────────────────────────────────────────────
const revealObserver = new IntersectionObserver((entries) => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      revealObserver.unobserve(entry.target);
    }
  });
}, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });

document.querySelectorAll('.reveal').forEach(el => revealObserver.observe(el));

// ── Mod filter ───────────────────────────────────────────────
const filterBtns = document.querySelectorAll('.filter-btn');
const modCards   = document.querySelectorAll('.mod-card[data-tags]');

filterBtns.forEach(btn => {
  btn.addEventListener('click', () => {
    filterBtns.forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    const filter = btn.dataset.filter;
    modCards.forEach(card => {
      const tags = card.dataset.tags || '';
      const show = filter === 'all' || tags.includes(filter);
      card.style.display    = show ? '' : 'none';
      card.style.opacity    = show ? '' : '0';
    });
  });
});

// ── Manifest-driven version badges ──────────────────────────
(async () => {
  const cards = document.querySelectorAll('[data-mod-id]');
  if (!cards.length) return;

  try {
    const res = await fetch(
      'https://raw.githubusercontent.com/AegeanGryphon/GryphonMods/main/manifest.json',
      { cache: 'no-cache' }
    );
    if (!res.ok) return;
    const { mods } = await res.json();

    cards.forEach(card => {
      const mod = mods.find(m => m.id === card.dataset.modId);
      if (!mod) return;

      // Version badge  (index: "v1.0.1"  |  mods: "v1.0.1 · AegeanGryphon")
      const versionEl = card.querySelector('.mod-card__version');
      if (versionEl) {
        const hasAuthor = versionEl.textContent.includes('·');
        versionEl.textContent = hasAuthor
          ? `v${mod.version} · ${mod.author}`
          : `v${mod.version}`;
      }

      // GitHub release link
      const githubLink = card.querySelector('a[href*="releases/tag"]');
      if (githubLink) {
        githubLink.href =
          `https://github.com/AegeanGryphon/GryphonMods/releases/tag/${mod.name}-v${mod.version}`;
      }

      // Changelog box (mods.html only — element has data-mod-changelog attribute)
      if (mod.changelog) {
        const changelogEl = card.querySelector('[data-mod-changelog]');
        if (changelogEl) {
          // Strip leading **vX.Y.Z** — prefix that markdown bold wraps
          const text = mod.changelog.replace(/^\*\*v[\d.]+\*\*\s*—\s*/, '');
          changelogEl.innerHTML =
            `<strong style="color:var(--text-primary);">v${mod.version} changelog:</strong> ${text}`;
        }
      }
    });
  } catch {
    // Silently fail — static version text in HTML remains visible
  }
})();

// ── Form submission (Formspree) ──────────────────────────────
const contactForm = document.getElementById('contact-form');
if (contactForm) {
  contactForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const btn = contactForm.querySelector('[type="submit"]');
    const orig = btn.innerHTML;
    btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"/></svg> Sending…';
    btn.disabled = true;
    try {
      const res = await fetch(contactForm.action, {
        method: 'POST',
        body: new FormData(contactForm),
        headers: { Accept: 'application/json' }
      });
      if (res.ok) {
        contactForm.innerHTML = `
          <div style="text-align:center; padding: 48px 24px;">
            <div style="font-size:3rem; margin-bottom:16px;">✅</div>
            <h3 style="font-family:'Outfit',sans-serif; font-size:1.4rem; margin-bottom:8px;">Got it, thanks!</h3>
            <p style="color:var(--text-secondary); font-size:0.9rem;">We'll review your submission and get back to you if needed.</p>
          </div>`;
      } else {
        throw new Error();
      }
    } catch {
      btn.innerHTML = orig;
      btn.disabled = false;
      alert('Something went wrong. Please try again or open a GitHub issue directly.');
    }
  });
}
