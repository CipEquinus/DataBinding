using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JH.DataBinding
{
  public readonly struct DirectReflectionOperations : IReflectionOperations
  {
    public static readonly DirectReflectionOperations Instance = new();

    public PropertyInfo FindProperty(Type type, string name)
    {
      return Array.Find(type.GetProperties(), (x) => x.Name == name);
    }

    public TValueType ResolveInstanceMember<TValueType>(object instance, string path)
    {
      if (instance == null)
      {
        return default;
      }

      var pathFragments = path.Split('.');
      var fragmentInstance = instance;

      foreach (var fragmentPath in pathFragments.Take(pathFragments.Length - 1))
      {
        if (fragmentInstance == null)
        {
          throw new InvalidOperationException(
            string.Format(
              "Binding path {0} does not match dataSource of type {1}",
              path,
              instance.GetType().Name
            )
          );
        }

        var memberInfo = fragmentInstance.GetType().GetMember(fragmentPath, BindingFlags.Instance);

        fragmentInstance = memberInfo.GetValue(0);
      }

      return (TValueType)
        fragmentInstance
          .GetType()
          .GetMember(pathFragments.Last(), BindingFlags.Instance)
          .GetValue(0);
    }

    public string GetFriendlyName(Type type)
    {
      return ReflectionExtensions.GetFriendlyName(this, type);
    }

    public IEnumerable<MethodInfo> ResolvePublicPropertyPath(
      object instance,
      string path,
      PathResolveOperation operation
    )
    {
      return ReflectionExtensions.ResolvePublicPropertyPath(this, instance, path, operation);
    }

    public void SyncValue(
      object source,
      object target,
      MethodInfo[] getAccessors,
      MethodInfo[] setAccessors
    )
    {
      IReflectionOperations.SyncValue(this, source, target, getAccessors, setAccessors);
    }

    public object InvokeGetAccessChain(MethodInfo[] accessors, object instance)
    {
      if (accessors.Length == 0)
      {
        throw new ArgumentException(
          "Accessors array must contain at least one element.",
          nameof(accessors)
        );
      }

      if (instance == null)
      {
        return null;
      }

      var currentInstance = instance;

      foreach (var getter in accessors.Take(accessors.Length - 1))
      {
        currentInstance = getter.Invoke(currentInstance, null);
      }

      return accessors[^1].Invoke(currentInstance, null);
    }

    public void InvokeSetAccessChain(
      MethodInfo[] accessors,
      object instance,
      params object[] values
    )
    {
      if (accessors.Length == 0)
      {
        throw new ArgumentException(
          "Accessors array must contain at least one element.",
          nameof(accessors)
        );
      }

      if (instance == null)
      {
        throw new ArgumentNullException(nameof(instance));
      }

      var currentInstance = instance;

      foreach (var getter in accessors.Take(accessors.Length - 1))
      {
        currentInstance = getter.Invoke(currentInstance, null);
      }

      var setter = accessors[^1];
      setter.Invoke(currentInstance, values);
    }

    public bool InheritsOrImplements(Type type, Type baseType)
    {
      baseType = ResolveGenericTypeDefinition(baseType);

      var currentDerived = type.IsGenericType ? type.GetGenericTypeDefinition() : type;

      while (currentDerived != typeof(object))
      {
        if (
          baseType == currentDerived
          || ImplementsGenericInterfaceDefinition(currentDerived, baseType)
        )
        {
          return true;
        }

        currentDerived =
          currentDerived.BaseType != null && currentDerived.BaseType.IsGenericType
            ? currentDerived.BaseType.GetGenericTypeDefinition()
            : currentDerived.BaseType;

        if (currentDerived == null)
        {
          return false;
        }
      }
      return false;
    }

    private static bool ImplementsGenericInterfaceDefinition(Type type, Type interfaceType)
    {
      foreach (var implementedInterface in type.GetInterfaces())
      {
        var candidate = implementedInterface.IsGenericType
          ? implementedInterface.GetGenericTypeDefinition()
          : implementedInterface;

        if (candidate == interfaceType)
          return true;
      }

      return false;
    }

    private static Type ResolveGenericTypeDefinition(Type type)
    {
      if (type is null)
      {
        throw new ArgumentNullException(nameof(type), "Given type must not be null.");
      }

      return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }
  }
}
