// gthx.net: loads data from the read-only API and renders it.
// Factoids are typed by anyone on IRC, so everything from the API goes into the page as text
// (textContent), never as HTML.
(function () {
  "use strict";

  var api = (window.GTHX_API || "/api").replace(/\/$/, "");

  // The API scales to zero, so the first request after a quiet spell can take a few seconds.
  function getJson(path, statusElement) {
    var slow = statusElement && setTimeout(function () {
      statusElement.textContent = "-- waking gthx up, this can take a few seconds --";
    }, 2000);
    return fetch(api + path, { headers: { Accept: "application/json" } })
      .then(function (response) {
        if (!response.ok) {
          return response.json().catch(function () { return {}; }).then(function (body) {
            throw new Error(body.error || "The API answered " + response.status + ".");
          });
        }
        return response.json();
      })
      .finally(function () { if (slow) clearTimeout(slow); });
  }

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (name) {
      if (name === "text") node.textContent = attrs.text;
      else node.setAttribute(name, attrs[name]);
    });
    (children || []).forEach(function (child) { if (child) node.appendChild(child); });
    return node;
  }

  function searchLink(item) {
    return "search.html?q=" + encodeURIComponent(item);
  }

  function showError(statusElement, error) {
    statusElement.textContent = "-- " + (error.message || "Something went wrong.") + " --";
    statusElement.classList.add("error");
    statusElement.hidden = false;
  }

  var numberFormat = new Intl.NumberFormat("en-US");

  function renderTop(list, factoids) {
    list.replaceChildren.apply(list, factoids.map(function (f) {
      return el("li", {}, [
        el("span", { "class": "rank", text: f.rank + "." }),
        el("a", { href: searchLink(f.item), text: f.item }),
        el("span", { "class": "count", text: numberFormat.format(f.count) })
      ]);
    }));
  }

  function loadTop(count) {
    var list = document.getElementById("top-list");
    var status = document.getElementById("top-status");
    if (!list) return;
    getJson("/factoids/top?count=" + count, status)
      .then(function (body) {
        renderTop(list, body.factoids);
        status.hidden = true;
      })
      .catch(function (error) { showError(status, error); });
  }

  function loadStats() {
    var target = document.getElementById("stat-factoids");
    if (!target) return;
    getJson("/stats")
      .then(function (body) { target.textContent = numberFormat.format(body.factoidCount); })
      .catch(function () { target.textContent = "?"; });
  }

  function renderResult(f) {
    var date = f.timestamp ? new Date(f.timestamp) : null;
    return el("article", { "class": "result" }, [
      el("time", date ? { datetime: f.timestamp, text: date.toISOString().slice(0, 10) } : { text: "" }),
      el("div", { "class": "result-body" }, [
        el("h3", {}, [el("a", { href: searchLink(f.item), text: f.item })]),
        el("span", { "class": "verb", text: f.isAre ? " are " : " is " }),
        el("span", { text: f.value }),
        el("p", { "class": "by", text: "— told by " + f.user })
      ])
    ]);
  }

  function runSearch() {
    var results = document.getElementById("results");
    if (!results) return;
    var heading = document.getElementById("results-heading");
    var status = document.getElementById("results-status");
    var input = document.getElementById("q");
    var query = (new URLSearchParams(location.search).get("q") || "").trim();
    input.value = query;

    if (!query) {
      heading.textContent = "-- type something to search for --";
      status.hidden = true;
      return;
    }

    document.title = query + " · gthx";
    heading.textContent = "-- searching for \"" + query + "\" --";
    getJson("/factoids?q=" + encodeURIComponent(query), status)
      .then(function (body) {
        var n = body.results.length;
        var more = n === body.limit ? "+" : "";
        heading.textContent = "-- " + n + more + (n === 1 ? " match" : " matches") + " for \"" + body.query + "\" --";
        results.replaceChildren.apply(results, body.results.map(renderResult));
        status.hidden = true;
      })
      .catch(function (error) {
        heading.textContent = "-- search failed --";
        showError(status, error);
      });
  }

  document.addEventListener("DOMContentLoaded", function () {
    var page = document.body.getAttribute("data-page");
    if (page === "home") {
      loadStats();
      loadTop(10);
    } else if (page === "search") {
      runSearch();
      loadTop(8);
    }
  });
})();
