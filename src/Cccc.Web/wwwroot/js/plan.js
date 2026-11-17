// The plan timeline's pointer work: time runs left to right, rows are spaces, groups or people. Click
// to look, double-click to edit, drag sideways to move in time, drag onto another space's row to move
// it there, drag an end to stretch, drag across empty time to draw a new class. All of it happens here
// and costs ONE round trip at the end.
(function () {
    const SNAP = 15;
    const snap = m => Math.round(m / SNAP) * SNAP;
    const label = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');

    function frame(root) { return { start: +root.dataset.start, end: +root.dataset.end }; }

    function minutesPerPx(root, track) {
        const f = frame(root);
        return (f.end - f.start) / track.getBoundingClientRect().width;
    }

    function rowAt(root, y) {
        for (const r of root.querySelectorAll('.tl-row[data-row]')) {
            const b = r.getBoundingClientRect();
            if (y >= b.top && y <= b.bottom) return r;
        }
        return null;
    }

    // On a narrow window the day is wider than the screen: start it scrolled to the part that matters,
    // the class somebody came here to look at, else an hour before now on today, else just before the
    // first class. Once per day shown, so it never fights somebody who has scrolled.
    function scrollToWork(root) {
        if (root.scrollWidth <= root.clientWidth + 2) return;
        const track = root.querySelector('.tl-row[data-row] .tl-track');
        if (!track) return;
        const f = frame(root);
        let target = null;
        const chosen = root.querySelector('.blk.chosen');
        if (chosen) target = +chosen.dataset.start - 30;
        else if (root.dataset.today === '1' && root.querySelector('.tl-now')) target = nowMinutes() - 60;
        else {
            const starts = Array.from(root.querySelectorAll('.blk')).map(b => +b.dataset.start);
            if (starts.length) target = Math.min.apply(null, starts) - 30;
        }
        if (target === null) return;
        root.scrollLeft = Math.max(0, (target - f.start) / (f.end - f.start) * track.getBoundingClientRect().width);
    }

    function attach(root, ref) {
        root.__ref = ref;
        if (root.__day !== root.dataset.day) {
            root.__day = root.dataset.day;
            requestAnimationFrame(() => scrollToWork(root));
        }
        if (root.__bound) return;
        root.__bound = true;
        root.addEventListener('pointerdown', e => down(root, e));
        root.addEventListener('dblclick', e => dbl(root, e));
        root.addEventListener('keydown', e => {
            const blk = e.target.closest && e.target.closest('.blk');
            if (!blk || e.target !== blk) return;
            if (e.key === 'Enter') { e.preventDefault(); root.__ref.invokeMethodAsync('Open', blk.dataset.id); }
            else if (e.key === ' ') { e.preventDefault(); root.__ref.invokeMethodAsync('Choose', blk.dataset.id); }
        });
    }

    // Press on empty time and drag across it to draw a class there; let go and the add form opens with
    // that space (or group, person or day) and those times. A press without a drag is a click, which
    // puts the chosen class down. Mouse and pen only: a finger dragging the timeline is scrolling it.
    function draw(root, e, track, row) {
        const ref = root.__ref;
        const f = frame(root);
        const rect = track.getBoundingClientRect();
        const mpp = (f.end - f.start) / rect.width;
        const at = x => f.start + (x - rect.left) * mpp;
        const m0 = at(e.clientX);
        let ghost = null, a = 0, b = 0;
        track.setPointerCapture(e.pointerId);
        const done = () => {
            track.removeEventListener('pointermove', move);
            track.removeEventListener('pointerup', up);
            track.removeEventListener('pointercancel', done);
            if (ghost) ghost.remove();
        };
        const move = ev => {
            if (!ghost && Math.abs(ev.clientX - e.clientX) < 6) return;
            const m1 = at(ev.clientX);
            a = Math.max(f.start, Math.floor(Math.min(m0, m1) / SNAP) * SNAP);
            b = Math.min(f.end, Math.ceil(Math.max(m0, m1) / SNAP) * SNAP);
            if (b - a < SNAP) b = a + SNAP;
            if (!ghost) {
                // Drawn over the page, not inside the timeline, so the page's own rendering never meets it.
                ghost = document.createElement('div');
                ghost.className = 'draw-ghost';
                document.body.appendChild(ghost);
            }
            const r = track.getBoundingClientRect();
            const px = r.width / (f.end - f.start);
            ghost.style.left = (r.left + (a - f.start) * px) + 'px';
            ghost.style.width = ((b - a) * px) + 'px';
            ghost.style.top = (r.top + 3) + 'px';
            ghost.style.height = Math.min(r.height - 6, 40) + 'px';
            ghost.dataset.when = label(a) + '–' + label(b);
        };
        const up = () => {
            const drew = !!ghost;
            done();
            if (drew) ref.invokeMethodAsync('Draw', row.dataset.row, a, b);
            else ref.invokeMethodAsync('ChooseNone');
        };
        track.addEventListener('pointermove', move);
        track.addEventListener('pointerup', up);
        track.addEventListener('pointercancel', done);
    }

    function down(root, e) {
        if (e.button !== 0) return;
        const ref = root.__ref;
        const blk = e.target.closest('.blk');
        if (!blk) {
            const track = e.target.closest('.tl-track');
            const row = track && track.closest('.tl-row[data-row]');
            if (track && row && root.dataset.edit === '1' && e.pointerType !== 'touch') draw(root, e, track, row);
            else if (track) ref.invokeMethodAsync('ChooseNone');
            return;
        }
        const editable = root.dataset.edit === '1' && blk.dataset.move === '1';
        const rect = blk.getBoundingClientRect();
        const offX = e.clientX - rect.left;
        const mode = !editable ? 'none' : offX < 8 ? 'start' : rect.right - e.clientX < 8 ? 'end' : 'move';
        const track = blk.closest('.tl-track');
        const row0 = blk.closest('.tl-row');
        const mpp = minutesPerPx(root, track);
        const s0 = +blk.dataset.start, e0 = +blk.dataset.end;
        const left0 = blk.style.left, width0 = blk.style.width;
        const f = frame(root);
        let dragging = false, result = null;
        blk.setPointerCapture(e.pointerId);
        const pct = m => ((m - f.start) / (f.end - f.start) * 100) + '%';
        const move = ev => {
            const dx = ev.clientX - e.clientX, dy = ev.clientY - e.clientY;
            if (!dragging && Math.hypot(dx, dy) < 5) return;
            if (mode === 'none') return;
            dragging = true;
            blk.classList.add('dragging');
            const dm = snap(dx * mpp);
            let row = row0;
            if (mode === 'move') {
                let ns = s0 + dm, ne = e0 + dm;
                if (ns < 0) { ne -= ns; ns = 0; }
                if (ne > 1440) { ns -= ne - 1440; ne = 1440; }
                let shiftY = 0;
                if (root.dataset.rooms === '1') {
                    const r = rowAt(root, ev.clientY);
                    if (r) { row = r; shiftY = r.getBoundingClientRect().top - row0.getBoundingClientRect().top; }
                }
                blk.style.left = pct(ns);
                blk.style.transform = `translateY(${shiftY}px)`;
                root.querySelectorAll('.tl-row.drop').forEach(x => x.classList.remove('drop'));
                if (row !== row0) row.classList.add('drop');
                result = { row: row.dataset.row, start: ns, end: ne };
            } else if (mode === 'start') {
                const ns = Math.min(Math.max(0, s0 + dm), e0 - SNAP);
                blk.style.left = pct(ns); blk.style.width = ((e0 - ns) / (f.end - f.start) * 100) + '%';
                result = { row: row0.dataset.row, start: ns, end: e0 };
            } else {
                const ne = Math.max(Math.min(1440, e0 + dm), s0 + SNAP);
                blk.style.width = ((ne - s0) / (f.end - f.start) * 100) + '%';
                result = { row: row0.dataset.row, start: s0, end: ne };
            }
            blk.dataset.when = label(result.start) + '–' + label(result.end);
        };
        const up = ev => {
            blk.removeEventListener('pointermove', move);
            blk.removeEventListener('pointerup', up);
            blk.removeEventListener('pointercancel', up);
            root.querySelectorAll('.tl-row.drop').forEach(x => x.classList.remove('drop'));
            if (dragging) {
                blk.classList.remove('dragging');
                blk.style.transform = ''; blk.style.left = left0; blk.style.width = width0;
                delete blk.dataset.when;
                if (ev.type === 'pointerup' && result && (result.start !== s0 || result.end !== e0 || result.row !== row0.dataset.row))
                    ref.invokeMethodAsync('Move', blk.dataset.id, result.row, result.start, result.end);
            } else if (ev.type === 'pointerup') {
                ref.invokeMethodAsync('Choose', blk.dataset.id);
            }
        };
        blk.addEventListener('pointermove', move);
        blk.addEventListener('pointerup', up);
        blk.addEventListener('pointercancel', up);
    }

    function dbl(root, e) {
        const ref = root.__ref;
        const blk = e.target.closest('.blk');
        if (blk) { ref.invokeMethodAsync('Open', blk.dataset.id); return; }
        const track = e.target.closest('.tl-track');
        const row = e.target.closest('.tl-row[data-row]');
        if (!track || !row || root.dataset.edit !== '1') return;
        const f = frame(root);
        const m = f.start + (e.clientX - track.getBoundingClientRect().left) * minutesPerPx(root, track);
        ref.invokeMethodAsync('AddAt', row.dataset.row, Math.max(0, Math.min(Math.floor(m / 30) * 30, 1380)));
    }

    // The line at "now" walks across the day by itself, once a minute, in the school's own time.
    const schoolTime = new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Copenhagen', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
    function nowMinutes() {
        const [h, m] = schoolTime.format(new Date()).split(':').map(Number);
        return h * 60 + m;
    }
    setInterval(() => {
        const now = nowMinutes();
        document.querySelectorAll('.tl[data-today="1"]').forEach(root => {
            const f = frame(root);
            root.querySelectorAll('.tl-now').forEach(line => {
                line.hidden = now < f.start || now > f.end;
                line.style.left = ((now - f.start) / (f.end - f.start) * 100) + '%';
            });
        });
        document.querySelectorAll('[data-now-clock]').forEach(el => { el.textContent = label(now); });
    }, 30000);

    window.ccccPlan = { attach };
})();
