#!/usr/bin/env python3
"""Writes the tracked release records for a version: releases/v<version>/ (changelog entry copy,
checksum file) and a row in releases/INDEX.md. Refuses to touch an existing release folder.
Usage: release-records.py <version> <sha256 of the zip> "<date time>" "<one-line summary>"
"""
import os, re, sys
version, sha, when, summary = sys.argv[1:5]
folder = f"releases/v{version}"
if os.path.exists(folder):
    sys.exit(f"{folder} already exists - stopping")
changelog = open("CHANGELOG.md", encoding="utf-8").read()
m = re.search(rf"^## {re.escape(version)} - .*?(?=^## |\Z)", changelog, re.S | re.M)
if not m:
    sys.exit(f"no CHANGELOG.md entry for {version}")
os.makedirs(folder)
open(f"{folder}/CHANGELOG-v{version}.md", "w", encoding="utf-8").write(m.group(0).rstrip() + "\n")
open(f"{folder}/RsmfInspector-v{version}-win-x64.zip.sha256", "w").write(f"{sha}  RsmfInspector-v{version}-win-x64.zip\n")
with open("releases/INDEX.md", "a", encoding="utf-8") as f:
    f.write(f"| {version} | {when} | {summary} | `{sha}` |\n")
print("release records written for", version)
