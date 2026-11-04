#!/usr/bin/env python3
"""Build TimeTrack.exe with the .NET Framework C# compiler.

The project intentionally has no NuGet/MSBuild dependency. The compiler ships
with Windows, and this script finds the .NET Framework reference assemblies
automatically. You can also point TIMETRACK_REFS at a folder containing all the
DLLs listed in REFS.
"""
import glob
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REFS = [
    "PresentationFramework",
    "PresentationCore",
    "WindowsBase",
    "System.Xaml",
    "System",
    "System.Core",
    "System.Drawing",
    "System.Windows.Forms",
    "System.Web.Extensions",
    "System.Runtime.Serialization",
    "System.Xml",
]
SOURCES = sorted(glob.glob(os.path.join(ROOT, "src", "*.cs")))
OUT = os.path.join(ROOT, "TimeTrack.exe")
ICON = os.path.join(ROOT, "assets", "timetrack.ico")
MANIFEST = os.path.join(ROOT, "src", "app.manifest")


def find_csc():
    for base in (r"C:\Windows\Microsoft.NET\Framework64",
                 r"C:\Windows\Microsoft.NET\Framework"):
        path = os.path.join(base, "v4.0.30319", "csc.exe")
        if os.path.isfile(path):
            return path
    return None


def refs_complete(folder):
    return all(os.path.isfile(os.path.join(folder, name + ".dll")) for name in REFS)


def find_refs(csc):
    checked = []
    env = os.environ.get("TIMETRACK_REFS")
    if env:
        checked.append(env)
    checked.append(os.path.join(ROOT, "build", "refs"))

    roots = [
        r"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework",
        r"C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework",
    ]
    for root in roots:
        if not os.path.isdir(root):
            continue
        versions = [d for d in os.listdir(root)
                    if os.path.isdir(os.path.join(root, d))]
        versions.sort(reverse=True)
        for version in versions:
            checked.append(os.path.join(root, version))

    for folder in checked:
        if os.path.isdir(folder) and refs_complete(folder):
            return folder
    return None


if not SOURCES:
    sys.exit("src 目录中没有 C# 源文件")
csc = find_csc()
if not csc:
    sys.exit("找不到 .NET Framework C# 编译器 csc.exe")
refs = find_refs(csc)
if not refs:
    sys.exit("找不到完整的 .NET Framework 4.x 引用程序集；"
             "请安装 .NET Framework 4.7.2 Developer Pack，"
             "或用 TIMETRACK_REFS 指向包含所需 DLL 的目录")

# The app cannot be replaced while it is running.
subprocess.run(["taskkill", "/IM", "TimeTrack.exe", "/F"],
               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

args = [
    csc,
    "/nologo",
    "/noconfig",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/out:" + OUT,
    "/win32icon:" + ICON,
    "/win32manifest:" + MANIFEST,
]
args += SOURCES
args += ["/r:" + os.path.join(refs, name + ".dll") for name in REFS]

print("编译器:", csc)
print("引用目录:", refs)
print("编译:", OUT)
result = subprocess.run(args)
if result.returncode != 0:
    sys.exit(result.returncode)

print("完成: %s (%.1f KB)" % (OUT, os.path.getsize(OUT) / 1024.0))
