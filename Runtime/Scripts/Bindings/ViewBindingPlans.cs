using System;
using System.Collections.Generic;

namespace JH.DataBinding
{
  /// <summary>
  /// Interned binding layouts and executable plans. Only metadata is shared;
  /// data sources, components and views remain owned by their instances.
  /// Accessed on Unity's main thread, like the views themselves.
  /// </summary>
  internal sealed class ViewBindingPlans : IEquatable<ViewBindingPlans>
  {
    private static readonly Dictionary<ViewBindingPlans, ViewBindingPlans> layouts = new();
    private readonly string[] componentSources;
    private readonly string[] componentTargets;
    private readonly Type[] targetTypes;
    private readonly string[] containerSources;
    private readonly int hashCode;
    private readonly Dictionary<Type, Plan> plans = new();
    private Plan nullPlan;
    private Type lastType;
    private Plan lastPlan;

    private ViewBindingPlans(View view)
    {
      var components = view.componentPropertyBindings;
      componentSources = new string[components.Length];
      componentTargets = new string[components.Length];
      targetTypes = new Type[components.Length];
      containerSources = new string[view.containerPropertyBindings.Length];
      var hash = 17;
      for (var i = 0; i < components.Length; ++i)
      {
        componentSources[i] = components[i].SourcePath;
        componentTargets[i] = components[i].TargetPath;
        targetTypes[i] = TargetType(components[i]);
        hash = Combine(hash, componentSources[i]);
        hash = Combine(hash, componentTargets[i]);
        hash = Combine(hash, targetTypes[i]);
      }

      hash = Combine(hash, components.Length);
      for (var i = 0; i < containerSources.Length; ++i)
      {
        containerSources[i] = view.containerPropertyBindings[i].SourcePath;
        hash = Combine(hash, containerSources[i]);
      }

      hashCode = Combine(hash, containerSources.Length);
    }

    internal static ViewBindingPlans For(View view, out LayoutSnapshot snapshot)
    {
      var candidate = new ViewBindingPlans(view);
      snapshot = new LayoutSnapshot(candidate);
      if (layouts.TryGetValue(candidate, out var shared)) return shared;
      layouts.Add(candidate, candidate);
      return candidate;
    }

    // Keep each view's original string references, even when its executable plans
    // are shared with a layout created from different, equal string instances.
    internal readonly struct LayoutSnapshot
    {
      private readonly string[] componentSources;
      private readonly string[] componentTargets;
      private readonly Type[] targetTypes;
      private readonly string[] containerSources;

      internal LayoutSnapshot(ViewBindingPlans layout)
      {
        componentSources = layout.componentSources;
        componentTargets = layout.componentTargets;
        targetTypes = layout.targetTypes;
        containerSources = layout.containerSources;
      }

      internal bool Matches(View view)
      {
        if (componentSources.Length != view.componentPropertyBindings.Length
          || containerSources.Length != view.containerPropertyBindings.Length) return false;
        for (var i = 0; i < componentSources.Length; ++i)
        {
          var binding = view.componentPropertyBindings[i];
          if (!ReferenceEquals(componentSources[i], binding.SourcePath)
            || !ReferenceEquals(componentTargets[i], binding.TargetPath)
            || targetTypes[i] != TargetType(binding)) return false;
        }

        for (var i = 0; i < containerSources.Length; ++i)
        {
          if (!ReferenceEquals(containerSources[i], view.containerPropertyBindings[i].SourcePath))
            return false;
        }

        return true;
      }
    }

    internal Plan GetPlan(View view, object source)
    {
      var type = source?.GetType();
      // Siblings usually have the same source type: no dictionary lookup at all.
      if (lastPlan != null && lastType == type) return lastPlan;
      Plan plan;
      if (type == null)
      {
        plan = nullPlan ??= new Plan(view, null);
      }
      else if (!plans.TryGetValue(type, out plan))
      {
        plan = new Plan(view, source);
        plans.Add(type, plan);
      }

      lastType = type;
      return lastPlan = plan;
    }

    private static Type TargetType(ComponentPropertyBinding binding) =>
      binding.TargetComponent == null ? null : binding.TargetComponent.GetType();

    private static int Combine(int hash, object value) =>
      unchecked(hash * 31 + (value?.GetHashCode() ?? 0));

    public override int GetHashCode() => hashCode;
    public override bool Equals(object obj) => obj is ViewBindingPlans other && Equals(other);

    public bool Equals(ViewBindingPlans other)
    {
      if (other == null || hashCode != other.hashCode
        || componentSources.Length != other.componentSources.Length
        || containerSources.Length != other.containerSources.Length) return false;
      for (var i = 0; i < componentSources.Length; ++i)
      {
        if (componentSources[i] != other.componentSources[i]
          || componentTargets[i] != other.componentTargets[i]
          || targetTypes[i] != other.targetTypes[i]) return false;
      }

      for (var i = 0; i < containerSources.Length; ++i)
      {
        if (containerSources[i] != other.containerSources[i]) return false;
      }

      return true;
    }

    internal sealed class Plan
    {
      private readonly PropertyAccessorChain[] componentGetters;
      private readonly PropertyAccessorChain[] componentSetters;
      private readonly Action<object, object>[] transfers;
      private readonly PropertyAccessorChain[] containerGetters;

      internal Plan(View view, object source)
      {
        var components = view.componentPropertyBindings;
        componentGetters = new PropertyAccessorChain[components.Length];
        componentSetters = new PropertyAccessorChain[components.Length];
        transfers = new Action<object, object>[components.Length];
        containerGetters = new PropertyAccessorChain[view.containerPropertyBindings.Length];
        for (var i = 0; i < components.Length; ++i)
        {
          var binding = components[i];
          var getter = Resolve(source, binding.SourcePath, PathResolveOperation.GetValue);
          var setter = getter.Length == 0 ? PropertyAccessorChain.Empty
            : Resolve(binding.TargetComponent, binding.TargetPath, PathResolveOperation.SetValue);
          componentGetters[i] = getter;
          componentSetters[i] = setter;
          if (getter.Length > 0 && setter.Length > 0) transfers[i] = getter.GetTransferTo(setter);
        }

        for (var i = 0; i < containerGetters.Length; ++i)
        {
          containerGetters[i] = Resolve(source, view.containerPropertyBindings[i].SourcePath,
            PathResolveOperation.GetValue);
        }
      }

      internal void Apply(View view, object source)
      {
        for (var i = 0; i < componentGetters.Length; ++i)
        {
          view.componentPropertyBindings[i].ApplyPlan(source, componentGetters[i],
            componentSetters[i], transfers[i]);
        }

        for (var i = 0; i < containerGetters.Length; ++i)
        {
          view.containerPropertyBindings[i].ApplyPlan(source, containerGetters[i]);
        }
      }

      private static PropertyAccessorChain Resolve(object instance, string path,
        PathResolveOperation operation) => instance == null || string.IsNullOrEmpty(path)
        ? PropertyAccessorChain.Empty
        : RuntimeReflectionOperations.Instance.ResolvePropertyAccessors(instance, path, operation);
    }
  }
}
