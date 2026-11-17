// Runs before the first paint: puts back the theme this browser chose. Bare :root is Setsquare Dark,
// so a first visit needs nothing.
(function () {
    try {
        var t = localStorage.getItem('cccc.theme');
        var allowed = ['Setsquare Dark', 'Chalkline Dark', 'Instrument Dark', 'Sandtable Dark'];
        if (t && allowed.indexOf(t) >= 0) document.documentElement.setAttribute('data-theme', t);
    } catch (e) { /* storage refused: the default theme stands */ }
})();
