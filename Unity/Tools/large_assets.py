#!/usr/bin/env python3
"""Restore and update the project's large, compressed Git assets."""

import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import sys
import tempfile
import uuid


ROOT = Path(__file__).resolve().parents[2]
ARCHIVE = Path("Unity/LargeAssets")
MANIFEST = ARCHIVE / "manifest.json"
CHUNK_SIZE = 32 * 1024 * 1024
READ_SIZE = 1024 * 1024
HEX = re.compile(r"^[0-9a-f]{64}$")


def safe_path(root, value):
    if not isinstance(value, str) or "\\" in value or "\0" in value:
        raise ValueError(f"Invalid relative path: {value!r}")
    path = PurePosixPath(value)
    if (not path.parts or path.is_absolute() or
            any(part in (".", "..") or ":" in part for part in path.parts)):
        raise ValueError(f"Invalid relative path: {value!r}")
    root = Path(root).resolve()
    result = (root / Path(*path.parts)).resolve()
    if not result.is_relative_to(root):
        raise ValueError(f"Path escapes the destination: {value!r}")
    return result


def digest_file(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        while block := stream.read(READ_SIZE):
            digest.update(block)
    return digest.hexdigest()


def read_manifest(root):
    path = Path(root) / MANIFEST
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if manifest.get("version") != 1 or manifest.get("chunk_size") != CHUNK_SIZE:
        raise ValueError("Unsupported large-assets manifest format")
    if not isinstance(manifest.get("files"), list):
        raise ValueError("Invalid large-assets file list")
    seen = set()
    for entry in manifest["files"]:
        name = entry["path"]
        safe_path(root, name)
        if name in seen or not HEX.fullmatch(entry["sha256"]):
            raise ValueError(f"Invalid or duplicate asset entry: {name}")
        seen.add(name)
        if not isinstance(entry["size"], int) or entry["size"] < 0:
            raise ValueError(f"Invalid asset size: {name}")
        total = 0
        for chunk in entry["chunks"]:
            if not HEX.fullmatch(chunk["sha256"]) or not HEX.fullmatch(chunk["raw_sha256"]):
                raise ValueError(f"Invalid chunk digest: {name}")
            if not isinstance(chunk["size"], int) or not 0 < chunk["size"] <= CHUNK_SIZE:
                raise ValueError(f"Invalid chunk size: {name}")
            if not isinstance(chunk["compressed_size"], int) or not 0 < chunk["compressed_size"] < 50 * 1024 * 1024:
                raise ValueError(f"Invalid compressed chunk size: {name}")
            total += chunk["size"]
        if total != entry["size"]:
            raise ValueError(f"Chunks do not match asset size: {name}")
    return manifest


def write_manifest(root, manifest):
    path = Path(root) / MANIFEST
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    fd, temporary = tempfile.mkstemp(prefix=".manifest-", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def archive_file(root, source, name):
    """Create content-addressed chunks without altering source or old chunks."""
    safe_path(root, name)
    source = Path(source)
    before = source.stat()
    chunks_dir = Path(root) / ARCHIVE / "chunks"
    chunks_dir.mkdir(parents=True, exist_ok=True)
    overall = hashlib.sha256()
    chunks = []
    with source.open("rb") as stream:
        while raw := stream.read(CHUNK_SIZE):
            overall.update(raw)
            fd, temporary = tempfile.mkstemp(prefix=".chunk-", dir=chunks_dir)
            try:
                with os.fdopen(fd, "wb") as target:
                    with gzip.GzipFile(filename="", mode="wb", fileobj=target,
                                       compresslevel=6, mtime=0) as compressor:
                        compressor.write(raw)
                compressed_digest = digest_file(temporary)
                compressed_size = os.path.getsize(temporary)
                final = chunks_dir / f"{compressed_digest}.gz"
                if final.exists():
                    if final.stat().st_size != compressed_size or digest_file(final) != compressed_digest:
                        raise ValueError(f"Existing chunk is corrupt: {final}")
                    os.unlink(temporary)
                else:
                    os.replace(temporary, final)
                chunks.append({"sha256": compressed_digest,
                               "raw_sha256": hashlib.sha256(raw).hexdigest(),
                               "size": len(raw), "compressed_size": compressed_size})
            finally:
                if os.path.exists(temporary):
                    os.unlink(temporary)
    after = source.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"Source changed while it was being packed: {source}")
    return {"path": name, "size": before.st_size,
            "sha256": overall.hexdigest(), "chunks": chunks}


def inspect_source(root, entry):
    path = safe_path(root, entry["path"])
    if not path.is_file():
        return "missing"
    before = path.stat()
    if before.st_size != entry["size"]:
        return "changed"
    value = digest_file(path)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"Source changed while it was being checked: {entry['path']}")
    return "unchanged" if value == entry["sha256"] else "changed"


