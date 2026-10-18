#!/usr/bin/env python3
"""
Generate an image with the HfG AI Hub textToImage API and save it to disk.

Stdlib only. Reads the API key from AIHUB_API_KEY (or --api-key) and the host from
AIHUB_BASE_URL (default https://ai-hub.hfg-gmuend.de).

Flow (per the AI Hub OpenAPI spec):
  POST /api/tools/textToImage/generate  -> 202 {"run": {id, threadId, status, result: null}}
  GET  /api/tools/textToImage/runs?threadId=<threadId>  -> poll until status is final
  The image URL arrives somewhere in run["result"]; it is located by searching the payload.

Usage:
  python3 aihub_image.py --prompt "..." --out textures/oak_albedo.png \
      [--model gpt-image-2.5-flare-2026-09-08] [--aspect 1:1] [--quality high] [--dump]
"""

import argparse
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE_URL = os.environ.get("AIHUB_BASE_URL", "https://ai-hub.hfg-gmuend.de").rstrip("/")
TOOL = "/api/tools/textToImage"
FINAL_OK = {"done", "completed", "complete", "succeeded", "success", "finished"}
FINAL_BAD = {"failed", "error", "errored", "cancelled", "canceled", "aborted"}
IMAGE_EXTS = (".png", ".jpg", ".jpeg", ".webp")


def request(method, url, key, body=None, accept="application/json"):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", "Bearer " + key)
    req.add_header("Accept", accept)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=120) as resp:
        return resp.status, resp.read(), resp.headers.get("Content-Type", "")


def find_images(node, found):
    """Collect every string in the payload that looks like an image reference."""
    if isinstance(node, dict):
        for v in node.values():
            find_images(v, found)
    elif isinstance(node, list):
        for v in node:
            find_images(v, found)
    elif isinstance(node, str):
        s = node.strip()
        low = s.lower().split("?")[0]
        if s.startswith("data:image/"):
            found.append(s)
        elif (s.startswith("http") or s.startswith("/")) and (
            low.endswith(IMAGE_EXTS) or "/image" in low or "/files/" in low or "/assets/" in low
        ):
            found.append(s)
    return found


def unwrap_run(payload):
    if isinstance(payload, dict) and isinstance(payload.get("run"), dict):
        return payload["run"]
    return payload


def save_image(ref, out, key):
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    if ref.startswith("data:image/"):
        raw = base64.b64decode(ref.split(",", 1)[1])
    else:
        url = urllib.parse.urljoin(BASE_URL, ref)
        try:
            _, raw, _ = request("GET", url, key, accept="image/*")
        except urllib.error.HTTPError:
            # Signed URLs (e.g. object storage) may reject an extra auth header.
            with urllib.request.urlopen(url, timeout=120) as resp:
                raw = resp.read()
    with open(out, "wb") as fh:
        fh.write(raw)
    return len(raw)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--prompt", required=True)
    ap.add_argument("--out", required=True, help="Output image path (.png recommended).")
    ap.add_argument("--model", default="gpt-image-2.5-flare-2026-09-08")
    ap.add_argument("--aspect", default="1:1", help="1:1, 16:9, 9:16, 4:3, 3:4, 3:2, 2:3")
    ap.add_argument("--quality", default=None,
                    help="Gemini/FLUX: 1K|2K|4K. GPT Image: low|medium|high|xhigh|max.")
    ap.add_argument("--timeout", type=int, default=600, help="Seconds to wait for the run.")
    ap.add_argument("--api-key", default=os.environ.get("AIHUB_API_KEY"))
    ap.add_argument("--dump", action="store_true", help="Print the final run JSON (debugging).")
    ap.add_argument("--ref", action="append", default=[],
                    help="Reference image path (repeatable), sent as referenceImages data URLs for style matching.")
    args = ap.parse_args()

    if not args.api_key:
        sys.exit("error: set AIHUB_API_KEY (create a key at https://ai-hub.hfg-gmuend.de/api-keys)")

    options = {"aspectRatio": args.aspect}
    if args.quality:
        options["quality"] = args.quality
    body = {"model": args.model, "prompt": args.prompt, "options": options}
    if args.ref:
        refs = []
        for r in args.ref:
            mime = "image/jpeg" if r.lower().endswith((".jpg", ".jpeg")) else "image/png"
            with open(r, "rb") as fh:
                refs.append("data:{};base64,{}".format(mime, base64.b64encode(fh.read()).decode()))
        body["referenceImages"] = refs

    for attempt in range(6):
        try:
            _, raw, _ = request("POST", BASE_URL + TOOL + "/generate", args.api_key, body)
            break
        except urllib.error.HTTPError as ex:
            # 429 = too many concurrent requests (seen when firing ~8 in parallel): back off and retry
            if ex.code == 429 and attempt < 5:
                time.sleep(10 * (attempt + 1))
                continue
            hints = {401: "invalid or revoked API key", 402: "weekly credit quota exceeded",
                     403: "API key scope does not include textToImage", 429: "rate limited, retries exhausted"}
            sys.exit("error: HTTP {} ({}) {}".format(ex.code, hints.get(ex.code, ""), ex.read()[:500].decode(errors="replace")))

    run = unwrap_run(json.loads(raw))
    thread_id = run.get("threadId")
    deadline = time.time() + args.timeout
    while True:
        status = str(run.get("status", "")).lower()
        res = run.get("result") or {}
        # Observed shape: result = {"imageUrl": "/api/tools/textToImage/images/<id>.png", "checkpointId": ...}
        images = [res["imageUrl"]] if isinstance(res, dict) and res.get("imageUrl") else find_images(res, [])
        if images or status in FINAL_OK or status in FINAL_BAD:
            break
        if time.time() > deadline:
            sys.exit("error: timed out waiting for run {} (status {})".format(run.get("id"), status))
        time.sleep(3)
        q = urllib.parse.urlencode({"threadId": thread_id})
        _, raw, _ = request("GET", BASE_URL + TOOL + "/runs?" + q, args.api_key)
        run = unwrap_run(json.loads(raw))

    if args.dump:
        print(json.dumps(run, indent=1)[:4000], file=sys.stderr)
    if status in FINAL_BAD or not images:
        sys.exit("error: run ended with status '{}' and no image. Run JSON:\n{}".format(
            status, json.dumps(run, indent=1)[:2000]))

    size = save_image(images[0], args.out, args.api_key)
    print(json.dumps({"out": os.path.abspath(args.out), "bytes": size, "model": args.model,
                      "runId": run.get("id"), "threadId": thread_id, "status": status}))


if __name__ == "__main__":
    main()
