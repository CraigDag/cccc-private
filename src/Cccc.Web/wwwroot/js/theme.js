// Runs before the first paint: puts back the look this browser chose. With no choice the device decides
// (Paper in light, Night in dark), which the stylesheet does by itself.
(function () {
    try {
        var t = localStorage.getItem('cccc.theme');
        if (t === 'Paper' || t === 'Night') document.documentElement.setAttribute('data-theme', t);
    } catch (e) { /* storage refused: the device's own preference stands */ }
})();
