# Memory Bloom contribution workflow

Apply this workflow to every user-requested feature or design change in this project.

Keep the two documentation audiences separate:

- `README.md` is the concise user- and portfolio-facing landing page. Show only the strongest representative compact and expanded images; do not turn it into an iteration log.
- `docs/HISTORY.md` is the exhaustive audit index and narrative. Every checkpoint also has a detailed `docs/history/<version>.md` record linking every retained screenshot, including user-supplied problem evidence.

1. Make the scoped change without overwriting unrelated user work.
2. Increase the application version for a functional checkpoint. A documentation-only reorganization does not require a new executable version or duplicate screenshots.
3. Build and verify with zero warnings where practical.
4. Run the built-in `--preview <folder>` capture and save the resulting images under `docs/screenshots/<version>/`.
5. Inspect the images and correct visible clipping, overlap, or regressions before delivery.
6. Update `CHANGELOG.md`, `docs/HISTORY.md`, and the applicable `docs/history/<version>.md`, including the last state, resulting state, next state, challenges, and screenshot links. Update `README.md` only with stable user-facing information.
7. Preserve a functional executable at `docs/releases/<version>/MemoryBloom.exe` for meaningful checkpoints.
8. Commit all changes belonging to the current user prompt as one descriptive Git commit. Do not include unrelated workspace changes.

When restarting the installed app during development, use `tools/restart-app.ps1 -Show` rather than launching `MemoryBloom.exe --show` directly. The helper starts the window passively and restores the previously focused window so validation does not leave a flashing taskbar button or interrupt the user's active work.

Firefox tab-to-process relationships are inferred rather than authoritative. Never fabricate a mapping: show unresolved tabs explicitly, retain confidence and observation age, and visually warn when a mapping is stale, uncertain, or has drifted.
