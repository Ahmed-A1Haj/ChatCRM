/* Tiny client-side i18n shim. The server injects the active culture's strings into
   window.__i18n__ before this script runs. t(key, arg0, arg1, ...) substitutes {0},
   {1}, etc. and falls back to the key when the translation is missing — matches the
   server-side JsonStringLocalizer behavior so missing strings are visible without
   throwing. Kept tiny on purpose; no dependencies. */
(function (root) {
    function t(key) {
        var dict = root.__i18n__ || {};
        var value = (dict[key] != null) ? dict[key] : key;
        if (arguments.length > 1) {
            for (var i = 1; i < arguments.length; i++) {
                value = value.split('{' + (i - 1) + '}').join(String(arguments[i]));
            }
        }
        return value;
    }
    root.t = t;
    root.i18n = {
        t: t,
        culture: function () { return root.__i18nCulture__ || 'en'; },
        // Helpers that use the active culture for natural date/number formatting.
        formatDate: function (iso, opts) {
            var d = new Date(iso);
            return d.toLocaleDateString(root.__i18nCulture__ || 'en', opts);
        },
        formatTime: function (iso, opts) {
            var d = new Date(iso);
            return d.toLocaleTimeString(root.__i18nCulture__ || 'en', opts || { hour: '2-digit', minute: '2-digit' });
        },
        formatNumber: function (n, opts) {
            return Number(n).toLocaleString(root.__i18nCulture__ || 'en', opts);
        }
    };
})(window);
