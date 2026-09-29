# Architecture Boundaries

The WPF control remains the delivery surface. Modernization separates new code by dependency direction instead of moving existing WPF-bound types in one disruptive change.

## Dependency Rule

`ImageViewerControl` may depend on WPF, the rendering backend, and `ImageViewer.Core`. `ImageViewer.Core` targets `net10.0` and must not reference `System.Windows`, `System.Windows.Media`, `Dispatcher`, or WPF bitmap types.

## Code Placement

- Put UI controls, bindings, dialogs, dispatcher scheduling, bitmap decoding, and render frames in the WPF project.
- Put new immutable data contracts, geometry algorithms using primitive coordinates, serialization formats, import/export schemas, and cancellation-aware analysis orchestration in `ImageViewer.Core`.
- Keep adapters at the WPF edge: convert `Point`, `Color`, and `BitmapSource` into core contracts before calling core services.

The first migrated slices are measurement statistics, geometry values, and persistence schemas. `ImageViewer.Core.Measurements.MeasurementStatistics` owns filtering, sorting, and aggregate calculations for dual-edge width samples. `ImageViewer.Core.Geometry.Geometry2D` owns framework-neutral distance, polygon, angle, centroid, and bounding-box operations. `PointD`, `RectD`, and `VectorD` are defined by Core; the WPF assembly keeps type-forwarders so existing consumers continue to resolve the old model type names. `ImageViewerControl.Models.CaliperWidthMeasurementResult` remains the compatibility-facing adapter and delegates those rules to Core. Session envelopes, ROI documents, camera calibration, and their source-generated JSON metadata now live in `ImageViewer.Core.Persistence`/`ImageViewer.Core` while the WPF session service retains file and plugin orchestration.

Volume orientation and slice provenance (`VolumeSliceOrientation`, `VolumeSliceProvenance`, and `VolumeSliceOrderMetadata`) are also framework-neutral Core contracts. `VolumeData`, bitmap reconstruction, 3D scene models, and MPR scheduling remain WPF adapters around those contracts.

Analysis execution is represented by `ImageViewer.Core.Analysis.IAnalysisStage<TContext>` and `IAnalysisPipeline<TContext>`. The WPF adapter composes histogram and profile stages through that contract; bitmap access and render services stay at the edge.

Caliper detection result contracts (`CircularCaliperDetectionResult`, `LineCaliperDetectionResult`, and `LineMeasureGradientDetectionResult`) now use `PointD` and Core line-fit geometry. Legacy WPF result records remain conversion shims for existing callers; production mapping and rendering consume the Core contracts.

The 2D/3D/MPR boundary is owned by `AdaptiveImageViewer`: it selects the active surface, coordinates slice and plane selection, and cancels stale MPR requests. `VolumeSliceService` keeps normalized intensity planes and a bounded per-volume LRU slice cache, while `ImageViewerRenderTileCache` provides a bounded LRU tile cache for large 2D frames. Both caches are invalidated when their source or render generation changes, so background work cannot publish stale pixels.

Command controllers depend on capability interfaces. View options, viewport operations, image transforms, analysis options, analysis operations, drawing modes, ROI selection/editing, file operations, and feature analysis/export/feedback are composed by small adapters. The command composition root no longer constructs large delegate bags; each controller receives only the capabilities it uses.

ROI plugins expose capability interfaces (`IRoiPluginDefinition`, `IRoiCollectionCapability`, `IRoiPersistenceCapability`, `IRoiInfoCapability`, and `IRoiEditorCapability`). `IRoiPlugin` remains the compatibility aggregate used by the registry while consumers can depend on the smallest capability they need.

## Migration Order

1. Define a framework-neutral contract for each new capability.
2. Add the implementation and deterministic tests in the core project.
3. Add a WPF adapter and preserve the public WPF API.
4. Move an existing capability only after its WPF dependency has been isolated behind that adapter.

Existing ROI classes use WPF coordinate and color types, so they remain in `ImageViewerControl` until a compatibility-preserving contract is introduced. This keeps release risk low while making new functionality portable and directly testable.
