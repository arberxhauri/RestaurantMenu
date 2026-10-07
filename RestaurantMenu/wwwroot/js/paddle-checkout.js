/* /billing/pay: starts Paddle.js with the client token. Paddle opens its checkout for the
   transaction in ?_ptxn= by itself; when the payment completes we go back to /billing, where the
   account turns active as soon as Paddle's webhook arrives. */
(function () {
    var el = document.querySelector('[data-paddle-checkout]');
    if (!el || !window.Paddle) return;
    if (el.dataset.sandbox === 'true') window.Paddle.Environment.set('sandbox');
    window.Paddle.Initialize({
        token: el.dataset.token,
        eventCallback: function (e) {
            if (e && e.name === 'checkout.completed') {
                setTimeout(function () { window.location.href = el.dataset.done; }, 1500);
            }
        }
    });
})();
