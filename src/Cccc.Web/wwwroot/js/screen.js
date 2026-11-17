// A screen in the building (/screen). Waiting to be paired, it asks every few seconds whether an
// administrator has typed its code. Paired, it keeps its clock ticking on the school's time (never the
// TV's own clock or zone), fetches the page again every half minute and swaps in the new board, and
// pages through the spaces when there are more than fit. If the timetable cannot be reached it keeps
// showing what it last knew and says since when, rather than going blank in front of everybody.
(function () {
    'use strict';
    var REFRESH = 30000, PAGE = 12000, PAIR = 3000, FULL = 6 * 3600 * 1000;
    var base = 0, t0 = 0, page = 0, pages = 1, lastGood = null, version = null, started = Date.now();

    function board() { return document.querySelector('[data-screen-board]'); }
    function pairing() { return document.querySelector('[data-screen-pair]'); }
    function two(n) { return (n < 10 ? '0' : '') + n; }

    function readClock() {
        var b = board();
        var s = b ? Number(b.getAttribute('data-seconds')) : NaN;
        if (!isNaN(s)) { base = s; t0 = performance.now(); }
    }

    function hm() {
        var secs = Math.floor(base + (performance.now() - t0) / 1000) % 86400;
        return two(Math.floor(secs / 3600)) + ':' + two(Math.floor(secs / 60) % 60);
    }

    function tick() {
        var el = document.querySelector('[data-screen-clock]');
        if (el && board()) el.textContent = hm();
    }

    function status(offline) {
        var b = board(), el = document.querySelector('[data-screen-status]');
        if (!b || !el) return;
        if (offline) {
            b.setAttribute('data-offline', '');
            el.textContent = 'Cannot reach the timetable' + (lastGood ? ' since ' + lastGood : '') + ': showing what it last knew';
        } else {
            b.removeAttribute('data-offline');
            lastGood = hm();
        }
    }

    // Spaces paged to fit: a heading never ends a page, so it always sits above its first space.
    function paginate() {
        var list = document.querySelector('[data-screen-rows]');
        var label = document.querySelector('[data-screen-page]');
        if (!list) { pages = 1; return; }
        var items = Array.prototype.slice.call(list.children);
        items.forEach(function (el) { el.style.display = ''; });
        // Heights to the fraction of a pixel, all read before any is hidden: rows grown to fill the board add
        // up to its height exactly, and whole pixels rounded one by one could add up to more.
        var room = list.getBoundingClientRect().height, used = 0, at = 0, groups = [[]];
        var heights = items.map(function (el) { return el.getBoundingClientRect().height; });
        items.forEach(function (el, i) {
            var h = heights[i];
            var heading = el.classList.contains('sb-section');
            var nextH = heading && i + 1 < items.length ? heights[i + 1] : 0;
            if (used > 0 && used + h + nextH > room + 1) { groups.push([]); at++; used = 0; }
            groups[at].push(el);
            used += h;
        });
        pages = groups.length;
        if (page >= pages) page = 0;
        groups.forEach(function (g, i) { g.forEach(function (el) { el.style.display = i === page ? '' : 'none'; }); });
        if (label) label.textContent = pages > 1 ? 'Page ' + (page + 1) + ' of ' + pages : '';
    }

    function nextPage() {
        if (pages <= 1) return;
        page = (page + 1) % pages;
        paginate();
    }

    function refresh() {
        return fetch(location.pathname, { cache: 'no-store', credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.text(); })
            .then(function (html) {
                var doc = new DOMParser().parseFromString(html, 'text/html');
                var next = doc.querySelector('[data-screen-board]');
                var cur = board();
                // Unpaired since, or a new version of the app: start again from the page itself.
                if (!next || !cur || (version && next.getAttribute('data-version') !== version)) { location.reload(); return; }
                cur.replaceWith(document.importNode(next, true));
                readClock();
                tick();
                paginate();
                status(false);
            })
            .catch(function () { status(true); });
    }

    function collect() {
        var p = pairing();
        if (!p) return;
        fetch('screen/collect', { method: 'POST', body: p.getAttribute('data-claim') || '', credentials: 'same-origin', cache: 'no-store' })
            .then(function (r) {
                if (r.status === 200 || r.status === 410) { location.reload(); return; }
                setTimeout(collect, PAIR);
            })
            .catch(function () { setTimeout(collect, PAIR); });
    }

    function start() {
        if (pairing()) {
            setTimeout(collect, PAIR);
            // A code lasts ten minutes; a new page brings a new one.
            setTimeout(function () { location.reload(); }, 9.5 * 60 * 1000);
            return;
        }
        var b = board();
        if (!b) return;
        version = b.getAttribute('data-version');
        readClock();
        tick();
        paginate();
        status(false);
        setInterval(tick, 1000);
        setInterval(function () {
            // Once in a while the whole page, so a long-running screen picks up a new stylesheet too.
            if (Date.now() - started > FULL) { location.reload(); return; }
            refresh();
        }, REFRESH);
        setInterval(nextPage, PAGE);
        window.addEventListener('resize', paginate);
        // The type arrives after the first layout and can change how much fits.
        if (document.fonts && document.fonts.ready) document.fonts.ready.then(paginate);
    }

    // For the walk-throughs: look again now, and turn the page now.
    window.ccccScreen = { refresh: refresh, nextPage: nextPage, pages: function () { return pages; } };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
