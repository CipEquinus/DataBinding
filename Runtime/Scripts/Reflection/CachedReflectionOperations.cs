using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace JH.DataBinding
{
  public readonly struct CachedReflectionOperations : IReflectionOperations
  {
    private enum Init
    {
      Init = 0
    }

    public static readonly CachedReflectionOperations Instance = new CachedReflectionOperations(Init.Init);

    private readonly DirectReflectionOperations reflectionOperations;
    private readonly ConcurrentDictionary<(Type, string), PropertyInfo> propertyNameCache;
    private readonly ConcurrentDictionary<(Type, string, PathResolveOperation), MethodInfo[]> propertyPathCache;
    private readonly ConcurrentDictionary<(Type, Type), bool> inheritanceCache;
    private readonly ConcurrentDictionary<Type, string> nameCache;

    private CachedReflectionOperations(Init _)
    {
      reflectionOperations = new();
      propertyNameCache = new();
      propertyPathCache = new();
      inheritanceCache = new();
      nameCache = new();
    }

    public PropertyInfo FindProperty(Type type, string name)
    {
      (Type, string) key = (type, name);
      if (!propertyNameCache.TryGetValue(key, out var propertyInfo))
      {
        propertyInfo = reflectionOperations.FindProperty(type, name);
        propertyNameCache[key] = propertyInfo;
      }

      return propertyInfo;
    }

    public TValueType ResolveInstanceMember<TValueType>(object instance, string path)
    {
      return reflectionOperations.ResolveInstanceMember<TValueType>(instance, path);
    }

    public string GetFriendlyName(Type type)
    {
      if (type == null)
      {
        return ReflectionExtensions.GetFriendlyName(this, type);
      }

      if (!nameCache.TryGetValue(type, out var name))
      {
        name = ReflectionExtensions.GetFriendlyName(this, type);
        nameCache[type] = name;
      }

      return name;
    }

    public MethodInfo[] ResolvePublicPropertyPath(object instance, string path, PathResolveOperation operation)
    {
      if (instance == null)
      {
        throw new ArgumentNullException(nameof(instance));
      }

      if (path == null)
      {
        throw new ArgumentNullException(nameof(path));
      }

      (Type, string, PathResolveOperation) key = (instance.GetType(), path, operation);
      if (!propertyPathCache.TryGetValue(key, out var accessors))
      {
        accessors = ReflectionExtensions.ResolvePublicPropertyPath(this, instance, path, operation);
        propertyPathCache[key] = accessors;
      }

      return accessors;
    }

    public object InvokeGetAccessChain(MethodInfo[] accessors, object instance)
    {
      return reflectionOperations.InvokeGetAccessChain(accessors, instance);
    }

    public void InvokeSetAccessChain(MethodInfo[] accessors, object instance, params object[] values)
    {
      reflectionOperations.InvokeSetAccessChain(accessors, instance, values);
    }

    public bool InheritsOrImplements(Type type, Type baseType)
    {
      (Type, Type) key = (type, baseType);
      if (!inheritanceCache.TryGetValue(key, out var inheritsOrImplements))
      {
        inheritsOrImplements = reflectionOperations.InheritsOrImplements(type, baseType);
        inheritanceCache[key] = inheritsOrImplements;
      }

      return inheritsOrImplements;
    }
  }
}
