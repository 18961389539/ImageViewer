using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImageViewer.Models;
using Xunit;

namespace ImageViewerControl.Tests
{
    /// <summary>
    /// 模型层边界守卫。
    /// Chinese: 断言 ImageViewer.Models 命名空间下不出现任何 WPF UI 类型（控件、画刷、Dispatcher 等），
    /// 把"模型层不依赖 UI"从口头约定变成可执行的约束。
    /// English: Architectural guard for the model layer. Asserts that no WPF UI type (control, brush,
    /// Dispatcher, ...) appears in the ImageViewer.Models namespace, turning "the model layer must not
    /// depend on UI" into an executable constraint.
    /// </summary>
    public class ModelsNamespaceBoundaryTests
    {
        /// <summary>
        /// 被禁止的 WPF 类型（按完整类型名精确匹配）。
        /// Chinese: 精确匹配而非可赋值性匹配——因为 BitmapSource 派生自 ImageSource，但前者是像素数据载体、被允许。
        /// 几何值类型（Point / Vector / Rect）自 v2 起也在禁止之列：模型层统一使用 PointD / VectorD / RectD。
        /// English: Forbidden WPF types, matched by exact full name rather than assignability, because BitmapSource
        /// derives from ImageSource yet is an allowed pixel-data carrier. The geometry value types (Point / Vector /
        /// Rect) are forbidden as well since the model layer uses PointD / VectorD / RectD.
        /// </summary>
        private static readonly HashSet<string> ForbiddenWpfUiTypes = new(StringComparer.Ordinal)
        {
            "System.Windows.DependencyObject",
            "System.Windows.FrameworkElement",
            "System.Windows.FrameworkContentElement",
            "System.Windows.Window",
            "System.Windows.Thickness",
            "System.Windows.Visibility",
            "System.Windows.Point",
            "System.Windows.Vector",
            "System.Windows.Rect",
            "System.Windows.Controls.Control",
            "System.Windows.Controls.Panel",
            "System.Windows.Controls.Canvas",
            "System.Windows.Controls.TextBlock",
            "System.Windows.Controls.MenuItem",
            "System.Windows.Shapes.Shape",
            "System.Windows.Media.Brush",
            "System.Windows.Media.SolidColorBrush",
            "System.Windows.Media.ImageSource",
            "System.Windows.Media.DoubleCollection",
            "System.Windows.Threading.Dispatcher",
            "System.Windows.Threading.DispatcherObject"
        };

        private const string ModelsNamespace = "ImageViewer.Models";

        [Fact]
        public void ModelsNamespace_DoesNotReferenceWpfUiTypes()
        {
            Type[] modelTypes = GetModelTypes();

            var violations = new List<string>();

            foreach (Type type in modelTypes)
            {
                foreach (Type referenced in EnumerateReferencedTypes(type))
                {
                    if (referenced.FullName is string fullName && ForbiddenWpfUiTypes.Contains(fullName))
                    {
                        violations.Add($"{type.FullName} 引用了 {fullName}");
                    }
                }
            }

            Assert.Empty(violations);
        }

        /// <summary>
        /// 守卫自身的自检。
        /// Chinese: 确认扫描确实覆盖了模型层，且自有几何类型（PointD）确实被引用、WPF 几何类型（Point）确实不再出现——
        /// 否则守卫可能因为扫描失效而"永远通过"。
        /// English: Self-check for the guard. Confirms the scan actually covers the model layer and that the
        /// model-owned geometry type (PointD) is genuinely referenced while the WPF one (Point) is gone, so the
        /// guard cannot pass vacuously.
        /// </summary>
        [Fact]
        public void Guard_ActuallyScansModelTypesAndSeesModelOwnedGeometryTypes()
        {
            Type[] modelTypes = GetModelTypes();
            HashSet<string> referencedNames = modelTypes
                .SelectMany(EnumerateReferencedTypes)
                .Select(type => type.FullName ?? type.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(modelTypes);
            Assert.Contains(typeof(RoiBase).FullName, referencedNames.Select(_ => typeof(RoiBase).FullName));
            Assert.Contains("ImageViewer.Models.PointD", referencedNames);
            Assert.DoesNotContain("System.Windows.Point", referencedNames);
            Assert.Contains("System.Windows.Media.Imaging.BitmapSource", referencedNames);
            Assert.DoesNotContain("System.Windows.Media.Color", referencedNames);
        }

        private static Type[] GetModelTypes()
        {
            return typeof(RoiBase).Assembly
                .GetTypes()
                .Where(type => string.Equals(type.Namespace, ModelsNamespace, StringComparison.Ordinal))
                .ToArray();
        }

        private static IEnumerable<Type> EnumerateReferencedTypes(Type type)
        {
            const BindingFlags DeclaredMembers =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in type.GetFields(DeclaredMembers))
            {
                foreach (Type referenced in Unwrap(field.FieldType))
                {
                    yield return referenced;
                }
            }

            foreach (PropertyInfo property in type.GetProperties(DeclaredMembers))
            {
                foreach (Type referenced in Unwrap(property.PropertyType))
                {
                    yield return referenced;
                }

                foreach (ParameterInfo indexParameter in property.GetIndexParameters())
                {
                    foreach (Type referenced in Unwrap(indexParameter.ParameterType))
                    {
                        yield return referenced;
                    }
                }
            }

            foreach (MethodInfo method in type.GetMethods(DeclaredMembers))
            {
                foreach (Type referenced in Unwrap(method.ReturnType))
                {
                    yield return referenced;
                }

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    foreach (Type referenced in Unwrap(parameter.ParameterType))
                    {
                        yield return referenced;
                    }
                }
            }
        }

        private static IEnumerable<Type> Unwrap(Type type)
        {
            yield return type;

            if (type.IsArray && type.GetElementType() is Type elementType)
            {
                foreach (Type referenced in Unwrap(elementType))
                {
                    yield return referenced;
                }
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    foreach (Type referenced in Unwrap(argument))
                    {
                        yield return referenced;
                    }
                }
            }
        }
    }
}
