# Memory Bloom contribution workflow

Apply this workflow to every user-requested feature or design change in this project.

1. Make the scoped change without overwriting unrelated user work.
2. Increase the application version for a functional checkpoint.
3. Build and verify with zero warnings where practical.
4. Run the built-in `--preview <folder>` capture and save the resulting images under `docs/screenshots/<version>/`.
5. Inspect the images and correct visible clipping, overlap, or regressions before delivery.
6. Update `CHANGELOG.md` and `PROGRESS.md`, including the last state, resulting state, next state, and screenshot links.
7. Preserve a functional executable at `docs/releases/<version>/MemoryBloom.exe` for meaningful checkpoints.
8. Commit all changes belonging to the current user prompt as one descriptive Git commit. Do not include unrelated workspace changes.

When restarting the installed app during development, use `tools/restart-app.ps1 -Show` rather than launching `MemoryBloom.exe --show` directly. The helper starts the window passively and restores the previously focused window so validation does not leave a flashing taskbar button or interrupt the user's active work.

Firefox tab-to-process relationships are inferred rather than authoritative. Never fabricate a mapping: show unresolved tabs explicitly, retain confidence and observation age, and visually warn when a mapping is stale, uncertain, or has drifted.
