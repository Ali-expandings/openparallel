// OpenParallel site: the visitor's system, project facts (project.json), downloads, the register.
(() => {
  "use strict";

  const SYSTEMS = {
    mac: { name: "macOS", dock: "Dock", menu: "Dock and Launchpad", menuShort: "Dock", trash: "Trash" },
    windows: { name: "Windows", dock: "taskbar", menu: "Start menu", menuShort: "Start menu", trash: "Recycle Bin" },
    linux: { name: "Linux", dock: "dash", menu: "applications menu", menuShort: "applications menu", trash: "Trash" },
  };

  function detectSystem() {
    const ua = navigator.userAgent || "";
    const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
    const both = `${platform} ${ua}`;
    if (/Win/i.test(both)) return "windows";
    if (/Android|iPhone|iPad|iPod/i.test(both)) return "mac";
    if (/Linux|X11|CrOS/i.test(both)) return "linux";
    return "mac";
  }

  const OP = (window.OP = {
    system: detectSystem(),
    systems: SYSTEMS,
    project: fetch("/project.json").then((r) => (r.ok ? r.json() : null)).catch(() => null),
    register: null,
    loadRegister() {
      if (!this.register) {
        this.register = fetch("/data/register.json")
          .then((r) => r.json())
          .then((d) => d.rows);
      }
      return this.register;
    },
    releaseUrl(project, file) {
      return project && project.repositoryUrl ? `${project.repositoryUrl.replace(/\/$/, "")}/releases/latest/download/${file}` : null;
    },
  });

  // Words that differ per system follow whichever system the desk is showing.
  function applyWords(system) {
    const words = SYSTEMS[system];
    const set = (selector, value) => document.querySelectorAll(selector).forEach((el) => (el.textContent = value));
    set("[data-dock-word]", words.dock);
    set("[data-menu-word]", words.menu);
    set("[data-menu-word-short]", words.menuShort);
    set("[data-trash-word]", words.trash);
  }
  document.addEventListener("op:system", (e) => applyWords(e.detail));
  applyWords(OP.system);

  // Mobile menu.
  const toggle = document.querySelector(".menu-toggle");
  const nav = document.getElementById("site-nav");
  if (toggle && nav) {
    toggle.addEventListener("click", () => {
      const open = nav.classList.toggle("open");
      toggle.setAttribute("aria-expanded", String(open));
    });
    nav.addEventListener("click", (e) => {
      if (e.target.closest("a")) {
        nav.classList.remove("open");
        toggle.setAttribute("aria-expanded", "false");
      }
    });
  }

  // Downloads and links that need the public repository.
  OP.project.then((project) => {
    const repo = project && project.repositoryUrl;
    document.querySelectorAll("[data-repo-link]").forEach((a) => {
      if (repo) {
        a.href = repo;
        a.hidden = false;
      }
    });
    const sentence = document.querySelector("[data-repo-sentence]");
    if (sentence && repo) {
      sentence.innerHTML = "";
      const link = document.createElement("a");
      link.href = repo;
      link.textContent = "Read every line on GitHub";
      sentence.append(link, ", or build it yourself.");
    }

    const yours = OP.system === "linux" ? "linux-x64" : OP.system;
    document.querySelectorAll("[data-system]").forEach((row) => {
      const key = row.dataset.system;
      const info = project && project.downloads && project.downloads[key];
      const file = info ? info.file : "";
      row.querySelector("[data-file]").textContent = file;
      row.querySelector("[data-requires]").textContent = info ? info.requires : "";
      const get = row.querySelector("[data-get]");
      const url = OP.releaseUrl(project, file);
      if (url) get.href = url;
      else {
        get.setAttribute("aria-disabled", "true");
        get.removeAttribute("href");
        get.title = "Available with the first public release";
      }
      row.classList.toggle("is-yours", key === yours);
    });
    const note = document.querySelector("[data-release-note]");
    if (note) note.hidden = Boolean(repo);

    const label = document.querySelector("[data-download-label]");
    if (label) label.textContent = `Download for ${SYSTEMS[OP.system].name}`;
    const auto = document.querySelector("[data-download-auto]");
    const info = project && project.downloads && project.downloads[yours];
    const url = info && OP.releaseUrl(project, info.file);
    if (auto && url) auto.href = url;
    const others = document.querySelector("[data-other-systems]");
    if (others) {
      others.textContent = { mac: "Windows and Linux", windows: "macOS and Linux", linux: "macOS and Windows" }[OP.system];
    }
  });

  // How it works: the steps drive the New Instance window beside them.
  const sheet = document.querySelector("[data-sheet]");
  document.querySelectorAll("[data-steps] li").forEach((li) => {
    const show = () => {
      document.querySelectorAll("[data-steps] li").forEach((x) => x.classList.toggle("on", x === li));
      if (sheet) sheet.dataset.step = li.dataset.step;
    };
    li.addEventListener("mouseenter", show);
    li.addEventListener("focus", show);
    li.addEventListener("click", show);
  });

  // The register (home preview and the Apps page).
  const STATUS = {
    tested: { word: "Tested", cls: "status-tested", order: 0 },
    detected: { word: "Supported", cls: "status-supported", order: 1 },
    wishlist: { word: "Wanted", cls: "status-wanted", order: 2 },
    limited: { word: "Not supported", cls: "status-no", order: 3 },
  };
  OP.status = STATUS;

  function rowHtml(row) {
    const s = STATUS[row.tier] || STATUS.detected;
    const tr = document.createElement("tr");
    const name = document.createElement("td");
    name.textContent = row.app;
    const status = document.createElement("td");
    const badge = document.createElement("span");
    badge.className = `status ${s.cls}`;
    badge.textContent = s.word;
    status.append(badge);
    const note = document.createElement("td");
    note.className = "note";
    note.textContent = row.note;
    tr.append(name, status, note);
    return tr;
  }

  OP.renderRows = (tbody, rows, emptyText) => {
    tbody.replaceChildren();
    if (rows.length === 0) {
      const tr = document.createElement("tr");
      const td = document.createElement("td");
      td.colSpan = 3;
      td.className = "empty";
      td.textContent = emptyText;
      tr.append(td);
      tbody.append(tr);
      return;
    }
    rows.forEach((r) => tbody.append(rowHtml(r)));
  };

  OP.sortRows = (rows) =>
    rows.slice().sort((a, b) => (STATUS[a.tier].order - STATUS[b.tier].order) || a.app.localeCompare(b.app));

  const preview = document.querySelector("[data-register][data-limit]");
  if (preview) {
    const limit = Number(preview.dataset.limit);
    const tbody = preview.querySelector("[data-rows]");
    const search = preview.querySelector("[data-search]");
    const summary = preview.querySelector("[data-summary]");
    OP.loadRegister().then((rows) => {
      const tested = rows.filter((r) => r.tier === "tested");
      const versions = tested.reduce((n, r) => n + (r.versions || 0), 0);
      summary.textContent = `On macOS: ${tested.length} apps tested in ${versions} versions, ${rows.length} in the list.`;
      search.placeholder = `Search ${rows.length} apps`;
      const draw = () => {
        const q = search.value.trim().toLowerCase();
        const pool = q ? rows.filter((r) => r.app.toLowerCase().includes(q)) : tested;
        OP.renderRows(tbody, OP.sortRows(pool).slice(0, limit), `“${search.value.trim()}” isn’t in the list yet. Report it and it can be tested.`);
      };
      search.addEventListener("input", draw);
      draw();
    }).catch(() => OP.renderRows(tbody, [], "The list couldn’t load. It’s also on the Apps page."));
  }
})();
