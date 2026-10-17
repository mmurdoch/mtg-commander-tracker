(function () {
    var chart = document.querySelector('[data-chart="deck-plays"]');
    if (!chart) return;

    var series = Array.prototype.slice.call(chart.querySelectorAll('g[data-deck]'));

    // SVG has no z-index, so the highlighted line is moved last to be drawn on top.
    // Skipping a no-op reorder avoids re-triggering hover events on the moved node.
    function reorder(deckId) {
        var desired = series.filter(function (el) { return el.getAttribute('data-deck') !== deckId; })
            .concat(series.filter(function (el) { return el.getAttribute('data-deck') === deckId; }));
        var current = Array.prototype.slice.call(chart.querySelectorAll('g[data-deck]'));
        if (desired.every(function (el, i) { return el === current[i]; })) return;
        desired.forEach(function (el) { el.parentNode.appendChild(el); });
    }

    function highlight(deckId) {
        Array.prototype.forEach.call(chart.querySelectorAll('[data-deck]'), function (el) {
            var dim = deckId !== null && el.getAttribute('data-deck') !== deckId;
            el.classList.toggle('line-chart-dimmed', dim);
        });
        reorder(deckId);
    }

    function deckOf(target) {
        var owner = target.closest ? target.closest('[data-deck]') : null;
        return owner ? owner.getAttribute('data-deck') : null;
    }

    chart.addEventListener('mouseover', function (event) { highlight(deckOf(event.target)); });
    chart.addEventListener('focusin', function (event) { highlight(deckOf(event.target)); });
    chart.addEventListener('mouseleave', function () { highlight(null); });
    chart.addEventListener('focusout', function () { highlight(null); });
})();
