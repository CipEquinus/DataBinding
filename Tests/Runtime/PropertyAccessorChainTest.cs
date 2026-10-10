using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace JH.DataBinding.Tests
{
  public class PropertyAccessorChainTest
  {
    private class Leaf
    {
      public int Value { get; set; }
      public Vector3 Position { get; set; }
    }

    private class Model
    {
      public Leaf Child { get; set; } = new Leaf();
      public virtual int Value { get; set; }
      public long LongValue { get; set; }
      public string Text { get; set; }
      public int? OptionalValue { get; set; }
    }

    private class DerivedModel : Model
    {
      public override int Value => 17;
    }

    private class HiddenModel : Model
    {
      public new int Value => 23;
    }

    private struct StructModel
    {
      public int Value { get; set; }
    }

    private class StaticModel
    {
      public static int Value { get; set; }
    }

    private class ThrowingModel
    {
      public int Value
      {
        get => throw new InvalidOperationException("getter");
        set => throw new InvalidOperationException("setter");
      }
    }

    private class ViewModel : DataSourceBase<ViewModel>
    {
      public Vector3 Position { get; set; }
      public Vector3 OtherPosition { get; set; }
      public INotifyDataSourceChanged[] Children { get; set; } = Array.Empty<INotifyDataSourceChanged>();
    }

    private class DerivedViewModel : ViewModel
    {
      public new Vector3 Position { get; set; }
    }

    private static object PrivateField(object instance, string name) =>
      instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(instance);

    private static View CreatePlannedView(string name)
    {
      var view = new GameObject(name).AddComponent<View>();
      view.componentPropertyBindings = new[]
      {
        new ComponentPropertyBinding
        {
          SourcePath = "Position", TargetComponent = view.transform, TargetPath = "localPosition",
        },
      };
      view.containerPropertyBindings = new[]
      {
        new ContainerPropertyBinding { SourcePath = "Children" },
      };
      return view;
    }

    [Test]
    public void EquivalentViewsShareWholePlansAcrossBranchesWithoutSharingInstances()
    {
      var first = CreatePlannedView("First branch");
      var second = CreatePlannedView("Second branch");
      try
      {
        var firstSource = new ViewModel { Position = Vector3.right };
        var secondSource = new ViewModel { Position = Vector3.up };
        first.DataSource = firstSource;
        var layout = PrivateField(first, "bindingPlans");
        var plan = PrivateField(layout, "lastPlan");
        second.DataSource = secondSource;
        Assert.AreSame(layout, PrivateField(second, "bindingPlans"));
        Assert.AreSame(plan, PrivateField(layout, "lastPlan"));
        Assert.AreSame(firstSource, first.containerPropertyBindings[0].DataSource);
        Assert.AreSame(secondSource, second.containerPropertyBindings[0].DataSource);
        Assert.AreEqual(Vector3.right, first.transform.localPosition);
        Assert.AreEqual(Vector3.up, second.transform.localPosition);
        first.DataSource = new ViewModel { Position = Vector3.forward };
        Assert.AreSame(plan, PrivateField(layout, "lastPlan"));
        Assert.AreEqual(Vector3.forward, first.transform.localPosition);
        Assert.AreEqual(Vector3.up, second.transform.localPosition);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(first.gameObject);
        UnityEngine.Object.DestroyImmediate(second.gameObject);
      }
    }

    [Test]
    public void SharedLayoutsKeepEachViewsPathReferencesForWarmMatching()
    {
      var first = CreatePlannedView("First path instances");
      var second = CreatePlannedView("Separate path instances");
      try
      {
        second.componentPropertyBindings = new[]
        {
          new ComponentPropertyBinding
          {
            SourcePath = new string("Position".ToCharArray()),
            TargetPath = new string("localPosition".ToCharArray()),
            TargetComponent = second.transform,
          },
        };
        second.containerPropertyBindings = new[]
        {
          new ContainerPropertyBinding { SourcePath = new string("Children".ToCharArray()) },
        };
        Assert.AreNotSame(first.componentPropertyBindings[0].SourcePath,
          second.componentPropertyBindings[0].SourcePath);
        first.DataSource = new ViewModel();
        second.DataSource = new ViewModel { Position = Vector3.up };
        Assert.AreSame(PrivateField(first, "bindingPlans"), PrivateField(second, "bindingPlans"));
        var snapshot = PrivateField(second, "bindingLayout");
        var sources = (string[])PrivateField(snapshot, "componentSources");
        Assert.AreSame(second.componentPropertyBindings[0].SourcePath, sources[0]);
        Assert.AreSame(second.componentPropertyBindings[0].TargetPath,
          ((string[])PrivateField(snapshot, "componentTargets"))[0]);
        Assert.AreSame(second.containerPropertyBindings[0].SourcePath,
          ((string[])PrivateField(snapshot, "containerSources"))[0]);
        second.DataSource = new ViewModel { Position = Vector3.forward };
        Assert.AreSame(sources, PrivateField(PrivateField(second, "bindingLayout"), "componentSources"));
        Assert.AreEqual(Vector3.forward, second.transform.localPosition);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(first.gameObject);
        UnityEngine.Object.DestroyImmediate(second.gameObject);
      }
    }

    [Test]
    public void LayoutSnapshotsRefreshAfterSerializedPathsAndSameLengthArraysChange()
    {
      var view = CreatePlannedView("Replaced paths");
      try
      {
        view.DataSource = new ViewModel();
        var layout = PrivateField(view, "bindingPlans");
        var path = new string("Position".ToCharArray());
        // Unity serialization bypasses property setters.
        typeof(ComponentPropertyBinding).GetField("sourcePath",
          BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view.componentPropertyBindings[0], path);
        var containerPath = new string("Children".ToCharArray());
        typeof(ContainerPropertyBinding).GetField("sourcePath",
          BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view.containerPropertyBindings[0], containerPath);
        view.DataSource = new ViewModel { Position = Vector3.right };
        Assert.AreSame(layout, PrivateField(view, "bindingPlans"));
        var snapshot = PrivateField(view, "bindingLayout");
        Assert.AreSame(path, ((string[])PrivateField(snapshot, "componentSources"))[0]);
        Assert.AreSame(containerPath, ((string[])PrivateField(snapshot, "containerSources"))[0]);
        Assert.AreEqual(Vector3.right, view.transform.localPosition);

        view.componentPropertyBindings = new[]
        {
          new ComponentPropertyBinding
          {
            SourcePath = "OtherPosition", TargetPath = "localScale", TargetComponent = view.transform,
          },
        };
        view.DataSource = new ViewModel { OtherPosition = Vector3.one * 3 };
        Assert.AreNotSame(layout, PrivateField(view, "bindingPlans"));
        Assert.AreEqual(Vector3.one * 3, view.transform.localScale);
      }
      finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
    }

    [Test]
    public void SeparateContainersApplyTheSamePlanToInstantiatedViews()
    {
      var template = CreatePlannedView("Shared template");
      var roots = new View[2];
      try
      {
        object sharedLayout = null;
        for (var branch = 0; branch < roots.Length; ++branch)
        {
          var root = roots[branch] = new GameObject("Container branch " + branch).AddComponent<View>();
          root.containerPropertyBindings = new[]
          {
            new ContainerPropertyBinding
            {
              SourcePath = "Children", TargetContainer = root.transform, ElementTemplate = template,
            },
          };
          root.DataSource = new ViewModel
          {
            Children = new INotifyDataSourceChanged[]
            {
              new ViewModel { Position = Vector3.one * (branch + 1) },
              new DerivedViewModel { Position = Vector3.up * (branch + 1) },
            },
          };
          Assert.AreEqual(2, root.transform.childCount);
          for (var child = 0; child < 2; ++child)
          {
            var childView = root.transform.GetChild(child).GetComponent<View>();
            var layout = PrivateField(childView, "bindingPlans");
            if (sharedLayout == null) sharedLayout = layout;
            Assert.AreSame(sharedLayout, layout);
            Assert.AreSame(childView.transform, childView.componentPropertyBindings[0].TargetComponent);
            Assert.AreEqual((child == 0 ? Vector3.one : Vector3.up) * (branch + 1),
              childView.transform.localPosition);
          }
        }
      }
      finally
      {
        foreach (var root in roots)
        {
          if (root == null) continue;
          // Clear children explicitly before the root's OnDestroy removes them asynchronously.
          while (root.transform.childCount > 0)
            UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
          UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
        UnityEngine.Object.DestroyImmediate(template.gameObject);
      }
    }

    [Test]
    public void SharedPlansSelectExactRuntimeTypesAndReuseEarlierTypePlans()
    {
      var view = CreatePlannedView("Mixed types");
      try
      {
        view.DataSource = new ViewModel { Position = Vector3.right };
        var layout = PrivateField(view, "bindingPlans");
        var basePlan = PrivateField(layout, "lastPlan");
        view.DataSource = new DerivedViewModel { Position = Vector3.up };
        Assert.AreEqual(Vector3.up, view.transform.localPosition);
        Assert.AreNotSame(basePlan, PrivateField(layout, "lastPlan"));
        view.DataSource = new ViewModel { Position = Vector3.forward };
        Assert.AreSame(basePlan, PrivateField(layout, "lastPlan"));
        Assert.AreEqual(Vector3.forward, view.transform.localPosition);
        view.DataSource = null;
        Assert.IsNull(view.componentPropertyBindings[0].DataSource);
        Assert.IsNull(view.containerPropertyBindings[0].DataSource);
        view.DataSource = new ViewModel { Position = Vector3.one };
        Assert.AreSame(basePlan, PrivateField(layout, "lastPlan"));
        Assert.AreEqual(Vector3.one, view.transform.localPosition);
      }
      finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
    }

    [Test]
    public void SharedLayoutsInvalidateOnEditsAndBindingArrayReplacement()
    {
      var view = CreatePlannedView("Edited layout");
      var otherTarget = new GameObject("Other target");
      try
      {
        view.DataSource = new ViewModel { Position = Vector3.right };
        var original = PrivateField(view, "bindingPlans");
        view.componentPropertyBindings[0].SourcePath = "OtherPosition";
        view.componentPropertyBindings[0].TargetPath = "localScale";
        view.componentPropertyBindings[0].TargetComponent = otherTarget.transform;
        view.DataSource = new ViewModel { OtherPosition = Vector3.one * 2 };
        Assert.AreNotSame(original, PrivateField(view, "bindingPlans"));
        Assert.AreEqual(Vector3.one * 2, otherTarget.transform.localScale);
        view.componentPropertyBindings = Array.Empty<ComponentPropertyBinding>();
        view.containerPropertyBindings = Array.Empty<ContainerPropertyBinding>();
        view.DataSource = new ViewModel();
        Assert.AreEqual(0, view.componentPropertyBindings.Length);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(view.gameObject);
        UnityEngine.Object.DestroyImmediate(otherTarget);
      }
    }

    private static PropertyAccessorChain Getter(object source, string path) =>
      CachedReflectionOperations.Instance.ResolvePropertyAccessors(
        source, path, PathResolveOperation.GetValue);

    private static PropertyAccessorChain Setter(object target, string path) =>
      CachedReflectionOperations.Instance.ResolvePropertyAccessors(
        target, path, PathResolveOperation.SetValue);

    [Test]
    public void NestedReferencePathsUseDelegatesAndUpdateTheActualTarget()
    {
      var source = new Model { Child = new Leaf { Value = 42 } };
      var target = new Model();
      var getter = Getter(source, "Child.Value");
      var setter = Setter(target, "Child.Value");

      Assert.IsFalse(getter.UsesReflection);
      Assert.IsFalse(setter.UsesReflection);
      Assert.AreEqual(42, getter.GetValue(source));
      getter.GetTransferTo(setter)(source, target);
      Assert.AreEqual(42, target.Child.Value);
    }

    [Test]
    public void ChainsAndTransfersAreSharedWithoutCapturingBoundObjects()
    {
      var first = new Model { Value = 3 };
      var second = new Model { Value = 9 };
      var firstTarget = new Model();
      var secondTarget = new Model();
      var firstGetter = Getter(first, "Value");
      var secondGetter = Getter(second, "Value");
      var firstSetter = Setter(firstTarget, "Value");
      var secondSetter = Setter(secondTarget, "Value");

      Assert.AreSame(firstGetter, secondGetter);
      Assert.AreSame(firstSetter, secondSetter);
      var transfer = firstGetter.GetTransferTo(firstSetter);
      Assert.AreSame(transfer, secondGetter.GetTransferTo(secondSetter));
      transfer(first, firstTarget);
      transfer(second, secondTarget);
      Assert.AreEqual(3, firstTarget.Value);
      Assert.AreEqual(9, secondTarget.Value);
    }

    [Test]
    public void InheritedOverridesAndHiddenPropertiesKeepTheirResolvedMeaning()
    {
      var derived = new DerivedModel();
      var hidden = new HiddenModel();
      Assert.AreEqual(17, Getter(derived, "Value").GetValue(derived));
      Assert.AreEqual(23, Getter(hidden, "Value").GetValue(hidden));
      Assert.AreNotSame(Getter(derived, "Value"), Getter(hidden, "Value"));
    }

    [Test]
    public void NumericConversionsAndNullAssignmentsKeepReflectionSemantics()
    {
      var source = new Model { Value = 5 };
      var target = new Model { Value = 10, OptionalValue = 20, Text = "old" };
      Getter(source, "Value").GetTransferTo(Setter(target, "LongValue"))(source, target);
      Assert.AreEqual(5L, target.LongValue);
      Setter(target, "Value").SetValue(target, null);
      Setter(target, "OptionalValue").SetValue(target, null);
      Setter(target, "Text").SetValue(target, null);
      Assert.AreEqual(0, target.Value);
      Assert.IsNull(target.OptionalValue);
      Assert.IsNull(target.Text);
      Setter(target, "OptionalValue").SetValue(target, 11);
      Assert.AreEqual(11, target.OptionalValue);
    }

    [Test]
    public void StructReceiversFallBackAndMutateTheExistingBox()
    {
      object model = new StructModel { Value = 1 };
      var getter = Getter(model, "Value");
      var setter = Setter(model, "Value");
      Assert.IsTrue(getter.UsesReflection);
      Assert.IsTrue(setter.UsesReflection);
      setter.SetValue(model, 7);
      Assert.AreEqual(7, getter.GetValue(model));
      Assert.AreEqual(7, ((StructModel)model).Value);
    }

    [Test]
    public void StaticPropertiesUseDelegates()
    {
      var model = new StaticModel();
      var getter = Getter(model, "Value");
      var setter = Setter(model, "Value");
      Assert.IsFalse(getter.UsesReflection);
      Assert.IsFalse(setter.UsesReflection);
      setter.SetValue(model, 31);
      Assert.AreEqual(31, getter.GetValue(model));
    }

    [Test]
    public void PropertyExceptionsRemainWrappedInTargetInvocationException()
    {
      var model = new ThrowingModel();
      var getterError = Assert.Throws<TargetInvocationException>(
        () => Getter(model, "Value").GetValue(model));
      var setterError = Assert.Throws<TargetInvocationException>(
        () => Setter(model, "Value").SetValue(model, 1));
      Assert.IsInstanceOf<InvalidOperationException>(getterError.InnerException);
      Assert.IsInstanceOf<InvalidOperationException>(setterError.InnerException);
      Assert.AreEqual("getter", getterError.InnerException.Message);
      Assert.AreEqual("setter", setterError.InnerException.Message);
    }

    [Test]
    public void NullRootsAndIntermediateObjectsPreserveExistingBehavior()
    {
      var model = new Model { Value = 99, Child = null };
      Assert.IsNull(Getter(model, "Value").GetValue(null));
      Getter(model, "Value").GetTransferTo(Setter(model, "Value"))(null, model);
      Assert.AreEqual(0, model.Value);
      Assert.Throws<TargetException>(() => Getter(model, "Child.Value").GetValue(model));
      Assert.Throws<ArgumentNullException>(() => Setter(model, "Value").SetValue(null, 1));
      Assert.Throws<TargetException>(() => Getter(model, "Value").GetValue(new object()));
    }

    [Test]
    public void ViewsUseDelegateTransfersAndRebindWhenPathsChange()
    {
      var gameObject = new GameObject("Delegate view");
      try
      {
        var view = gameObject.AddComponent<View>();
        var binding = new ComponentPropertyBinding
        {
          SourcePath = "Position", TargetComponent = view.transform, TargetPath = "localPosition",
        };
        view.componentPropertyBindings = new[] { binding };
        var source = new ViewModel { Position = Vector3.right, OtherPosition = Vector3.up };
        view.DataSource = source;
        Assert.AreEqual(Vector3.right, view.transform.localPosition);
        source.NotifyChanges(value => value.Position = Vector3.forward);
        Assert.AreEqual(Vector3.forward, view.transform.localPosition);
        binding.SourcePath = "OtherPosition";
        binding.UpdateBinding();
        Assert.AreEqual(Vector3.up, view.transform.localPosition);
        binding.TargetPath = "localScale";
        binding.UpdateBinding();
        Assert.AreEqual(Vector3.up, view.transform.localScale);
        view.DataSource = new ViewModel { OtherPosition = Vector3.one * 2 };
        Assert.AreEqual(Vector3.one * 2, view.transform.localScale);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(gameObject);
      }
    }

    [Test]
    public void WarmViewPlansReportPerBindingLookupComparison()
    {
      const int iterations = 20000;
      var view = CreatePlannedView("Plan benchmark");
      try
      {
        var bindings = new ComponentPropertyBinding[8];
        for (var i = 0; i < bindings.Length; ++i)
          bindings[i] = new ComponentPropertyBinding
          {
            SourcePath = "Position", TargetComponent = view.transform, TargetPath = "localPosition",
          };
        view.componentPropertyBindings = bindings;
        var first = new ViewModel { Position = Vector3.right };
        var second = new ViewModel { Position = Vector3.up };
        var bind = (Action<object>)typeof(View).GetMethod("BindBindingDataSources",
          BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Action<object>), view);
        bind(first);
        bind(second);
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < iterations; ++i) bind((i & 1) == 0 ? first : second);
        timer.Stop();
        var planMilliseconds = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        for (var i = 0; i < iterations; ++i)
        {
          var source = (i & 1) == 0 ? first : second;
          foreach (var binding in bindings) binding.DataSource = source;
          foreach (var binding in view.containerPropertyBindings) binding.DataSource = source;
        }
        timer.Stop();
        TestContext.WriteLine($"{iterations} rebindings of 8 component + 1 container bindings: " +
          $"shared view plan {planMilliseconds:F3} ms; per-binding caches " +
          $"{timer.Elapsed.TotalMilliseconds:F3} ms (execution excluded).");
        Assert.AreSame(second, bindings[0].DataSource);
      }
      finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
    }

    [Test]
    public void WarmVectorTransfersReportReflectionComparison()
    {
      const int iterations = 20000;
      var source = new Model { Child = new Leaf { Position = new Vector3(1, 2, 3) } };
      var target = new Model();
      var getter = Getter(source, "Child.Position");
      var setter = Setter(target, "Child.Position");
      var transfer = getter.GetTransferTo(setter);
      Assert.IsFalse(getter.UsesReflection);
      Assert.IsFalse(setter.UsesReflection);
      var reflection = CachedReflectionOperations.Instance;
      var getterMethods = reflection.ResolvePublicPropertyPath(
        source, "Child.Position", PathResolveOperation.GetValue);
      var setterMethods = reflection.ResolvePublicPropertyPath(
        target, "Child.Position", PathResolveOperation.SetValue);
      for (var i = 0; i < 100; ++i)
      {
        transfer(source, target);
        IReflectionOperations.SyncValue(reflection, source, target, getterMethods, setterMethods);
      }

      var timer = new Stopwatch();
      timer.Start();
      for (var i = 0; i < iterations; ++i) transfer(source, target);
      timer.Stop();
      var delegateMilliseconds = timer.Elapsed.TotalMilliseconds;
      timer.Reset();
      timer.Start();
      for (var i = 0; i < iterations; ++i)
        IReflectionOperations.SyncValue(reflection, source, target, getterMethods, setterMethods);
      timer.Stop();

      TestContext.WriteLine($"{iterations} nested Vector3 transfers: delegates " +
        $"{delegateMilliseconds:F3} ms; reflection {timer.Elapsed.TotalMilliseconds:F3} ms.");
      Assert.AreEqual(source.Child.Position, target.Child.Position);
    }
  }
}
