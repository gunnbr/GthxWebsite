// gthx.net admin panel. Signs in with Microsoft Entra and calls the API's /manage endpoints with the token.
//
// Sign-in is the OAuth authorization code flow with PKCE, done by hand so the site still has no build step or
// third-party script: send the browser to Entra, which comes back here with a code in the fragment, then
// trade the code for tokens. Tokens live in sessionStorage, so they last only as long as this tab.
// App Service Authentication on the function app checks the token, and the API checks the account.
//
// Factoids are typed by anyone on IRC, so everything from the API goes into the page as text (textContent),
// never as HTML.
(function () {
  "use strict";

  var api = (window.GTHX_API || "/api").replace(/\/$/, "");
  var config = window.GTHX_ADMIN || {};
  var login = "https://login.microsoftonline.com/" + encodeURIComponent(config.tenantId || "") + "/oauth2/v2.0/";
  var scope = "api://" + config.clientId + "/Factoids.Manage openid profile offline_access";
  var redirectUri = location.origin + location.pathname;
  var maxItemLength = 30;

  var store = {
    get: function (key) { try { return sessionStorage.getItem("gthx-admin:" + key); } catch (e) { return null; } },
    set: function (key, value) { try { sessionStorage.setItem("gthx-admin:" + key, value); } catch (e) { /* signs in again */ } },
    clear: function () {
      try {
        Object.keys(sessionStorage)
          .filter(function (k) { return k.indexOf("gthx-admin:") === 0; })
          .forEach(function (k) { sessionStorage.removeItem(k); });
      } catch (e) { /* nothing stored */ }
    }
  };

  // --- Sign-in -------------------------------------------------------------------------------------------

  function base64Url(bytes) {
    return btoa(String.fromCharCode.apply(null, new Uint8Array(bytes)))
      .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }

  function randomString() {
    return base64Url(crypto.getRandomValues(new Uint8Array(32)));
  }

  function signIn() {
    var verifier = randomString();
    var state = randomString();
    store.set("verifier", verifier);
    store.set("state", state);
    return crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier)).then(function (hash) {
      location.assign(login + "authorize?" + new URLSearchParams({
        client_id: config.clientId,
        response_type: "code",
        response_mode: "fragment",
        redirect_uri: redirectUri,
        scope: scope,
        state: state,
        // Lets someone signed in with the wrong account pick another.
        prompt: "select_account",
        code_challenge: base64Url(hash),
        code_challenge_method: "S256"
      }));
    });
  }

  function requestTokens(params) {
    params.client_id = config.clientId;
    params.scope = scope;
    return fetch(login + "token", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams(params)
    }).then(function (response) {
      return response.json().then(function (body) {
        if (!response.ok) throw new Error(body.error_description ? body.error_description.split("\n")[0] : "Sign-in failed.");
        store.set("token", body.access_token);
        // A minute early, so a token never runs out on its way to the API.
        store.set("expires", String(Date.now() + (body.expires_in - 60) * 1000));
        if (body.refresh_token) store.set("refresh", body.refresh_token);
        return body.access_token;
      });
    });
  }

  // Finishes a sign-in when Entra has just sent the browser back here. Resolves to whether it did.
  function finishSignIn() {
    var params = new URLSearchParams(location.hash.slice(1));
    if (!params.has("code") && !params.has("error")) return Promise.resolve(false);
    history.replaceState(null, "", redirectUri + location.search);

    var expected = store.get("state");
    var verifier = store.get("verifier");
    store.set("state", "");
    store.set("verifier", "");
    if (params.has("error")) {
      return Promise.reject(new Error((params.get("error_description") || params.get("error")).split("\n")[0]));
    }
    if (!expected || params.get("state") !== expected || !verifier) {
      return Promise.reject(new Error("That sign-in didn't start here. Try again."));
    }
    return requestTokens({
      grant_type: "authorization_code",
      code: params.get("code"),
      redirect_uri: redirectUri,
      code_verifier: verifier
    }).then(function () { return true; });
  }

  // A current access token, refreshing it if it has run out; null when the user needs to sign in.
  function accessToken() {
    var token = store.get("token");
    if (token && Number(store.get("expires")) > Date.now()) return Promise.resolve(token);
    var refresh = store.get("refresh");
    if (!refresh) return Promise.resolve(null);
    return requestTokens({ grant_type: "refresh_token", refresh_token: refresh })
      .catch(function () { store.clear(); return null; });
  }

  // --- API -----------------------------------------------------------------------------------------------

  function NeedsSignIn(message) { this.message = message; }

  function call(method, path, body) {
    var headers = { Accept: "application/json" };
    var ready = config.localObjectId
      // Running against `func start`, which has no App Service Authentication: say who we are directly.
      // The deployed API ignores these headers unless they came from a checked token.
      ? Promise.resolve(Object.assign(headers, {
        "X-MS-CLIENT-PRINCIPAL-ID": config.localObjectId,
        "X-MS-CLIENT-PRINCIPAL-NAME": "local@localhost"
      }))
      : accessToken().then(function (token) {
        if (!token) throw new NeedsSignIn("Sign in to continue.");
        headers.Authorization = "Bearer " + token;
        return headers;
      });

    return ready.then(function () {
      if (body !== undefined) headers["Content-Type"] = "application/json";
      return fetch(api + path, { method: method, headers: headers, body: body === undefined ? undefined : JSON.stringify(body) });
    }).then(function (response) {
      return response.json().catch(function () { return {}; }).then(function (result) {
        if (response.status === 401) { store.clear(); throw new NeedsSignIn(result.error || "Sign in again."); }
        if (!response.ok) throw new Error(result.error || "The API answered " + response.status + ".");
        return result;
      });
    });
  }

  // --- Page ----------------------------------------------------------------------------------------------

  function $(id) { return document.getElementById(id); }

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (name) {
      if (name === "text") node.textContent = attrs.text;
      else node.setAttribute(name, attrs[name]);
    });
    (children || []).forEach(function (child) { if (child) node.appendChild(child); });
    return node;
  }

  function setStatus(node, text, isError) {
    node.textContent = text ? "-- " + text + " --" : "";
    node.classList.toggle("error", !!isError);
    node.hidden = !text;
  }

  function showSignedOut(message) {
    setStatus($("account-status"), message || "not signed in");
    $("sign-in").hidden = !!config.localObjectId || !config.clientId;
    $("sign-out").hidden = true;
    $("admin-panels").hidden = true;
  }

  // Reports a failed action, or goes back to the sign-in button if the token is gone.
  function failed(statusNode) {
    return function (error) {
      if (error instanceof NeedsSignIn) showSignedOut(error.message);
      else setStatus(statusNode, error.message || "Something went wrong.", true);
    };
  }

  function busy(button, promise) {
    button.disabled = true;
    return promise.finally(function () { button.disabled = false; });
  }

  // One factoid, with its controls. onChange is called after anything changes it, to redraw the list.
  function renderFactoid(f, onChange, selectable) {
    var date = new Date(f.timestamp).toISOString().slice(0, 10);
    var value = el("span", { "class": "admin-value", text: f.value });
    var editor = el("textarea", { "class": "admin-editor", maxlength: "512", rows: "3", "aria-label": "New value for " + f.item, hidden: "" });
    var status = el("p", { "class": "status", hidden: "" });

    var edit = el("button", { type: "button", "class": "small-button", text: "edit" });
    var save = el("button", { type: "button", "class": "small-button", text: "save", hidden: "" });
    var cancel = el("button", { type: "button", "class": "small-button", text: "cancel", hidden: "" });
    var lock = el("button", { type: "button", "class": "small-button", text: f.isLocked ? "unlock item" : "lock item" });
    var remove = el("button", { type: "button", "class": "small-button danger", text: "delete" });

    function editing(on) {
      editor.hidden = save.hidden = cancel.hidden = !on;
      value.hidden = edit.hidden = on;
      if (on) { editor.value = f.value; editor.focus(); }
    }
    edit.addEventListener("click", function () { editing(true); });
    cancel.addEventListener("click", function () { editing(false); });
    save.addEventListener("click", function () {
      busy(save, call("PUT", "/manage/factoids/" + f.id, { value: editor.value })
        .then(function (updated) { Object.assign(f, updated); onChange(); }))
        .catch(failed(status));
    });
    lock.addEventListener("click", function () {
      busy(lock, call("POST", "/manage/items/lock", { item: f.item, isLocked: !f.isLocked })
        .then(function (result) { onChange({ item: f.item, isLocked: result.isLocked }); }))
        .catch(failed(status));
    });
    remove.addEventListener("click", function () {
      if (!confirm("Delete \"" + f.item + "\" " + (f.isAre ? "are" : "is") + " \"" + f.value + "\"?")) return;
      busy(remove, call("POST", "/manage/factoids/delete", { ids: [f.id] })
        .then(function () { onChange({ deleted: [f.id] }); }))
        .catch(failed(status));
    });

    var check = selectable && el("input", { type: "checkbox", "class": "admin-select", value: String(f.id), "aria-label": "Select " + f.item });
    return el("article", { "class": "result admin-factoid" }, [
      check,
      el("time", { datetime: f.timestamp, text: date }),
      el("div", { "class": "result-body" }, [
        el("h3", { text: f.item }),
        f.isLocked ? el("span", { "class": "chip", text: "locked" }) : null,
        el("span", { "class": "verb", text: f.isAre ? " are " : " is " }),
        value,
        editor,
        el("p", { "class": "by", text: "— told by " + f.user + (selectable ? " · " + f.item.length + " characters" : "") }),
        el("div", { "class": "admin-actions" }, [edit, save, cancel, lock, remove]),
        status
      ])
    ]);
  }

  // A list of factoids that redraws itself after changes, so a lock shows on every value of the item.
  function factoidList(container, factoids, selectable, onSelect) {
    function apply(change) {
      if (change && change.deleted) {
        factoids = factoids.filter(function (f) { return change.deleted.indexOf(f.id) < 0; });
      }
      if (change && change.item !== undefined) {
        factoids.forEach(function (f) {
          if (f.item.toLowerCase() === change.item.toLowerCase()) f.isLocked = change.isLocked;
        });
      }
      container.replaceChildren.apply(container, factoids.map(function (f) { return renderFactoid(f, apply, selectable); }));
      if (onSelect) onSelect();
    }
    apply();
    return apply;
  }

  function find(event) {
    event.preventDefault();
    var query = $("find-q").value.trim();
    var status = $("find-status");
    if (!query) return;
    setStatus(status, "searching");
    call("GET", "/manage/factoids?q=" + encodeURIComponent(query))
      .then(function (body) {
        var n = body.factoids.length;
        setStatus(status, n + (n === 50 ? "+" : "") + (n === 1 ? " match" : " matches"));
        factoidList($("find-results"), body.factoids, false);
      })
      .catch(failed(status));
  }

  var redrawLong = null;

  function selectedLong() {
    return Array.prototype.map.call(document.querySelectorAll("#long-results .admin-select:checked"), function (c) { return Number(c.value); });
  }

  function updateLongButtons() {
    var n = selectedLong().length;
    var total = document.querySelectorAll("#long-results .admin-select").length;
    $("long-delete").disabled = n === 0;
    $("long-delete").textContent = n ? "delete " + n + " selected" : "delete selected";
    $("long-all").checked = total > 0 && n === total;
    $("long-all-label").hidden = $("long-delete").hidden = total === 0;
  }

  function loadLong() {
    var status = $("long-status");
    setStatus(status, "loading");
    busy($("long-load"), call("GET", "/manage/factoids/long?limit=500"))
      .then(function (body) {
        var n = body.factoids.length;
        setStatus(status, n === 0
          ? "nothing over " + maxItemLength + " characters"
          : n + (n === 500 ? "+" : "") + " over " + maxItemLength + " characters, longest first");
        redrawLong = factoidList($("long-results"), body.factoids, true, updateLongButtons);
      })
      .catch(failed(status));
  }

  function deleteLong() {
    var ids = selectedLong();
    if (!ids.length || !confirm("Delete " + ids.length + " factoid" + (ids.length === 1 ? "" : "s") + "? This can't be undone here.")) return;
    busy($("long-delete"), call("POST", "/manage/factoids/delete", { ids: ids }))
      .then(function (result) {
        setStatus($("long-status"), "deleted " + result.deleted);
        redrawLong({ deleted: ids });
      })
      .catch(failed($("long-status")));
  }

  // --- Email keywords ------------------------------------------------------------------------------------

  var keywords = [];

  function renderKeywords() {
    var n = keywords.length;
    setStatus($("keyword-status"), n === 0 ? "no keywords yet" : n + (n === 1 ? " keyword" : " keywords"));
    $("keyword-results").replaceChildren.apply($("keyword-results"), keywords.map(function (k) {
      var status = el("p", { "class": "status", hidden: "" });
      var remove = el("button", { type: "button", "class": "small-button danger", text: "remove" });
      remove.addEventListener("click", function () {
        busy(remove, call("DELETE", "/manage/keywords/" + k.id)
          .then(function () {
            keywords = keywords.filter(function (other) { return other.id !== k.id; });
            renderKeywords();
          }))
          .catch(failed(status));
      });
      return el("article", { "class": "result admin-factoid" }, [
        el("time", { datetime: k.timestamp, text: new Date(k.timestamp).toISOString().slice(0, 10) }),
        el("div", { "class": "result-body" }, [
          el("h3", { text: k.keyword }),
          el("p", { "class": "by", text: "— added by " + (k.user || "unknown") }),
          el("div", { "class": "admin-actions" }, [remove]),
          status
        ])
      ]);
    }));
  }

  function loadKeywords() {
    setStatus($("keyword-status"), "loading");
    return call("GET", "/manage/keywords")
      .then(function (body) { keywords = body.keywords; renderKeywords(); })
      .catch(failed($("keyword-status")));
  }

  function addKeyword(event) {
    event.preventDefault();
    var input = $("keyword-new");
    var keyword = input.value.trim();
    if (!keyword) return;
    busy(document.querySelector("#keyword-form button"), call("POST", "/manage/keywords", { keyword: keyword }))
      .then(function (added) {
        input.value = "";
        keywords = keywords.concat([added]).sort(function (a, b) {
          return a.keyword.toLowerCase() < b.keyword.toLowerCase() ? -1 : 1;
        });
        renderKeywords();
      })
      .catch(failed($("keyword-status")));
  }

  function showSignedIn() {
    setStatus($("account-status"), "checking your account");
    return call("GET", "/manage/me").then(function (me) {
      setStatus($("account-status"), "signed in as " + me.name);
      $("sign-in").hidden = true;
      $("sign-out").hidden = !!config.localObjectId;
      $("admin-panels").hidden = false;
      return loadKeywords();
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    $("sign-in").addEventListener("click", function () { signIn(); });
    $("sign-out").addEventListener("click", function () { store.clear(); showSignedOut("signed out"); });
    $("find-form").addEventListener("submit", find);
    $("long-load").addEventListener("click", loadLong);
    $("long-delete").addEventListener("click", deleteLong);
    $("keyword-form").addEventListener("submit", addKeyword);
    $("long-results").addEventListener("change", updateLongButtons);
    $("long-all").addEventListener("change", function () {
      var on = this.checked;
      document.querySelectorAll("#long-results .admin-select").forEach(function (c) { c.checked = on; });
      updateLongButtons();
    });

    if (!config.clientId && !config.localObjectId) {
      showSignedOut("sign-in isn't set up here (GTHX_ADMIN in assets/config.js)");
      return;
    }

    finishSignIn()
      .then(function () { return config.localObjectId || store.get("token") || store.get("refresh") ? showSignedIn() : showSignedOut(); })
      .catch(function (error) {
        if (error instanceof NeedsSignIn) showSignedOut(error.message);
        else { showSignedOut(); setStatus($("account-status"), error.message, true); }
      });
  });
})();
