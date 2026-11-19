# FRExplorer working rules

- Keep FRExplorer independent from OneCrypt and from any consuming application.
- Public documentation must describe the current reusable component, its API,
  build, tests and contribution workflow. Keep extraction notes and private
  product plans outside this repository.
- Shell enumeration must never run on the UI thread. Cancellation may discard a
  stale result, but it must not leave partial nodes or list entries visible.
- Keep authoritative defaults and behavioral contracts beside their public
  properties. Update tests and `docs/api.md` when those contracts change.
- Do not commit credentials, signing keys, user paths or machine-specific data.

