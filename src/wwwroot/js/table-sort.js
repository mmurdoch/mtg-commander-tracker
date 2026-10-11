(function () {
    function cellValue(row, index, type) {
        var cell = row.cells[index];
        var raw = cell.hasAttribute('data-sort-value') ? cell.getAttribute('data-sort-value') : cell.textContent.trim();
        if (type === 'number') {
            return raw === '' ? null : parseFloat(raw);
        }
        return raw.toLowerCase();
    }

    function initTable(table) {
        var headers = Array.prototype.slice.call(table.querySelectorAll('thead th'));
        var body = table.tBodies[0];

        headers.forEach(function (header, index) {
            var button = header.querySelector('button[data-sort-type]');
            if (!button) return;
            var type = button.getAttribute('data-sort-type');

            button.addEventListener('click', function () {
                var direction = header.getAttribute('aria-sort') === 'ascending' ? 'descending' : 'ascending';
                var factor = direction === 'ascending' ? 1 : -1;

                headers.forEach(function (h) { h.setAttribute('aria-sort', 'none'); });
                header.setAttribute('aria-sort', direction);

                var rows = Array.prototype.slice.call(body.rows);
                rows.sort(function (a, b) {
                    var x = cellValue(a, index, type);
                    var y = cellValue(b, index, type);
                    // Missing values stay at the bottom in both directions.
                    if (x === null || y === null) return x === y ? 0 : (x === null ? 1 : -1);
                    return x < y ? -factor : x > y ? factor : 0;
                });
                rows.forEach(function (row) { body.appendChild(row); });
            });
        });
    }

    Array.prototype.forEach.call(document.querySelectorAll('table[data-sortable]'), initTable);
})();
