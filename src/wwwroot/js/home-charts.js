(function () {
    var MAX_DECKS = 5;
    var dataElement = document.getElementById('home-charts-data');
    if (!dataElement) return;
    var data = JSON.parse(dataElement.textContent);

    function percent(entry) {
        return entry.games === 0 ? 0 : entry.wins / entry.games * 100;
    }

    function byWinPercent(a, b) {
        return Math.round(percent(b)) - Math.round(percent(a)) || b.games - a.games || percent(b) - percent(a) || a.label.localeCompare(b.label);
    }

    function element(tag, className, text) {
        var el = document.createElement(tag);
        el.className = className;
        if (text !== undefined) el.textContent = text;
        return el;
    }

    function render(container, entries, emptyMessage) {
        var bars = container.querySelector('.chart-bars');
        var players = parseInt(container.getAttribute('data-players'), 10);
        bars.textContent = '';
        if (entries.length === 0) {
            bars.appendChild(element('p', 'text-muted small mb-0', emptyMessage));
            return;
        }
        var expected = element('div', 'chart-expected');
        expected.style.left = (100 / players) + '%';
        expected.style.gridRow = '1 / span ' + entries.length;
        bars.appendChild(expected);
        entries.forEach(function (entry, index) {
            var pct = percent(entry);
            var row = element('div', 'chart-row');
            var label = element('span', 'chart-label', entry.label);
            label.title = entry.label;
            row.appendChild(label);
            row.appendChild(element('div', 'chart-track'));
            var above = entry.wins * players > entry.games;
            var bar = element('div', 'chart-bar ' + (above ? 'chart-bar-above' : 'chart-bar-below'));
            bar.style.width = pct + '%';
            row.appendChild(bar);
            row.appendChild(element('span', 'chart-value', Math.round(pct) + '% (' + entry.wins + '/' + entry.games + ')'));
            // Explicit rows let the track and bar overlap in the same grid cell.
            Array.prototype.forEach.call(row.children, function (item) { item.style.gridRow = String(index + 1); });
            bars.appendChild(row);
        });
    }

    Array.prototype.forEach.call(document.querySelectorAll('[data-chart="people"]'), function (container) {
        var entries = (data.people[container.getAttribute('data-players')] || []).slice().sort(byWinPercent);
        render(container, entries, 'No games yet.');
    });

    Array.prototype.forEach.call(document.querySelectorAll('[data-chart="decks"]'), function (container) {
        var all = data.decks[container.getAttribute('data-players')] || [];
        var slider = container.querySelector('input[type="range"]');
        var output = container.querySelector('.min-games-value');

        function update() {
            var min = parseInt(slider.value, 10);
            output.textContent = String(min);
            var entries = all
                .filter(function (entry) { return entry.games >= min && entry.games > 0; })
                .sort(byWinPercent)
                .slice(0, MAX_DECKS);
            render(container, entries, 'No decks with at least ' + min + ' games.');
        }

        slider.addEventListener('input', update);
        update();
    });
})();
