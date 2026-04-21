(function () {
    function setupErrorUi() {
        var el = document.getElementById('blazor-error-ui');
        if (!el) return;
        var dismiss = el.querySelector('.dismiss');
        if (dismiss) {
            dismiss.addEventListener('click', function () {
                el.style.display = 'none';
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupErrorUi);
    } else {
        setupErrorUi();
    }
})();
