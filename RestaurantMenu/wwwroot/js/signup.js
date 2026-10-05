/* Signup: shows the menu link the restaurant name will get ("oliva-durres", or "oliva-2" if
   taken) while typing. The server does the same after a submit, so this is only a nicety. */
(function () {
    var input = document.querySelector('[data-link-check]');
    var hint = document.querySelector('[data-link-hint]');
    if (!input || !hint) return;
    var timer = null, seq = 0;

    function check() {
        var name = input.value.trim();
        var mine = ++seq;
        if (name.length < 2) { hint.textContent = ''; return; }
        fetch(input.dataset.linkCheck + '?name=' + encodeURIComponent(name), { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (res) {
                if (mine !== seq || !res || !res.ok) return;
                hint.textContent = (res.taken ? hint.dataset.taken : hint.dataset.hint).replace('{0}', res.url);
            })
            .catch(function () { /* offline: the server shows it after submit */ });
    }

    input.addEventListener('input', function () {
        clearTimeout(timer);
        timer = setTimeout(check, 350);
    });
})();
