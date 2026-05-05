using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    /// <summary>
    /// Reflection helpers used by the dynamic SR2 integration layer.
    /// The mod avoids compile-time references to SR2 game classes here because the exported type
    /// signatures have been moving across game builds and IL2CPP generated assemblies.
    /// </summary>
    internal static class ReflectionTools
    {
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<(Type, string), MethodInfo> MethodCache = new Dictionary<(Type, string), MethodInfo>();

        public static Type TypeByName(params string[] names)
        {
            foreach (var requestedName in names)
            {
                foreach (var name in ExpandIl2CppTypeCandidates(requestedName))
                {
                    if (TypeCache.TryGetValue(name, out var cached) && cached != null)
                        return cached;

                    // Prefer direct assembly lookup. Harmony AccessTools.TypeByName scans all loaded
                    // assemblies and emits noisy ReflectionTypeLoadException spam against some Unity
                    // modules on Unity 6. Direct lookup is also what we want for Il2Cpp* wrappers.
                    var found = FindLoadedType(name);
                    if (found == null)
                        continue;

                    TypeCache[name] = found;
                    TypeCache[requestedName] = found;
                    return found;
                }
            }

            // Do not cache misses. Early boot can run before the generated Il2Cpp wrapper assembly
            // is available, and caching null made v0.5 permanently miss OptionsUIRoot/MapDirector.
            return null;
        }

        private static IEnumerable<string> ExpandIl2CppTypeCandidates(string name)
        {
            if (string.IsNullOrEmpty(name))
                yield break;

            yield return name;

            if (!name.StartsWith("Il2Cpp", StringComparison.Ordinal))
                yield return "Il2Cpp" + name;

            // Root-level game classes in generated wrappers often live under Il2Cpp.*
            // Example from old runtime probing: Il2Cpp.TeleportablePlayer.
            if (!name.Contains("."))
            {
                yield return "Il2Cpp." + name;
                yield return "Il2CppMonomiPark.SlimeRancher." + name;
                yield return "Il2CppMonomiPark.SlimeRancher.Map." + name;
            }

            if (name.StartsWith("MonomiPark.", StringComparison.Ordinal))
                yield return "Il2Cpp" + name;
        }

        public static MethodInfo Method(Type type, string name)
        {
            if (type == null)
                return null;

            var key = (type, name);
            if (MethodCache.TryGetValue(key, out var cached))
                return cached;

            var method = AccessTools.Method(type, name)
                         ?? type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                             .FirstOrDefault(m => m.Name == name);
            MethodCache[key] = method;
            return method;
        }

        public static object Call(object target, string methodName, params object[] args)
        {
            if (target == null)
                return null;

            var method = Method(target.GetType(), methodName);
            return method == null ? null : method.Invoke(target, args);
        }

        public static T Call<T>(object target, string methodName, params object[] args)
        {
            var value = Call(target, methodName, args);
            if (value == null)
                return default;

            if (value is T typed)
                return typed;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        public static object GetFieldOrProperty(object target, params string[] names)
        {
            if (target == null)
                return null;

            var type = target.GetType();
            foreach (var name in names)
            {
                var field = AccessTools.Field(type, name);
                if (field != null)
                    return field.GetValue(target);

                var prop = AccessTools.Property(type, name);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(target, null);
            }

            return null;
        }

        public static bool SetFieldOrProperty(object target, object value, params string[] names)
        {
            if (target == null)
                return false;

            var type = target.GetType();
            foreach (var name in names)
            {
                var field = AccessTools.Field(type, name);
                if (field != null && TryCoerce(value, field.FieldType, out var fieldValue))
                {
                    try
                    {
                        field.SetValue(target, fieldValue);
                        return true;
                    }
                    catch { }
                }

                var prop = AccessTools.Property(type, name);
                if (prop != null && prop.CanWrite && prop.GetIndexParameters().Length == 0 && TryCoerce(value, prop.PropertyType, out var propValue))
                {
                    try
                    {
                        prop.SetValue(target, propValue, null);
                        return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        private static bool TryCoerce(object value, Type targetType, out object coerced)
        {
            coerced = value;
            if (value == null)
                return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

            var valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType))
                return true;

            var nullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (nullableTarget.IsEnum && value is string enumName)
            {
                try
                {
                    coerced = Enum.Parse(nullableTarget, enumName);
                    return true;
                }
                catch { return false; }
            }

            if (typeof(IConvertible).IsAssignableFrom(nullableTarget) && value is IConvertible)
            {
                try
                {
                    coerced = Convert.ChangeType(value, nullableTarget);
                    return true;
                }
                catch { }
            }

            return false;
        }

        public static IEnumerable AsEnumerable(object obj)
        {
            return Enumerate(obj);
        }

        public static IEnumerable<object> Enumerate(object collection)
        {
            if (collection == null)
                yield break;

            if (collection is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    yield return item;
                yield break;
            }

            var type = collection.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var getEnumerator = type.GetMethod("GetEnumerator", flags, null, Type.EmptyTypes, null);
            if (getEnumerator != null)
            {
                object enumerator = null;
                try { enumerator = getEnumerator.Invoke(collection, null); }
                catch { }

                if (enumerator != null)
                {
                    var enumeratorType = enumerator.GetType();
                    var moveNext = enumeratorType.GetMethod("MoveNext", flags);
                    var currentProperty = enumeratorType.GetProperty("Current", flags);
                    if (moveNext != null && currentProperty != null)
                    {
                        while (true)
                        {
                            bool hasNext;
                            try { hasNext = moveNext.Invoke(enumerator, null) is bool b && b; }
                            catch { yield break; }

                            if (!hasNext)
                                yield break;

                            object current;
                            try { current = currentProperty.GetValue(enumerator, null); }
                            catch { yield break; }
                            yield return current;
                        }
                    }
                }
            }

            var count = GetCollectionCount(collection, type);
            var getItem = type.GetMethod("get_Item", flags, null, new[] { typeof(int) }, null);
            if (count < 0 || getItem == null)
                yield break;

            for (var i = 0; i < count; i++)
            {
                object item;
                try { item = getItem.Invoke(collection, new object[] { i }); }
                catch { yield break; }
                yield return item;
            }
        }

        private static int GetCollectionCount(object collection, Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "Count", "Length", "count", "_size", "size" })
            {
                try
                {
                    var prop = type.GetProperty(name, flags);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        var value = prop.GetValue(collection, null);
                        if (value is int pi)
                            return pi;
                    }
                }
                catch { }

                try
                {
                    var field = type.GetField(name, flags);
                    if (field != null)
                    {
                        var value = field.GetValue(collection);
                        if (value is int fi)
                            return fi;
                    }
                }
                catch { }
            }

            return -1;
        }

        public static IList FirstMutableListField(object target, Func<object, bool> itemPredicate)
        {
            foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetValue(target) is not IList list)
                    continue;

                if (list.Count == 0 || itemPredicate(list[0]))
                    return list;
            }

            return null;
        }

        public static void DumpObjectShape(string key, object target, int maxMembers = 40)
        {
            ReflectionDiagnostics.DumpObjectShape(key, target, maxMembers);
        }

        public static T FindFirstUnityObject<T>(Type type) where T : Object
        {
            var objects = Resources.FindObjectsOfTypeAll(Il2CppType.From(type));
            if (objects == null || objects.Length == 0)
                return null;

            return objects[0] as T;
        }

        public static object FindFirstUnityObject(Type type)
        {
            var objects = Resources.FindObjectsOfTypeAll(Il2CppType.From(type));
            return objects == null || objects.Length == 0 ? null : objects[0];
        }

        public static Type FindAssemblyType(string assemblyName, string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var simpleName = assembly.GetName().Name ?? string.Empty;
                if (!string.Equals(simpleName, assemblyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Type type = null;
                try { type = assembly.GetType(fullTypeName, false, false); }
                catch { }
                if (type != null)
                    return type;
            }

            return null;
        }

        public static object GetFieldOrPropertyQuiet(object target, params string[] names)
        {
            if (target == null)
                return null;

            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in names)
            {
                try
                {
                    var field = type.GetField(name, flags);
                    if (field != null)
                        return field.GetValue(target);
                }
                catch { }

                try
                {
                    var prop = type.GetProperty(name, flags);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                        return prop.GetValue(target, null);
                }
                catch { }
            }

            return null;
        }

        private static Type FindLoadedType(string name)
        {
            try
            {
                var qualified = Type.GetType(name, false, false);
                if (qualified != null)
                    return qualified;
            }
            catch { }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(name, false, false);
                }
                catch { }

                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
