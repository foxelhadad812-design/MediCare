/**
 * MediCare — animations.js
 * Scroll reveal, count-up, navbar scroll, back-to-top, toast system.
 * Pure vanilla JS — no external libraries.
 */

(function () {
  'use strict';

  /* -------------------------------------------------------------------------
     1. SCROLL REVEAL — IntersectionObserver
     Adds `.is-visible` to any element with class `.reveal` or `.reveal-scale`.
     Auto-staggers sibling groups using `.reveal-delay-N` applied in markup.
  -------------------------------------------------------------------------- */
  function initScrollReveal() {
    const targets = document.querySelectorAll('.reveal, .reveal-scale');
    if (!targets.length) return;

    const io = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            io.unobserve(entry.target);          // run once
          }
        });
      },
      { threshold: 0.1, rootMargin: '0px 0px -40px 0px' }
    );

    targets.forEach(function (el) { io.observe(el); });
  }

  /* -------------------------------------------------------------------------
     2. NAVBAR — shadow + background on scroll
  -------------------------------------------------------------------------- */
  function initNavbarScroll() {
    var navbar = document.querySelector('.mc-navbar');
    if (!navbar) return;

    function onScroll() {
      if (window.scrollY > 20) {
        navbar.classList.add('scrolled');
      } else {
        navbar.classList.remove('scrolled');
      }
    }

    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();                                  // check initial state
  }

  /* -------------------------------------------------------------------------
     3. BACK-TO-TOP BUTTON
  -------------------------------------------------------------------------- */
  function initBackToTop() {
    var btn = document.getElementById('mc-back-to-top');
    if (!btn) return;

    window.addEventListener('scroll', function () {
      if (window.scrollY > 300) {
        btn.classList.add('visible');
      } else {
        btn.classList.remove('visible');
      }
    }, { passive: true });

    btn.addEventListener('click', function () {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    });
  }

  /* -------------------------------------------------------------------------
     4. SMOOTH SCROLLING for anchor links
  -------------------------------------------------------------------------- */
  function initSmoothScroll() {
    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
      anchor.addEventListener('click', function (e) {
        var target = document.querySelector(this.getAttribute('href'));
        if (target) {
          e.preventDefault();
          target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     5. COUNT-UP — triggers when stat number enters viewport
     Usage: <span class="count-up" data-target="1234">0</span>
  -------------------------------------------------------------------------- */
  function initCountUp() {
    var elements = document.querySelectorAll('.count-up[data-target]');
    if (!elements.length) return;

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        io.unobserve(entry.target);
        animateCount(entry.target);
      });
    }, { threshold: 0.5 });

    elements.forEach(function (el) { io.observe(el); });
  }

  function animateCount(el) {
    var target = parseFloat(el.dataset.target) || 0;
    var duration = 1200;
    var start = performance.now();
    var suffix = el.dataset.suffix || '';

    function tick(now) {
      var elapsed = now - start;
      var progress = Math.min(elapsed / duration, 1);
      var ease = 1 - Math.pow(1 - progress, 3);  // ease-out cubic
      var current = Math.round(ease * target);
      el.textContent = current.toLocaleString() + suffix;
      if (progress < 1) requestAnimationFrame(tick);
    }

    requestAnimationFrame(tick);
  }

  /* -------------------------------------------------------------------------
     6. SUBMIT BUTTON LOADING STATE
     Adds `.btn-loading` while the form is submitting.
  -------------------------------------------------------------------------- */
  function initSubmitLoading() {
    document.querySelectorAll('form').forEach(function (form) {
      var submitBtn = form.querySelector('[type="submit"]');
      if (!submitBtn) return;

      form.addEventListener('submit', function () {
        // Only show spinner if HTML5 validation passed
        if (!form.checkValidity || form.checkValidity()) {
          submitBtn.classList.add('btn-loading');
          submitBtn.disabled = true;
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     7. SHAKE invalid forms on submit
  -------------------------------------------------------------------------- */
  function initFormShake() {
    document.querySelectorAll('form').forEach(function (form) {
      form.addEventListener('submit', function (e) {
        if (form.checkValidity && !form.checkValidity()) {
          e.preventDefault();
          e.stopPropagation();
          form.classList.add('was-validated');

          var firstInvalid = form.querySelector(':invalid');
          if (firstInvalid) {
            firstInvalid.classList.remove('shake');
            void firstInvalid.offsetWidth;         // force reflow
            firstInvalid.classList.add('shake');

            firstInvalid.addEventListener('animationend', function () {
              firstInvalid.classList.remove('shake');
            }, { once: true });
          }
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     8. TOAST SYSTEM
     Usage: window.mcToast({ message: '...', type: 'success' | 'danger' | 'warning' | 'info', duration: 4000 })
  -------------------------------------------------------------------------- */
  function initToastContainer() {
    if (!document.getElementById('mc-toast-container')) {
      var container = document.createElement('div');
      container.id = 'mc-toast-container';
      document.body.appendChild(container);
    }
  }

  window.mcToast = function (options) {
    var opts = Object.assign({ message: '', type: 'info', duration: 4000 }, options);
    var container = document.getElementById('mc-toast-container');
    if (!container) return;

    var iconMap = {
      success: 'bi-check-circle-fill text-success',
      danger:  'bi-exclamation-triangle-fill text-danger',
      warning: 'bi-exclamation-circle-fill text-warning',
      info:    'bi-info-circle-fill text-primary'
    };
    var icon = iconMap[opts.type] || iconMap.info;

    var toast = document.createElement('div');
    toast.className = 'mc-toast';
    toast.innerHTML =
      '<div class="mc-toast-body">' +
        '<i class="bi ' + icon + ' mc-toast-icon"></i>' +
        '<span class="mc-toast-text">' + opts.message + '</span>' +
        '<button type="button" style="background:none;border:none;margin-left:auto;padding:0;opacity:0.5;cursor:pointer;font-size:1rem;line-height:1;" onclick="this.closest(\'.mc-toast\').dispatchEvent(new CustomEvent(\'dismiss\'))">' +
          '<i class="bi bi-x"></i>' +
        '</button>' +
      '</div>' +
      '<div class="mc-toast-progress" style="animation-duration:' + opts.duration + 'ms;"></div>';

    container.appendChild(toast);

    function dismiss() {
      toast.classList.add('dismissing');
      toast.addEventListener('animationend', function () {
        toast.remove();
      }, { once: true });
    }

    toast.addEventListener('dismiss', dismiss);
    setTimeout(dismiss, opts.duration);
  };

  /* -------------------------------------------------------------------------
     9. DROPDOWN animation — ensure Bootstrap dropdowns get animation
  -------------------------------------------------------------------------- */
  function initDropdowns() {
    document.querySelectorAll('.dropdown').forEach(function (dd) {
      dd.addEventListener('shown.bs.dropdown', function () {
        var menu = dd.querySelector('.dropdown-menu');
        if (menu) {
          menu.style.animation = 'none';
          void menu.offsetWidth;
          menu.style.animation = '';
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     10. TIME SLOT SELECTION — Book.cshtml
  -------------------------------------------------------------------------- */
  function initTimeSlots() {
    var slots = document.querySelectorAll('.time-slot-btn');
    if (!slots.length) return;

    slots.forEach(function (slot) {
      slot.addEventListener('click', function () {
        slots.forEach(function (s) { s.classList.remove('selected'); });
        slot.classList.add('selected');
      });
    });
  }

  /* -------------------------------------------------------------------------
     INIT — run everything after DOM is ready
  -------------------------------------------------------------------------- */
  function init() {
    initScrollReveal();
    initNavbarScroll();
    initBackToTop();
    initSmoothScroll();
    initCountUp();
    initSubmitLoading();
    initFormShake();
    initToastContainer();
    initDropdowns();
    initTimeSlots();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

})();
