(function () {
    var dataElement = document.getElementById('deck-network-data');
    var container = document.getElementById('deck-network');
    var slider = document.getElementById('minDeckGames');
    var filter = document.getElementById('network-filter');
    var output = document.querySelector('.min-games-value');
    if (!dataElement || !container || !slider || !filter) return;
    var data = JSON.parse(dataElement.textContent);
    var DEFAULT_DECKS = 20;
    var focusId = null; // deck whose opponents are shown, or null for the whole graph
    var NAMED_OPPONENTS = 5;
    var SVG_NS = 'http://www.w3.org/2000/svg';
    var WIDTH = 960;
    var HEIGHT = 640;
    var MARGIN_LEFT = 40;
    var MARGIN_RIGHT = 300; // room for labels to the right of a node
    var MARGIN_Y = 30;
    var NODE_RADIUS = 8;
    var LABEL_HEIGHT = 28;
    var LABEL_PADDING = 12; // lozenge space after the label text
    var ESTIMATED_CHAR_WIDTH = 7; // used where text can't be measured
    var MAX_EDGE_WIDTH = 12;
    var MAX_WIDTH_PER_GAME = 4;
    var ITERATIONS = 300;

    function svgElement(tag, className) {
        var el = document.createElementNS(SVG_NS, tag);
        if (className) el.setAttribute('class', className);
        return el;
    }

    // Fruchterman-Reingold from a circular start, so the same data always gives the same picture.
    function layout(nodes, edges) {
        var cx = (MARGIN_LEFT + WIDTH - MARGIN_RIGHT) / 2;
        var cy = HEIGHT / 2;
        var radius = Math.min(cx - MARGIN_LEFT, cy - MARGIN_Y);
        var points = {};
        var list = nodes.map(function (node, i) {
            var angle = 2 * Math.PI * i / nodes.length;
            var p = { id: node.deckId, x: cx + radius * Math.cos(angle), y: cy + radius * Math.sin(angle), dx: 0, dy: 0 };
            points[node.deckId] = p;
            return p;
        });
        var k = Math.sqrt((WIDTH - MARGIN_LEFT - MARGIN_RIGHT) * (HEIGHT - 2 * MARGIN_Y) / nodes.length) * 0.6;

        for (var iteration = 0; iteration < ITERATIONS; iteration++) {
            var temperature = WIDTH / 10 * (1 - iteration / ITERATIONS) + 1;
            list.forEach(function (p) { p.dx = 0; p.dy = 0; });
            for (var i = 0; i < list.length; i++) {
                for (var j = i + 1; j < list.length; j++) {
                    var a = list[i], b = list[j];
                    var rx = a.x - b.x, ry = a.y - b.y;
                    var distance = Math.max(Math.hypot(rx, ry), 0.01);
                    var push = k * k / distance;
                    a.dx += rx / distance * push; a.dy += ry / distance * push;
                    b.dx -= rx / distance * push; b.dy -= ry / distance * push;
                }
            }
            edges.forEach(function (edge) {
                var a = points[edge.source], b = points[edge.target];
                var rx = a.x - b.x, ry = a.y - b.y;
                var distance = Math.max(Math.hypot(rx, ry), 0.01);
                var pull = distance * distance / k;
                a.dx -= rx / distance * pull; a.dy -= ry / distance * pull;
                b.dx += rx / distance * pull; b.dy += ry / distance * pull;
            });
            list.forEach(function (p) {
                var move = Math.max(Math.hypot(p.dx, p.dy), 0.01);
                var step = Math.min(move, temperature);
                p.x = Math.min(WIDTH - MARGIN_RIGHT, Math.max(MARGIN_LEFT, p.x + p.dx / move * step));
                p.y = Math.min(HEIGHT - MARGIN_Y, Math.max(MARGIN_Y, p.y + p.dy / move * step));
            });
        }
        // Dense graphs settle into a small clump, so stretch the result to the full drawing area.
        function fit(key, low, high) {
            var values = list.map(function (p) { return p[key]; });
            var min = Math.min.apply(null, values), span = Math.max.apply(null, values) - min;
            list.forEach(function (p) {
                p[key] = span > 1e-6 ? low + (p[key] - min) / span * (high - low) : (low + high) / 2;
            });
        }
        fit('x', MARGIN_LEFT, WIDTH - MARGIN_RIGHT);
        fit('y', MARGIN_Y, HEIGHT - MARGIN_Y);
        list.forEach(function (p) { p.x = Math.round(p.x * 10) / 10; p.y = Math.round(p.y * 10) / 10; });
        return points;
    }

    function gamesText(games) {
        return games + (games === 1 ? ' game' : ' games');
    }

    // Starts at the game count of the 20th most played deck (ties may show a few more).
    function setUpSlider() {
        var counts = data.nodes.map(function (node) { return node.games; }).sort(function (a, b) { return b - a; });
        if (!counts.length) return;
        slider.max = String(counts[0]);
        slider.value = String(counts[Math.min(DEFAULT_DECKS, counts.length) - 1]);
    }

    function deckUrl(deckId) {
        return container.getAttribute('data-deck-url') + '?id=' + deckId;
    }

    function focusBar(label, opponents) {
        var bar = document.createElement('div');
        bar.className = 'network-focus d-flex flex-wrap align-items-center gap-3 mb-2';
        var text = document.createElement('span');
        text.textContent = label + ' and the ' + opponents + (opponents === 1 ? ' deck' : ' decks') + ' it has played against';
        var reset = document.createElement('button');
        reset.type = 'button';
        reset.className = 'btn btn-outline-secondary btn-sm network-reset';
        reset.textContent = 'Show all decks';
        reset.addEventListener('click', function () {
            focusId = null;
            render();
        });
        var matchups = document.createElement('a');
        matchups.className = 'network-matchups';
        matchups.href = deckUrl(focusId);
        matchups.textContent = 'View matchups';
        [text, reset, matchups].forEach(function (el) { bar.appendChild(el); });
        return bar;
    }

    function render() {
        var min = parseInt(slider.value, 10);
        output.textContent = String(min);
        filter.hidden = focusId !== null;
        var nodes, edges;
        if (focusId === null) {
            nodes = data.nodes.filter(function (node) { return node.games >= min; });
            var shown = {};
            nodes.forEach(function (node) { shown[node.deckId] = true; });
            edges = data.edges.filter(function (edge) { return shown[edge.source] && shown[edge.target]; });
        } else {
            edges = data.edges.filter(function (edge) { return edge.source === focusId || edge.target === focusId; });
            var related = {};
            related[focusId] = true;
            edges.forEach(function (edge) { related[edge.source] = true; related[edge.target] = true; });
            nodes = data.nodes.filter(function (node) { return related[node.deckId]; });
        }

        container.textContent = '';
        if (!data.nodes.length) {
            var empty = document.createElement('p');
            empty.className = 'text-muted';
            empty.textContent = 'No games with more than one deck yet.';
            container.appendChild(empty);
            return;
        }

        var points = layout(nodes, edges);
        var labels = {};
        nodes.forEach(function (node) { labels[node.deckId] = node.label; });
        if (focusId !== null) container.appendChild(focusBar(labels[focusId], nodes.length - 1));

        // In the focused view only the most played opponents are named; the rest show their name on hover.
        var named = null;
        if (focusId !== null) {
            named = {};
            named[focusId] = true;
            edges.slice()
                .sort(function (a, b) {
                    return b.games - a.games || labels[a.source + a.target - focusId].localeCompare(labels[b.source + b.target - focusId]);
                })
                .slice(0, NAMED_OPPONENTS)
                .forEach(function (edge) { named[edge.source + edge.target - focusId] = true; });
        }
        var maxGames = edges.reduce(function (max, edge) { return Math.max(max, edge.games); }, 1);
        var perGame = Math.min(MAX_EDGE_WIDTH / maxGames, MAX_WIDTH_PER_GAME);

        var svg = svgElement('svg', 'network-chart');
        svg.setAttribute('viewBox', '0 0 ' + WIDTH + ' ' + HEIGHT);
        svg.setAttribute('role', 'img');
        svg.setAttribute('aria-label', 'Network of decks that have played each other');

        edges.forEach(function (edge) {
            var a = points[edge.source], b = points[edge.target];
            var line = svgElement('line', 'network-edge');
            line.setAttribute('data-source', edge.source);
            line.setAttribute('data-target', edge.target);
            line.setAttribute('x1', a.x); line.setAttribute('y1', a.y);
            line.setAttribute('x2', b.x); line.setAttribute('y2', b.y);
            line.setAttribute('stroke-width', edge.games * perGame);
            var title = svgElement('title');
            title.textContent = labels[edge.source] + ' vs ' + labels[edge.target] + ': ' + gamesText(edge.games);
            line.appendChild(title);
            svg.appendChild(line);
        });

        var lozenges = [];
        // SVG has no z-index: later nodes paint over earlier ones, so decks with visible names go last.
        function depth(node) {
            if (node.deckId === focusId) return 2;
            return named && !named[node.deckId] ? 0 : 1;
        }
        nodes.slice().sort(function (a, b) { return depth(a) - depth(b); }).forEach(function (node) {
            var p = points[node.deckId];
            var link = svgElement('a', node.deckId === focusId ? 'network-node network-focused' : 'network-node');
            link.setAttribute('href', deckUrl(node.deckId));
            link.addEventListener('click', function (event) {
                event.preventDefault();
                focusId = node.deckId;
                render();
            });
            link.addEventListener('mouseenter', function () {
                if (svg.lastChild !== link) svg.appendChild(link);
            });
            var hidden = named && !named[node.deckId] ? 'network-label-hidden' : '';
            // The left cap is centred on the node so the circle sits inside the lozenge's rounded end.
            var background = svgElement('rect', ('network-label-bg ' + hidden).trim());
            background.setAttribute('x', p.x - LABEL_HEIGHT / 2); background.setAttribute('y', p.y - LABEL_HEIGHT / 2);
            background.setAttribute('height', LABEL_HEIGHT);
            background.setAttribute('rx', LABEL_HEIGHT / 2);
            var circle = svgElement('circle');
            circle.setAttribute('cx', p.x); circle.setAttribute('cy', p.y); circle.setAttribute('r', NODE_RADIUS);
            var text = svgElement('text', hidden);
            text.setAttribute('x', p.x + NODE_RADIUS + 4); text.setAttribute('y', p.y + 4);
            text.textContent = node.label;
            link.appendChild(background);
            link.appendChild(circle);
            link.appendChild(text);
            svg.appendChild(link);
            lozenges.push({ background: background, text: text, left: p.x - LABEL_HEIGHT / 2, textX: p.x + NODE_RADIUS + 4 });
        });

        container.appendChild(svg);

        // Text can only be measured once it is in the page.
        lozenges.forEach(function (item) {
            var textWidth = typeof item.text.getComputedTextLength === 'function'
                ? item.text.getComputedTextLength()
                : item.text.textContent.length * ESTIMATED_CHAR_WIDTH;
            item.background.setAttribute('width', item.textX + textWidth + LABEL_PADDING - item.left);
        });
    }

    setUpSlider();
    slider.addEventListener('input', render);
    render();
})();
