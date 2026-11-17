// The shell's few jobs that must not cost a round trip: the theme picker, the dialog's focus trap and
// Escape, the keyboard shortcuts, the tab's remembered week, and downloads.
(function () {
    const themes = ['Setsquare Dark', 'Chalkline Dark', 'Instrument Dark', 'Sandtable Dark'];
    let keysRef = null;
    let trapped = null;

    function currentTheme() {
        return document.documentElement.getAttribute('data-theme') || 'Setsquare Dark';
    }

    document.addEventListener('change', e => {
        const sel = e.target.closest && e.target.closest('[data-theme-picker]');
        if (!sel) return;
        const t = sel.value;
        if (themes.indexOf(t) < 0) return;
        document.documentElement.setAttribute('data-theme', t);
        try { localStorage.setItem('cccc.theme', t); } catch (err) { }
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
