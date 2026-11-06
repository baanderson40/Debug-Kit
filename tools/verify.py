"""Development verification only. Does not load the plugin, call IPC, or query external services."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def manifests(root):
    version = ET.parse(root / "Directory.Build.props").findtext("./PropertyGroup/Version")
    require(version and re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?", version), "Invalid project version")
    expected = ".".join((version.split(".") + ["0"] * 4)[:4])
    source = json.loads((root / "DebugKit/DebugKit.json").read_text(encoding="utf-8"))
    output = json.loads((root / "DebugKit/bin/Debug/DebugKit.json").read_text(encoding="utf-8"))
    for manifest in (source, output):
        require(manifest.get("InternalName") == "DebugKit", "Unexpected manifest internal name")
        require(manifest.get("AssemblyVersion") == expected, "Manifest/project version mismatch")
        require(manifest.get("DalamudApiLevel") == 15, "Unexpected Dalamud API level")
    for key, value in source.items():
        require(output.get(key) == value, f"Output manifest differs: {key}")
    return expected


def artifacts(root):
    for name in ("DebugKit.dll", "DebugKit.Core.dll"):
        path = root / "DebugKit/bin/Debug" / name
        require(path.is_file() and path.stat().st_size >= 128, f"Missing/empty artifact: {name}")
        with path.open("rb") as file:
            header = file.read(64)
            require(header[:2] == b"MZ", f"Not a PE DLL: {name}")
            offset = int.from_bytes(header[60:64], "little")
            require(64 <= offset <= path.stat().st_size - 4, f"Invalid PE offset: {name}")
            file.seek(offset)
            require(file.read(4) == b"PE\0\0", f"Invalid PE header: {name}")
        print(f"PASS {name} SHA-256 {hashlib.sha256(path.read_bytes()).hexdigest()}")


def documentation(root):
    # File targets only; this is not a remote URL or Markdown-anchor checker.
    for path in [root / "README.md", root / "AGENTS.md", *(root / "docs").glob("*.md")]:
        for link in re.findall(r"\]\(([^)]+)\)", path.read_text(encoding="utf-8")):
            if "://" in link or link.startswith("#"):
                continue
            target = (path.parent / link.split("#", 1)[0]).resolve()
            require(target.is_relative_to(root.resolve()) and target.exists(), f"Broken local link: {path.name}: {link}")
    print("PASS local documentation file links")


def run(label, command, summary=False):
    print(f"CHECK {label}", flush=True)
    result = subprocess.run(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            encoding="utf-8", errors="replace", check=False)
    if result.returncode != 0 or not summary:
        print(result.stdout, end="")
    else:
        print("\n".join(result.stdout.strip().splitlines()[-1:]))
    require(result.returncode == 0, f"{label} failed (exit {result.returncode})")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    default = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe" if os.name == "nt" else Path("/mnt/c/Program Files/dotnet/dotnet.exe")
    parser.add_argument("--dotnet", type=Path, default=default, help="Existing Windows dotnet.exe; never installs an SDK")
    args = parser.parse_args()
    require(args.dotnet.is_file() and args.dotnet.name.lower() == "dotnet.exe", "Existing Windows dotnet.exe required")
    locks = {ROOT / folder / "packages.lock.json" for folder in ("DebugKit", "DebugKit.Core", "DebugKit.Tests")}
    before = {path: path.read_bytes() for path in locks}
    run("verifier failure-gate unit tests", [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tools", "-p", "test_verify.py"])
    run("locked restore (may contact configured package feeds)", [str(args.dotnet), "restore", "DebugKit.slnx", "--locked-mode", "--nologo"])
    run("Debug/x64 build; warnings treated as errors", [str(args.dotnet), "build", "DebugKit.slnx", "-c", "Debug", "-p:Platform=x64", "--no-restore", "--nologo", "-warnaserror"])
    run("offline production-code tests", [str(args.dotnet), "run", "--project", "DebugKit.Tests/DebugKit.Tests.csproj", "-c", "Debug", "--no-build", "--no-restore"], summary=True)
    run("source safety tripwires", [sys.executable, "-B", "tools/check_safety.py"])
    require(all(path.read_bytes() == data for path, data in before.items()), "A package lockfile changed")
    version = manifests(ROOT)
    artifacts(ROOT)
    documentation(ROOT)
    print(f"PASS development verification: {version} / API 15; lockfiles unchanged")
    print("NOT RELEASE APPROVAL: live game/UI acceptance and third-party license/attribution compatibility remain separate gates.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"FAIL {error}", file=sys.stderr)
        sys.exit(1)
