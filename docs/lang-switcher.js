// PenguinLang docs — EN ⇄ ZH toolbar language switcher.
//
// `make docs-site` builds two mdBook trees from this one config: English at
// the book root (build/book) and Chinese under zh/ (build/book-zh → served at
// /zh/). Both books carry this file at <book-root>/docs/lang-switcher.js, so
// the script's own absolute URL pins the site root no matter how the site is
// served (github.io project subpath, localhost preview, file://). All pages
// exist 1:1 in both books under identical relative paths, so switching
// language is just a /zh/ prefix swap on the current URL.
(function () {
    "use strict";

    function scriptSrc() {
        var scripts = document.getElementsByTagName("script");
        for (var i = scripts.length - 1; i >= 0; i--) {
            var src = scripts[i].getAttribute("src");
            if (src && src.indexOf("lang-switcher") !== -1) return src;
        }
        return null;
    }

    // Returns {en, zh, isZh} — the absolute path prefixes of both books and
    // which one this page belongs to, or null when they cannot be determined.
    function books() {
        var src = scriptSrc();
        if (!src) return null;
        var url;
        try { url = new URL(src, window.location.href); } catch (e) { return null; }
        var path;
        try { path = decodeURIComponent(url.pathname); }
        catch (e) { path = url.pathname; }
        var at = path.indexOf("/docs/lang-switcher.js");
        if (at < 0) return null;
        var root = path.slice(0, at + 1); // this book's root, with trailing "/"
        var isZh = /\/zh\/$/.test(root);
        var en = isZh ? root.replace(/\/zh\/$/, "/") : root;
        var zh = isZh ? root : root + "zh/";
        return { en: en, zh: zh, isZh: isZh };
    }

    function counterpartUrl(b) {
        var here;
        try { here = decodeURIComponent(window.location.pathname); }
        catch (e) { here = window.location.pathname; }
        // Check the zh prefix first: a zh page also matches the en root.
        if (here.indexOf(b.zh) === 0) return b.en + here.slice(b.zh.length);
        if (here.indexOf(b.en) === 0) return b.zh + here.slice(b.en.length);
        return null; // outside both books (e.g. the test-report page)
    }

    function install() {
        var b = books();
        if (!b) return;
        var target = counterpartUrl(b);
        if (!target) return;
        target += window.location.search + window.location.hash;

        var bar = document.querySelector(".right-buttons") ||
                  document.querySelector(".menu-bar");
        var a = document.createElement("a");
        a.href = target;
        a.className = "lang-switcher";
        a.title = b.isZh ? "Switch to English" : "切换到中文";
        a.setAttribute("aria-label", a.title);
        a.textContent = b.isZh ? "English" : "中文";
        if (bar) {
            a.style.cssText =
                "display:inline-flex;align-items:center;align-self:center;" +
                "margin:0 2px;padding:0 8px;height:30px;border-radius:4px;" +
                "font-size:0.85em;line-height:1;text-decoration:none;" +
                "cursor:pointer;color:var(--fg,#333);opacity:0.8;" +
                "border:1px solid var(--icons,#ccc);";
            a.onmouseenter = function () {
                a.style.background = "var(--theme-hover,rgba(0,0,0,0.05))";
                a.style.opacity = "1";
            };
            a.onmouseleave = function () {
                a.style.background = "transparent";
                a.style.opacity = "0.8";
            };
            bar.insertBefore(a, bar.firstChild);
        } else {
            // No toolbar found (unexpected theme) — float a badge top-right.
            a.style.cssText =
                "position:fixed;top:8px;right:12px;z-index:1000;" +
                "padding:4px 10px;border-radius:4px;font-size:0.85em;" +
                "text-decoration:none;cursor:pointer;background:rgba(0,0,0,0.6);" +
                "color:#fff;";
            document.body.appendChild(a);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", install);
    } else {
        install();
    }
})();
