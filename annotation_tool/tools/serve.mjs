// Zero-dependency static server, so the prototype can be opened from a phone
// on the same wifi. `npm run serve`, then browse to the printed LAN address.

import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const port = Number(process.env.PORT || 8099);

const TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".wav": "audio/wav",
  ".mp3": "audio/mpeg",
  ".m4a": "audio/mp4",
  ".ogg": "audio/ogg",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".ico": "image/x-icon",
};

const server = http.createServer((req, res) => {
  const url = decodeURIComponent((req.url || "/").split("?")[0]);
  const rel = url === "/" ? "index.html" : url.replace(/^\/+/, "");
  const file = path.join(root, rel);

  if (!file.startsWith(root)) {
    res.writeHead(403).end("forbidden");
    return;
  }

  fs.stat(file, (err, stat) => {
    if (err || !stat.isFile()) {
      res.writeHead(404, { "content-type": "text/plain" }).end("not found");
      return;
    }
    res.writeHead(200, {
      "content-type": TYPES[path.extname(file).toLowerCase()] || "application/octet-stream",
      "content-length": stat.size,
      "cache-control": "no-cache",
      "accept-ranges": "bytes",
    });
    fs.createReadStream(file).pipe(res);
  });
});

server.listen(port, "0.0.0.0", () => {
  const nets = os.networkInterfaces();
  const lan = Object.values(nets).flat()
    .filter((n) => n && n.family === "IPv4" && !n.internal)
    .map((n) => n.address);
  console.log(`attention annotator: http://localhost:${port}`);
  for (const ip of lan) console.log(`  on your phone:    http://${ip}:${port}`);
});
