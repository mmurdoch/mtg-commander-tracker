(function () {
    var display = document.getElementById('ColorIdentityDisplay');
    var inputs = [
        { value: document.getElementById('Commander'), list: document.getElementById('CommanderList') },
        { value: document.getElementById('SecondCommander'), list: document.getElementById('SecondCommanderList') }
    ];
    if (!display || !inputs[0].value || !inputs[0].list) return;

    var table = JSON.parse(display.getAttribute('data-colors') || '[]');

    function normalize(letters) {
        var seen = {};
        return letters.toUpperCase().split('').filter(function (letter) {
            if ('WUBRG'.indexOf(letter) === -1 || seen[letter]) return false;
            seen[letter] = true;
            return true;
        }).sort().join('');
    }

    // Returns the color letters for the chosen commander, or null when unknown.
    function colorsOf(input) {
        var chosen = input.value ? input.value.value : '';
        if (!chosen) return '';
        var items = input.list ? Array.prototype.slice.call(input.list.children) : [];
        var match = items.filter(function (item) { return item.getAttribute('data-value') === chosen; })[0];
        return match && match.hasAttribute('data-colors') ? match.getAttribute('data-colors') : null;
    }

    function update() {
        display.value = '';
        if (!inputs[0].value.value) return;
        var letters = '';
        for (var i = 0; i < inputs.length; i++) {
            var colors = colorsOf(inputs[i]);
            if (colors === null) return;
            letters += colors;
        }
        var wanted = normalize(letters);
        var found = table.filter(function (entry) { return normalize(entry.abbreviation) === wanted; })[0];
        if (found) display.value = found.name + ' (' + found.abbreviation + ')';
    }

    inputs.forEach(function (input) {
        if (input.value) input.value.addEventListener('change', update);
    });
})();
