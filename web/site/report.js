// Report a problem: builds one plain-text report, then opens the visitor's mail app, a
// pre-filled GitHub issue, or copies it. Nothing is sent from this page.
(() => {
  "use strict";
  const form = document.querySelector("[data-report]");
  if (!form || !window.OP) return;
  const preview = document.querySelector("[data-preview]");
  const email = document.querySelector("[data-send-email]");
  const github = document.querySelector("[data-send-github]");
  const copy = document.querySelector("[data-copy]");
  const note = document.querySelector("[data-send-note]");
  const what = form.elements.what;
  const error = document.getElementById("what-error");

  const KINDS = {
    bug: "Something doesn’t work",
    separate: "An app doesn’t keep its data separate",
    idea: "An idea or an app to support",
    other: "Something else",
  };
  const MAIL_LIMIT = 1800; // mail apps start dropping longer mailto: bodies

  function guessSystem() {
    const ua = navigator.userAgent;
    const mac = ua.match(/Mac OS X (\d+)[._](\d+)/);
    if (/Windows NT 10/.test(ua)) return "Windows 10 or 11";
    if (/Windows/.test(ua)) return "Windows";
    if (mac) return "macOS";
    if (/Linux/.test(ua)) return /aarch64|arm64/i.test(ua) ? "Linux (ARM)" : "Linux";
    return "";
  }

  // The page can be opened pre-filled: /report/?kind=separate&app=Discord&version=0.0.402
  const params = new URLSearchParams(location.search);
  if (params.get("kind") && KINDS[params.get("kind")]) form.elements.kind.value = params.get("kind");
  if (params.get("app")) form.elements.app.value = params.get("app");
  if (params.get("version")) form.elements.appVersion.value = params.get("version");
  form.elements.system.value = params.get("system") || guessSystem();

  let project = null;
  window.OP.project.then((p) => {
    project = p;
    if (!form.elements.opVersion.value) form.elements.opVersion.value = params.get("op") || (p && p.version) || "";
    if (p && p.repositoryUrl) github.hidden = false;
    update();
  });

  function report() {
    const f = form.elements;
    const kind = KINDS[f.kind.value] || KINDS.other;
    const app = [f.app.value.trim(), f.appVersion.value.trim()].filter(Boolean).join(" ");
    const subject = `[OpenParallel] ${kind}${app ? `: ${f.app.value.trim()}` : ""}`;
    const lines = [`What happened: ${kind}`];
    if (app) lines.push(`App: ${app}`);
    if (f.includeSystem.checked) {
      if (f.opVersion.value.trim()) lines.push(`OpenParallel: ${f.opVersion.value.trim()}`);
      if (f.system.value.trim()) lines.push(`System: ${f.system.value.trim()}`);
    }
    lines.push("---", f.what.value.trim() || "(what happened)");
    if (f.steps.value.trim()) lines.push("", "How to see it again:", f.steps.value.trim());
    return { subject, body: lines.join("\n") };
  }

  function update() {
    const { subject, body } = report();
    preview.textContent = `Subject: ${subject}\n\n${body}`;
    const address = project && project.reportEmail;
    if (address) {
      const mailBody = body.length > MAIL_LIMIT ? `${body.slice(0, MAIL_LIMIT)}\n\n(Trimmed. The full report is on your clipboard; paste it here.)` : body;
      email.href = `mailto:${address}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(mailBody)}`;
      email.removeAttribute("aria-disabled");
    } else {
      email.removeAttribute("href");
      email.setAttribute("aria-disabled", "true");
      note.textContent = "The project’s report address isn’t published yet. Copy the report for now.";
    }
    if (project && project.repositoryUrl) {
      github.href = `${project.repositoryUrl.replace(/\/$/, "")}/issues/new?title=${encodeURIComponent(subject.replace("[OpenParallel] ", ""))}&body=${encodeURIComponent(body)}`;
    }
  }

  function valid() {
    const ok = what.value.trim().length > 0;
    what.setAttribute("aria-invalid", String(!ok));
    error.hidden = ok;
    if (!ok) what.focus();
    return ok;
  }

  form.addEventListener("input", () => {
    if (what.value.trim()) {
      what.removeAttribute("aria-invalid");
      error.hidden = true;
    }
    update();
  });
  form.addEventListener("change", update);
  form.addEventListener("submit", (e) => e.preventDefault());

  async function copyText(text) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      return false;
    }
  }

  email.addEventListener("click", async (e) => {
    if (email.getAttribute("aria-disabled") === "true" || !valid()) {
      e.preventDefault();
      return;
    }
    const { subject, body } = report();
    if (body.length > MAIL_LIMIT) await copyText(`Subject: ${subject}\n\n${body}`);
    note.textContent = "Your mail app should open now. If it doesn’t, copy the report and paste it into an email.";
  });
  github.addEventListener("click", (e) => {
    if (!valid()) e.preventDefault();
  });
  copy.addEventListener("click", async () => {
    if (!valid()) return;
    const { subject, body } = report();
    const ok = await copyText(`Subject: ${subject}\n\n${body}`);
    copy.textContent = ok ? "Copied" : "Select the report and copy it";
    setTimeout(() => (copy.textContent = "Copy report"), 2400);
  });

  update();
})();
