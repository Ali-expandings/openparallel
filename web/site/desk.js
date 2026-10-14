// The desk: one app and its copy, open side by side, on the visitor's kind of desktop.
(() => {
  "use strict";
  const desk = document.querySelector(".desk");
  if (!desk) return;
  const stage = desk.querySelector("[data-stage]");

  // Illustration content. People and workspaces are made up; app names are real tested apps.
  const APPS = {
    claude: {
      name: "Claude", letter: "C", tile: "var(--tile-1)", layout: "chat",
      a: { account: "Personal", side: "Recents", items: ["Trip to Lisbon", "Bread starter ratios", "Tax letter draft"], on: 1,
        msgs: [["You", "My starter doubles in 6 hours. Is that ready for a loaf?"], ["Claude", "Yes. Use it near the peak, about 6 to 8 hours after feeding."]] },
      b: { account: "Fernhill Studio", side: "Projects", items: ["Q4 brand refresh", "Client brief: Okafor", "Hiring plan"], on: 0,
        msgs: [["You", "Summarise the Okafor brief in five bullets for the team."], ["Claude", "Here are the five points the brief turns on, in order of priority."]] },
    },
    discord: {
      name: "Discord", letter: "D", tile: "var(--tile-2)", layout: "chat",
      a: { account: "mayab", side: "Weekend Climbers", items: ["# general", "# trips", "# gear-swap"], on: 1,
        msgs: [["jules", "Saturday at the north wall? Forecast looks dry."], ["mayab", "In. I’ll bring the 70 m rope."]] },
      b: { account: "maya.fernhill", side: "Fernhill Studio", items: ["# announcements", "# design", "# shipping"], on: 1,
        msgs: [["tomas", "New homepage comps are in Figma."], ["maya.fernhill", "Looking now, feedback by noon."]] },
    },
    telegram: {
      name: "Telegram", letter: "T", tile: "var(--tile-3)", layout: "chat",
      a: { account: "+44 ··· 1187", side: "Chats", items: ["Mum", "Football Thursdays", "Lena"], on: 0,
        msgs: [["Mum", "Sunday lunch at one?"], ["You", "Perfect, I’ll bring dessert."]] },
      b: { account: "Kiln Ceramics support", side: "Chats", items: ["Order #2291", "Wholesale: Harbour & Co", "Courier"], on: 0,
        msgs: [["Customer", "Has order 2291 shipped yet?"], ["Kiln Ceramics", "It left today. Tracking link sent by email."]] },
    },
    vscode: {
      name: "Visual Studio Code", dock: "VS Code", letter: "V", tile: "var(--tile-4)", layout: "editor", parallName: "Visual Studio Code Parall",
      a: { account: "Profile: Personal", side: "Explorer", items: ["garden-planner", "src", "planner.ts", "README.md"], on: 2, tint: "var(--st-supported)",
        code: [["const", " beds = loadBeds("], ["", "  '/plots/2026.json'"], ["", ");"], ["export", " function sow(bed) {"]] },
      b: { account: "Profile: Fernhill", side: "Explorer", items: ["client-portal", "app", "routes.ts", "package.json"], on: 2, tint: "var(--st-tested)",
        code: [["import", " { auth } from './sso'"], ["", ""], ["export", " const routes = ["], ["", "  { path: '/invoices' },"]] },
    },
    obsidian: {
      name: "Obsidian", letter: "O", tile: "var(--tile-5)", layout: "editor",
      a: { account: "Vault: Notes", side: "Files", items: ["Daily", "2026-10-14", "Reading list", "Recipes"], on: 1, tint: "var(--tile-5)",
        code: [["#", " Wednesday"], ["", "- Repot the fig"], ["", "- Call Sam about Lisbon"], ["", "- [[Reading list]]"]] },
      b: { account: "Vault: Fernhill", side: "Files", items: ["Clients", "Okafor", "Meeting notes", "Rates 2026"], on: 2, tint: "var(--tile-1)",
        code: [["#", " Okafor kickoff"], ["", "- Launch window: March"], ["", "- Budget agreed"], ["", "- Next: [[Rates 2026]]"]] },
    },
  };
  const BLANKS = 3;

  let app = desk.dataset.app || "discord";
  let os = (window.OP && window.OP.system) || "mac";
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

  function el(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  }

  function svg(id, viewBox) {
    const ns = "http://www.w3.org/2000/svg";
    const s = document.createElementNS(ns, "svg");
    s.setAttribute("viewBox", viewBox);
    s.setAttribute("aria-hidden", "true");
    const use = document.createElementNS(ns, "use");
    use.setAttribute("href", `#${id}`);
    s.append(use);
    return s;
  }

  function windowFor(which) {
    const data = APPS[app];
    const side = data[which];
    const w = el("div", `window ${which === "a" ? "w-original" : "w-copy"}`);
    w.setAttribute("aria-hidden", "true");
    const bar = el("div", "titlebar");
    const lights = el("span", "lights");
    lights.append(el("i"), el("i"), el("i"));
    const title = el("span", "title", which === "a" ? data.name : data.parallName || `${data.name} Parall`);
    const account = el("span", "account", side.account);
    const controls = el("span", "wincontrols");
    controls.append(svg("i-min", "0 0 10 10"), svg("i-max", "0 0 10 10"), svg("i-x", "0 0 10 10"));
    const close = el("span", "gnome-close");
    close.append(svg("i-x", "0 0 10 10"));
    bar.append(lights, title, account, controls, close);

    const body = el("div", "winbody");
    const aside = el("div", "side");
    aside.append(el("h4", null, side.side));
    const list = el("ul");
    side.items.forEach((item, i) => list.append(el("li", i === side.on ? "on" : null, item)));
    aside.append(list);
    const main = el("div", "main");
    if (data.layout === "chat") {
      side.msgs.forEach(([who, text], i) => {
        const msg = el("div", "msg");
        const av = el("span", "avatar", who.slice(0, 1).toUpperCase());
        av.style.setProperty("--av", i % 2 ? data.tile : "var(--tile-6)");
        const words = el("div");
        words.append(el("b", null, who), el("p", null, text));
        msg.append(av, words);
        main.append(msg);
      });
    } else {
      main.style.setProperty("--tint", side.tint);
      side.code.forEach(([kw, rest]) => {
        const line = el("div", "code-line");
        if (kw) line.append(el("em", null, kw));
        line.append(el("span", null, rest || " "));
        main.append(line);
      });
      main.append(el("div", "statusbar", side.account));
    }
    body.append(aside, main);
    w.append(bar, body);
    return w;
  }

  function tile(kind) {
    const data = APPS[app];
    if (kind === "blank") {
      const t = el("span", "tile blank");
      t.append(el("span", "face"));
      return t;
    }
    const isCopy = kind === "copy";
    const label = isCopy ? data.parallName || `${data.name} Parall` : data.name;
    const t = el("button", `tile running ${isCopy ? "t-copy" : "t-original"}`);
    t.type = "button";
    t.setAttribute("aria-label", isCopy ? `Open ${label}, the copy` : `Bring ${label} forward`);
    const face = el("span", "face", data.letter);
    face.style.setProperty("--face", data.tile);
    t.append(face, el("span", "label", isCopy ? label : data.dock || data.name), el("span", "dot"));
    t.addEventListener("click", () => (isCopy ? launchCopy() : bringForward("original")));
    return t;
  }

  function render() {
    stage.replaceChildren();
    desk.dataset.app = app;
    desk.dataset.os = os;
    desk.dataset.front = "copy";
    const name = APPS[app].name;
    desk.setAttribute("aria-label", `Illustration: ${name} and ${APPS[app].parallName || name + " Parall"} open side by side on ${window.OP ? window.OP.systems[os].name : os}, each signed in to a different account.`);
    stage.append(windowFor("a"), windowFor("b"));
    const dock = el("div", "dock");
    for (let i = 0; i < BLANKS; i++) dock.append(tile("blank"));
    dock.append(tile("original"), tile("copy"));
    stage.append(dock);
    markFront("copy");
  }

  function markFront(which) {
    desk.dataset.front = which;
    stage.querySelectorAll(".tile.running").forEach((t) =>
      t.classList.toggle("front", (which === "copy") === t.classList.contains("t-copy")));
  }

  function bringForward(which) {
    markFront(which);
  }

  let launching = null;
  function launchCopy() {
    const w = stage.querySelector(".w-copy");
    const t = stage.querySelector(".t-copy");
    if (!w || !t) return;
    if (reduced.matches) {
      w.classList.remove("is-closed");
      markFront("copy");
      return;
    }
    clearTimeout(launching);
    // The window gathers into its icon, the icon bounces, the window opens again from it.
    const stageBox = stage.getBoundingClientRect();
    const tileBox = t.getBoundingClientRect();
    const wBox = w.getBoundingClientRect();
    const x = ((tileBox.left + tileBox.width / 2 - wBox.left) / wBox.width) * 100;
    w.style.setProperty("--from-x", `${Math.max(0, Math.min(100, x))}%`);
    w.classList.add("is-closed");
    t.classList.remove("launching");
    void t.offsetWidth;
    t.classList.add("launching");
    launching = setTimeout(() => {
      w.classList.remove("is-closed");
      markFront("copy");
    }, 620);
    void stageBox;
  }

  function choose(group, attr, value) {
    group.querySelectorAll("[role=radio]").forEach((b) => b.setAttribute("aria-checked", String(b.dataset[attr] === value)));
  }

  function restage(change) {
    const windows = stage.querySelectorAll(".window");
    if (reduced.matches || windows.length === 0) {
      change();
      render();
      return;
    }
    windows.forEach((w) => w.classList.add("is-restaging"));
    setTimeout(() => {
      change();
      render();
      const fresh = stage.querySelectorAll(".window");
      fresh.forEach((w) => w.classList.add("is-restaging"));
      requestAnimationFrame(() => requestAnimationFrame(() => fresh.forEach((w) => w.classList.remove("is-restaging"))));
    }, 220);
  }

  const appPicker = desk.querySelector("[data-app-picker]");
  const osPicker = desk.querySelector("[data-os-picker]");
  appPicker.addEventListener("click", (e) => {
    const b = e.target.closest("[data-app]");
    if (!b || b.dataset.app === app) return;
    choose(appPicker, "app", b.dataset.app);
    restage(() => (app = b.dataset.app));
  });
  osPicker.addEventListener("click", (e) => {
    const b = e.target.closest("[data-os]");
    if (!b || b.dataset.os === os) return;
    choose(osPicker, "os", b.dataset.os);
    restage(() => (os = b.dataset.os));
    document.dispatchEvent(new CustomEvent("op:system", { detail: b.dataset.os }));
  });
  // Arrow keys move within each radio group, as native segmented controls do.
  [appPicker, osPicker].forEach((group) =>
    group.addEventListener("keydown", (e) => {
      if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
      const buttons = [...group.querySelectorAll("[role=radio]")];
      const i = buttons.indexOf(document.activeElement);
      if (i < 0) return;
      const next = buttons[(i + (e.key === "ArrowRight" ? 1 : buttons.length - 1)) % buttons.length];
      next.focus();
      next.click();
      e.preventDefault();
    }));

  choose(osPicker, "os", os);
  render();
  // The copy opens once on arrival, so the first thing seen is the mechanism.
  if (!reduced.matches) setTimeout(launchCopy, 700);
})();
