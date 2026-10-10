using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JH.DataBinding
{
  // Global plans contain only binding descriptions, types and accessors. No
  // sources, components, Views or template objects are retained by this cache.
  internal sealed class ViewBindingPlans
  {
    private static readonly Dictionary<BindingLayout, ViewBindingPlans> plansByLayout = new();
    private readonly BindingLayout layout;
    private readonly MethodInfo[][] targetAccessors;
    private readonly Dictionary<Type, ViewBindingPlan> sourcePlans = new();
    private ViewBindingPlan nullSourcePlan;
    internal static int Generation { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearCache()
    {
      plansByLayout.Clear();
      ++Generation;
    }

    internal static ViewBindingPlans ForTemplate(View template)
    {
      var layout = new BindingLayout(template);
      if (!plansByLayout.TryGetValue(layout, out var plans))
      {
        plans = new ViewBindingPlans(layout);
        plansByLayout.Add(layout, plans);
      }

      return plans;
    }

    private ViewBindingPlans(BindingLayout layout)
    {
      this.layout = layout;
      targetAccessors = new MethodInfo[layout.Components.Length][];
      for (var i = 0; i < targetAccessors.Length; ++i)
      {
        var component = layout.Components[i];
        targetAccessors[i] = component.TargetType != null && !string.IsNullOrEmpty(component.TargetPath)
          ? ReflectionExtensions.ResolvePublicPropertyPathForType(RuntimeReflectionOperations.Instance,
            component.TargetType, component.TargetPath, PathResolveOperation.SetValue)
          : Array.Empty<MethodInfo>();
      }
    }

    internal ViewBindingPlan ForSource(object source)
    {
      if (source == null)
      {
        return nullSourcePlan ??= CreatePlan(null);
      }

      var type = source.GetType();
      if (!sourcePlans.TryGetValue(type, out var plan))
      {
        plan = CreatePlan(source);
        sourcePlans.Add(type, plan);
      }

      return plan;
    }

    private ViewBindingPlan CreatePlan(object source)
    {
      var componentSources = new MethodInfo[layout.Components.Length][];
      for (var i = 0; i < componentSources.Length; ++i)
      {
        componentSources[i] = ResolveSource(source, layout.Components[i].SourcePath);
      }

      var containerSources = new MethodInfo[layout.ContainerSourcePaths.Length][];
      for (var i = 0; i < containerSources.Length; ++i)
      {
        containerSources[i] = ResolveSource(source, layout.ContainerSourcePaths[i]);
      }

      return new ViewBindingPlan(this, source?.GetType(), componentSources,
        containerSources, targetAccessors);
    }

    private static MethodInfo[] ResolveSource(object source, string path)
    {
      return source != null && !string.IsNullOrEmpty(path)
        ? RuntimeReflectionOperations.Instance.ResolvePublicPropertyPath(
          source, path, PathResolveOperation.GetValue)
        : Array.Empty<MethodInfo>();
    }

    // Constructed once for a View's layout, not for each source assignment.
    // Structural equality lets independent templates and cloned branches share
    // plans even when their component and nested-template objects differ.
    private sealed class BindingLayout : IEquatable<BindingLayout>
    {
      internal readonly ComponentDefinition[] Components;
      internal readonly string[] ContainerSourcePaths;
      private readonly int hashCode;

      internal BindingLayout(View template)
      {
        Components = new ComponentDefinition[template.componentPropertyBindings.Length];
        ContainerSourcePaths = new string[template.containerPropertyBindings.Length];
        unchecked
        {
          var hash = 17;
          hash = hash * 31 + Components.Length;
          hash = hash * 31 + ContainerSourcePaths.Length;
          for (var i = 0; i < Components.Length; ++i)
          {
            var binding = template.componentPropertyBindings[i];
            var definition = new ComponentDefinition(binding.SourcePath, binding.TargetPath,
              binding.TargetComponent != null ? binding.TargetComponent.GetType() : null);
            Components[i] = definition;
            hash = hash * 31 + (definition.SourcePath?.GetHashCode() ?? 0);
            hash = hash * 31 + (definition.TargetPath?.GetHashCode() ?? 0);
            hash = hash * 31 + (definition.TargetType?.GetHashCode() ?? 0);
          }

          for (var i = 0; i < ContainerSourcePaths.Length; ++i)
          {
            var path = template.containerPropertyBindings[i].SourcePath;
            ContainerSourcePaths[i] = path;
            hash = hash * 31 + (path?.GetHashCode() ?? 0);
          }

          hashCode = hash;
        }
      }

      public bool Equals(BindingLayout other)
      {
        if (other == null || hashCode != other.hashCode || Components.Length != other.Components.Length
          || ContainerSourcePaths.Length != other.ContainerSourcePaths.Length)
        {
          return false;
        }

        for (var i = 0; i < Components.Length; ++i)
        {
          var left = Components[i];
          var right = other.Components[i];
          if (left.SourcePath != right.SourcePath || left.TargetPath != right.TargetPath
            || left.TargetType != right.TargetType)
          {
            return false;
          }
        }

        for (var i = 0; i < ContainerSourcePaths.Length; ++i)
        {
          if (ContainerSourcePaths[i] != other.ContainerSourcePaths[i]) return false;
        }

        return true;
      }

      public override bool Equals(object other) => other is BindingLayout layout && Equals(layout);
      public override int GetHashCode() => hashCode;
    }

    private readonly struct ComponentDefinition
    {
      internal readonly string SourcePath;
      internal readonly string TargetPath;
      internal readonly Type TargetType;

      internal ComponentDefinition(string sourcePath, string targetPath, Type targetType)
      {
        SourcePath = sourcePath;
        TargetPath = targetPath;
        TargetType = targetType;
      }
    }
  }

  internal sealed class ViewBindingPlan
  {
    internal readonly ViewBindingPlans Definitions;
    internal readonly Type SourceType;
    private readonly MethodInfo[][] componentSources;
    private readonly MethodInfo[][] containerSources;
    private readonly MethodInfo[][] targetAccessors;

    internal ViewBindingPlan(ViewBindingPlans definitions, Type sourceType, MethodInfo[][] componentSources,
      MethodInfo[][] containerSources, MethodInfo[][] targetAccessors)
    {
      Definitions = definitions;
      SourceType = sourceType;
      this.componentSources = componentSources;
      this.containerSources = containerSources;
      this.targetAccessors = targetAccessors;
    }

    // Install accessor arrays only when the plan changes. Ordinary rebinding
    // subsequently assigns just the per-instance source object.
    internal void Bind(View view, object source)
    {
      for (var i = 0; i < componentSources.Length; ++i)
      {
        view.componentPropertyBindings[i].BindPrepared(source, componentSources[i], targetAccessors[i]);
      }

      for (var i = 0; i < containerSources.Length; ++i)
      {
        view.containerPropertyBindings[i].BindPrepared(source, containerSources[i]);
      }
    }
  }
}
