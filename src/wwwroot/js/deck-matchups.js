(function () {
    var dataElement = document.getElementById('deck-matchups-data');
    var chart = document.getElementById('matchup-chart');
    var slider = document.getElementById('minGames');
    if (!dataElement || !chart || !slider) return;
    var data = JSON.parse(dataElement.textContent);
    var output = document.querySelector('.min-games-value');
    var IMAGE_URL = 'https://api.scryfall.com/cards/named?format=image&version=normal&exact=';
    var players = 'all';

    // Swaps in the counts for the chosen game size.
    function forPlayers(matchup) {
        if (players === 'all') return matchup;
        var record = (matchup.bySize || {})[players] || { games: 0, wins: 0, losses: 0 };
        return Object.assign({}, matchup, { games: record.games, wins: record.wins, losses: record.losses });
    }

    function element(tag, className, text) {
        var el = document.createElement(tag);
        if (className) el.className = className;
        if (text !== undefined) el.textContent = text;
        return el;
    }

    function pick(matchups, share) {
        var best = null;
        matchups.forEach(function (m) {
            if (share(m) <= 0) return;
            if (!best ||
                share(m) / m.games > share(best) / best.games ||
                (share(m) / m.games === share(best) / best.games && m.games > best.games) ||
                (share(m) / m.games === share(best) / best.games && m.games === best.games && m.label.localeCompare(best.label) < 0)) {
                best = m;
            }
        });
        return best;
    }

    function deckColumn(deck, linked) {
        var column = element(linked ? 'a' : 'div', 'matchup-deck');
        if (linked) column.href = chart.getAttribute('data-deck-url') + '?id=' + deck.deckId;
        [deck.commander, deck.secondCommander].forEach(function (name) {
            if (!name) return;
            var image = element('img', 'matchup-card');
            image.src = IMAGE_URL + encodeURIComponent(name);
            image.alt = name;
            image.loading = 'lazy';
            column.appendChild(image);
        });
        column.appendChild(element('div', 'matchup-deck-label', deck.label));
        return column;
    }

    function percent(count, games) {
        return count / games * 100;
    }

    // The right-hand deck's wins are the top segment, so the bar always reads as "right over left".
    function linkColumn(heading, matchup, selectedIsRight) {
        var selectedPct = percent(matchup.wins, matchup.games);
        var opponentPct = percent(matchup.losses, matchup.games);
        var otherPct = Math.max(0, 100 - selectedPct - opponentPct);
        var top = selectedIsRight ? { cls: 'matchup-selected', pct: selectedPct } : { cls: 'matchup-opponent', pct: opponentPct };
        var bottom = selectedIsRight ? { cls: 'matchup-opponent', pct: opponentPct } : { cls: 'matchup-selected', pct: selectedPct };

        var column = element('div', 'matchup-link');
        var above = element('div', 'matchup-above');
        above.appendChild(element('div', 'matchup-heading', heading));
        above.appendChild(element('div', 'matchup-games', matchup.games + ' games'));
        column.appendChild(above);
        var bar = element('div', 'matchup-bar');
        [top, { cls: 'matchup-other', pct: otherPct }, bottom].forEach(function (segment) {
            var el = element('div', segment.cls);
            el.style.height = segment.pct + '%';
            bar.appendChild(el);
        });
        column.appendChild(bar);
        var text = element('div', 'matchup-percent');
        [top, bottom].forEach(function (segment) {
            text.appendChild(element('span', segment.cls, Math.round(segment.pct) + '%'));
        });
        var below = element('div', 'matchup-below');
        below.appendChild(text);
        column.appendChild(below);
        return column;
    }

    function render() {
        var min = parseInt(slider.value, 10);
        output.textContent = String(min);
        var eligible = data.matchups
            .map(forPlayers)
            .filter(function (m) { return m.games > 0 && m.games >= min; });
        var best = pick(eligible, function (m) { return m.wins; });
        var worst = pick(eligible, function (m) { return m.losses; });

        chart.textContent = '';
        var row = element('div', 'matchup-row');
        if (best) {
            row.appendChild(deckColumn(best, true));
            row.appendChild(linkColumn('Best performance against', best, true));
        }
        row.appendChild(deckColumn(data.selected));
        if (worst) {
            row.appendChild(linkColumn('Worst performance against', worst, false));
            row.appendChild(deckColumn(worst, true));
        }
        chart.appendChild(row);
        if (!best && !worst) {
            chart.appendChild(element('p', 'text-muted', 'No decks with at least ' + min + ' games.'));
        }
    }

    slider.addEventListener('input', render);

    var buttons = document.querySelectorAll('[data-players]');
    Array.prototype.forEach.call(buttons, function (button) {
        button.addEventListener('click', function () {
            players = button.getAttribute('data-players');
            Array.prototype.forEach.call(buttons, function (other) {
                other.setAttribute('aria-pressed', String(other === button));
            });
            render();
        });
    });

    render();
})();