def verify_entry(root, entry, target=None):
    overall = hashlib.sha256()
    total = 0
    for chunk in entry["chunks"]:
        path = Path(root) / ARCHIVE / "chunks" / (chunk["sha256"] + ".gz")
        if not path.is_file() or path.stat().st_size != chunk["compressed_size"]:
            raise ValueError(f"Missing or wrong-sized chunk: {path}")
        if digest_file(path) != chunk["sha256"]:
            raise ValueError(f"Corrupt compressed chunk: {path}")
        digest = hashlib.sha256()
        count = 0
        with gzip.open(path, "rb") as stream:
            while block := stream.read(READ_SIZE):
                count += len(block)
                if count > chunk["size"]:
                    raise ValueError(f"Expanded chunk is too large: {path}")
                digest.update(block)
                overall.update(block)
                total += len(block)
                if target is not None:
                    target.write(block)
        if count != chunk["size"] or digest.hexdigest() != chunk["raw_sha256"]:
            raise ValueError(f"Corrupt expanded chunk: {path}")
    if total != entry["size"] or overall.hexdigest() != entry["sha256"]:
        raise ValueError(f"Restored asset does not match manifest: {entry['path']}")


def restore_entry(root, destination, entry):
    target = safe_path(destination, entry["path"])
    if target.exists():
        if target.is_file() and target.stat().st_size == entry["size"] and digest_file(target) == entry["sha256"]:
            return "unchanged"
        return "different file kept"
    target.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=".large-asset-restore-", suffix=".tmp", dir=target.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            verify_entry(root, entry, stream)
            stream.flush()
            os.fsync(stream.fileno())
        # A hard link gives us an atomic, no-overwrite install on the same disk.
        os.link(temporary, target)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return "restored"


def selected_entries(manifest, paths):
    entries = {entry["path"]: entry for entry in manifest["files"]}
    if not paths:
        return list(entries.values())
    paths = [path.replace("\\", "/") for path in paths]
    unknown = [path for path in paths if path not in entries]
    if unknown:
        raise ValueError("Not a managed asset: " + ", ".join(unknown))
    return [entries[path] for path in dict.fromkeys(paths)]


def pack(root, manifest, entries):
    original_manifest_digest = digest_file(Path(root) / MANIFEST)
    for entry in entries:
        if not safe_path(root, entry["path"]).is_file():
            raise ValueError(f"Cannot pack missing asset: {entry['path']}")
    old_chunks = {chunk["sha256"] for entry in manifest["files"] for chunk in entry["chunks"]}
    replacements = {}
    for entry in entries:
        name = entry["path"]
        if inspect_source(root, entry) == "unchanged":
            print(f"unchanged: {name}", flush=True)
            continue
        print(f"packing: {name}", flush=True)
        replacements[name] = archive_file(root, safe_path(root, name), name)
    if not replacements:
        return
    if digest_file(Path(root) / MANIFEST) != original_manifest_digest:
        raise ValueError("Manifest changed while assets were being packed; nothing was replaced")
    manifest["files"] = [replacements.get(entry["path"], entry) for entry in manifest["files"]]
    write_manifest(root, manifest)
    new_chunks = {chunk["sha256"] for entry in manifest["files"] for chunk in entry["chunks"]}
    old_dir = Path(root) / ARCHIVE / ".previous" / uuid.uuid4().hex
    for digest in sorted(old_chunks - new_chunks):
        old = Path(root) / ARCHIVE / "chunks" / (digest + ".gz")
        if old.is_file() and digest_file(old) == digest:
            old_dir.mkdir(parents=True, exist_ok=True)
            shutil.move(str(old), str(old_dir / old.name))
        elif old.exists():
            print(f"kept modified old chunk: {old}", file=sys.stderr)
    print("Packed. Review and stage Unity/LargeAssets with git add -A Unity/LargeAssets.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    status = actions.add_parser("status", help="compare restored assets to the committed archive")
    status.add_argument("paths", nargs="*")
    verify = actions.add_parser("verify", help="verify compressed and expanded checksums")
    verify.add_argument("paths", nargs="*")
    restore = actions.add_parser("restore", help="restore missing assets without overwriting changes")
    restore.add_argument("--destination", default=str(ROOT), help="destination root (default: repository)")
    restore.add_argument("paths", nargs="*")
    save = actions.add_parser("pack", help="update the archive from selected restored assets")
    save.add_argument("--all", action="store_true", help="pack all changed managed assets")
    save.add_argument("paths", nargs="*")
    args = parser.parse_args(argv)
    try:
        manifest = read_manifest(ROOT)
        if args.action == "pack" and args.all == bool(args.paths):
            raise ValueError("Choose --all or provide one or more asset paths")
        entries = selected_entries(manifest, args.paths)
        if args.action == "pack":
            pack(ROOT, manifest, entries)
        elif args.action == "status":
            for entry in entries:
                print(f"{inspect_source(ROOT, entry)}: {entry['path']}", flush=True)
        elif args.action == "verify":
            for entry in entries:
                verify_entry(ROOT, entry)
                print(f"verified: {entry['path']}", flush=True)
        else:
            changed = False
            for entry in entries:
                result = restore_entry(ROOT, args.destination, entry)
                print(f"{result}: {entry['path']}", flush=True)
                changed |= result == "different file kept"
            if changed:
                return 1
        return 0
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as error:
        print(f"large_assets: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
