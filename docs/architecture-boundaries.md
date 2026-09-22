# Architecture Boundaries

The WPF control remains the delivery surface. Modernization separates new code by dependency direction instead of moving existing WPF-bound types in one disruptive change.

## Dependency Rule

`ImageViewerControl` may depend on WPF, the rendering backend, and the future core libraries. A future `ImageViewer.Core` project must only target `net10.0` and must not reference `System.Windows`, `System.Windows.Media`, `Dispatcher`, or WPF bitmap types.

## Code Placement

- Put UI controls, bindings, dialogs, dispatcher scheduling, bitmap decoding, and render frames in the WPF project.
- Put new immutable data contracts, geometry algorithms using primitive coordinates, serialization formats, import/export schemas, and cancellation-aware analysis orchestration in `ImageViewer.Core`.
- Keep adapters at the WPF edge: convert `Point`, `Color`, and `BitmapSource` into core contracts before calling core services.

## Migration Order

1. Define a framework-neutral contract for each new capability.
2. Add the implementation and deterministic tests in the core project.
3. Add a WPF adapter and preserve the public WPF API.
4. Move an existing capability only after its WPF dependency has been isolated behind that adapter.

Existing ROI classes use WPF coordinate and color types, so they remain in `ImageViewerControl` until a compatibility-preserving contract is introduced. This keeps release risk low while making new functionality portable and directly testable.