# 项目文件格式说明

## 概览

ImageViewer 当前有三类主要持久化文件：

- `.json`：ROI 集合导出文件。
- `.ivsession`：会话文件，记录图像路径、ROI 文档和当前视图状态。
- `.ivpkg`：项目包，实质上是一个 ZIP 容器，包含会话文件和可选图像资产。

## ROI JSON

ROI JSON 由 `RoiPersistenceService` 负责读写，顶层结构如下：

```json
{
  "Version": 1,
  "PixelSize": 0.5,
  "PhysicalUnit": "mm",
  "Items": [
    {
      "Type": "circle",
      "Label": "孔位 A",
      "StrokeColor": "#FF00FFFF",
      "StrokeThickness": 2.0,
      "IsVisible": true,
      "IsLocked": false,
      "Center": { "X": 120.0, "Y": 84.0 },
      "Radius": 15.0
    }
  ]
}
```

字段说明：

- `Version`：当前固定为 `1`。读取时会校验：高于当前支持版本的文件会被拒绝加载；缺失或非正数视为早期文件，宽容接受。
- `PixelSize`：像素尺寸；读取时如果小于等于 `0`，会回退到 `1.0`。
- `PhysicalUnit`：物理单位；空值会回退到 `px`。
- `Items`：ROI 项数组。

### ROI 项约定

`Items` 中的单个对象对外仍保持扁平字段结构，以兼容历史文件；在代码内部已经映射到四组语义数据：

- `Common`：标签、颜色、线宽、可见性、锁定状态。
- `Geometry`：点位、中心、宽高、半径、角度等几何参数。
- `Measurement`：卡尺数量、搜索范围、采样宽度、最小梯度等测量参数。
- `Options`：闭合、多边形自由绘制、阈值、暗目标检测等行为参数。

## `.ivsession` 会话文件

`.ivsession` 是一个 JSON 文件，由 `ImageViewerSessionService` 生成。其概念结构如下：

```json
{
  "Version": 2,
  "SessionName": "sample-session",
  "SavedAtUtc": "2025-05-19T08:30:00+00:00",
  "ImagePath": "images/sample.png",
  "RoiDocument": {
    "Version": 1,
    "PixelSize": 0.5,
    "PhysicalUnit": "mm",
    "Items": []
  },
  "Scale": 1.25,
  "TranslateX": 24.0,
  "TranslateY": -12.0
}
```

字段说明：

- `Version`：会话"信封"结构版本，当前为 `2`。读取时会校验：高于当前支持版本的文件会被拒绝加载；缺失或非正数视为早期文件，宽容接受。
  - `1`：ROI 载荷以转义字符串形式内嵌在 `RoiDocumentJson` 字段中（历史格式）。
  - `2`：ROI 载荷作为嵌套对象写在 `RoiDocument` 字段中（当前格式）。
- `SessionName`：默认使用文件名。
- `SavedAtUtc`：保存时间戳。
- `ImagePath`：图像路径，可为绝对路径，也可为相对会话文件所在目录的相对路径。
- `RoiDocument`：完整的 ROI 文档对象，结构与 `.json` ROI 文件一致（含自己的 `Version`）。
- `Scale`、`TranslateX`、`TranslateY`：保存当前视图缩放和平移状态。

## `.ivpkg` 项目包

`.ivpkg` 本质上是 ZIP 包，由 `ImageViewerProjectPackageService` 读写。包内至少包含：

```text
session.ivsession
assets/<original-image-file>
```

规则如下：

- `session.ivsession` 是必需项。
- 如果导出时图像路径存在且文件可访问，会把原始图像复制到 `assets/` 目录中。
- 会话文件中的 `ImagePath` 会改写为包内相对路径，例如 `assets/sample.png`。
- 加载 `.ivpkg` 时，资产会解压到本机缓存目录，再按相对路径解析图像。
- 导出会先写入同目录临时文件，成功完成后才替换目标包；取消或失败不会留下半成品目标文件。
- 为避免恶意或损坏归档耗尽资源，加载时最多接受 1,024 个条目，单个条目解压后不超过 256 MiB，全部条目解压后合计不超过 512 MiB。

缓存目录模式：

```text
%LocalAppData%/ImageViewer/PackageCache/<package-name>-<hash>
```

## 向后兼容性

- ROI JSON 读取时同时支持 `Type` 对应插件类型键，以及旧格式中按 ROI 类型名匹配。
- ROI 项字段继续保持扁平 JSON 形态，以兼容既有导出文件。
- 未识别的 ROI 插件项在加载时跳过，而不是导致整份文件失败；其原始载荷会被原样保留并随下次保存写回，因此缺少插件不再等于标注丢失（加载时会给出状态栏提示）。
- 会话文件的 ROI 载荷读取时同时接受嵌套对象（当前格式）与转义字符串（历史格式），字段名同时接受 `RoiDocument` 与 `RoiDocumentJson`；写出时统一使用嵌套对象。
- 版本号只提供单向保护：新程序可以读旧文件，但旧程序读不了新文件（旧程序遇到缺失的 `RoiDocumentJson` 会报解析错误）。因此不要用降级后的程序去打开升级后保存的会话。