using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JH.DataBinding
{
  public static class ReflectionExtensions
  {
    public static string GetFriendlyName<T>(this T reflectionOperations, Type type)
      where T : struct, IReflectionOperations
    {
      if (type == null)
      {
        return "None";
      }

      string friendlyName = TypeAliases.TryGetValue(type, out var alias) ? alias : type.Name;

      if (type.IsGenericType)
      {
        friendlyName = friendlyName.Split('`')[0];

        friendlyName += "<";

        Type[] typeParameters = type.GetGenericArguments();

        for (int i = 0; i < typeParameters.Length; ++i)
        {
          Type typeParam = typeParameters[i];
          string typeParamName = TypeAliases.TryGetValue(typeParam, out alias)
            ? alias
            : typeParam.Name;
          friendlyName += (i == 0 ? typeParamName : "," + typeParamName);
        }

        friendlyName += ">";
      }

      if (type.IsNested)
      {
        return reflectionOperations.GetFriendlyName(type.DeclaringType) + "." + friendlyName;
      }

      return friendlyName;
    }

    public static IEnumerable<MethodInfo> ResolvePublicPropertyPath<T>(
      this T reflectionOperations,
      object instance,
      string path,
      PathResolveOperation operation
    )
      where T : struct, IReflectionOperations
    {
      if (instance == null)
      {
        throw new ArgumentNullException(nameof(instance));
      }

      if (path == null)
      {
        throw new ArgumentNullException(nameof(path));
      }

      var pathFragments = path.Split('.');
      var fragmentInstanceType = instance.GetType();

      foreach (var fragmentPath in pathFragments.Take(pathFragments.Length - 1))
      {
        var currentPathPropertyInfo = reflectionOperations.FindProperty(fragmentInstanceType, fragmentPath);
        fragmentInstanceType = currentPathPropertyInfo.PropertyType;

        yield return currentPathPropertyInfo.GetMethod;
      }

      var finalFragment = pathFragments.Last();
      var propertyInfo = reflectionOperations.FindProperty(fragmentInstanceType, finalFragment);

      if (propertyInfo == null)
      {
        throw new InvalidOperationException($"Failed to resolve property path {path}.");
      }

      switch (operation)
      {
        case PathResolveOperation.GetValue:
          yield return propertyInfo.GetMethod;
          break;
        case PathResolveOperation.SetValue:
          yield return propertyInfo.SetMethod;
          break;
      }
    }

    private static readonly Dictionary<Type, string> TypeAliases = new()
    {
      { typeof(sbyte), "sbyte" },
      { typeof(byte), "byte" },
      { typeof(short), "short" },
      { typeof(ushort), "ushort" },
      { typeof(int), "int" },
      { typeof(uint), "uint" },
      { typeof(long), "long" },
      { typeof(ulong), "ulong" },
      { typeof(float), "float" },
      { typeof(double), "double" },
      { typeof(bool), "bool" },
      { typeof(char), "char" },
      { typeof(string), "string" },
      { typeof(object), "object" },
    };

  }
}
