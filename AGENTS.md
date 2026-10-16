# AGENTS.md

Instructions for AI agent sessions working in this repository.

## Unity work goes through the Unity CLI

**Drive this project's Unity Editor with the CLI at `/Users/user/.unity/bin/unity`.**

The Editor is already running and holds `Temp/UnityLockfile`, so batchmode is unavailable — every
scene, asset, material, and script change is made against the **live Editor** through the Pipeline
package. Use the absolute path: `unity` is also on PATH in a login shell, but the absolute path keeps
working in a shell, subagent, or harness that did not inherit it.

```bash
/Users/user/.unity/bin/unity status                  # confirm the live Editor; want state "ready"
/Users/user/.unity/bin/unity list                    # the commands this Editor exposes
/Users/user/.unity/bin/unity command <name> --json   # run one
```

The live Editor owns `Assets/**` — scenes, `.mat`, `.prefab`, `.asset`, shaders. Make changes through
the CLI so Unity applies them, imports them, and reports the outcome in its console. Hand-editing
that YAML bypasses every import step, and the damage surfaces later and somewhere else.

Put multi-line C# in a file **outside `Assets/`** (e.g. `AgentScripts/`) so editing it triggers no
asset import and no domain reload, then run the entry point:

```bash
/Users/user/.unity/bin/unity command run_script --file AgentScripts/X.cs --entry X.Y
```

Keep `eval` and `eval_file` for genuine one-liners.

### Verifying a render — the completion criterion

A capture that disagrees with reality is the *normal* failure mode here, not an unusual one. Treat a
screenshot as evidence only once you have shown it is fresh:

1. Make a change you know must be visible.
2. `/Users/user/.unity/bin/unity command editor_focus` — the Editor redraws the Game view only while
   it is the frontmost app. Unfocused, it keeps returning the last frame it drew: byte-identical, no
   error, nothing in the image to give it away. Material changes flush a redraw by themselves, so
   shader tuning *appears* to work while **scene** edits (transform, `SetActive`, scale) silently do
   not — which is how "this change does nothing" gets believed for hours. `set_autotick` does **not**
   fix this.
3. Capture and confirm it **differs** from the previous state. Two stale frames agree perfectly, so
   "I captured twice and they match" proves nothing on its own.
4. `/Users/user/.unity/bin/unity command console --json` — a shader that failed to compile, or a
   main-thread operation that timed out, leaves its error here and nowhere else.

Do not report a visual change as done until step 3 shows a difference attributable to that change.

### CLI traps

- **Captures are forced under `Assets/`.** A `save_path` of `Temp/x.png` is written to
  `Assets/Temp/x.png` and imported as a texture. Copy the image out to the gitignored project-root
  `Temp/`, then delete `Assets/Temp` **and** `Assets/Temp.meta`. `AssetDatabase.DeleteAsset` can time
  out on the main thread; a verified `rm -rf` followed by a refresh is fine.
- **A shader edit is not a C# recompile, and a capture can render the previously compiled variant.**
  Force the import before capturing:
  `AssetDatabase.ImportAsset(path, ForceUpdate | ForceSynchronousImport)`. Related: a
  `new Material(shader)` built against a *stale* shader bakes in the old declared defaults, which
  looks exactly like "my tuning did nothing" even though the file on disk is correct.
- **`recompile_status` can still report `completed` from the previous compile** when polled straight
  after `recompile`. Wait for it to pass through `compiling`, or sleep first, before trusting it.
- **Every command accepts `--project-path`.** Pass it when more than one Editor may be open; without
  it the CLI targets the Editor that owns the current directory.

## Source reference video is on disk — refer to it, do not download it

Parts of this game are matched against an external video. **The copy is already local.** Check
`.source_reference/` before reaching for the network: re-downloading a 237 MiB file that is sitting
right there wastes minutes and will silently succeed, so nothing will tell you it was pointless.

| reference | local copy |
|---|---|
| `youtu.be/OU3B3D-52us` — *Remaking My First Game! \| Devlog #1* (Mythic Legion) | `.source_reference/reference_video_OU3B3D-52us_1080p60.mp4` |

`.source_reference/README.md` is the full manifest — resolution, duration, `sha256`, provenance.
That directory is **gitignored**, so it exists on a machine that has fetched it and not in a fresh
clone; if the table's file is absent, see the fallback at the end of this section.

Pull frames straight out of the local file (1920x1080 beats any screenshot of the same shot):

```bash
ffmpeg -ss 00:12:12 -i .source_reference/reference_video_OU3B3D-52us_1080p60.mp4 -frames:v 1 frame.png
```

**Fallback only if the file is genuinely missing.** yt-dlp is **not vendored in this repo** — use one
on `PATH`, or install it (`pipx install yt-dlp`). The system `python3` is 3.9, below yt-dlp's >=3.10
floor, so `python3 -m yt_dlp` exits with an `ImportError`; use a newer interpreter or a standalone
binary:

```bash
yt-dlp -f "bv*[ext=mp4][vcodec^=avc1]+ba[ext=m4a]/b[ext=mp4]" --merge-output-format mp4 \
  -o ".source_reference/reference_video_OU3B3D-52us_1080p60.mp4" \
  "https://www.youtube.com/watch?v=OU3B3D-52us"
```

Two traps that make a re-download look fine when it isn't: **1080p60 is this video's ceiling** (no
1440p/2160p tier exists, so nothing is gained by trying), and **`deno` is not installed**, so yt-dlp
runs YouTube's deprecated no-JS path — it prints a warning and can return *fewer formats* rather
than failing loudly. Confirm any fresh copy against the `sha256` in the manifest.
