(function () {
    var modalElement = document.getElementById('newDeckModal');
    var form = document.getElementById('newDeckForm');
    if (!modalElement || !form || !window.comboBox) return;

    var submit = document.getElementById('newDeckSubmit');
    var general = document.getElementById('newDeckError');
    var nameInput = document.getElementById('NewDeckName');
    var combos = Array.prototype.slice.call(form.querySelectorAll('[data-modal-combo]'));
    var originRow = null;
    var commandersLoading = null;

    combos.forEach(function (combo) {
        window.comboBox.init(
            combo,
            combo.querySelector('.combo-filter'),
            combo.querySelector('.combo-value'),
            combo.querySelector('.combo-list'),
            combo.querySelector('.combo-toggle'));
    });

    function modal() {
        return window.bootstrap.Modal.getOrCreateInstance(modalElement);
    }

    function comboValue(name) {
        return form.querySelector('[name="' + name + '"]');
    }

    function setComboValue(name, value) {
        var input = comboValue(name);
        input.value = value;
        input.dispatchEvent(new Event('combo:refresh'));
    }

    function showGeneralError(message) {
        general.textContent = message;
        general.hidden = false;
    }

    function clearErrors() {
        general.textContent = '';
        general.hidden = true;
        Array.prototype.forEach.call(form.querySelectorAll('[data-error-for]'), function (span) {
            span.textContent = '';
        });
    }

    function reset() {
        clearErrors();
        nameInput.value = '';
        ['commander', 'secondCommander', 'ownerId'].forEach(function (name) { setComboValue(name, ''); });
    }

    function fillCommanderLists(commanders) {
        Array.prototype.forEach.call(form.querySelectorAll('.commander-list'), function (list) {
            list.textContent = '';
            commanders.forEach(function (commander) {
                var li = document.createElement('li');
                li.className = 'list-group-item list-group-item-action';
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', 'false');
                li.setAttribute('data-value', commander.name);
                li.setAttribute('data-name', commander.name);
                if (commander.colors !== null && commander.colors !== undefined) li.setAttribute('data-colors', commander.colors);
                li.textContent = commander.name;
                list.appendChild(li);
            });
        });
    }

    // The commander catalog is large, so it is only fetched once the modal is first needed.
    function loadCommanders() {
        if (commandersLoading) return commandersLoading;
        commandersLoading = fetch(modalElement.getAttribute('data-commanders-url'))
            .then(function (response) {
                if (!response.ok) throw new Error('status ' + response.status);
                return response.json();
            })
            .then(fillCommanderLists)
            .catch(function () {
                commandersLoading = null;
                showGeneralError('Could not load the commander list. Close and reopen this dialog to try again.');
            });
        return commandersLoading;
    }

    function deckInputs() {
        return Array.prototype.slice.call(document.querySelectorAll('.participant-row .deck-id'));
    }

    function rebuildDeckLists(decks) {
        deckInputs().forEach(function (input) {
            var list = input.closest('[data-combo]').querySelector('.combo-list');
            list.textContent = '';
            decks.forEach(function (deck) {
                var li = document.createElement('li');
                li.className = 'list-group-item list-group-item-action';
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', 'false');
                li.setAttribute('data-value', String(deck.id));
                li.setAttribute('data-owner-id', String(deck.ownerId));
                li.setAttribute('data-name', deck.label);
                li.textContent = deck.label;
                list.appendChild(li);
            });
            if (input.value) input.dispatchEvent(new Event('combo:refresh'));
        });
    }

    function applyCreated(result) {
        rebuildDeckLists(result.decks);
        var deckId = originRow.querySelector('.deck-id');
        deckId.value = String(result.deckId);
        deckId.dispatchEvent(new Event('change', { bubbles: true }));
        modal().hide();
    }

    function showErrors(errors) {
        var unplaced = [];
        Object.keys(errors).forEach(function (key) {
            var span = form.querySelector('[data-error-for="' + key + '"]');
            if (span) span.textContent = errors[key];
            else unplaced.push(errors[key]);
        });
        if (unplaced.length) showGeneralError(unplaced.join(' '));
    }

    document.addEventListener('click', function (event) {
        var button = event.target.closest ? event.target.closest('.new-deck-btn') : null;
        if (!button) return;
        originRow = button.closest('.participant-row');
        reset();
        var player = originRow.querySelector('.player-id').value;
        if (player && player !== '0') setComboValue('ownerId', player);
        modal().show();
        loadCommanders();
    });

    modalElement.addEventListener('shown.bs.modal', function () {
        var filter = combos[0].querySelector('.combo-filter');
        if (filter) filter.focus();
    });

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        clearErrors();
        submit.disabled = true;
        fetch(form.getAttribute('action'), { method: 'POST', body: new FormData(form) })
            .then(function (response) {
                if (response.ok) return response.json().then(applyCreated);
                if (response.status === 400) return response.json().then(function (body) { showErrors(body.errors || {}); });
                throw new Error('status ' + response.status);
            })
            .catch(function () {
                showGeneralError('Could not add the deck. Please try again.');
            })
            .then(function () {
                submit.disabled = false;
            });
    });
})();
