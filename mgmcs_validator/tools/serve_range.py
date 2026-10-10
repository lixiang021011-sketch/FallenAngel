"""
Serve a folder read-only over HTTP, with byte ranges.

Why this exists instead of `python -m http.server`: that one does not answer
Range requests - every request gets the whole file with a 200. A browser reading
a stream like that decides the resource is not seekable and then ignores
`audio.currentTime = ...`. For a tool whose whole point is "jump to this bar and
listen", that is not a detail: it is the difference between a validator and a
player that can only start at zero.

Both servers this project needs use it, because both of them hand out audio:

    # the validator itself (run.bat does this)
    python tools/serve_range.py --directory static --port 8123
    # a music folder elsewhere, for songs not copied into static/audio/ (serve_music.bat)
    python tools/serve_range.py --directory "C:\\Users\\you\\Music" --port 8125

Read-only in the ordinary sense: only GET and HEAD are answered, nothing is
written, and it binds to 127.0.0.1 unless told otherwise.
"""

import argparse
import os
import re
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

RANGE_RE = re.compile(r"^bytes=(\d*)-(\d*)$")


class RangeHandler(SimpleHTTPRequestHandler):
    """The directory server, plus the one thing a media element needs."""

    server_version = "MGMCSMusic/1.0"
    _ranges_answered = False

    def parse_range(self, header, size):
        """Return (start, end) inclusive, or raise ValueError to ignore it."""
        match = RANGE_RE.match(header.strip())
        if not match:
            raise ValueError(header)
        first, last = match.group(1), match.group(2)
        if first == "":
            if last == "":
                raise ValueError(header)
            length = int(last)
            start, end = max(0, size - length), size - 1
        else:
            start = int(first)
            end = int(last) if last else size - 1
            end = min(end, size - 1)
        if start > end or start >= size:
            raise ValueError(header)
        return start, end

    def do_GET(self):
        self._ranges_answered = False
        header = self.headers.get("Range")
        path = self.translate_path(self.path)
        if not header or not os.path.isfile(path):
            return super().do_GET()
        size = os.path.getsize(path)
        try:
            start, end = self.parse_range(header, size)
        except ValueError:
            # A range nobody can make sense of is answered whole, which is what
            # the spec says to do; the client will ask again if it cares.
            return super().do_GET()
        with open(path, "rb") as handle:
            handle.seek(start)
            body = handle.read(end - start + 1)
        self._ranges_answered = True
        self.send_response(206)
        self.send_header("Content-Type", self.guess_type(path))
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, size))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def end_headers(self):
        # Advertise ranges on the ordinary 200 too, so a client that starts with a
        # plain GET still learns it can jump.
        if not self._ranges_answered:
            self.send_header("Accept-Ranges", "bytes")
        super().end_headers()

    def log_message(self, fmt, *args):
        # One line per request is noise when a player is streaming; keep errors.
        if not str(args[1] if len(args) > 1 else "").startswith(("2", "3")):
            super().log_message(fmt, *args)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, help="folder to serve, read-only")
    parser.add_argument("--port", type=int, default=8125)
    parser.add_argument("--bind", default="127.0.0.1")
    args = parser.parse_args(argv)

    if not os.path.isdir(args.directory):
        print("Not a folder: %s" % args.directory, file=sys.stderr)
        return 2

    def handler(*a, **kw):
        return RangeHandler(*a, directory=args.directory, **kw)

    with ThreadingHTTPServer((args.bind, args.port), handler) as httpd:
        print("Serving %s on http://%s:%d/  (Ctrl+C or close the window to stop)"
              % (args.directory, args.bind, args.port))
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nstopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
