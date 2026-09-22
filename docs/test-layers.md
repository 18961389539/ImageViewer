# Test Layers

ImageViewer tests use xUnit traits for CI selection:

- `Category=Smoke`: host construction, dependency composition, and WPF lifecycle checks.
- `Category=Unit`: deterministic service, controller, and state behavior.
- `Category=Integration`: workflows crossing multiple ImageViewer services.
- `Category=Wpf`: tests requiring the STA WPF dispatcher.
- `Category=Compatibility`: legacy API and compatibility-shim contracts.
- `Category=Performance`: behavior sentinels and focused performance checks; these do not impose timing thresholds on normal pull requests.

Smoke tests run separately in CI. The coverage run uses `Category!=Smoke` so the complete non-smoke test set remains visible without relying on class-name filters.