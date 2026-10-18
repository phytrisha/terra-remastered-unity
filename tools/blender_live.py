"""Send a Python file to the Blender that is open with the MCP add-on running (default localhost:9876).

Run with the Blender MCP install's Python (BLENDER_MCP_PYTHON), which provides the `blmcp` socket client:
    $BLENDER_MCP_PYTHON -I tools/blender_live.py script.py
Prints the script's stdout and stderr, so the RESULT_JSON line from blender_run.sh comes through.
Live runs change the scene the user is looking at: scope every edit to named collections and save first.
"""
import json
import sys

try:
    from blmcp.tools_helpers.connection import send_code
except ImportError:                                        # fallback: speak the add-on's protocol directly
    import os, socket

    def send_code(code, strict_json):
        host = os.environ.get("BLENDER_MCP_HOST", "localhost"); port = int(os.environ.get("BLENDER_MCP_PORT", "9876"))
        with socket.create_connection((host, port), timeout=300) as s:
            s.sendall((json.dumps({"type": "execute", "code": code, "strict_json": strict_json}) + "\0").encode())
            buf = bytearray()
            while b"\0" not in buf:
                chunk = s.recv(65536)
                if not chunk: break
                buf.extend(chunk)
        return json.loads(buf.split(b"\0", 1)[0].decode())

r = send_code(open(sys.argv[1], encoding="utf-8").read(), strict_json=False)
if r.get("stdout"): print(r["stdout"], end="")
if r.get("stderr"): print(r["stderr"], end="", file=sys.stderr)
if r.get("status") != "ok":
    print("BLENDER ERROR: " + str(r.get("message") or r), file=sys.stderr); sys.exit(1)
