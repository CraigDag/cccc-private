// The shell's few jobs that must not cost a round trip: the theme picker, the dialog's focus trap and
// Escape, the keyboard shortcuts, the tab's remembered week, and downloads.
(function () {
    const themes = ['auto', 'Paper', 'Night'];
    let keysRef = null;
    let trapped = null;

    function currentTheme() {
        return document.documentElement.getAttribute('data-theme') || 'auto';
    }

    document.addEventListener('change', e => {
        const sel = e.target.closest && e.target.closest('[data-theme-picker]');
        if (!sel) return;
        const t = sel.value;
        if (themes.indexOf(t) < 0) return;
        if (t === 'auto') document.documentElement.removeAttribute('data-theme');
        else document.documentElement.setAttribute('data-theme', t);
        try { if (t === 'auto') localStorage.removeItem('cccc.theme'); else localStorage.setItem('cccc.theme', t); } catch (err) { }
    });

    // Ctrl+K or / opens Find from anywhere; choosing a link in an open menu closes the menu.
    document.addEventListener('keydown', e => {
        const tag = (e.target.tagName || '').toLowerCase();
        const typing = tag === 'input' || tag === 'textarea' || tag === 'select' || e.target.isContentEditable;
        if (((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) || (!typing && e.key === '/' && !e.ctrlKey && !e.metaKey)) {
            const b = document.querySelector('[data-find-open]');
            if (b && !document.querySelector('[data-testid=dialog]')) { e.preventDefault(); b.click(); }
        }
    }, true);
    document.addEventListener('click', e => {
        document.querySelectorAll('details[open]').forEach(d => {
            if (!d.contains(e.target) || e.target.closest('a, button[type=submit], .dock-sheet a')) d.removeAttribute('open');
        });
    });

    // A theme picker drawn by a render shows the theme in use.
    new MutationObserver(() => {
        document.querySelectorAll('[data-theme-picker]').forEach(s => { if (s.value !== currentTheme()) s.value = currentTheme(); });
    }).observe(document.documentElement, { childList: true, subtree: true });

    function focusables(root) {
        return Array.from(root.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
            .filter(el => el.offsetParent !== null);
    }

    document.addEventListener('keydown', e => {
        if (trapped && document.body.contains(trapped)) {
            if (e.key === 'Escape') {
                e.preventDefault();
                const x = trapped.querySelector('[data-close]');
                if (x) x.click();
                return;
            }
            if (e.key === 'Tab') {
                const f = focusables(trapped);
                if (f.length === 0) return;
                const first = f[0], last = f[f.length - 1];
                if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
                else if (!trapped.contains(document.activeElement)) { e.preventDefault(); first.focus(); }
            }
            return;
        }
        if (!keysRef) return;
        const tag = (e.target.tagName || '').toLowerCase();
        if (tag === 'input' || tag === 'textarea' || tag === 'select' || e.target.isContentEditable) return;
        let name = null;
        const mod = e.ctrlKey || e.metaKey;
        if (e.key === 'Delete') name = 'delete';
        else if (e.key === 'Escape') name = 'escape';
        else if (mod && !e.shiftKey && (e.key === 'z' || e.key === 'Z')) name = 'undo';
        else if (mod && (e.key === 'y' || e.key === 'Y' || (e.shiftKey && (e.key === 'z' || e.key === 'Z')))) name = 'redo';
        else if (!mod && e.key === 'ArrowLeft' && e.altKey) name = 'previous';
        else if (!mod && e.key === 'ArrowRight' && e.altKey) name = 'next';
        if (!name) return;
        e.preventDefault();
        keysRef.invokeMethodAsync('Key', name);
    });

    window.ccccShell = {
        trap(panel) {
            // When the dialog goes, focus goes back where it came from (by id for a block, which a
            // re-render may have replaced), so a keyboard user is not dropped at the top of the page.
            const from = panel.contains(document.activeElement) ? null : document.activeElement;
            const fromId = from && from.dataset ? from.dataset.id : null;
            const watch = new MutationObserver(() => {
                if (document.body.contains(panel)) return;
                watch.disconnect();
                if (document.querySelector('[data-testid=dialog]')) return;   // another dialog took over
                const back = (from && document.body.contains(from)) ? from
                    : fromId ? document.querySelector(`[data-id="${CSS.escape(fromId)}"][tabindex]`) : null;
                if (back && (document.activeElement === document.body || !document.activeElement)) back.focus();
            });
            watch.observe(document.body, { childList: true, subtree: true });
            trapped = panel;
            const f = focusables(panel).filter(el => !el.hasAttribute('data-close'));
            const first = panel.querySelector('[autofocus]') || f.find(el => el.matches('input, select, textarea')) || f[0];
            if (first) first.focus();
        },
        trapped() { return !!(trapped && document.body.contains(trapped)); },
        keys(ref) { keysRef = ref; },
        unkeys(ref) { if (keysRef === ref) keysRef = null; },
        remember(key, value) { try { sessionStorage.setItem(key, value); } catch (e) { } },
        recall(key) { try { return sessionStorage.getItem(key); } catch (e) { return null; } },
        narrow() { return window.matchMedia('(max-width: 639px)').matches; },
        setBoxText(el, text) { if (el) el.value = text; },
        open(url) { window.open(url, '_blank', 'noopener'); },
    };
})();
