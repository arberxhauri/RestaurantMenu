/* /billing right after a card payment: reload a few times until Paddle's webhook has made the
   account active (the page says so meanwhile). Stops after about a minute. */
(function () {
    var el = document.querySelector('[data-await-payment]');
    if (!el) return;
    var url = new URL(window.location.href);
    var tries = parseInt(url.searchParams.get('tries') || '0', 10);
    if (tries >= 6) return;
    setTimeout(function () {
        url.searchParams.set('paid', 'true');
        url.searchParams.set('tries', String(tries + 1));
        window.location.replace(url.toString());
    }, 10000);
})();
