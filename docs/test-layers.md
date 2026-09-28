# Test Layers

ImageViewer tests use xUnit traits for CI selection:

- `Category=Smoke`: host construction, dependency composition, and WPF lifecycle checks.
- `Category=Unit`: deterministic service, controller, and state behavior.
- `Category=Integration`: workflows crossing multiple ImageViewer services.
- `Category=Wpf`: tests requiring the STA WPF dispatcher.
- `Category=Compatibility`: legacy API and compatibility-shim contracts.
- `Category=Performance`: behavior sentinels and focused performance checks; these do not impose timing thresholds on normal pull requests.

`ImageViewer.Core.Tests` is a separate `net10.0` test project. It can run without WPF and is the first coverage-gated layer. The desktop test project continues to cover adapters, rendering, dialogs, and WPF lifecycle behavior.

Smoke tests run separately in CI. The coverage run uses `Category!=Smoke` so the complete non-smoke test set remains visible without relying on class-name filters.

The GitHub Actions workflow also runs a focused export/batch gate before the full regression gate. Full regression failures block the workflow, while the raw test and coverage artifacts are still uploaded for diagnosis.
