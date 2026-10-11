(function () {
    var rows = Array.prototype.slice.call(document.querySelectorAll('.participant-row'));
    var radios = document.querySelectorAll('.player-count-option');
    var more = document.getElementById('morePlayerCount');
    var draw = document.querySelector('.draw-checkbox');
    var date = document.querySelector('.game-date');
    var submit = document.querySelector('form button[type="submit"]');

    function currentCount() {
        var selected = document.querySelector('.player-count-option:checked');
        if (!selected) return 4;
        if (selected.value !== 'more') return parseInt(selected.value, 10);
        var n = parseInt(more.value, 10);
        return Math.min(10, Math.max(5, isNaN(n) ? 5 : n));
    }

    function isSet(input) {
        return !!input.value && input.value !== '0';
    }

    function validate() {
        var visible = rows.slice(0, currentCount());
        var filled = visible.every(function (row) {
            return isSet(row.querySelector('.deck-id')) && isSet(row.querySelector('.player-id'));
        });
        var hasWinner = visible.some(function (row) {
            var winner = row.querySelector('.winner-radio');
            return winner.checked && isSet(winner);
        });
        submit.disabled = !(filled && (hasWinner || draw.checked) && (!date || date.value));
    }

    function update() {
        var count = currentCount();
        rows.forEach(function (row, i) {
            row.classList.toggle('d-none', i >= count);
            if (i >= count) row.querySelector('.winner-radio').checked = false;
        });
        validate();
    }

    function setPlayer(row, id) {
        var playerId = row.querySelector('.player-id');
        playerId.value = id;
        playerId.dispatchEvent(new Event('change', { bubbles: true }));
    }

    rows.forEach(function (row) {
        var deckId = row.querySelector('.deck-id');
        var playerId = row.querySelector('.player-id');
        var winner = row.querySelector('.winner-radio');
        var defaulted = null;

        // The player follows the deck's owner until the user picks a player themselves.
        deckId.addEventListener('change', function () {
            var option = Array.prototype.filter.call(
                deckId.closest('[data-combo]').querySelectorAll('li'),
                function (li) { return li.getAttribute('data-value') === deckId.value; })[0];
            var owner = option && option.getAttribute('data-owner-id');
            if (owner && (!playerId.value || playerId.value === defaulted)) {
                defaulted = owner;
                setPlayer(row, owner);
            }
        });

        playerId.addEventListener('change', function () {
            if (playerId.value !== defaulted) defaulted = null;
            winner.value = playerId.value || '0';
            validate();
        });

        // Radios in one group share a single tab stop, so move to this row's winner explicitly.
        playerId.closest('[data-combo]').querySelector('.combo-filter').addEventListener('keydown', function (event) {
            if (event.key !== 'Tab' || event.shiftKey) return;
            event.preventDefault();
            winner.focus();
        });

        winner.addEventListener('change', function () {
            if (winner.checked) draw.checked = false;
            validate();
        });
    });

    draw.addEventListener('change', function () {
        if (draw.checked) rows.forEach(function (row) { row.querySelector('.winner-radio').checked = false; });
        validate();
    });

    rows.forEach(function (row) {
        row.querySelectorAll('.deck-id').forEach(function (el) { el.addEventListener('change', validate); });
    });
    if (date) date.addEventListener('input', validate);
    Array.prototype.forEach.call(radios, function (r) { r.addEventListener('change', update); });
    more.addEventListener('input', function () {
        document.getElementById('players-more').checked = true;
        update();
    });

    if (draw.checked) rows.forEach(function (row) { row.querySelector('.winner-radio').checked = false; });
    update();
})();
