# bdwgc (Boehm-Demers-Weiser garbage collector)

Version 8.2.8, from https://github.com/ivmai/bdwgc/releases/tag/v8.2.8 — the conservative garbage
collector used by the native runtime for objects of translated C# code.

Only the files needed to build it as one translation unit are kept: compile `extra/gc.c` with
`include/` on the include path. License: see `LICENSE.txt` (a permissive X11-style license);
authors in `AUTHORS`.
