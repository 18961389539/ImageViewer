# 直线拟合结果定义

直线卡尺的几何结果统一使用源图像像素中心坐标：整数坐标表示像素采样中心，坐标允许亚像素值。当前定义与显示缩放、WPF 像素对齐和导出单位无关。

## 两类线段

- `FittedLine`、`FittedEdge1`、`FittedEdge2`：参与拟合的边缘点集合的有限支撑段。它们只表示拟合点覆盖的起止范围。
- 单边缘的 `DetectedP1/DetectedP2`：将原始 ROI 两个端点正交投影到拟合无限直线后的结果段。
- 双边缘的 `DetectedP1/DetectedP2`：测量中心线分别与两条拟合无限直线的交点，结果段长度就是中心线方向的测量宽度。

拟合时可以按 HALCON `fit_line_contour_xld` 的规则从轮廓首尾各裁剪指定数量的点。裁剪点只影响拟合，不影响输出端点；输出端点始终是原始轮廓首点和末点投影到拟合无限直线后的结果。裁剪数量为 0 时使用全部点。

## 无限拟合直线

`LineFitGeometry` 由以下值组成：

```text
Normal.X * X + Normal.Y * Y = Offset
```

其中 `Normal` 是单位法向量，方向固定为 X 分量优先为正；接近垂直时 Y 分量为正。这样端点顺序改变不会改变同一条无向直线的偏移量符号。`AngleDegrees` 是无向切线角，归一化到 `[-90, 90)`。

`ResidualRms` 和 `ResidualMax` 始终表示参与拟合点到该无限直线的正交距离，单位为源图像像素；它们不包含显示缩放，也不包含像素尺寸换算。

## 代码入口

- `ImageViewerControl/Models/LineFitGeometry.cs`：无限直线、投影和距离计算。
- `LineCaliperDetectionResult.FittedGeometry`：单边缘拟合直线。
- `LineMeasureGradientDetectionResult.FittedEdge1Geometry` / `FittedEdge2Geometry`：双边缘两条拟合直线。
- `FittingAlgorithmMetadata.LineGeometry`：导出元数据中的结果定义版本。

边缘提取模式和直线拟合模式分别配置。`HalconEdgeExtractionMode` 提供 GaussianDerivative（兼容默认）、Deriche、Lanser1、Lanser2、Mshen、Canny、SobelFast 和 Sobel；`HalconLineFitMode` 提供 regression、huber、tukey、drop 和 gauss。新增模式是 HALCON 风格的剖面/权重实现，默认值保持现有 Tukey + GaussianDerivative 结果。
