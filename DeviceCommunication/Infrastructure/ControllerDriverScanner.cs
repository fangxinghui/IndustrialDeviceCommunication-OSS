using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// 扫描 [ControllerDriver] 标注的驱动类，建立 (品牌, 型号, 类型) → 驱动 Type 注册表。
    /// </summary>
    public static class ControllerDriverScanner
    {
        public sealed class DriverInfo
        {
            public string Brand { get; }
            public string Model { get; }
            public ControllerType ControllerType { get; }
            public Type DriverType { get; }
            public bool IsDefault { get; }

            public DriverInfo(string brand, string model, ControllerType controllerType, Type driverType, bool isDefault)
            {
                Brand = brand;
                Model = model;
                ControllerType = controllerType;
                DriverType = driverType;
                IsDefault = isDefault;
            }
        }

        private static readonly object Lock = new object();
        private static bool _scanned;
        private static readonly List<DriverInfo> Drivers = new List<DriverInfo>();

        public static void EnsureScanned()
        {
            if (_scanned) return;
            lock (Lock)
            {
                if (_scanned) return;
                ScanAssemblies();
                _scanned = true;
            }
        }

        /// <summary>
        /// 根据品牌、型号、控制器类型查找驱动类型（大小写不敏感；品牌可省略，仅按型号匹配）。
        /// </summary>
        public static Type? FindDriverType(string? brand, string? model, ControllerType controllerType)
        {
            EnsureScanned();
            if (string.IsNullOrWhiteSpace(model))
                return FindDefaultDriverType(controllerType);

            lock (Lock)
            {
                var normalizedModel = Normalize(model);
                var normalizedBrand = NormalizeBrand(brand);

                var exactBrandAndModel = Drivers.FirstOrDefault(d =>
                    d.ControllerType == controllerType &&
                    ModelEquals(d.Model, normalizedModel) &&
                    BrandEquals(d.Brand, normalizedBrand));

                if (exactBrandAndModel != null)
                    return exactBrandAndModel.DriverType;

                var exactBrandWildcardModel = Drivers.FirstOrDefault(d =>
                    d.ControllerType == controllerType &&
                    IsWildcard(d.Model) &&
                    BrandEquals(d.Brand, normalizedBrand));

                if (exactBrandWildcardModel != null)
                    return exactBrandWildcardModel.DriverType;

                var wildcardBrandExactModel = Drivers.FirstOrDefault(d =>
                    d.ControllerType == controllerType &&
                    !d.IsDefault &&
                    IsWildcard(d.Brand) &&
                    ModelEquals(d.Model, normalizedModel));

                return wildcardBrandExactModel?.DriverType ?? FindDefaultDriverType(controllerType);
            }
        }

        /// <summary>
        /// 根据品牌与型号推断 ControllerType。
        /// </summary>
        public static ControllerType? ResolveControllerType(string? brand, string? model)
        {
            EnsureScanned();
            if (string.IsNullOrWhiteSpace(model))
                return null;

            lock (Lock)
            {
                var normalizedModel = Normalize(model);
                var normalizedBrand = NormalizeBrand(brand);

                var exactBrandAndModel = Drivers.FirstOrDefault(d =>
                    !d.IsDefault &&
                    ModelEquals(d.Model, normalizedModel) &&
                    BrandEquals(d.Brand, normalizedBrand));

                if (exactBrandAndModel != null)
                    return exactBrandAndModel.ControllerType;

                var exactBrandWildcardModel = Drivers.FirstOrDefault(d =>
                    !d.IsDefault &&
                    IsWildcard(d.Model) &&
                    BrandEquals(d.Brand, normalizedBrand));

                if (exactBrandWildcardModel != null)
                    return exactBrandWildcardModel.ControllerType;

                var wildcardBrandExactModel = Drivers.FirstOrDefault(d =>
                    !d.IsDefault &&
                    IsWildcard(d.Brand) &&
                    ModelEquals(d.Model, normalizedModel));

                return wildcardBrandExactModel?.ControllerType;
            }
        }

        public static IController? TryCreate(string? brand, string? model, ControllerType controllerType,
            ControllerConfiguration configuration)
        {
            var driverType = FindDriverType(brand, model, controllerType);
            if (driverType == null)
                return null;

            return Activator.CreateInstance(driverType, configuration) as IController;
        }

        private static Type? FindDefaultDriverType(ControllerType controllerType)
        {
            return Drivers.FirstOrDefault(d => d.ControllerType == controllerType && d.IsDefault)?.DriverType;
        }

        private static void ScanAssemblies()
        {
            Drivers.Clear();
            var scannedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ScanAssemblyOnce(typeof(ControllerDriverScanner).Assembly, scannedAssemblies);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                var name = assembly.FullName ?? string.Empty;
                if (name.StartsWith("System.", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.", StringComparison.Ordinal))
                    continue;

                try
                {
                    ScanAssemblyOnce(assembly, scannedAssemblies);
                }
                catch
                {
                    // 忽略无法扫描的程序集
                }
            }
        }

        private static void ScanAssemblyOnce(Assembly assembly, ISet<string> scannedAssemblies)
        {
            var name = assembly.FullName ?? assembly.GetName().Name ?? string.Empty;
            if (!scannedAssemblies.Add(name))
                return;

            ScanAssembly(assembly);
        }

        private static void ScanAssembly(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }
            catch
            {
                return;
            }

            foreach (var type in types)
            {
                if (!type.IsClass || type.IsAbstract || !typeof(IController).IsAssignableFrom(type))
                    continue;

                if (!HasValidConstructor(type))
                    continue;

                foreach (var attr in type.GetCustomAttributes<ControllerDriverAttribute>(inherit: false))
                {
                    Drivers.Add(new DriverInfo(
                        attr.Brand,
                        attr.Model,
                        attr.ControllerType,
                        type,
                        attr.IsDefault));
                }
            }
        }

        private static bool HasValidConstructor(Type type)
        {
            return type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(c =>
                {
                    var parameters = c.GetParameters();
                    return parameters.Length == 1 &&
                           parameters[0].ParameterType == typeof(ControllerConfiguration);
                });
        }

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();

        private static string? NormalizeBrand(string? brand)
        {
            if (string.IsNullOrWhiteSpace(brand))
                return null;
            return Normalize(brand);
        }

        private static bool BrandEquals(string driverBrand, string? requestBrand)
        {
            if (requestBrand == null || IsWildcard(driverBrand))
                return false;

            return string.Equals(Normalize(driverBrand), requestBrand, StringComparison.Ordinal);
        }

        private static bool ModelEquals(string driverModel, string requestModel)
        {
            if (IsWildcard(driverModel))
                return false;
            return string.Equals(Normalize(driverModel), requestModel, StringComparison.Ordinal);
        }

        private static bool IsWildcard(string value)
            => string.Equals(value?.Trim(), "*", StringComparison.Ordinal);
    }
}
