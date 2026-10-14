// Serves a download larger than Cloudflare Pages' 25 MiB per-file cap. add-download.sh splits the
// file into parts under /files/ and writes /files/<name>.json; this joins the parts on the fly,
// so the visitor gets one ordinary file at /download/<name>.
export async function onRequest(context) {
  const { request, env, params } = context;
  if (request.method !== "GET" && request.method !== "HEAD") {
    return new Response("Method not allowed", { status: 405, headers: { Allow: "GET, HEAD" } });
  }
  const name = String(params.file);
  const asset = (path) => env.ASSETS.fetch(new URL(path, request.url));
  const listing = /^[A-Za-z0-9._-]+$/.test(name) ? await asset(`/files/${name}.json`) : null;
  if (!listing || !listing.ok) return asset("/404.html").then((page) => new Response(page.body, { status: 404, headers: page.headers }));

  const manifest = await listing.json();
  const headers = {
    "Content-Type": manifest.type || "application/octet-stream",
    "Content-Disposition": `attachment; filename="${name}"`,
    "Cache-Control": "public, max-age=300",
    "X-Content-Type-Options": "nosniff",
  };
  if (request.method === "HEAD") {
    return new Response(null, { headers: { ...headers, "Content-Length": String(manifest.size) } });
  }

  const { readable, writable } = new FixedLengthStream(manifest.size);
  context.waitUntil((async () => {
    for (const [index, part] of manifest.parts.entries()) {
      const response = await asset(`/files/${part}`);
      if (!response.ok || !response.body) {
        await writable.abort(new Error(`missing part ${part}`));
        return;
      }
      await response.body.pipeTo(writable, { preventClose: index < manifest.parts.length - 1 });
    }
  })());
  return new Response(readable, { headers });
}
