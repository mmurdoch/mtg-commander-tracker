(function () {
    var MAX_DECKS = 5;
    var dataElement = document.getElementById('home-charts-data');
    if (!dataElement) return;
    var data = JSON.parse(dataElement.textContent);

    function percent(entry) {
        return entry.games === 0 ? 0 : entry.wins / entry.games * 100;
    }

    function byWinPercent(a, b) {
        return percent(b) - percent(a) || b.games - a.games || a.label.localeCompare(b.label);
    }

    function element(tag, className, text) {
        var el = document.createElement(tag);
        el.className = className;
        if (text !== undefined) el.textContent = text;
        return el;
    }

    function render(container, entries, emptyMessage) {
        var bars = container.querySelector('.chart-bars');
        bars.textContent = '';
        if (entries.length === 0) {
            bars.appendChild(element('p', 'text-muted small mb-0', emptyMessage));
            return;
        }
        entries.forEach(function (entry) {
            var pct = percent(entry);
            var row = element('div', 'chart-row');
            row.appendChild(element('span', 'chart-label', entry.label));
            var track = element('div', 'chart-track');
            var bar = element('div', 'chart-bar');
            bar.style.width = pct + '%';
            track.appendChild(bar);
            row.appendChild(track);
            row.appendChild(element('span', 'chart-value', Math.round(pct) + '% (' + entry.wins + '/' + entry.games + ')'));
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
