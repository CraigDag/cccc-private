// The week calendar's pointer work: choose, box-select, drag to move, drag an edge to stretch. All of
// it happens here and costs ONE round trip at the end of the gesture; a dragover or pointermove handler
// in Blazor would be dozens.
(function () {
    const SNAP = 15;

    function cols(root) { return Array.from(root.querySelectorAll('.wk-col')); }

    function colAt(root, x) {
        const list = cols(root);
        for (const c of list) { const r = c.getBoundingClientRect(); if (x >= r.left && x <= r.right) return c; }
        return null;
    }

    function minuteAt(root, col, y) {
        const start = +root.dataset.start, hour = +root.dataset.hour;
        const r = col.getBoundingClientRect();
        return start + (y - r.top) / hour * 60;
    }

    function snap(m) { return Math.round(m / SNAP) * SNAP; }

    function attach(root, ref) {
        root.__ref = ref;
        if (root.__bound) return;
        root.__bound = true;
        root.addEventListener('pointerdown', e => down(root, e));
        root.addEventListener('dblclick', e => dbl(root, e));
    }

    function down(root, e) {
        if (e.button !== 0) return;
        const ref = root.__ref;
        const block = e.target.closest('.block');
        const editable = root.dataset.edit === '1';
        const hour = +root.dataset.hour;
        if (block) {
            const rect = block.getBoundingClientRect();
            const offY = e.clientY - rect.top;
            const movable = editable && block.dataset.move === '1';
            const mode = !movable ? 'none' : offY < 7 ? 'top' : rect.bottom - e.clientY < 7 ? 'bottom' : 'move';
            const col0 = block.closest('.wk-col');
            const s0 = +block.dataset.start, e0 = +block.dataset.end;
            const top0 = block.style.top, height0 = block.style.height;
            let dragging = false, result = null;
            block.setPointerCapture(e.pointerId);
            const move = ev => {
                const dx = ev.clientX - e.clientX, dy = ev.clientY - e.clientY;
                if (!dragging && Math.hypot(dx, dy) < 5) return;
                if (mode === 'none') return;
                dragging = true;
                block.classList.add('dragging');
                const dm = snap(dy / hour * 60);
                if (mode === 'move') {
                    const col = colAt(root, ev.clientX) || col0;
                    const shiftX = col.getBoundingClientRect().left - col0.getBoundingClientRect().left;
                    let ns = s0 + dm, ne = e0 + dm;
                    if (ns < 0) { ne -= ns; ns = 0; }
                    if (ne > 1440) { ns -= ne - 1440; ne = 1440; }
                    block.style.transform = `translate(${shiftX}px, ${(ns - s0) / 60 * hour}px)`;
                    result = { day: +col.dataset.day, start: ns, end: ne };
                } else if (mode === 'top') {
                    const ns = Math.min(Math.max(0, s0 + dm), e0 - SNAP);
                    block.style.top = (parseFloat(top0) + (ns - s0) / 60 * hour) + 'px';
                    block.style.height = (parseFloat(height0) - (ns - s0) / 60 * hour) + 'px';
                    result = { day: +col0.dataset.day, start: ns, end: e0 };
                } else {
                    const ne = Math.max(Math.min(1440, e0 + dm), s0 + SNAP);
                    block.style.height = (parseFloat(height0) + (ne - e0) / 60 * hour) + 'px';
                    result = { day: +col0.dataset.day, start: s0, end: ne };
                }
                block.dataset.when = label(result.start) + '–' + label(result.end);
            };
            const up = ev => {
                block.removeEventListener('pointermove', move);
                block.removeEventListener('pointerup', up);
                block.removeEventListener('pointercancel', up);
                if (dragging) {
                    block.classList.remove('dragging');
                    block.style.transform = '';
                    block.style.top = top0;
                    block.style.height = height0;
                    delete block.dataset.when;
                    if (ev.type === 'pointerup' && result && (result.start !== s0 || result.end !== e0 || result.day !== +col0.dataset.day))
                        ref.invokeMethodAsync('Move', block.dataset.id, result.day, result.start, result.end);
                } else if (ev.type === 'pointerup') {
                    ref.invokeMethodAsync('Choose', block.dataset.id, ev.shiftKey || ev.ctrlKey || ev.metaKey);
                }
            };
            block.addEventListener('pointermove', move);
            block.addEventListener('pointerup', up);
            block.addEventListener('pointercancel', up);
            return;
        }
        const col = e.target.closest('.wk-col');
        if (!col) return;
        const rootRect = root.getBoundingClientRect();
        const box = document.createElement('div');
        box.className = 'wk-box';
        root.appendChild(box);
        const x0 = e.clientX, y0 = e.clientY;
        let moved = false;
        root.setPointerCapture(e.pointerId);
        const move = ev => {
            if (Math.hypot(ev.clientX - x0, ev.clientY - y0) > 5) moved = true;
            const l = Math.min(x0, ev.clientX), t = Math.min(y0, ev.clientY);
            box.style.left = (l - rootRect.left + root.scrollLeft) + 'px';
            box.style.top = (t - rootRect.top + root.scrollTop) + 'px';
            box.style.width = Math.abs(ev.clientX - x0) + 'px';
            box.style.height = Math.abs(ev.clientY - y0) + 'px';
        };
        const up = ev => {
            root.removeEventListener('pointermove', move);
            root.removeEventListener('pointerup', up);
            root.removeEventListener('pointercancel', up);
            const b = box.getBoundingClientRect();
            box.remove();
            if (ev.type !== 'pointerup') return;
            const add = ev.shiftKey || ev.ctrlKey || ev.metaKey;
            if (!moved) { if (!add) ref.invokeMethodAsync('ChooseNone'); return; }
            const ids = new Set();
            root.querySelectorAll('.block').forEach(el => {
                const r = el.getBoundingClientRect();
                if (r.left < b.right && b.left < r.right && r.top < b.bottom && b.top < r.bottom) ids.add(el.dataset.id);
            });
            ref.invokeMethodAsync('ChooseMany', Array.from(ids), add);
        };
        root.addEventListener('pointermove', move);
        root.addEventListener('pointerup', up);
        root.addEventListener('pointercancel', up);
    }

    function dbl(root, e) {
        const ref = root.__ref;
        const block = e.target.closest('.block');
        if (block) { ref.invokeMethodAsync('Open', block.dataset.id); return; }
        const col = e.target.closest('.wk-col');
        if (!col || root.dataset.edit !== '1') return;
        const m = Math.floor(minuteAt(root, col, e.clientY) / 30) * 30;
        ref.invokeMethodAsync('AddAt', +col.dataset.day, Math.max(0, Math.min(m, 1380)));
    }

    function label(m) {
        const h = Math.floor(m / 60), mm = m % 60;
        return String(h).padStart(2, '0') + ':' + String(mm).padStart(2, '0');
    }

    window.ccccWeek = { attach };
})();
