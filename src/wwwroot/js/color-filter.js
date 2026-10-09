(function () {
    function initCombo(ids) {
    var input = document.getElementById(ids.filter);
    var value = document.getElementById(ids.value);
    var list = document.getElementById(ids.list);
    var toggle = document.getElementById(ids.toggle);
    var combo = document.getElementById(ids.combo);
    if (!input || !value || !list || !combo) return;

    var items = Array.prototype.slice.call(list.children);
    var committedText = value.value ? input.value : '';
    var highlightedIndex = -1;

    // Query matches the name, or (when made only of color letters) any-order subset of the abbreviation.
    function matches(item, query) {
        var q = query.trim().toLowerCase();
        if (!q) return true;
        if ((item.getAttribute('data-name') || '').toLowerCase().indexOf(q) !== -1) return true;
        var abbreviation = (item.getAttribute('data-abbreviation') || '').toLowerCase();
        if (!/^[wubrg]+$/.test(q)) return false;
        return q.split('').every(function (letter) { return abbreviation.indexOf(letter) !== -1; });
    }

    function visibleItems() {
        return items.filter(function (item) { return !item.hidden; });
    }

    function highlight(item) {
        items.forEach(function (candidate) {
            var active = candidate === item;
            candidate.setAttribute('aria-selected', active ? 'true' : 'false');
            candidate.classList.toggle('active', active);
        });
        highlightedIndex = item ? visibleItems().indexOf(item) : -1;
        if (item && item.scrollIntoView) item.scrollIntoView({ block: 'nearest' });
    }

    function isOpen() {
        return !list.hidden;
    }

    function open() {
        list.hidden = false;
        input.setAttribute('aria-expanded', 'true');
    }

    function close() {
        list.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        highlight(null);
    }

    function showAll() {
        items.forEach(function (item) { item.hidden = false; });
        var current = items.filter(function (item) { return item.getAttribute('data-value') === value.value; })[0];
        open();
        highlight(current || null);
    }

    function revert() {
        input.value = committedText;
        close();
    }

    function setValue(newValue) {
        value.value = newValue;
        value.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function commit(item) {
        if (!item) return;
        setValue(item.getAttribute('data-value'));
        committedText = item.textContent.trim();
        input.value = committedText;
        close();
    }

    function highlighted() {
        return highlightedIndex >= 0 ? visibleItems()[highlightedIndex] : null;
    }

    input.addEventListener('input', function () {
        if (!input.value.trim() && value.value) setValue('');
        items.forEach(function (item) { item.hidden = !matches(item, input.value); });
        var visible = visibleItems();
        if (!visible.length) {
            close();
            return;
        }
        open();
        highlight(visible[0]);
    });

    input.addEventListener('click', function () {
        if (!isOpen()) showAll();
    });

    input.addEventListener('keydown', function (event) {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            if (!isOpen()) {
                showAll();
                return;
            }
            var visible = visibleItems();
            var next = highlightedIndex + (event.key === 'ArrowDown' ? 1 : -1);
            if (next >= 0 && next < visible.length) highlight(visible[next]);
        } else if (event.key === 'Enter' && isOpen()) {
            event.preventDefault();
            commit(highlighted());
        } else if (event.key === 'Escape' && isOpen()) {
            event.preventDefault();
            revert();
        }
    });

    if (toggle) {
        toggle.addEventListener('mousedown', function (event) { event.preventDefault(); });
        toggle.addEventListener('click', function () {
            if (isOpen()) {
                close();
            } else {
                input.focus();
                showAll();
            }
        });
    }

    // Keep focus in the text box so the click below runs before focusout.
    list.addEventListener('mousedown', function (event) { event.preventDefault(); });

    list.addEventListener('click', function (event) {
        var target = event.target;
        while (target && target !== list && target.parentNode !== list) target = target.parentNode;
        if (target && target !== list) commit(target);
    });

    combo.addEventListener('focusout', function (event) {
        if (!combo.contains(event.relatedTarget)) revert();
    });
    }

    initCombo({ combo: 'ColorCombo', filter: 'ColorFilter', value: 'ColorDataId', list: 'ColorList', toggle: 'ColorToggle' });
    initCombo({ combo: 'OwnerCombo', filter: 'OwnerFilter', value: 'OwnerId', list: 'OwnerList', toggle: 'OwnerToggle' });
    initCombo({ combo: 'CommanderCombo', filter: 'CommanderFilter', value: 'Commander', list: 'CommanderList', toggle: 'CommanderToggle' });
    initCombo({ combo: 'SecondCommanderCombo', filter: 'SecondCommanderFilter', value: 'SecondCommander', list: 'SecondCommanderList', toggle: 'SecondCommanderToggle' });
})();
