// The Apps page: the full register with search and a status filter.
(() => {
  "use strict";
  const box = document.querySelector("[data-register-full]");
  if (!box || !window.OP) return;
  const tbody = box.querySelector("[data-rows]");
  const search = box.querySelector("[data-search]");
  const filter = box.querySelector("[data-filter]");
  const summary = box.querySelector("[data-summary]");
  let tier = new URLSearchParams(location.search).get("status") || "all";

  window.OP.loadRegister().then((rows) => {
    const counts = rows.reduce((c, r) => ((c[r.tier] = (c[r.tier] || 0) + 1), c), {});
    filter.querySelectorAll("[data-tier]").forEach((b) => {
      const n = b.dataset.tier === "all" ? rows.length : counts[b.dataset.tier] || 0;
      b.textContent = `${b.textContent} ${n}`;
    });
    search.placeholder = `Search ${rows.length} apps`;
    const draw = () => {
      const q = search.value.trim().toLowerCase();
      const shown = window.OP.sortRows(rows.filter((r) => (tier === "all" || r.tier === tier) && (!q || r.app.toLowerCase().includes(q))));
      window.OP.renderRows(tbody, shown, q ? `“${search.value.trim()}” isn’t in the list yet. Report it and it can be tested.` : "Nothing here yet.");
      summary.textContent = `${shown.length} of ${rows.length} apps, as tested on macOS.`;
    };
    const select = (value) => {
      tier = value;
      filter.querySelectorAll("[data-tier]").forEach((b) => b.setAttribute("aria-checked", String(b.dataset.tier === tier)));
      draw();
    };
    filter.addEventListener("click", (e) => {
      const b = e.target.closest("[data-tier]");
      if (b) select(b.dataset.tier);
    });
    filter.addEventListener("keydown", (e) => {
      if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
      const buttons = [...filter.querySelectorAll("[data-tier]")];
      const i = buttons.indexOf(document.activeElement);
      if (i < 0) return;
      const next = buttons[(i + (e.key === "ArrowRight" ? 1 : buttons.length - 1)) % buttons.length];
      next.focus();
      select(next.dataset.tier);
      e.preventDefault();
    });
    search.addEventListener("input", draw);
    select(tier);
  }).catch(() => window.OP.renderRows(tbody, [], "The list couldn’t load. Try reloading the page."));
})();
